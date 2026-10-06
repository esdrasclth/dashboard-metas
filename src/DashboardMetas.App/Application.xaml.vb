Imports System.IO
Imports System.Threading
Imports System.Windows.Threading
Imports DashboardMetas.App.Services
Imports DashboardMetas.App.ViewModels
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Processing
Imports DashboardMetas.Data.Announcements
Imports DashboardMetas.Data.Demo
Imports DashboardMetas.Data.Odbc
Imports Microsoft.Extensions.Configuration
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Serilog

Class Application

    Private Const SingleInstanceName As String = "Local\DashboardMetas.SingleInstance"
    Public Const CompanyFileName As String = "appsettings.empresa.json"
    ''' <summary>Started by «reiniciar» from the panel: the previous process is letting go of the lock right now.</summary>
    Public Const RestartArgument As String = "--reinicio"

    Private _host As IHost
    Private _singleInstance As Mutex

    Protected Overrides Async Sub OnStartup(e As StartupEventArgs)
        MyBase.OnStartup(e)

        Dim createdNew As Boolean
        _singleInstance = New Mutex(True, SingleInstanceName, createdNew)
        ' Started by an automatic update: the previous version is letting go of the lock right now
        Dim afterUpdate = e.Args.Contains(UpdateService.AfterUpdateArgument)
        If Not createdNew AndAlso (afterUpdate OrElse e.Args.Contains(RestartArgument)) Then
            Try
                createdNew = _singleInstance.WaitOne(TimeSpan.FromSeconds(20))
            Catch ex As AbandonedMutexException
                createdNew = True
            End Try
        End If
        If Not createdNew Then
            MessageBox.Show("Dashboard Metas ya está abierto.", "Dashboard Metas", MessageBoxButton.OK, MessageBoxImage.Information)
            Shutdown()
            Return
        End If

        Dim paths As New AppPaths()
        Log.Logger = New LoggerConfiguration().
            MinimumLevel.Information().
            Enrich.FromLogContext().
            WriteTo.File(Path.Combine(paths.LogsFolder, "dashboard-.log"),
                         rollingInterval:=RollingInterval.Day, retainedFileCountLimit:=60, shared:=True,
                         outputTemplate:="{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}").
            CreateLogger()

        AddHandler DispatcherUnhandledException, AddressOf OnDispatcherUnhandledException
        AddHandler AppDomain.CurrentDomain.UnhandledException, Sub(s, args) Log.Fatal(TryCast(args.ExceptionObject, Exception), "Error no controlado")
        AddHandler TaskScheduler.UnobservedTaskException, Sub(s, args)
                                                              Log.Error(args.Exception, "Tarea con error no observado")
                                                              args.SetObserved()
                                                          End Sub

        Try
            _host = BuildHost(e.Args, paths)
            Await _host.StartAsync()
            Dim mode = _host.Services.GetRequiredService(Of AppMode)()
            Log.Information("==== Dashboard Metas {Version} iniciado ({Mode}, proceso de {Bits} bits) ====",
                            GetType(Application).Assembly.GetName().Version, If(mode.IsDemo, "DEMO", "JDE"), If(Environment.Is64BitProcess, 64, 32))

            Dim window = _host.Services.GetRequiredService(Of MainWindow)()
            MainWindow = window
            If afterUpdate Then AddHandler window.ContentRendered, Sub() SignalUpdateReady()
            window.Show()
        Catch ex As Exception
            Log.Fatal(ex, "No se pudo iniciar la aplicación")
            MessageBox.Show("No se pudo iniciar Dashboard Metas:" & vbCrLf & ex.Message, "Dashboard Metas", MessageBoxButton.OK, MessageBoxImage.Error)
            Shutdown(1)
        End Try
    End Sub

    ''' <summary>Tells the previous version (waiting in UpdateService.HandOff) that this one opened fine.</summary>
    Private Shared Sub SignalUpdateReady()
        Try
            Dim ready As EventWaitHandle = Nothing
            If EventWaitHandle.TryOpenExisting(UpdateService.ReadyEventName, ready) Then
                Using ready
                    ready.Set()
                End Using
            End If
            Log.Information("Versión {Version} iniciada tras la actualización automática", UpdateService.CurrentVersion.ToString(3))
        Catch ex As Exception
            Log.Warning(ex, "No se pudo avisar a la versión anterior")
        End Try
    End Sub

    ''' <summary>During an update: let the new version take the single-instance lock (call on the UI thread).</summary>
    Public Sub ReleaseSingleInstance()
        Try
            _singleInstance?.ReleaseMutex()
        Catch ex As ApplicationException
            ' not owned
        End Try
    End Sub

    ''' <summary>The update failed: this version keeps running and takes the lock back.</summary>
    Public Sub AcquireSingleInstance()
        Try
            _singleInstance?.WaitOne(TimeSpan.FromSeconds(10))
        Catch ex As AbandonedMutexException
            ' the failed version died holding it: it is ours now
        End Try
    End Sub

    ''' <summary>
    ''' «Reiniciar» from the panel (UI thread): starts a new copy with the same arguments and closes this one. If the
    ''' new copy cannot start, this one keeps running.
    ''' </summary>
    Public Sub Restart()
        Dim exe = Environment.ProcessPath
        If String.IsNullOrEmpty(exe) OrElse Not exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) Then
            Log.Warning("Reinicio pedido desde el panel, pero la app no corre desde DashboardMetas.exe; se ignora")
            Return
        End If
        Dim info As New Diagnostics.ProcessStartInfo(exe) With {.UseShellExecute = False, .WorkingDirectory = Path.GetDirectoryName(exe)}
        For Each a In Environment.GetCommandLineArgs().Skip(1).Where(Function(x) x <> RestartArgument AndAlso x <> UpdateService.AfterUpdateArgument)
            info.ArgumentList.Add(a)
        Next
        info.ArgumentList.Add(RestartArgument)
        ReleaseSingleInstance()
        Try
            Diagnostics.Process.Start(info)
        Catch ex As Exception
            Log.Error(ex, "No se pudo reiniciar la app")
            AcquireSingleInstance()
            Return
        End Try
        Log.Information("Reinicio pedido desde el panel: se abre una copia nueva y se cierra esta")
        Shutdown()
    End Sub

    Private Shared Function BuildHost(args As String(), paths As AppPaths) As IHost
        Dim builder = Host.CreateApplicationBuilder(New HostApplicationBuilderSettings With {
            .Args = Array.Empty(Of String)(),
            .ContentRootPath = AppContext.BaseDirectory,
            .DisableDefaults = True})

        ' appsettings.json next to the exe (generic), appsettings.empresa.json (the company's real values,
        ' not in source control) and finally the user's overrides from the settings screen
        builder.Configuration.
            SetBasePath(AppContext.BaseDirectory).
            AddJsonFile("appsettings.json", optional:=True, reloadOnChange:=False).
            AddJsonFile(CompanyFileName, optional:=True, reloadOnChange:=False).
            AddJsonFile(paths.UserSettingsFile, optional:=True, reloadOnChange:=False)

        builder.Logging.ClearProviders()
        builder.Services.AddSerilog(dispose:=True)

        Dim services = builder.Services
        services.Configure(Of JdeSettings)(builder.Configuration.GetSection(JdeSettings.SectionName))
        services.Configure(Of DashboardSettings)(builder.Configuration.GetSection(DashboardSettings.SectionName))
        services.Configure(Of DemoSettings)(builder.Configuration.GetSection(DemoSettings.SectionName))
        services.Configure(Of AnnouncementSettings)(builder.Configuration.GetSection(AnnouncementSettings.SectionName))
        services.Configure(Of StatusSettings)(builder.Configuration.GetSection(StatusSettings.SectionName))
        services.Configure(Of UpdateSettings)(builder.Configuration.GetSection(UpdateSettings.SectionName))

        services.AddSingleton(paths)
        services.AddSingleton(New CommandLineArgs(args))
        services.AddSingleton(Of AppMode)()
        services.AddSingleton(Of IClock, AppClock)()

        ' Data: the real and the demo repository implement the same interface
        services.AddSingleton(Of OdbcProductionRepository)()
        services.AddSingleton(Of DemoProductionRepository)()
        services.AddSingleton(Of IProductionRepository, ModeAwareProductionRepository)()

        ' Core
        services.AddSingleton(Of ICredentialStore, DpapiCredentialStore)()
        services.AddSingleton(Of IPasswordPrompt, PasswordPrompt)()
        services.AddSingleton(Of JdeSessionOpener)()
        services.AddSingleton(Of ProductionRefresher)()

        ' App services (the goals and shifts managed from the panel are laid over the saved preferences)
        services.AddSingleton(Of RemoteConfigService)()
        services.AddSingleton(Of PreferencesStore)()
        services.AddSingleton(Of IAreaShiftProvider)(Function(sp) sp.GetRequiredService(Of PreferencesStore)())
        services.AddSingleton(Of UserSettingsWriter)()
        services.AddSingleton(Of DiagnosticsService)()
        services.AddSingleton(Of IDialogService, DialogService)()

        ' Remote announcements: checked in the background, shown on top of the dashboard
        services.AddSingleton(Of AnnouncementFeedClient)()
        services.AddSingleton(Of DemoAnnouncementSource)()
        services.AddSingleton(Of AnnouncementService)()
        services.AddHostedService(Function(sp) sp.GetRequiredService(Of AnnouncementService)())
        services.AddSingleton(Of AnnouncementsViewModel)()

        ' Screen status reported to the panel, signed with this PC's own key
        services.AddSingleton(Of DeviceIdentity)()
        ' Commands from the panel (they travel in the control file the announcements come from)
        services.AddSingleton(Of RemoteCommandService)()
        services.AddSingleton(Of ScreenReporter)()
        services.AddHostedService(Function(sp) sp.GetRequiredService(Of ScreenReporter)())

        ' Automatic updates (they arrive with the replies to the status reports)
        services.AddSingleton(Of UpdateService)()
        services.AddHostedService(Function(sp) sp.GetRequiredService(Of UpdateService)())

        ' View models and windows
        services.AddSingleton(Of MainViewModel)()
        services.AddSingleton(Of MainWindow)()
        services.AddTransient(Of SettingsViewModel)()
        services.AddTransient(Of SettingsWindow)()
        services.AddTransient(Of DiagnosticsViewModel)()
        services.AddTransient(Of DiagnosticsWindow)()

        Return builder.Build()
    End Function

    Private _lastErrorShownAt As Date = Date.MinValue

    Private Sub OnDispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs)
        Log.Error(e.Exception, "Error no controlado en la interfaz")
        e.Handled = True
        ' On a TV nobody may be there to close it: show the same error at most once every 10 minutes
        If (Date.Now - _lastErrorShownAt).TotalMinutes < 10 Then Return
        _lastErrorShownAt = Date.Now
        MessageBox.Show("Ocurrió un error inesperado:" & vbCrLf & e.Exception.Message & vbCrLf & vbCrLf &
                        "El detalle quedó en el log.", "Dashboard Metas", MessageBoxButton.OK, MessageBoxImage.Error)
    End Sub

    Protected Overrides Sub OnExit(e As ExitEventArgs)
        Try
            If _host IsNot Nothing Then
                _host.Services.GetService(Of MainViewModel)()?.Shutdown()
                _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult()
                _host.Dispose()
            End If
        Catch
        End Try
        Log.Information("==== Dashboard Metas cerrado ====")
        Log.CloseAndFlush()
        _singleInstance?.Dispose()
        MyBase.OnExit(e)
    End Sub

End Class
