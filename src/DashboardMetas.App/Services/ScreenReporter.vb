Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports DashboardMetas.App.ViewModels
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Models
Imports DashboardMetas.Core.Processing
Imports DashboardMetas.Core.Remote
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options

Namespace Services

    ''' <summary>Last result of the reports to the panel, for the settings screen and diagnostics.</summary>
    Public NotInheritable Class ReporterStatus
        Public Property IsConfigured As Boolean
        Public Property Url As String = String.Empty
        Public Property DeviceId As String = String.Empty
        Public Property LastAttempt As DateTimeOffset?
        Public Property LastSuccess As DateTimeOffset?
        Public Property LastError As String = String.Empty
    End Class

    ''' <summary>
    ''' Tells the panel how this screen is doing: every Status:IntervalSeconds (5 min), and within a minute when
    ''' something that matters changes (JDE goes down or comes back, an announcement is shown or closed, a new
    ''' announcements file, demo/real). Each report is signed with this PC's key. Sends only the screen's state —
    ''' never JDE figures or passwords — and never opens dialogs; failures are kept in the status and retried.
    ''' </summary>
    Public NotInheritable Class ScreenReporter
        Inherits BackgroundService

        Private Shared ReadOnly CheckEvery As TimeSpan = TimeSpan.FromSeconds(15)
        Private Shared ReadOnly MinGap As TimeSpan = TimeSpan.FromSeconds(60)
        Private Shared ReadOnly StartedAt As DateTimeOffset = New DateTimeOffset(Diagnostics.Process.GetCurrentProcess().StartTime)

        Private ReadOnly _settings As IOptionsMonitor(Of StatusSettings)
        Private ReadOnly _jde As IOptionsMonitor(Of JdeSettings)
        Private ReadOnly _identity As DeviceIdentity
        Private ReadOnly _refresher As ProductionRefresher
        Private ReadOnly _announcements As AnnouncementService
        Private ReadOnly _updates As UpdateService
        Private ReadOnly _mode As AppMode
        Private ReadOnly _services As IServiceProvider
        Private ReadOnly _logger As ILogger
        Private ReadOnly _http As HttpClient
        Private ReadOnly _sending As New SemaphoreSlim(1, 1)
        Private ReadOnly _wake As New SemaphoreSlim(0, 1)
        Private ReadOnly _gate As New Object()
        Private _status As New ReporterStatus()
        Private _lastSent As DateTimeOffset = DateTimeOffset.MinValue
        Private _lastSentAt As DateTimeOffset = DateTimeOffset.MinValue
        Private _lastFingerprint As String = String.Empty

        Public Sub New(settings As IOptionsMonitor(Of StatusSettings), jde As IOptionsMonitor(Of JdeSettings), identity As DeviceIdentity,
                       refresher As ProductionRefresher, announcements As AnnouncementService, updates As UpdateService, mode As AppMode,
                       services As IServiceProvider, logger As ILogger(Of ScreenReporter))
            _updates = updates
            _settings = settings
            _jde = jde
            _identity = identity
            _refresher = refresher
            _announcements = announcements
            _mode = mode
            _services = services
            _logger = logger
            _http = New HttpClient(New HttpClientHandler With {.UseProxy = True, .DefaultProxyCredentials = CredentialCache.DefaultCredentials}) With {
                .Timeout = TimeSpan.FromSeconds(20)}
            _http.DefaultRequestHeaders.UserAgent.Add(New ProductInfoHeaderValue("DashboardMetas", AppVersion()))
            _settings.OnChange(Sub(s, name) Wake())
        End Sub

        Public Function Status() As ReporterStatus
            SyncLock _gate
                Dim s = _settings.CurrentValue
                Return New ReporterStatus With {
                    .IsConfigured = s.IsConfigured, .Url = If(s.Url, String.Empty).Trim(), .DeviceId = _identity.Id,
                    .LastAttempt = _status.LastAttempt, .LastSuccess = _status.LastSuccess, .LastError = _status.LastError}
            End SyncLock
        End Function

        Public Sub Wake()
            Try
                If _wake.CurrentCount = 0 Then _wake.Release()
            Catch ex As SemaphoreFullException
            End Try
        End Sub

        ''' <summary>"Reportar ahora": sends right away and returns the result.</summary>
        Public Async Function ReportNowAsync(cancellationToken As CancellationToken) As Task(Of ReporterStatus)
            Await SendAsync(cancellationToken).ConfigureAwait(False)
            Return Status()
        End Function

        Protected Overrides Async Function ExecuteAsync(stoppingToken As CancellationToken) As Task
            Try
                ' First report a few seconds after the window opens
                Await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return
            End Try
            Do While Not stoppingToken.IsCancellationRequested
                Try
                    Dim settings = _settings.CurrentValue
                    If settings.IsConfigured Then
                        Dim now = DateTimeOffset.Now
                        Dim fingerprint = Me.Fingerprint()
                        Dim elapsed = now - _lastSent
                        If elapsed >= TimeSpan.FromSeconds(settings.EffectiveIntervalSeconds()) OrElse
                           (fingerprint <> _lastFingerprint AndAlso elapsed >= MinGap) Then
                            Await SendAsync(stoppingToken).ConfigureAwait(False)
                        End If
                    End If
                Catch ex As OperationCanceledException When stoppingToken.IsCancellationRequested
                    Exit Do
                Catch ex As Exception
                    _logger.LogError(ex, "Error al reportar el estado de la pantalla")
                End Try
                Try
                    Dim woken = Await _wake.WaitAsync(CheckEvery, stoppingToken).ConfigureAwait(False)
                    If woken Then _lastSent = DateTimeOffset.MinValue ' a wake-up (settings changed) reports now
                Catch ex As OperationCanceledException
                    Exit Do
                End Try
            Loop
        End Function

        ''' <summary>What makes a report urgent (the area on screen changes every 30 s with rotation, so it is left out).</summary>
        Private Function Fingerprint() As String
            Dim jde = AreaCatalog.AllQueries.Select(Function(s) _refresher.GetStatus(s)).Where(Function(s) s.HasError).
                Select(Function(s) AreaCatalog.SourceName(s.Source) & If(s.NeedsPassword, "*", "")).OrderBy(Function(x) x, StringComparer.Ordinal)
            Dim ann = _announcements.Status()
            Dim views = _announcements.Views()
            Dim update = _updates.Status()
            Return String.Join("|", _mode.IsDemo.ToString(), String.Join(",", jde), ann.Version.ToString(Globalization.CultureInfo.InvariantCulture),
                               update.State, update.Version,
                               ann.LastError, String.Join(",", views.Seen.Keys.OrderBy(Function(k) k, StringComparer.Ordinal)),
                               String.Join(",", views.Dismissed.Keys.OrderBy(Function(k) k, StringComparer.Ordinal)))
        End Function

        Private Async Function SendAsync(cancellationToken As CancellationToken) As Task
            If Not Await _sending.WaitAsync(0, cancellationToken).ConfigureAwait(False) Then Return
            Try
                Dim settings = _settings.CurrentValue
                If Not settings.IsConfigured Then Return
                Dim errors = settings.Validate()
                If errors.Count > 0 Then
                    Record(Nothing, errors(0))
                    Return
                End If
                Dim fingerprint = Me.Fingerprint()
                Dim report = Await BuildAsync().ConfigureAwait(False)
                Dim body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, AnnouncementJson.Options))
                Using request As New HttpRequestMessage(HttpMethod.Post, settings.Url.Trim())
                    request.Content = New ByteArrayContent(body)
                    request.Content.Headers.ContentType = New MediaTypeHeaderValue("application/json")
                    request.Headers.Add("x-dm-signature", _identity.Sign(body))
                    Dim attempt = DateTimeOffset.Now
                    Try
                        Using response = Await _http.SendAsync(request, cancellationToken).ConfigureAwait(False)
                            If response.IsSuccessStatusCode Then
                                _lastSent = attempt
                                _lastFingerprint = fingerprint
                                Record(attempt, String.Empty)
                                Await HandleReplyAsync(response).ConfigureAwait(False)
                            Else
                                Dim detail = Await ErrorOf(response).ConfigureAwait(False)
                                ' Do not retry in a tight loop: wait at least the minimum gap
                                _lastSent = attempt - TimeSpan.FromSeconds(settings.EffectiveIntervalSeconds()) + MinGap
                                Record(Nothing, $"El panel respondió {CInt(response.StatusCode)}: {detail}")
                            End If
                        End Using
                    Catch ex As Exception When TypeOf ex Is HttpRequestException OrElse (TypeOf ex Is TaskCanceledException AndAlso Not cancellationToken.IsCancellationRequested)
                        _lastSent = attempt - TimeSpan.FromSeconds(settings.EffectiveIntervalSeconds()) + MinGap
                        Record(Nothing, If(TypeOf ex Is TaskCanceledException, "El panel no respondió en 20 s.", "Sin acceso al panel: " & (If(ex.InnerException?.Message, ex.Message))))
                    End Try
                End Using
            Finally
                _sending.Release()
            End Try
        End Function

        Private Sub Record(success As DateTimeOffset?, [error] As String)
            Dim previous As String
            SyncLock _gate
                previous = _status.LastError
                _status.LastAttempt = DateTimeOffset.Now
                If success.HasValue Then _status.LastSuccess = success
                _status.LastError = [error]
            End SyncLock
            If Not String.IsNullOrEmpty([error]) AndAlso [error] <> previous Then _logger.LogWarning("Panel: {Error}", [error])
            If String.IsNullOrEmpty([error]) AndAlso Not String.IsNullOrEmpty(previous) Then _logger.LogInformation("Panel: reporte correcto de nuevo")
        End Sub

        ''' <summary>The reply may carry a version for this screen (signed manifest + temporary download link).</summary>
        Private Async Function HandleReplyAsync(response As HttpResponseMessage) As Task
            Try
                Using doc = JsonDocument.Parse(Await response.Content.ReadAsStringAsync().ConfigureAwait(False))
                    Dim update As JsonElement
                    If doc.RootElement.TryGetProperty("update", update) AndAlso update.ValueKind = JsonValueKind.Object Then
                        _updates.Offer(update.GetProperty("manifest").GetString(), update.GetProperty("downloadUrl").GetString())
                    End If
                End Using
            Catch ex As Exception When TypeOf ex Is JsonException OrElse TypeOf ex Is KeyNotFoundException OrElse TypeOf ex Is InvalidOperationException
                _logger.LogDebug(ex, "Respuesta del panel sin actualización legible")
            End Try
        End Function

        Private Shared Async Function ErrorOf(response As HttpResponseMessage) As Task(Of String)
            Try
                Dim text = Await response.Content.ReadAsStringAsync().ConfigureAwait(False)
                Using doc = JsonDocument.Parse(text)
                    Dim value As JsonElement
                    If doc.RootElement.TryGetProperty("error", value) Then Return value.GetString()
                End Using
                Return If(text.Length > 200, text.Substring(0, 200), text)
            Catch
                Return response.ReasonPhrase
            End Try
        End Function

        ''' <summary>The report: the screen part is read on the UI thread, the rest from thread-safe services.</summary>
        Private Async Function BuildAsync() As Task(Of ScreenReport)
            Dim screen As ScreenView = Nothing
            Dim app = Application.Current
            If app IsNot Nothing Then
                screen = Await app.Dispatcher.InvokeAsync(Function() _services.GetRequiredService(Of MainViewModel)().ScreenState()).Task.ConfigureAwait(False)
            End If

            Dim sources = AreaCatalog.AllQueries.Select(Function(s) _refresher.GetStatus(s)).Select(
                Function(s) New JdeSourceState With {
                    .Name = AreaCatalog.SourceName(s.Source),
                    .LastSuccess = ToOffset(s.LastSuccess),
                    .Error = If(s.HasError, s.LastError, Nothing),
                    .ErrorAt = If(s.HasError, ToOffset(s.LastErrorAt), Nothing)}).ToList()
            Dim ann = _announcements.Status()
            Dim views = _announcements.Views()

            Return New ScreenReport With {
                .DeviceId = _identity.Id,
                .PublicKey = _identity.PublicKey,
                .SentAt = NextSentAt(),
                .StartedAt = StartedAt,
                .Machine = Environment.MachineName,
                .Branch = _jde.CurrentValue.Branch.Trim(),
                .AppVersion = UpdateService.CurrentVersion.ToString(3),
                .Update = _updates.Status(),
                .Os = RuntimeInformation.OSDescription,
                .Mode = If(_mode.IsDemo, "demo", "real"),
                .Screen = If(screen, New ScreenView()),
                .Jde = New JdeState With {.Sources = sources, .NeedsPassword = AreaCatalog.AllQueries.Any(Function(s) _refresher.GetStatus(s).NeedsPassword)},
                .Announcements = New AnnouncementsState With {
                    .Enabled = _announcements.IsEnabled, .FeedUrl = ann.Source, .Version = ann.Version, .LastCheck = ann.LastCheck,
                    .LastError = If(String.IsNullOrEmpty(ann.LastError), Nothing, ann.LastError),
                    .Active = _announcements.Active().Select(Function(a) a.Id).ToList(),
                    .Seen = views.Seen.ToDictionary(Function(p) p.Key, Function(p) p.Value),
                    .Dismissed = views.Dismissed.ToDictionary(Function(p) p.Key, Function(p) p.Value)}}
        End Function

        ''' <summary>Real clock (the panel rejects dates far from its own), always later than the previous report.</summary>
        Private Function NextSentAt() As DateTimeOffset
            Dim now = DateTimeOffset.Now
            If now <= _lastSentAt Then now = _lastSentAt.AddMilliseconds(1)
            _lastSentAt = now
            Return now
        End Function

        Private Shared Function ToOffset(value As Date?) As DateTimeOffset?
            Return If(value.HasValue, New DateTimeOffset(value.Value), CType(Nothing, DateTimeOffset?))
        End Function

        Friend Shared Function AppVersion() As String
            Dim v = GetType(ScreenReporter).Assembly.GetName().Version
            Return If(v Is Nothing, "1.0.0", $"{v.Major}.{v.Minor}.{v.Build}")
        End Function

        Public Overrides Sub Dispose()
            _http.Dispose()
            MyBase.Dispose()
        End Sub

    End Class

End Namespace
