Imports System.IO
Imports System.Text.Json
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Models
Imports DashboardMetas.Core.Processing
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options

Namespace Services

    ''' <summary>
    ''' Reads and writes preferencias.json (what the Access version kept in tblConfig and tblAreas) and the
    ''' last data received (what it kept in tblDiario). Writes go through a temp file so a power cut never
    ''' leaves a half-written file.
    ''' </summary>
    Public NotInheritable Class PreferencesStore
        Implements IAreaShiftProvider

        Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {.WriteIndented = True}

        Private ReadOnly _paths As AppPaths
        Private ReadOnly _dashboard As IOptionsMonitor(Of DashboardSettings)
        Private ReadOnly _logger As ILogger
        Private ReadOnly _remote As RemoteConfigService

        Public Sub New(paths As AppPaths, dashboard As IOptionsMonitor(Of DashboardSettings), remote As RemoteConfigService, logger As ILogger(Of PreferencesStore))
            _paths = paths
            _dashboard = dashboard
            _remote = remote
            _logger = logger
        End Sub

        Public Function Load() As DashboardPreferences
            Dim prefs As DashboardPreferences = Nothing
            Try
                If File.Exists(_paths.PreferencesFile) Then
                    prefs = JsonSerializer.Deserialize(Of DashboardPreferences)(File.ReadAllText(_paths.PreferencesFile), JsonOptions)
                End If
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudieron leer las preferencias; se usan los valores iniciales")
            End Try
            If prefs Is Nothing Then prefs = New DashboardPreferences()
            prefs.Normalize(_dashboard.CurrentValue)
            ' Goals and shifts managed from the panel win over what was saved here
            _remote.Current()?.ApplyTo(prefs)
            Return prefs
        End Function

        ''' <summary>Shift of an area as saved in preferencias.json (used by the demo data).</summary>
        Public Function ShiftOf(areaCode As String) As ShiftSchedule Implements IAreaShiftProvider.ShiftOf
            Dim area = Load().FindArea(areaCode)
            Return If(area Is Nothing, _dashboard.CurrentValue.DefaultShift(), area.Shift())
        End Function

        Public Sub Save(preferences As DashboardPreferences)
            Try
                WriteAtomic(_paths.PreferencesFile, JsonSerializer.Serialize(preferences, JsonOptions))
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudieron guardar las preferencias")
            End Try
        End Sub

        Public Function LoadCache(demo As Boolean) As ProductionCache
            Try
                Dim file = _paths.CacheFile(demo)
                If Not IO.File.Exists(file) Then Return Nothing
                Return JsonSerializer.Deserialize(Of ProductionCache)(IO.File.ReadAllText(file), JsonOptions)
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudieron leer los últimos datos guardados")
                Return Nothing
            End Try
        End Function

        Public Sub SaveCache(demo As Boolean, cache As ProductionCache)
            Try
                WriteAtomic(_paths.CacheFile(demo), JsonSerializer.Serialize(cache, JsonOptions))
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudieron guardar los últimos datos")
            End Try
        End Sub

        Private Shared Sub WriteAtomic(path As String, content As String)
            Dim temp = path & ".tmp"
            File.WriteAllText(temp, content)
            File.Move(temp, path, overwrite:=True)
        End Sub

    End Class

End Namespace
