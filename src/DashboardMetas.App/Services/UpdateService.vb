Imports System.IO
Imports System.IO.Compression
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Remote
Imports DashboardMetas.Core.Updates
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options

Namespace Services

    ''' <summary>What survives a restart: anti-rollback, versions that failed here, and a package already downloaded.</summary>
    Public NotInheritable Class UpdateStateFile
        Public Property LastRelease As Long
        Public Property Blocked As List(Of String) = New List(Of String)()
    End Class

    ''' <summary>
    ''' Automatic updates. A version published in the panel arrives with the reply to a status report (signed
    ''' manifest + temporary link). This service checks it (signature, anti-rollback, target, version), downloads the
    ''' package and checks its size and SHA-256 and the exe's version, waits for the install window (off shift, or now)
    ''' and installs: swaps DashboardMetas.exe and appsettings.json (never appsettings.empresa.json), starts the new
    ''' version and waits for it to confirm it opened. If it does not, everything goes back as it was and that version is
    ''' never tried again on this PC. Never opens dialogs.
    ''' </summary>
    Public NotInheritable Class UpdateService
        Inherits BackgroundService

        ''' <summary>Set by the new version once its window is up (the old one is waiting for it).</summary>
        Public Const ReadyEventName As String = "Local\DashboardMetas.ActualizacionLista"
        Public Const AfterUpdateArgument As String = "--tras-actualizar"
        Private Shared ReadOnly HandoffTimeout As TimeSpan = TimeSpan.FromMinutes(2)
        Private Shared ReadOnly CheckEvery As TimeSpan = TimeSpan.FromSeconds(30)
        ''' <summary>Do not install in the first minutes after opening (the screen just came up; give JDE a first refresh).</summary>
        Private Shared ReadOnly SettleTime As TimeSpan = TimeSpan.FromMinutes(3)

        Private ReadOnly _settings As IOptionsMonitor(Of UpdateSettings)
        Private ReadOnly _jde As IOptionsMonitor(Of JdeSettings)
        Private ReadOnly _paths As AppPaths
        Private ReadOnly _store As PreferencesStore
        Private ReadOnly _clock As IClock
        Private ReadOnly _logger As ILogger
        Private ReadOnly _http As HttpClient
        Private ReadOnly _gate As New Object()
        Private ReadOnly _started As DateTimeOffset = DateTimeOffset.Now
        Private ReadOnly _folder As String
        Private ReadOnly _stateFile As String
        Private _persisted As New UpdateStateFile()
        Private _offer As ReleaseManifest
        Private _offerUrl As String = String.Empty
        Private _ready As (Manifest As ReleaseManifest, Folder As String)?
        Private _status As New UpdateState()
        Private _installing As Boolean

        Public Sub New(settings As IOptionsMonitor(Of UpdateSettings), jde As IOptionsMonitor(Of JdeSettings), paths As AppPaths,
                       store As PreferencesStore, clock As IClock, logger As ILogger(Of UpdateService))
            _settings = settings
            _jde = jde
            _paths = paths
            _store = store
            _clock = clock
            _logger = logger
            _folder = Path.Combine(paths.DataFolder, "actualizaciones")
            _stateFile = Path.Combine(_folder, "estado.json")
            Directory.CreateDirectory(_folder)
            _http = New HttpClient(New HttpClientHandler With {.UseProxy = True, .DefaultProxyCredentials = CredentialCache.DefaultCredentials}) With {
                .Timeout = TimeSpan.FromMinutes(30)}
            LoadState()
            _status = New UpdateState With {.Enabled = _settings.CurrentValue.Enabled, .State = "al-dia", .Version = CurrentVersion.ToString(3)}
        End Sub

        Public Shared ReadOnly Property CurrentVersion As Version
            Get
                Dim v = GetType(UpdateService).Assembly.GetName().Version
                Return If(v Is Nothing, New Version(1, 0, 0), New Version(v.Major, v.Minor, Math.Max(0, v.Build)))
            End Get
        End Property

        Public Function Status() As UpdateState
            SyncLock _gate
                Return New UpdateState With {.Enabled = _settings.CurrentValue.Enabled, .State = _status.State, .Version = _status.Version, .Detail = _status.Detail, .At = _status.At}
            End SyncLock
        End Function

        Private Sub SetStatus(state As String, version As String, Optional detail As String = Nothing)
            Dim changed As Boolean
            SyncLock _gate
                changed = _status.State <> state OrElse _status.Version <> version OrElse _status.Detail <> detail
                _status = New UpdateState With {.Enabled = _settings.CurrentValue.Enabled, .State = state, .Version = version, .Detail = detail, .At = DateTimeOffset.Now}
            End SyncLock
            If changed Then
                If state = "error" Then
                    _logger.LogWarning("Actualización {Version}: {Detail}", version, detail)
                Else
                    _logger.LogInformation("Actualización {Version}: {State}{Detail}", version, state, If(String.IsNullOrEmpty(detail), "", " · " & detail))
                End If
            End If
        End Sub

        ''' <summary>From the reply to a status report. Checks everything before keeping it; never throws.</summary>
        Public Sub Offer(manifestJson As String, downloadUrl As String)
            Try
                If Not _settings.CurrentValue.Enabled Then Return
                Dim check = ReleaseSigning.Verify(manifestJson, AnnouncementKeys.Trusted(), _persisted.LastRelease)
                If Not check.IsValid Then
                    ' The same manifest arrives every report: an older one is not news
                    If Not check.Error.Contains("más vieja") Then SetStatus("error", "", check.Error)
                    Return
                End If
                Dim m = check.Manifest
                Dim reason = UpdatePolicy.WhyNot(m, CurrentVersion, New AnnouncementAudience(Environment.MachineName, _jde.CurrentValue.Branch), _persisted.Blocked)
                If reason IsNot Nothing Then
                    ' Paused, retargeted or blocked after a failed install: drop anything already downloaded
                    If _installing Then Return
                    Forget(Nothing)
                    ' Keep «revertida» on view: the panel must show that this version failed here
                    If _persisted.Blocked.Contains(m.Version) AndAlso Status().State = "revertida" Then Return
                    SetStatus("no-aplica", m.Version, reason)
                    Return
                End If
                SyncLock _gate
                    If _offer IsNot Nothing AndAlso _offer.Release = m.Release Then
                        _offerUrl = downloadUrl ' a fresh link for the same version
                        Return
                    End If
                    _offer = m
                    _offerUrl = downloadUrl
                    If _ready.HasValue AndAlso _ready.Value.Manifest.Version <> m.Version Then _ready = Nothing
                End SyncLock
                _persisted.LastRelease = Math.Max(_persisted.LastRelease, m.Release)
                SaveState()
            Catch ex As Exception
                _logger.LogError(ex, "No se pudo procesar la oferta de actualización")
            End Try
        End Sub

        Protected Overrides Async Function ExecuteAsync(stoppingToken As CancellationToken) As Task
            CleanUpOldFiles()
            Do While Not stoppingToken.IsCancellationRequested
                Try
                    Await StepAsync(stoppingToken).ConfigureAwait(False)
                Catch ex As OperationCanceledException When stoppingToken.IsCancellationRequested
                    Exit Do
                Catch ex As Exception
                    _logger.LogError(ex, "Error en la actualización automática")
                End Try
                Try
                    Await Task.Delay(CheckEvery, stoppingToken).ConfigureAwait(False)
                Catch ex As OperationCanceledException
                    Exit Do
                End Try
            Loop
        End Function

        Private Async Function StepAsync(cancellationToken As CancellationToken) As Task
            If Not _settings.CurrentValue.Enabled OrElse _installing Then Return
            Dim offer As ReleaseManifest
            Dim url As String
            Dim ready As (Manifest As ReleaseManifest, Folder As String)?
            SyncLock _gate
                offer = _offer
                url = _offerUrl
                ready = _ready
            End SyncLock
            If offer Is Nothing Then Return
            If _persisted.Blocked.Contains(offer.Version) Then
                Forget(offer.Version)
                Return
            End If

            If Not ready.HasValue OrElse ready.Value.Manifest.Release <> offer.Release Then
                If String.IsNullOrEmpty(url) Then Return ' wait for a fresh link with the next report
                Dim folder = Await DownloadAsync(offer, url, cancellationToken).ConfigureAwait(False)
                If folder Is Nothing Then Return
                SyncLock _gate
                    _ready = (offer, folder)
                End SyncLock
                ready = (offer, folder)
            End If

            Dim window = InstallWindowOpen(offer)
            If Not window.Open Then
                SetStatus("lista", offer.Version, window.Why)
                Return
            End If
            Await InstallAsync(ready.Value.Manifest, ready.Value.Folder).ConfigureAwait(False)
        End Function

        ''' <summary>Downloads, checks size and SHA-256, unpacks and checks the exe's version. Nothing = failed (status says why).</summary>
        Private Async Function DownloadAsync(m As ReleaseManifest, url As String, cancellationToken As CancellationToken) As Task(Of String)
            Dim zip = Path.Combine(_folder, $"DashboardMetas-{m.Version}.zip")
            Dim folder = Path.Combine(_folder, m.Version)
            Try
                If Not (File.Exists(zip) AndAlso New FileInfo(zip).Length = m.File.Size AndAlso Sha256Of(zip) = m.File.Sha256) Then
                    SetStatus("descargando", m.Version)
                    Dim part = zip & ".part"
                    Using response = Await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(False)
                        If Not response.IsSuccessStatusCode Then
                            SetStatus("error", m.Version, $"La descarga respondió {CInt(response.StatusCode)} (se reintenta con el siguiente reporte).")
                            SyncLock _gate
                                _offerUrl = String.Empty
                            End SyncLock
                            Return Nothing
                        End If
                        Using source = Await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(False),
                              target As New FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync:=True)
                            Dim buffer(81919) As Byte
                            Dim total As Long
                            Do
                                Dim read = Await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(False)
                                If read = 0 Then Exit Do
                                total += read
                                If total > m.File.Size Then Throw New InvalidDataException("El paquete es más grande de lo anunciado.")
                                Await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(False)
                            Loop
                        End Using
                    End Using
                    If New FileInfo(part).Length <> m.File.Size Then Throw New InvalidDataException("El paquete llegó incompleto.")
                    If Sha256Of(part) <> m.File.Sha256 Then Throw New InvalidDataException("La huella SHA-256 del paquete no coincide con la publicada.")
                    File.Move(part, zip, overwrite:=True)
                End If

                If Directory.Exists(folder) Then Directory.Delete(folder, recursive:=True)
                ZipFile.ExtractToDirectory(zip, folder)
                Dim exe = Path.Combine(folder, "DashboardMetas.exe")
                If Not File.Exists(exe) OrElse Not File.Exists(Path.Combine(folder, "appsettings.json")) Then
                    Throw New InvalidDataException("El paquete no trae DashboardMetas.exe y appsettings.json.")
                End If
                Dim info = Diagnostics.FileVersionInfo.GetVersionInfo(exe)
                Dim inside = New Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart)
                If inside <> ReleaseManifest.ParseVersion(m.Version) Then
                    Throw New InvalidDataException($"El exe del paquete es la versión {inside.ToString(3)}, no {m.Version}.")
                End If
                Return folder
            Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                Throw
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is InvalidDataException OrElse TypeOf ex Is HttpRequestException OrElse TypeOf ex Is TaskCanceledException
                SetStatus("error", m.Version, ex.Message)
                SyncLock _gate
                    _offerUrl = String.Empty
                End SyncLock
                Return Nothing
            End Try
        End Function

        ''' <summary>«ahora», or «fuera de turno»: no active area is working now (and the app has been open a few minutes).</summary>
        Private Function InstallWindowOpen(m As ReleaseManifest) As (Open As Boolean, Why As String)
            If DateTimeOffset.Now - _started < SettleTime Then Return (False, "en unos minutos")
            If m.Install = ReleaseManifest.InstallNow Then Return (True, "")
            Dim now = _clock.Now.TimeOfDay
            Dim working = _store.Load().ActiveAreas().Where(Function(a) a.Shift().IsWorking(now)).Select(Function(a) a.Code).ToList()
            If working.Count > 0 Then Return (False, "fuera de turno (ahora trabajan " & String.Join(", ", working) & ")")
            Return (True, "")
        End Function

        ''' <summary>
        ''' Swaps the files, starts the new version and waits for it to say it is up. The window is hidden meanwhile;
        ''' on success this process closes, on failure everything is restored and this version keeps running.
        ''' </summary>
        Private Async Function InstallAsync(m As ReleaseManifest, staged As String) As Task
            Dim exePath = Environment.ProcessPath
            If String.IsNullOrEmpty(exePath) OrElse Not exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) Then
                SetStatus("error", m.Version, "Solo se actualiza la app publicada (DashboardMetas.exe), no desde Visual Studio.")
                Return
            End If
            Dim dir = Path.GetDirectoryName(exePath)
            Dim settingsPath = Path.Combine(dir, "appsettings.json")
            Dim probe = Path.Combine(dir, $".escritura-{Guid.NewGuid():N}.tmp")
            Try
                File.WriteAllText(probe, "")
                File.Delete(probe)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                SetStatus("error", m.Version, $"Sin permiso para escribir en {dir}: la app debe estar en una carpeta del usuario.")
                Return
            End Try

            _installing = True
            SetStatus("instalando", m.Version)
            Dim oldExe = exePath & ".old"
            Dim oldSettings = settingsPath & ".old"
            Dim swapped = False
            Try
                If File.Exists(oldExe) Then File.Delete(oldExe)
                If File.Exists(oldSettings) Then File.Delete(oldSettings)
                File.Move(exePath, oldExe)            ' Windows allows renaming the running exe
                swapped = True
                File.Copy(Path.Combine(staged, "DashboardMetas.exe"), exePath)
                If File.Exists(settingsPath) Then File.Move(settingsPath, oldSettings)
                File.Copy(Path.Combine(staged, "appsettings.json"), settingsPath)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                If swapped Then Restore(exePath, oldExe, settingsPath, oldSettings)
                _installing = False
                SetStatus("error", m.Version, "No se pudieron reemplazar los archivos: " & ex.Message)
                Return
            End Try

            Dim ok = Await Application.Current.Dispatcher.InvokeAsync(Function() HandOff(exePath, m.Version)).Task.Unwrap().ConfigureAwait(False)
            If ok Then Return ' this process is closing
            Restore(exePath, oldExe, settingsPath, oldSettings)
            If Not _persisted.Blocked.Contains(m.Version) Then _persisted.Blocked.Add(m.Version)
            SaveState()
            Forget(m.Version)
            _installing = False
            SetStatus("revertida", m.Version, "La versión nueva no abrió bien; se volvió a la anterior y no se reintenta.")
        End Function

        ''' <summary>On the UI thread: hide, free the single-instance lock, start the new exe and wait for its signal.</summary>
        Private Async Function HandOff(exePath As String, version As String) As Task(Of Boolean)
            Dim app = DirectCast(Application.Current, Global.DashboardMetas.App.Application)
            Dim window = app.MainWindow
            Using ready As New EventWaitHandle(False, EventResetMode.ManualReset, ReadyEventName)
                window?.Hide()
                app.ReleaseSingleInstance()
                Dim args = Environment.GetCommandLineArgs().Skip(1).Where(Function(a) a <> AfterUpdateArgument).ToList()
                args.Add(AfterUpdateArgument)
                Dim info As New Diagnostics.ProcessStartInfo(exePath) With {.UseShellExecute = False, .WorkingDirectory = Path.GetDirectoryName(exePath)}
                For Each a In args
                    info.ArgumentList.Add(a)
                Next
                Dim child As Diagnostics.Process = Nothing
                Try
                    child = Diagnostics.Process.Start(info)
                Catch ex As Exception
                    _logger.LogError(ex, "No se pudo iniciar la versión {Version}", version)
                End Try
                Dim signaled = False
                If child IsNot Nothing Then
                    Dim deadline = DateTimeOffset.Now + HandoffTimeout
                    Do While DateTimeOffset.Now < deadline AndAlso Not child.HasExited
                        If Await Task.Run(Function() ready.WaitOne(500)) Then
                            signaled = True
                            Exit Do
                        End If
                    Loop
                    If Not signaled AndAlso ready.WaitOne(0) Then signaled = True
                End If
                If signaled Then
                    _logger.LogInformation("Versión {Version} iniciada correctamente; se cierra la anterior", version)
                    app.Shutdown()
                    Return True
                End If
                _logger.LogError("La versión {Version} no confirmó su inicio en {Seconds} s; se vuelve a la anterior", version, HandoffTimeout.TotalSeconds)
                Try
                    If child IsNot Nothing AndAlso Not child.HasExited Then child.Kill(entireProcessTree:=True)
                    child?.WaitForExit(5000)
                Catch ex As Exception
                    _logger.LogWarning(ex, "No se pudo cerrar la versión fallida")
                End Try
                app.AcquireSingleInstance()
                window?.Show()
                Return False
            End Using
        End Function

        ''' <summary>Drops the pending offer and the downloaded package (all of them, or only those of one version).</summary>
        Private Sub Forget(version As String)
            Dim folder As String = Nothing
            SyncLock _gate
                If _offer IsNot Nothing AndAlso (version Is Nothing OrElse _offer.Version = version) Then
                    _offer = Nothing
                    _offerUrl = String.Empty
                End If
                If _ready.HasValue AndAlso (version Is Nothing OrElse _ready.Value.Manifest.Version = version) Then
                    folder = _ready.Value.Folder
                    _ready = Nothing
                End If
            End SyncLock
            If folder Is Nothing Then Return
            Try
                If Directory.Exists(folder) Then Directory.Delete(folder, recursive:=True)
                Dim zip = Path.Combine(_folder, $"DashboardMetas-{Path.GetFileName(folder)}.zip")
                If File.Exists(zip) Then File.Delete(zip)
            Catch ex As Exception
                _logger.LogDebug(ex, "No se pudo borrar el paquete descartado")
            End Try
        End Sub

        Private Sub Restore(exePath As String, oldExe As String, settingsPath As String, oldSettings As String)
            Try
                If File.Exists(oldExe) Then
                    If File.Exists(exePath) Then File.Delete(exePath)
                    File.Move(oldExe, exePath)
                End If
                If File.Exists(oldSettings) Then
                    If File.Exists(settingsPath) Then File.Delete(settingsPath)
                    File.Move(oldSettings, settingsPath)
                End If
            Catch ex As Exception
                _logger.LogError(ex, "No se pudieron restaurar los archivos de la versión anterior")
            End Try
        End Sub

        ''' <summary>Leftovers of the previous update (the .old files stay until the next one, for a manual rollback).</summary>
        Private Sub CleanUpOldFiles()
            Try
                For Each unpacked In Directory.GetDirectories(_folder)
                    Dim version = ReleaseManifest.ParseVersion(Path.GetFileName(unpacked))
                    If version IsNot Nothing AndAlso version <= CurrentVersion Then Directory.Delete(unpacked, recursive:=True)
                Next
                For Each zip In Directory.GetFiles(_folder, "DashboardMetas-*.zip*")
                    Dim name = Path.GetFileNameWithoutExtension(zip).Replace("DashboardMetas-", "").Replace(".zip", "")
                    Dim v = ReleaseManifest.ParseVersion(name)
                    If v Is Nothing OrElse v <= CurrentVersion Then File.Delete(zip)
                Next
            Catch ex As Exception
                _logger.LogDebug(ex, "No se pudieron limpiar archivos de actualizaciones anteriores")
            End Try
        End Sub

        Private Shared Function Sha256Of(file As String) As String
            Using stream = IO.File.OpenRead(file)
                Return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
            End Using
        End Function

        Private Sub LoadState()
            Try
                If File.Exists(_stateFile) Then _persisted = If(JsonSerializer.Deserialize(Of UpdateStateFile)(File.ReadAllText(_stateFile), AnnouncementJson.Options), New UpdateStateFile())
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudo leer el estado de las actualizaciones")
                _persisted = New UpdateStateFile()
            End Try
            If _persisted.Blocked Is Nothing Then _persisted.Blocked = New List(Of String)()
        End Sub

        Private Sub SaveState()
            Try
                Dim temp = _stateFile & ".tmp"
                File.WriteAllText(temp, JsonSerializer.Serialize(_persisted, AnnouncementJson.Options))
                File.Move(temp, _stateFile, overwrite:=True)
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudo guardar el estado de las actualizaciones")
            End Try
        End Sub

        Public Overrides Sub Dispose()
            _http.Dispose()
            MyBase.Dispose()
        End Sub

    End Class

End Namespace
