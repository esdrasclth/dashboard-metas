Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Text
Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Configuration
Imports Microsoft.Extensions.Options

Namespace Announcements

    ''' <summary>
    ''' Downloads the signed control file from the configured address: HTTPS through the Windows proxy with the
    ''' user's credentials (usual in a plant), with ETag / Last-Modified so an unchanged file is not downloaded
    ''' again, or a file path / network share. Only reads, never sends anything about the PC.
    ''' </summary>
    Public NotInheritable Class AnnouncementFeedClient
        Implements IAnnouncementSource, IDisposable

        Private Shared ReadOnly Timeout As TimeSpan = TimeSpan.FromSeconds(20)

        Private ReadOnly _settings As IOptionsMonitor(Of AnnouncementSettings)
        Private ReadOnly _http As HttpClient
        Private _validatorsFor As String = String.Empty
        Private _etag As EntityTagHeaderValue
        Private _lastModified As DateTimeOffset?

        Public Sub New(settings As IOptionsMonitor(Of AnnouncementSettings))
            Me.New(settings, New HttpClientHandler With {
                .UseProxy = True,
                .DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                .AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate Or DecompressionMethods.Brotli})
        End Sub

        ''' <summary>For tests: a fake handler instead of the network.</summary>
        Public Sub New(settings As IOptionsMonitor(Of AnnouncementSettings), handler As HttpMessageHandler)
            _settings = settings
            _http = New HttpClient(handler) With {.Timeout = Timeout}
            _http.DefaultRequestHeaders.UserAgent.Add(New ProductInfoHeaderValue("DashboardMetas", If(GetType(AnnouncementFeedClient).Assembly.GetName().Version?.ToString(), "1.0")))
        End Sub

        Private ReadOnly Property Address As String
            Get
                Dim s = _settings.CurrentValue
                Return If(s.Enabled, If(s.FeedUrl, String.Empty).Trim(), String.Empty)
            End Get
        End Property

        Public ReadOnly Property IsConfigured As Boolean Implements IAnnouncementSource.IsConfigured
            Get
                Return Address.Length > 0
            End Get
        End Property

        Public ReadOnly Property Description As String Implements IAnnouncementSource.Description
            Get
                Return Address
            End Get
        End Property

        Public Function TrustedKeys() As IReadOnlyDictionary(Of String, Byte()) Implements IAnnouncementSource.TrustedKeys
            Return AnnouncementKeys.Trusted()
        End Function

        Public Async Function FetchAsync(cancellationToken As CancellationToken) As Task(Of AnnouncementFetch) Implements IAnnouncementSource.FetchAsync
            Dim address = Me.Address
            If address.Length = 0 Then Throw New InvalidOperationException("No hay dirección de anuncios configurada.")
            Dim errors = New AnnouncementSettings With {.FeedUrl = address}.Validate()
            If errors.Count > 0 Then Throw New InvalidOperationException(errors(0))

            Dim uri As New Uri(address, UriKind.Absolute)
            If uri.IsFile Then Return Await ReadFileAsync(uri.LocalPath, cancellationToken).ConfigureAwait(False)

            ' Validators belong to one address: changing it in the settings downloads again
            If Not String.Equals(_validatorsFor, address, StringComparison.Ordinal) Then
                _validatorsFor = address
                _etag = Nothing
                _lastModified = Nothing
            End If

            Using request As New HttpRequestMessage(HttpMethod.Get, uri)
                request.Headers.CacheControl = New CacheControlHeaderValue With {.NoCache = True}
                If _etag IsNot Nothing Then request.Headers.IfNoneMatch.Add(_etag)
                If _lastModified.HasValue Then request.Headers.IfModifiedSince = _lastModified
                Dim response As HttpResponseMessage
                Try
                    response = Await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(False)
                Catch ex As TaskCanceledException When Not cancellationToken.IsCancellationRequested
                    Throw New TimeoutException($"La dirección de anuncios no respondió en {Timeout.TotalSeconds:0} s.", ex)
                Catch ex As HttpRequestException
                    Throw New InvalidOperationException("Sin acceso a la dirección de anuncios: " & Describe(ex), ex)
                End Try

                Using response
                    If response.StatusCode = HttpStatusCode.NotModified Then Return AnnouncementFetch.Unchanged
                    If response.StatusCode = HttpStatusCode.ProxyAuthenticationRequired Then
                        Throw New InvalidOperationException("El proxy de la red pide autenticación (407); pedir a TI que permita la dirección de anuncios.")
                    End If
                    If Not response.IsSuccessStatusCode Then
                        Throw New InvalidOperationException($"La dirección de anuncios respondió {CInt(response.StatusCode)} {response.ReasonPhrase}.")
                    End If
                    If response.Content.Headers.ContentLength.GetValueOrDefault() > AnnouncementRules.MaxFileBytes Then
                        Throw New InvalidOperationException($"El archivo de anuncios pasa de {AnnouncementRules.MaxFileBytes \ 1024} KB.")
                    End If
                    Dim text = Await ReadLimitedAsync(Await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(False), cancellationToken).ConfigureAwait(False)
                    _etag = response.Headers.ETag
                    _lastModified = response.Content.Headers.LastModified
                    Return New AnnouncementFetch With {.Content = text, .Source = address}
                End Using
            End Using
        End Function

        Private Shared Async Function ReadFileAsync(path As String, cancellationToken As CancellationToken) As Task(Of AnnouncementFetch)
            Try
                Using stream As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete, 4096, useAsync:=True)
                    Return New AnnouncementFetch With {.Content = Await ReadLimitedAsync(stream, cancellationToken).ConfigureAwait(False), .Source = path}
                End Using
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Throw New InvalidOperationException("No se pudo leer el archivo de anuncios: " & ex.Message, ex)
            End Try
        End Function

        ''' <summary>Reads at most the allowed size (a server that lies about the length cannot fill the memory).</summary>
        Private Shared Async Function ReadLimitedAsync(stream As Stream, cancellationToken As CancellationToken) As Task(Of String)
            Dim buffer As New MemoryStream()
            Dim chunk(16383) As Byte
            Do
                Dim read = Await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(False)
                If read = 0 Then Exit Do
                If buffer.Length + read > AnnouncementRules.MaxFileBytes Then
                    Throw New InvalidOperationException($"El archivo de anuncios pasa de {AnnouncementRules.MaxFileBytes \ 1024} KB.")
                End If
                buffer.Write(chunk, 0, read)
            Loop
            Return New UTF8Encoding(False).GetString(buffer.GetBuffer(), 0, CInt(buffer.Length)).TrimStart(ChrW(&HFEFF))
        End Function

        Private Shared Function Describe(ex As HttpRequestException) As String
            Dim inner = ex.InnerException
            Do While inner?.InnerException IsNot Nothing
                inner = inner.InnerException
            Loop
            Dim detail = If(inner?.Message, ex.Message)
            If TypeOf inner Is System.Security.Authentication.AuthenticationException Then
                Return "el certificado HTTPS no es válido o la red lo intercepta (" & detail & ")."
            End If
            Return detail
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            _http.Dispose()
        End Sub

    End Class

End Namespace
