Imports System.IO
Imports System.Text.Json
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Remote
Imports Microsoft.Extensions.Logging

Namespace Services

    ''' <summary>What survives a restart: the signed «metas y turnos» file in use and when it arrived.</summary>
    Public NotInheritable Class RemoteConfigFile
        Public Property Signed As String = String.Empty
        Public Property AppliedAt As DateTimeOffset?
    End Class

    ''' <summary>
    ''' «Metas y turnos» from the panel. The signed file arrives with the reply to a status report (only for screens
    ''' approved in the panel); it is checked like an announcement (trusted key, signature, contents, newer revision)
    ''' and kept in metas-panel.json. <see cref="PreferencesStore"/> lays the managed areas over the screen's own
    ''' values every time the preferences are read, so the settings screen and a stale copy can never undo them.
    ''' </summary>
    Public NotInheritable Class RemoteConfigService

        Private ReadOnly _logger As ILogger
        Private ReadOnly _file As String
        Private ReadOnly _gate As New Object()
        Private _config As RemoteConfig
        Private _signed As String = String.Empty
        Private _appliedAt As DateTimeOffset?
        Private _error As String = String.Empty

        Public Sub New(paths As AppPaths, logger As ILogger(Of RemoteConfigService))
            _logger = logger
            _file = Path.Combine(paths.DataFolder, "metas-panel.json")
            Load()
        End Sub

        ''' <summary>Raised (on a background thread) when new goals or shifts were applied.</summary>
        Public Event Changed As EventHandler

        ''' <summary>The goals and shifts managed by the panel, or Nothing.</summary>
        Public Function Current() As RemoteConfig
            SyncLock _gate
                Return _config
            End SyncLock
        End Function

        Public Function Status() As RemoteConfigState
            SyncLock _gate
                Return New RemoteConfigState With {
                    .Revision = If(_config?.Revision, 0L), .AppliedAt = _appliedAt,
                    .Managed = If(_config?.Managed().ToList(), New List(Of String)()),
                    .Error = If(String.IsNullOrEmpty(_error), Nothing, _error)}
            End SyncLock
        End Function

        ''' <summary>From the reply to a status report. Never throws.</summary>
        Public Sub Offer(signedJson As String)
            Try
                Dim minimum As Long
                SyncLock _gate
                    If String.Equals(signedJson, _signed, StringComparison.Ordinal) Then Return
                    minimum = If(_config?.Revision, 0L)
                End SyncLock
                Dim check = RemoteConfigSigning.Verify(signedJson, AnnouncementKeys.Trusted(), minimum)
                If Not check.IsValid Then
                    SyncLock _gate
                        _error = check.Error
                    End SyncLock
                    _logger.LogWarning("Metas del panel rechazadas: {Error}", check.Error)
                    Return
                End If
                If check.Config.Revision = minimum Then Return
                Dim now = DateTimeOffset.Now
                SyncLock _gate
                    _config = check.Config
                    _signed = signedJson
                    _appliedAt = now
                    _error = String.Empty
                End SyncLock
                Save()
                _logger.LogInformation("Metas y turnos del panel: revisión {Revision}, áreas {Areas}", check.Config.Revision,
                                       If(check.Config.Areas.Count = 0, "ninguna (todo local)", String.Join(", ", check.Config.Managed())))
                RaiseEvent Changed(Me, EventArgs.Empty)
            Catch ex As Exception
                _logger.LogError(ex, "No se pudieron aplicar las metas del panel")
            End Try
        End Sub

        Private Sub Load()
            Try
                If Not File.Exists(_file) Then Return
                Dim saved = JsonSerializer.Deserialize(Of RemoteConfigFile)(File.ReadAllText(_file), AnnouncementJson.Options)
                If saved Is Nothing OrElse String.IsNullOrEmpty(saved.Signed) Then Return
                ' Checked again: a hand-edited file is ignored
                Dim check = RemoteConfigSigning.Verify(saved.Signed, AnnouncementKeys.Trusted())
                If Not check.IsValid Then
                    _logger.LogWarning("metas-panel.json no es válido y se ignora: {Error}", check.Error)
                    Return
                End If
                _config = check.Config
                _signed = saved.Signed
                _appliedAt = saved.AppliedAt
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudieron leer las metas del panel")
            End Try
        End Sub

        Private Sub Save()
            Try
                Dim content As String
                SyncLock _gate
                    content = JsonSerializer.Serialize(New RemoteConfigFile With {.Signed = _signed, .AppliedAt = _appliedAt}, AnnouncementJson.Options)
                End SyncLock
                Dim temp = _file & ".tmp"
                File.WriteAllText(temp, content)
                File.Move(temp, _file, overwrite:=True)
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudieron guardar las metas del panel")
            End Try
        End Sub

    End Class

End Namespace
