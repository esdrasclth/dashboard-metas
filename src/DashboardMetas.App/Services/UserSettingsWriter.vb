Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports DashboardMetas.Core.Configuration
Imports Microsoft.Extensions.Configuration

Namespace Services

    ''' <summary>
    ''' Writes the connection settings edited in "Configuración" to %LocalAppData%\DashboardMetas\usersettings.json,
    ''' which overrides appsettings.json. The password is never stored here.
    ''' </summary>
    Public NotInheritable Class UserSettingsWriter

        Private ReadOnly _paths As AppPaths
        Private ReadOnly _configuration As IConfiguration

        Public Sub New(paths As AppPaths, configuration As IConfiguration)
            _paths = paths
            _configuration = configuration
        End Sub

        Public Sub Save(jde As JdeSettings, dashboard As DashboardSettings, demo As DemoSettings, announcements As AnnouncementSettings, status As StatusSettings, update As UpdateSettings)
            Dim root As JsonObject = Nothing
            Try
                If File.Exists(_paths.UserSettingsFile) Then root = TryCast(JsonNode.Parse(File.ReadAllText(_paths.UserSettingsFile)), JsonObject)
            Catch
                root = Nothing
            End Try
            If root Is Nothing Then root = New JsonObject()

            root(JdeSettings.SectionName) = New JsonObject From {
                {"Dsn", jde.Dsn.Trim()}, {"User", jde.User.Trim()}, {"UseDriverSignOn", jde.UseDriverSignOn}, {"Branch", jde.Branch.Trim()},
                {"AssemblyLibrary", jde.AssemblyLibrary.Trim()}, {"StationsLibrary", jde.StationsLibrary.Trim()},
                {"PricesLibrary", jde.PricesLibrary.Trim()}, {"DcLinkLibrary", jde.DcLinkLibrary.Trim()},
                {"PriceType", jde.PriceType.Trim()}, {"AssemblyOperation", jde.AssemblyOperation},
                {"ShipmentTransaction", jde.ShipmentTransaction.Trim()}, {"FloatStatuses", jde.FloatStatuses.Trim()},
                {"StationsQuantityDivisor", jde.StationsQuantityDivisor}, {"PriceDivisor", jde.PriceDivisor},
                {"CommandTimeoutSeconds", jde.CommandTimeoutSeconds}, {"ConnectionTimeoutSeconds", jde.ConnectionTimeoutSeconds}}
            root(DashboardSettings.SectionName) = New JsonObject From {
                {"RefreshMinutes", dashboard.RefreshMinutes}, {"ExcludeSundays", dashboard.ExcludeSundays},
                {"DefaultDailyGoal", dashboard.DefaultDailyGoal}, {"StartFullScreen", dashboard.StartFullScreen},
                {"ShiftStart", dashboard.ShiftStart}, {"ShiftEnd", dashboard.ShiftEnd}, {"BreakStart", dashboard.BreakStart}, {"BreakEnd", dashboard.BreakEnd},
                {"MinMinutesForProjection", dashboard.MinMinutesForProjection}}
            root(DemoSettings.SectionName) = New JsonObject From {
                {"Enabled", demo.Enabled}, {"QueryDelayMilliseconds", demo.QueryDelayMilliseconds}, {"FailingSource", demo.FailingSource}, {"SimulatedTime", demo.SimulatedTime},
                {"SimulateAnnouncements", demo.SimulateAnnouncements}}
            root(AnnouncementSettings.SectionName) = New JsonObject From {
                {"Enabled", announcements.Enabled}, {"FeedUrl", announcements.FeedUrl.Trim()}, {"PollSeconds", announcements.PollSeconds}}
            root(StatusSettings.SectionName) = New JsonObject From {
                {"Enabled", status.Enabled}, {"Url", status.Url.Trim()}, {"IntervalSeconds", status.IntervalSeconds}}
            root(UpdateSettings.SectionName) = New JsonObject From {{"Enabled", update.Enabled}}

            Dim temp = _paths.UserSettingsFile & ".tmp"
            File.WriteAllText(temp, root.ToJsonString(New JsonSerializerOptions With {.WriteIndented = True}))
            File.Move(temp, _paths.UserSettingsFile, overwrite:=True)

            ' Apply immediately (IOptionsMonitor picks up the new values)
            TryCast(_configuration, IConfigurationRoot)?.Reload()
        End Sub

        ''' <summary>"Restaurar valores de la empresa": forget the overrides.</summary>
        Public Sub Reset()
            If File.Exists(_paths.UserSettingsFile) Then File.Delete(_paths.UserSettingsFile)
            TryCast(_configuration, IConfigurationRoot)?.Reload()
        End Sub

    End Class

End Namespace
