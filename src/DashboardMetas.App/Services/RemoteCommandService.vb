Imports System.IO
Imports System.Text.Json
Imports DashboardMetas.App.ViewModels
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Remote
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Logging

Namespace Services

    ''' <summary>What survives a restart: commands already run (until they expire) and the last results.</summary>
    Public NotInheritable Class RemoteCommandState
        Public Property Done As Dictionary(Of String, DateTimeOffset) = New Dictionary(Of String, DateTimeOffset)()
        Public Property Results As List(Of CommandResult) = New List(Of CommandResult)()
    End Class

    ''' <summary>
    ''' Runs the commands the panel puts in the control file («actualizar», «reiniciar», «mostrar…»). Each one must be
    ''' signed with a trusted key, be for this screen, be in date, and is run once: its id is saved before running it,
    ''' so a «reiniciar» never repeats after the restart. Results go to the panel with the next status report.
    ''' </summary>
    Public NotInheritable Class RemoteCommandService

        Private Const MaxResults As Integer = 10

        Private ReadOnly _identity As DeviceIdentity
        Private ReadOnly _services As IServiceProvider
        Private ReadOnly _logger As ILogger
        Private ReadOnly _file As String
        Private ReadOnly _gate As New Object()
        Private _state As New RemoteCommandState()
        Private ReadOnly _rejected As New HashSet(Of String)(StringComparer.Ordinal)

        Public Sub New(identity As DeviceIdentity, announcements As AnnouncementService, paths As AppPaths, services As IServiceProvider,
                       logger As ILogger(Of RemoteCommandService))
            _identity = identity
            _services = services
            _logger = logger
            _file = Path.Combine(paths.DataFolder, "comandos.json")
            Load()
            AddHandler announcements.FileReceived, Sub(s, content) Process(content)
        End Sub

        ''' <summary>A command ran (raised on a background thread): the status report goes out soon.</summary>
        Public Event Changed As EventHandler

        Public Function Results() As List(Of CommandResult)
            SyncLock _gate
                Return _state.Results.Select(Function(r) New CommandResult With {.Id = r.Id, .Action = r.Action, .At = r.At, .Ok = r.Ok, .Detail = r.Detail}).ToList()
            End SyncLock
        End Function

        ''' <summary>The «commands» of a control file (each one a signed envelope). Never throws.</summary>
        Public Sub Process(content As String)
            Try
                Dim envelopes As New List(Of String)()
                Using doc = JsonDocument.Parse(content)
                    Dim list As JsonElement
                    If doc.RootElement.ValueKind <> JsonValueKind.Object OrElse Not doc.RootElement.TryGetProperty("commands", list) OrElse list.ValueKind <> JsonValueKind.Array Then Return
                    For Each item In list.EnumerateArray().Take(50)
                        envelopes.Add(item.GetRawText())
                    Next
                End Using
                For Each envelope In envelopes
                    Handle(envelope)
                Next
            Catch ex As Exception
                _logger.LogError(ex, "No se pudieron leer los comandos del panel")
            End Try
        End Sub

        Private Sub Handle(envelope As String)
            Dim check = RemoteCommandSigning.Verify(envelope, AnnouncementKeys.Trusted())
            If Not check.Ok Then
                SyncLock _gate
                    If Not _rejected.Add(envelope) Then Return ' say it once
                End SyncLock
                _logger.LogWarning("Comando del panel rechazado: {Error}", check.Error)
                Return
            End If
            Dim command = check.Command
            Dim now = DateTimeOffset.Now
            Dim why As String
            SyncLock _gate
                why = RemoteCommandSigning.WhyNot(command, now, _identity.Id, _state.Done.Keys)
                If why Is Nothing Then
                    ' Saved before running it: a restart never runs it twice
                    _state.Done(command.Id) = command.ExpiresAt + RemoteCommandSigning.ClockSkew
                End If
            End SyncLock
            If why IsNot Nothing Then Return ' not for this screen, already done or expired: nothing to say
            Save()
            _logger.LogInformation("Comando del panel: {Command}", command.Describe())

            If command.Action = RemoteCommand.ActionRestart Then
                Record(command, True, "Reiniciando la app")
                Dim app = Application.Current
                app?.Dispatcher.BeginInvoke(Sub() DirectCast(app, Global.DashboardMetas.App.Application).Restart())
                Return
            End If

            Dim outcome As (Ok As Boolean, Detail As String) = (False, "La ventana no está lista.")
            Try
                Dim app = Application.Current
                If app IsNot Nothing Then
                    outcome = app.Dispatcher.Invoke(Function() _services.GetRequiredService(Of MainViewModel)().ExecuteRemote(command))
                End If
            Catch ex As Exception
                _logger.LogError(ex, "Error al ejecutar el comando {Command}", command.Describe())
                outcome = (False, ex.Message)
            End Try
            Record(command, outcome.Ok, outcome.Detail)
        End Sub

        Private Sub Record(command As RemoteCommand, ok As Boolean, detail As String)
            SyncLock _gate
                _state.Results.Insert(0, New CommandResult With {.Id = command.Id, .Action = command.Describe(), .At = DateTimeOffset.Now, .Ok = ok, .Detail = If(detail, String.Empty)})
                If _state.Results.Count > MaxResults Then _state.Results.RemoveRange(MaxResults, _state.Results.Count - MaxResults)
            End SyncLock
            Save()
            RaiseEvent Changed(Me, EventArgs.Empty)
        End Sub

        Private Sub Load()
            Try
                If File.Exists(_file) Then _state = If(JsonSerializer.Deserialize(Of RemoteCommandState)(File.ReadAllText(_file), AnnouncementJson.Options), New RemoteCommandState())
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudo leer comandos.json")
                _state = New RemoteCommandState()
            End Try
            If _state.Done Is Nothing Then _state.Done = New Dictionary(Of String, DateTimeOffset)()
            If _state.Results Is Nothing Then _state.Results = New List(Of CommandResult)()
        End Sub

        Private Sub Save()
            Try
                Dim content As String
                SyncLock _gate
                    ' Ids of expired commands are no longer needed: an expired command is never run anyway
                    Dim now = DateTimeOffset.Now
                    For Each old In _state.Done.Where(Function(p) p.Value < now).Select(Function(p) p.Key).ToList()
                        _state.Done.Remove(old)
                    Next
                    content = JsonSerializer.Serialize(_state, AnnouncementJson.Options)
                End SyncLock
                Dim temp = _file & ".tmp"
                File.WriteAllText(temp, content)
                File.Move(temp, _file, overwrite:=True)
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudo guardar comandos.json")
            End Try
        End Sub

    End Class

End Namespace
