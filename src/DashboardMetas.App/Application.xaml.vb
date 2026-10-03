Imports System.IO
Imports System.Threading
Imports System.Windows.Threading
Imports DashboardMetas.App.Services
Imports DashboardMetas.App.ViewModels
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Processing
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

    Private _host As IHost
    Private _singleInstance As Mutex

    Protected Overrides Async Sub OnStartup(e As StartupEventArgs)
        MyBase.OnStartup(e)

        Dim createdNew As Boolean
        _singleInstance = New Mutex(True, SingleInstanceName, createdNew)
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
                         rollingInterval:=RollingInterval.Day, retainedFileCountLimit:=60,
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
            window.Show()
        Catch ex As Exception
            Log.Fatal(ex, "No se pudo iniciar la aplicación")
            MessageBox.Show("No se pudo iniciar Dashboard Metas:" & vbCrLf & ex.Message, "Dashboard Metas", MessageBoxButton.OK, MessageBoxImage.Error)
            Shutdown(1)
        End Try
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

        ' App services
        services.AddSingleton(Of PreferencesStore)()
        services.AddSingleton(Of IAreaShiftProvider)(Function(sp) sp.GetRequiredService(Of PreferencesStore)())
        services.AddSingleton(Of UserSettingsWriter)()
        services.AddSingleton(Of DiagnosticsService)()
        services.AddSingleton(Of IDialogService, DialogService)()

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
