Imports System.IO

Namespace Services

    ''' <summary>
    ''' Folders used by the app. User data lives in %LocalAppData%\DashboardMetas (config, preferences,
    ''' last data, credentials, logs); nothing is written next to the executable, so the app can run from any
    ''' folder without admin rights.
    ''' </summary>
    Public NotInheritable Class AppPaths

        Public Sub New()
            Me.New(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DashboardMetas"))
        End Sub

        Public Sub New(dataFolder As String)
            Me.DataFolder = dataFolder
            Directory.CreateDirectory(dataFolder)
            Directory.CreateDirectory(LogsFolder)
        End Sub

        Public ReadOnly Property DataFolder As String

        Public ReadOnly Property AppFolder As String = AppContext.BaseDirectory

        Public ReadOnly Property LogsFolder As String
            Get
                Return Path.Combine(DataFolder, "logs")
            End Get
        End Property

        ''' <summary>JDE connection overrides from "Configuración" (overrides appsettings.json).</summary>
        Public ReadOnly Property UserSettingsFile As String
            Get
                Return Path.Combine(DataFolder, "usersettings.json")
            End Get
        End Property

        ''' <summary>Language, days, rotation, area names and goals.</summary>
        Public ReadOnly Property PreferencesFile As String
            Get
                Return Path.Combine(DataFolder, "preferencias.json")
            End Get
        End Property

        ''' <summary>Last data received from JDE (or demo), so the screen opens with numbers.</summary>
        Public Function CacheFile(demo As Boolean) As String
            Return Path.Combine(DataFolder, If(demo, "ultimos_datos_demo.json", "ultimos_datos.json"))
        End Function

        Public Function CredentialFile(demo As Boolean) As String
            Return Path.Combine(DataFolder, If(demo, "clave_demo.dat", "clave_jde.dat"))
        End Function

        ''' <summary>Most recent log file (Serilog rolls one per day).</summary>
        Public Function LatestLogFile() As String
            If Not Directory.Exists(LogsFolder) Then Return Nothing
            Return New DirectoryInfo(LogsFolder).EnumerateFiles("dashboard-*.log").
                OrderByDescending(Function(f) f.LastWriteTimeUtc).
                Select(Function(f) f.FullName).FirstOrDefault()
        End Function

    End Class

End Namespace
