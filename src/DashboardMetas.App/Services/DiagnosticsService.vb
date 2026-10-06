Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Configuration
Imports Microsoft.Extensions.Options
Imports Microsoft.Win32

Namespace Services

    Public Enum DiagnosticLevel
        Ok
        Info
        Warning
        [Error]
    End Enum

    Public NotInheritable Class DiagnosticItem
        Public Sub New(group As String, label As String, value As String, Optional level As DiagnosticLevel = DiagnosticLevel.Info)
            Me.Group = group
            Me.Label = label
            Me.Value = If(value, String.Empty)
            Me.Level = level
        End Sub
        Public ReadOnly Property Group As String
        Public ReadOnly Property Label As String
        Public ReadOnly Property Value As String
        Public ReadOnly Property Level As DiagnosticLevel
    End Class

    Public NotInheritable Class OdbcDataSource
        Public Property Name As String
        Public Property Driver As String
        Public Property Scope As String
    End Class

    ''' <summary>Environment checks for support on the company PC (nothing is written anywhere).</summary>
    Public NotInheritable Class DiagnosticsService

        Private ReadOnly _jde As IOptionsMonitor(Of JdeSettings)
        Private ReadOnly _dashboard As IOptionsMonitor(Of DashboardSettings)
        Private ReadOnly _mode As AppMode
        Private ReadOnly _paths As AppPaths
        Private ReadOnly _announcements As AnnouncementService
        Private ReadOnly _reporter As ScreenReporter
        Private ReadOnly _updates As UpdateService
        Private ReadOnly _remoteConfig As RemoteConfigService
        Private ReadOnly _commands As RemoteCommandService

        Public Sub New(jde As IOptionsMonitor(Of JdeSettings), dashboard As IOptionsMonitor(Of DashboardSettings), mode As AppMode, paths As AppPaths,
                       announcements As AnnouncementService, reporter As ScreenReporter, updates As UpdateService,
                       remoteConfig As RemoteConfigService, commands As RemoteCommandService)
            _remoteConfig = remoteConfig
            _commands = commands
            _updates = updates
            _announcements = announcements
            _reporter = reporter
            _jde = jde
            _dashboard = dashboard
            _mode = mode
            _paths = paths
        End Sub

        Public Function Collect() As IReadOnlyList(Of DiagnosticItem)
            Dim items As New List(Of DiagnosticItem)()
            Dim settings = _jde.CurrentValue

            ' ---- Aplicación ----
            Const app = "Aplicación"
            items.Add(New DiagnosticItem(app, "Versión", GetType(DiagnosticsService).Assembly.GetName().Version?.ToString()))
            items.Add(New DiagnosticItem(app, "Proceso", If(Environment.Is64BitProcess, "64 bits", "32 bits (x86)"),
                                         If(Environment.Is64BitProcess, DiagnosticLevel.Error, DiagnosticLevel.Ok)))
            items.Add(New DiagnosticItem(app, "Modo", If(_mode.IsDemo, "DEMO (datos inventados)" & If(_mode.ForcedByArgument, " por argumento --demo", ""), "JDE real"),
                                         If(_mode.IsDemo, DiagnosticLevel.Warning, DiagnosticLevel.Ok)))
            items.Add(New DiagnosticItem(app, ".NET", RuntimeInformation.FrameworkDescription))
            items.Add(New DiagnosticItem(app, "Carpeta de la app", _paths.AppFolder))
            items.Add(New DiagnosticItem(app, "Datos de usuario", _paths.DataFolder))
            items.Add(New DiagnosticItem(app, "Actualización", $"cada {_dashboard.CurrentValue.EffectiveRefreshMinutes()} min"))

            ' ---- Windows ----
            Const win = "Windows"
            items.Add(New DiagnosticItem(win, "Sistema", $"{WindowsProductName()} ({RuntimeInformation.OSDescription}, {If(Environment.Is64BitOperatingSystem, "64", "32")} bits)"))
            items.Add(New DiagnosticItem(win, "Usuario", $"{Environment.UserDomainName}\{Environment.UserName}"))
            items.Add(New DiagnosticItem(win, "Administrador", If(IsAdministrator(), "Sí (no es necesario)", "No (correcto, no se necesita)"), DiagnosticLevel.Ok))
            Dim culture = CultureInfo.CurrentCulture
            items.Add(New DiagnosticItem(win, "Configuración regional",
                $"{culture.Name} - decimal ""{culture.NumberFormat.NumberDecimalSeparator}"", fecha {culture.DateTimeFormat.ShortDatePattern} (la pantalla no depende de esto)"))
            Try
                Dim screen = $"{SystemParameters.PrimaryScreenWidth:0} x {SystemParameters.PrimaryScreenHeight:0} (unidades WPF)"
                items.Add(New DiagnosticItem(win, "Pantalla principal", screen))
            Catch
            End Try

            ' ---- ODBC ----
            Const odbc = "ODBC / JDE"
            items.Add(New DiagnosticItem(odbc, "DSN configurado", settings.Dsn))
            Dim dsn32 = GetDataSources(RegistryView.Registry32)
            Dim dsn64 = GetDataSources(RegistryView.Registry64)
            Dim found = dsn32.FirstOrDefault(Function(d) String.Equals(d.Name, settings.Dsn, StringComparison.OrdinalIgnoreCase))
            If found IsNot Nothing Then
                items.Add(New DiagnosticItem(odbc, "DSN (32 bits)", $"Encontrado ({found.Scope}) - driver: {found.Driver}", DiagnosticLevel.Ok))
                Dim version = GetDriverVersion(found.Driver)
                items.Add(New DiagnosticItem(odbc, "Versión del driver", If(version, "No se pudo leer"), If(version Is Nothing, DiagnosticLevel.Warning, DiagnosticLevel.Ok)))
            ElseIf dsn64.Any(Function(d) String.Equals(d.Name, settings.Dsn, StringComparison.OrdinalIgnoreCase)) Then
                items.Add(New DiagnosticItem(odbc, "DSN (32 bits)", "NO existe en 32 bits, solo en 64 bits. Créalo en C:\Windows\SysWOW64\odbcad32.exe", DiagnosticLevel.Error))
            Else
                items.Add(New DiagnosticItem(odbc, "DSN (32 bits)", "No encontrado. Revisa el nombre en Configuración o créalo en C:\Windows\SysWOW64\odbcad32.exe",
                                             If(_mode.IsDemo, DiagnosticLevel.Warning, DiagnosticLevel.Error)))
            End If
            items.Add(New DiagnosticItem(odbc, "DSN de 32 bits disponibles", If(dsn32.Count = 0, "(ninguno)", String.Join(", ", dsn32.Select(Function(d) $"{d.Name} [{d.Driver}]")))))
            Dim ibmDrivers = GetInstalledDrivers(RegistryView.Registry32).Where(Function(d) d.IndexOf("IBM", StringComparison.OrdinalIgnoreCase) >= 0 OrElse d.IndexOf("iSeries", StringComparison.OrdinalIgnoreCase) >= 0).ToList()
            items.Add(New DiagnosticItem(odbc, "Drivers IBM i (32 bits)", If(ibmDrivers.Count = 0, "No instalado", String.Join(", ", ibmDrivers)),
                                         If(ibmDrivers.Count = 0, If(_mode.IsDemo, DiagnosticLevel.Warning, DiagnosticLevel.Error), DiagnosticLevel.Ok)))
            items.Add(New DiagnosticItem(odbc, "Usuario / planta", $"{settings.User} / {settings.Branch.Trim()} (""{JdeSettings.PadBranch(settings.Branch)}"")"))
            items.Add(New DiagnosticItem(odbc, "Inicio de sesión", If(settings.UseDriverSignOn, "El del driver IBM i Access (sin contraseña en la app)", "Contraseña guardada con DPAPI")))
            items.Add(New DiagnosticItem(odbc, "Bibliotecas", $"Y1: {settings.AssemblyLibrary} · Estaciones y Float: {settings.StationsLibrary} · Precios: {settings.PricesLibrary} · dcLINK: {settings.DcLinkLibrary}"))
            items.Add(New DiagnosticItem(odbc, "Parámetros", $"Precio {settings.PriceType} · operación {settings.AssemblyOperation} · transacción {settings.ShipmentTransaction} · estatus Float {String.Join(",", settings.FloatStatusList())} · divisores {settings.StationsQuantityDivisor}/{settings.PriceDivisor}"))
            Dim errors = settings.Validate()
            If errors.Count > 0 Then items.Add(New DiagnosticItem(odbc, "Configuración", String.Join(" ", errors), DiagnosticLevel.Error))

            ' ---- Anuncios ----
            Const ann = "Anuncios"
            Dim status = _announcements.Status()
            Dim audience = _announcements.Audience()
            items.Add(New DiagnosticItem(ann, "Dirección", If(status.IsConfigured, status.Source, "(sin configurar: anuncios apagados)"),
                                         If(status.IsConfigured, DiagnosticLevel.Ok, DiagnosticLevel.Info)))
            If status.IsConfigured Then
                items.Add(New DiagnosticItem(ann, "Última consulta", If(status.LastCheck.HasValue, status.LastCheck.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), "todavía no"),
                                             If(String.IsNullOrEmpty(status.LastError), DiagnosticLevel.Ok, DiagnosticLevel.Error)))
                If Not String.IsNullOrEmpty(status.LastError) Then items.Add(New DiagnosticItem(ann, "Error", status.LastError, DiagnosticLevel.Error))
            End If
            items.Add(New DiagnosticItem(ann, "Archivo", If(status.Version = 0, "ninguno recibido",
                $"versión {status.Version} · {status.InFile} anuncio(s) · {_announcements.Active().Count} activo(s) aquí · {status.Dismissed} cerrado(s) · clave {status.KeyId}")))
            items.Add(New DiagnosticItem(ann, "Claves aceptadas", String.Join(", ", AnnouncementKeys.Trusted().Keys)))
            items.Add(New DiagnosticItem(ann, "Esta PC", $"equipo:{audience.MachineName} · planta:{audience.Branch}"))

            ' ---- Panel (screen status) ----
            Const panel = "Panel remoto"
            Dim report = _reporter.Status()
            items.Add(New DiagnosticItem(panel, "Id de esta pantalla", report.DeviceId))
            items.Add(New DiagnosticItem(panel, "Reporte", If(report.IsConfigured, report.Url, "(apagado)"), If(report.IsConfigured, DiagnosticLevel.Ok, DiagnosticLevel.Info)))
            If report.IsConfigured Then
                items.Add(New DiagnosticItem(panel, "Último reporte correcto",
                    If(report.LastSuccess.HasValue, report.LastSuccess.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), "todavía no"),
                    If(String.IsNullOrEmpty(report.LastError), DiagnosticLevel.Ok, DiagnosticLevel.Error)))
                If Not String.IsNullOrEmpty(report.LastError) Then items.Add(New DiagnosticItem(panel, "Error", report.LastError, DiagnosticLevel.Error))
            End If
            Dim update = _updates.Status()
            items.Add(New DiagnosticItem(panel, "Actualizaciones", $"{If(update.Enabled, "automáticas", "apagadas")} · versión {UpdateService.CurrentVersion.ToString(3)} · {update.State}{If(String.IsNullOrEmpty(update.Version), "", " " & update.Version)}{If(String.IsNullOrEmpty(update.Detail), "", " · " & update.Detail)}",
                                         If(update.State = "error" OrElse update.State = "revertida", DiagnosticLevel.Error, DiagnosticLevel.Ok)))
            Dim config = _remoteConfig.Status()
            items.Add(New DiagnosticItem(panel, "Metas y turnos",
                If(Not String.IsNullOrEmpty(config.Error), config.Error,
                   If(config.Revision = 0, "los de esta pantalla (el panel no ha enviado ninguna)",
                      $"revisión {config.Revision} del panel{If(config.AppliedAt.HasValue, " (" & config.AppliedAt.Value.ToString("dd/MM HH:mm") & ")", "")} · administra {If(config.Managed.Count = 0, "ninguna área", String.Join(", ", config.Managed))}")),
                If(String.IsNullOrEmpty(config.Error), DiagnosticLevel.Ok, DiagnosticLevel.Error)))
            Dim last = _commands.Results().FirstOrDefault()
            If last IsNot Nothing Then
                items.Add(New DiagnosticItem(panel, "Último comando", $"{last.Action} · {last.At:dd/MM HH:mm}{If(last.Ok, "", " · NO SE PUDO: " & last.Detail)}",
                                             If(last.Ok, DiagnosticLevel.Ok, DiagnosticLevel.Error)))
            End If
            items.Add(New DiagnosticItem(panel, "Carpeta de la app", If(IO.Path.GetDirectoryName(Environment.ProcessPath), "")))

            Return items
        End Function

        Public Shared Function ToText(items As IEnumerable(Of DiagnosticItem)) As String
            Dim sb As New StringBuilder()
            sb.AppendLine($"Diagnóstico Dashboard Metas - {Date.Now:yyyy-MM-dd HH:mm:ss}")
            For Each group In items.GroupBy(Function(i) i.Group)
                sb.AppendLine().AppendLine($"[{group.Key}]")
                For Each item In group
                    Dim mark = If(item.Level = DiagnosticLevel.Error, "ERROR ", If(item.Level = DiagnosticLevel.Warning, "AVISO ", ""))
                    sb.AppendLine($"  {item.Label}: {mark}{item.Value}")
                Next
            Next
            Return sb.ToString()
        End Function

        Public Shared Function GetDataSources(view As RegistryView) As IReadOnlyList(Of OdbcDataSource)
            Dim result As New List(Of OdbcDataSource)()
            For Each hive In {RegistryHive.CurrentUser, RegistryHive.LocalMachine}
                Try
                    Using baseKey = RegistryKey.OpenBaseKey(hive, view)
                        Using key = baseKey.OpenSubKey("SOFTWARE\ODBC\ODBC.INI\ODBC Data Sources")
                            If key Is Nothing Then Continue For
                            For Each name In key.GetValueNames()
                                result.Add(New OdbcDataSource With {
                                    .Name = name, .Driver = Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture),
                                    .Scope = If(hive = RegistryHive.CurrentUser, "usuario", "sistema")})
                            Next
                        End Using
                    End Using
                Catch
                    ' no access: ignore
                End Try
            Next
            Return result
        End Function

        Private Shared Function GetInstalledDrivers(view As RegistryView) As IReadOnlyList(Of String)
            Try
                Using baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)
                    Using key = baseKey.OpenSubKey("SOFTWARE\ODBC\ODBCINST.INI\ODBC Drivers")
                        Return If(key Is Nothing, Array.Empty(Of String)(), key.GetValueNames())
                    End Using
                End Using
            Catch
                Return Array.Empty(Of String)()
            End Try
        End Function

        Private Shared Function GetDriverVersion(driverName As String) As String
            Try
                Using baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                    Using key = baseKey.OpenSubKey("SOFTWARE\ODBC\ODBCINST.INI\" & driverName)
                        Dim dll = Convert.ToString(key?.GetValue("Driver"), CultureInfo.InvariantCulture)
                        If String.IsNullOrEmpty(dll) Then Return Nothing
                        dll = Environment.ExpandEnvironmentVariables(dll)
                        If Not File.Exists(dll) Then Return $"Archivo no encontrado: {dll}"
                        Dim info = FileVersionInfo.GetVersionInfo(dll)
                        Return $"{info.FileVersion} ({dll})"
                    End Using
                End Using
            Catch
                Return Nothing
            End Try
        End Function

        Private Shared Function WindowsProductName() As String
            Try
                Using key = Registry.LocalMachine.OpenSubKey("SOFTWARE\Microsoft\Windows NT\CurrentVersion")
                    Dim name = Convert.ToString(key?.GetValue("ProductName"), CultureInfo.InvariantCulture)
                    Dim display = Convert.ToString(key?.GetValue("DisplayVersion"), CultureInfo.InvariantCulture)
                    Dim build = Convert.ToString(key?.GetValue("CurrentBuildNumber"), CultureInfo.InvariantCulture)
                    ' Windows 11 still reports "Windows 10" in ProductName
                    Dim buildNumber As Integer
                    If Integer.TryParse(build, buildNumber) AndAlso buildNumber >= 22000 Then name = name.Replace("Windows 10", "Windows 11")
                    Return $"{name} {display}".Trim()
                End Using
            Catch
                Return "Windows"
            End Try
        End Function

        Private Shared Function IsAdministrator() As Boolean
            Try
                Using identity = WindowsIdentity.GetCurrent()
                    Return New WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
                End Using
            Catch
                Return False
            End Try
        End Function

    End Class

End Namespace
