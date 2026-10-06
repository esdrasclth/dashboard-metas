Imports System.Text.Json
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Models

Namespace Remote

    ''' <summary>
    ''' An order for the screens from the panel («actualizar datos», «reiniciar», «mostrar el Float»…), signed with the
    ''' publisher's key. It travels in the control file every screen reads each minute, lives a few minutes
    ''' (<see cref="ExpiresAt"/>) and runs once per screen. Same shape as web/anuncios/public/remote.js.
    ''' </summary>
    Public NotInheritable Class RemoteCommand
        Public Const Format As String = "dashboardmetas-comando/1"
        Public Const ActionRefresh As String = "actualizar"
        Public Const ActionRestart As String = "reiniciar"
        Public Const ActionShow As String = "mostrar"
        ''' <summary>Views of «mostrar»: an area is "area" + <see cref="Area"/>; "rotacion" goes back to the normal rotation.</summary>
        Public Const ViewFloat As String = "float"
        Public Const ViewOverview As String = "general"
        Public Const ViewArea As String = "area"
        Public Const ViewRotation As String = "rotacion"
        Public Const MaxLifetimeMinutes As Integer = 60
        Public Const MaxHoldMinutes As Integer = 480

        ''' <summary>Random, unique: a screen runs each id once.</summary>
        Public Property Id As String = String.Empty
        Public Property IssuedAt As DateTimeOffset
        Public Property ExpiresAt As DateTimeOffset
        ''' <summary>Screen ids (device key fingerprints); empty = every screen.</summary>
        Public Property Targets As List(Of String) = New List(Of String)()
        Public Property Action As String = String.Empty
        Public Property View As String = String.Empty
        Public Property Area As String = String.Empty
        ''' <summary>«mostrar»: minutes the view stays without rotating (0 = the rotation goes on as usual).</summary>
        Public Property HoldMinutes As Integer

        ''' <summary>Problems in Spanish (empty = valid). Same rules as validateCommand in remote.js.</summary>
        Public Function Validate() As IReadOnlyList(Of String)
            Dim errors As New List(Of String)()
            If String.IsNullOrWhiteSpace(Id) OrElse Id.Length > 64 Then errors.Add("Falta el id del comando.")
            If ExpiresAt <= IssuedAt OrElse ExpiresAt - IssuedAt > TimeSpan.FromMinutes(MaxLifetimeMinutes) Then
                errors.Add($"El comando debe vencer después de enviarse y en {MaxLifetimeMinutes} minutos o menos.")
            End If
            If If(Targets, New List(Of String)()).Count > 200 Then errors.Add("Demasiados destinos.")
            Select Case Action
                Case ActionRefresh, ActionRestart
                Case ActionShow
                    Select Case View
                        Case ViewFloat, ViewOverview, ViewRotation
                        Case ViewArea
                            Dim known = AreaCatalog.Defaults(1D).Select(Function(a) a.Code)
                            If Not known.Contains(If(Area, String.Empty).Trim().ToUpperInvariant()) Then errors.Add($"Área «{Area}» desconocida.")
                        Case Else
                            errors.Add($"Vista «{View}» desconocida.")
                    End Select
                    If HoldMinutes < 0 OrElse HoldMinutes > MaxHoldMinutes Then errors.Add($"Se puede mantener entre 0 y {MaxHoldMinutes} minutos.")
                Case Else
                    errors.Add($"Acción «{Action}» desconocida.")
            End Select
            Return errors
        End Function

        Public Function IsFor(deviceId As String) As Boolean
            Dim list = If(Targets, New List(Of String)())
            Return list.Count = 0 OrElse list.Any(Function(t) String.Equals(t?.Trim(), deviceId, StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>In words, for the log and the panel: «Mostrar el Float durante 30 min».</summary>
        Public Function Describe() As String
            Select Case Action
                Case ActionRefresh : Return "Actualizar los datos"
                Case ActionRestart : Return "Reiniciar la app"
                Case ActionShow
                    Dim what = If(View = ViewFloat, "el Float", If(View = ViewOverview, "la vista general", If(View = ViewRotation, "la rotación normal", "el área " & Area)))
                    Return If(View = ViewRotation, "Volver a la rotación normal", "Mostrar " & what & If(HoldMinutes > 0, $" durante {HoldMinutes} min", ""))
                Case Else : Return Action
            End Select
        End Function
    End Class

    ''' <summary>What a screen did with a command (reported to the panel).</summary>
    Public NotInheritable Class CommandResult
        Public Property Id As String = String.Empty
        Public Property Action As String = String.Empty
        Public Property At As DateTimeOffset
        Public Property Ok As Boolean
        Public Property Detail As String = String.Empty
    End Class

    Public NotInheritable Class RemoteCommandSigning

        Private Sub New()
        End Sub

        ''' <summary>Tolerance for PC clocks that are a bit off when checking the dates of a command.</summary>
        Public Shared ReadOnly ClockSkew As TimeSpan = TimeSpan.FromMinutes(5)

        Public Shared Function Verify(json As String, trustedKeys As IReadOnlyDictionary(Of String, Byte())) As (Ok As Boolean, Command As RemoteCommand, [Error] As String)
            Dim opened = AnnouncementSigning.OpenEnvelope(json, trustedKeys, RemoteCommand.Format, "de comando")
            If Not opened.Ok Then Return (False, Nothing, opened.Error)
            Dim command As RemoteCommand
            Try
                command = JsonSerializer.Deserialize(Of RemoteCommand)(opened.Payload, AnnouncementJson.Options)
            Catch ex As JsonException
                Return (False, Nothing, "El comando firmado no es válido: " & ex.Message)
            End Try
            If command Is Nothing Then Return (False, Nothing, "El comando firmado está vacío.")
            Dim problems = command.Validate()
            If problems.Count > 0 Then Return (False, command, problems(0))
            Return (True, command, String.Empty)
        End Function

        ''' <summary>Why this screen must not run it now (Nothing = run it). Expired ones are silently skipped by the caller.</summary>
        Public Shared Function WhyNot(command As RemoteCommand, now As DateTimeOffset, deviceId As String, done As ICollection(Of String)) As String
            If done IsNot Nothing AndAlso done.Contains(command.Id) Then Return "ya ejecutado"
            If Not command.IsFor(deviceId) Then Return "es para otra pantalla"
            If now > command.ExpiresAt + ClockSkew Then Return "vencido"
            If command.IssuedAt > now + ClockSkew Then Return "con fecha futura (¿la hora de esta PC está mal?)"
            Return Nothing
        End Function

        Public Shared Function Sign(command As RemoteCommand, key As Security.Cryptography.ECDsa) As String
            Return AnnouncementSigning.Serialize(AnnouncementSigning.SignPayload(command, key, RemoteCommand.Format))
        End Function

    End Class

End Namespace
