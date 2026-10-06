Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Threading
Imports DashboardMetas.Core.Remote
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging

Namespace Services

    ''' <summary>
    ''' Instant notices from the panel. Keeps one HTTPS stream (Server-Sent Events, Ably) open with a listen-only token that
    ''' arrives with the reply to the status report. Each «something changed» makes the screen check the control file
    ''' right away, which brings the announcements, commands and signals as usual (signed, checked here). The notice
    ''' itself carries nothing to trust. If the stream cannot open (proxy, no internet, token expired) it retries with
    ''' a growing wait, and the screen still checks the control file every 30 s.
    ''' </summary>
    Public NotInheritable Class RealtimeListener
        Inherits BackgroundService

        ''' <summary>Ably sends a keepalive every ~15 s: this long without any line means the stream is dead.</summary>
        Private Shared ReadOnly SilenceLimit As TimeSpan = TimeSpan.FromSeconds(50)
        Private Shared ReadOnly Waits As TimeSpan() = {TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)}

        Private ReadOnly _announcements As AnnouncementService
        Private ReadOnly _logger As ILogger
        Private ReadOnly _http As HttpClient
        Private ReadOnly _gate As New Object()
        Private ReadOnly _wake As New SemaphoreSlim(0, 1)
        Private _token As String = String.Empty
        Private _expires As DateTimeOffset?
        Private _channel As String = String.Empty
        Private _url As String = String.Empty
        Private _tokenChanged As CancellationTokenSource = New CancellationTokenSource()
        Private _state As New RealtimeState()

        Public Sub New(announcements As AnnouncementService, logger As ILogger(Of RealtimeListener))
            _announcements = announcements
            _logger = logger
            _http = New HttpClient(New HttpClientHandler With {.UseProxy = True, .DefaultProxyCredentials = CredentialCache.DefaultCredentials}) With {
                .Timeout = Timeout.InfiniteTimeSpan}
        End Sub

        ''' <summary>Asks for a token when there is none or it ends within the hour (the panel decides from this).</summary>
        Public Event NeedsToken As EventHandler

        Public Function Status() As RealtimeState
            SyncLock _gate
                Return New RealtimeState With {.Connected = _state.Connected, .TokenExpiresAt = _expires, .LastMessageAt = _state.LastMessageAt, .Error = _state.Error}
            End SyncLock
        End Function

        ''' <summary>A listen-only token from the reply to a status report.</summary>
        Public Sub Offer(token As String, expires As DateTimeOffset, channel As String, url As String)
            If String.IsNullOrWhiteSpace(token) OrElse String.IsNullOrWhiteSpace(channel) OrElse
               Not url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then Return
            Dim previous As CancellationTokenSource
            SyncLock _gate
                If token = _token Then Return
                _token = token
                _expires = expires
                _channel = channel
                _url = url
                previous = _tokenChanged
                _tokenChanged = New CancellationTokenSource()
            End SyncLock
            previous.Cancel() ' reconnect with the new token
            Try
                If _wake.CurrentCount = 0 Then _wake.Release()
            Catch ex As SemaphoreFullException
            End Try
        End Sub

        Private Sub SetState(connected As Boolean, [error] As String, Optional message As Boolean = False)
            Dim changed As Boolean
            SyncLock _gate
                changed = _state.Connected <> connected OrElse _state.Error <> [error]
                _state.Connected = connected
                _state.Error = [error]
                If message Then _state.LastMessageAt = DateTimeOffset.Now
            End SyncLock
            If changed Then
                If connected Then
                    _logger.LogInformation("Avisos instantáneos del panel: conectado")
                ElseIf Not String.IsNullOrEmpty([error]) Then
                    _logger.LogWarning("Avisos instantáneos del panel: {Error} (sigue la consulta cada 30 s)", [error])
                End If
            End If
        End Sub

        Protected Overrides Async Function ExecuteAsync(stoppingToken As CancellationToken) As Task
            Dim failures = 0
            Do While Not stoppingToken.IsCancellationRequested
                Dim token, channel, url As String
                Dim expires As DateTimeOffset?
                Dim changed As CancellationToken
                SyncLock _gate
                    token = _token
                    channel = _channel
                    url = _url
                    expires = _expires
                    changed = _tokenChanged.Token
                End SyncLock

                If String.IsNullOrEmpty(token) OrElse (expires.HasValue AndAlso expires.Value <= DateTimeOffset.Now.AddMinutes(1)) Then
                    RaiseEvent NeedsToken(Me, EventArgs.Empty)
                    Try
                        Await _wake.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(False)
                    Catch ex As OperationCanceledException
                        Exit Do
                    End Try
                    Continue Do
                End If
                ' Ask for the next token an hour before this one ends (it comes with the next report)
                If expires.HasValue AndAlso expires.Value <= DateTimeOffset.Now.AddHours(1) Then RaiseEvent NeedsToken(Me, EventArgs.Empty)

                Dim outcome As String
                Using linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, changed)
                    outcome = Await ListenAsync(url, channel, token, linked.Token).ConfigureAwait(False)
                End Using
                If stoppingToken.IsCancellationRequested Then Exit Do
                If changed.IsCancellationRequested Then
                    failures = 0
                    Continue Do ' new token: connect again right away
                End If
                If outcome = "token" Then
                    SyncLock _gate
                        _token = String.Empty
                    End SyncLock
                    Continue Do
                End If
                failures = If(outcome = "ok", 0, failures + 1)
                Try
                    Await Task.Delay(Waits(Math.Min(failures, Waits.Length - 1)), stoppingToken).ConfigureAwait(False)
                Catch ex As OperationCanceledException
                    Exit Do
                End Try
            Loop
            SetState(False, String.Empty)
        End Function

        ''' <summary>One SSE session. Returns "ok" (closed after working), "token" (expired or refused) or "error".</summary>
        Private Async Function ListenAsync(url As String, channel As String, token As String, cancellationToken As CancellationToken) As Task(Of String)
            Dim address = $"{url}?v=1.2&heartbeats=true&channels={Uri.EscapeDataString(channel)}&accessToken={Uri.EscapeDataString(token)}"
            Dim worked = False
            Try
                Using request As New HttpRequestMessage(HttpMethod.Get, address)
                    request.Headers.Accept.ParseAdd("text/event-stream")
                    Using response = Await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(False)
                        If response.StatusCode = HttpStatusCode.Unauthorized OrElse response.StatusCode = HttpStatusCode.Forbidden Then
                            SetState(False, "permiso de escucha vencido o rechazado; se pide otro")
                            Return "token"
                        End If
                        If Not response.IsSuccessStatusCode Then
                            SetState(False, $"el servicio respondió {CInt(response.StatusCode)}")
                            Return "error"
                        End If
                        Using stream = Await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(False),
                              reader As New StreamReader(stream)
                            SetState(True, String.Empty)
                            worked = True
                            Dim lastEvent = String.Empty
                            Do
                                Dim line As String
                                Using silence = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                                    silence.CancelAfter(SilenceLimit)
                                    Try
                                        line = Await reader.ReadLineAsync(silence.Token).ConfigureAwait(False)
                                    Catch ex As OperationCanceledException When Not cancellationToken.IsCancellationRequested
                                        SetState(False, "la conexión quedó en silencio; se reconecta")
                                        Return "ok"
                                    End Try
                                End Using
                                If line Is Nothing Then
                                    SetState(False, "el servicio cerró la conexión; se reconecta")
                                    Return "ok"
                                End If
                                If line.StartsWith("event:", StringComparison.Ordinal) Then
                                    lastEvent = line.Substring(6).Trim()
                                ElseIf line.StartsWith("data:", StringComparison.Ordinal) Then
                                    If lastEvent = "message" Then
                                        SetState(True, String.Empty, message:=True)
                                        _logger.LogInformation("Aviso instantáneo del panel: se consulta ahora")
                                        _announcements.Wake()
                                    ElseIf lastEvent = "error" Then
                                        ' Ably reports an expired token this way (code 401xx)
                                        Dim tokenProblem = line.Contains("""code"":401", StringComparison.Ordinal) OrElse line.Contains("""statusCode"":401", StringComparison.Ordinal)
                                        SetState(False, If(tokenProblem, "permiso de escucha vencido; se pide otro", "el servicio avisó un error: " & line.Substring(5).Trim()))
                                        Return If(tokenProblem, "token", "error")
                                    End If
                                ElseIf line.Length = 0 Then
                                    lastEvent = String.Empty
                                End If
                            Loop
                        End Using
                    End Using
                End Using
            Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                Return "ok"
            Catch ex As Exception When TypeOf ex Is HttpRequestException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is OperationCanceledException
                SetState(False, "sin conexión con el servicio de avisos: " & If(ex.InnerException?.Message, ex.Message))
                Return If(worked, "ok", "error")
            End Try
        End Function

        Public Overrides Sub Dispose()
            _http.Dispose()
            _tokenChanged.Dispose()
            MyBase.Dispose()
        End Sub

    End Class

End Namespace
