Imports System.Text.Json
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Models

Namespace Remote

    ''' <summary>Goals and shift of one area, set from the panel. Same shape as web/anuncios/public/remote.js.</summary>
    Public NotInheritable Class RemoteAreaConfig
        Public Property Code As String = String.Empty
        ''' <summary>US$ per day (&gt; 0).</summary>
        Public Property DailyGoal As Decimal
        ''' <summary>US$ per month; 0 = automatic (daily goal × working days).</summary>
        Public Property MonthlyGoal As Decimal
        Public Property ShiftStart As String = String.Empty
        Public Property ShiftEnd As String = String.Empty
        ''' <summary>Both empty = no break.</summary>
        Public Property BreakStart As String = String.Empty
        Public Property BreakEnd As String = String.Empty
    End Class

    ''' <summary>
    ''' «Metas y turnos» published from the panel, signed with the publisher's key like the announcements. The areas
    ''' listed here are managed by the panel on every screen that receives it (the settings screen shows them
    ''' locked); the rest stay as each screen has them. An empty list hands every area back to the screens.
    ''' </summary>
    Public NotInheritable Class RemoteConfig
        Public Const Format As String = "dashboardmetas-metas/1"
        Public Const MaxAreas As Integer = 20
        Public Const MaxGoal As Decimal = 1_000_000_000D

        ''' <summary>Grows with every publication (anti-rollback): a screen never goes back to an older one.</summary>
        Public Property Revision As Long
        Public Property PublishedAt As DateTimeOffset
        Public Property Areas As List(Of RemoteAreaConfig) = New List(Of RemoteAreaConfig)()

        ''' <summary>Problems in Spanish (empty = valid). Same rules as validateConfig in remote.js.</summary>
        Public Function Validate() As IReadOnlyList(Of String)
            Dim errors As New List(Of String)()
            If Revision <= 0 Then errors.Add("Falta el número de revisión.")
            Dim list = If(Areas, New List(Of RemoteAreaConfig)())
            If list.Count > MaxAreas Then errors.Add($"Máximo {MaxAreas} áreas.")
            Dim known = AreaCatalog.Defaults(1D).Select(Function(a) a.Code).ToHashSet(StringComparer.OrdinalIgnoreCase)
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each a In list
                If a Is Nothing Then
                    errors.Add("Hay un área vacía.")
                    Continue For
                End If
                Dim code = If(a.Code, String.Empty).Trim()
                If Not known.Contains(code) Then
                    errors.Add($"Área «{code}» desconocida.")
                    Continue For
                End If
                If Not seen.Add(code) Then errors.Add($"El área {code} está repetida.")
                If a.DailyGoal <= 0D OrElse a.DailyGoal > MaxGoal Then errors.Add($"{code}: la meta diaria debe ser mayor que 0.")
                If a.MonthlyGoal < 0D OrElse a.MonthlyGoal > MaxGoal Then errors.Add($"{code}: la meta mensual no es válida.")
                Dim shift As ShiftSchedule = Nothing
                Dim problem As String = Nothing
                If Not ShiftSchedule.TryCreate(a.ShiftStart, a.ShiftEnd, a.BreakStart, a.BreakEnd, shift, problem) Then errors.Add($"{code}: {problem}")
            Next
            Return errors
        End Function

        Public Function Managed() As IReadOnlyList(Of String)
            Return If(Areas, New List(Of RemoteAreaConfig)()).Select(Function(a) a.Code.Trim().ToUpperInvariant()).ToList()
        End Function

        Public Function IsManaged(code As String) As Boolean
            Return Managed().Contains(If(code, String.Empty).Trim().ToUpperInvariant())
        End Function

        ''' <summary>Writes the managed goals and shifts over the screen's own (names, order and visibility stay local).</summary>
        Public Sub ApplyTo(preferences As DashboardPreferences)
            For Each fromPanel In If(Areas, New List(Of RemoteAreaConfig)())
                Dim area = preferences.FindArea(fromPanel.Code)
                If area Is Nothing Then Continue For
                Dim shift As ShiftSchedule = Nothing
                Dim ignored As String = Nothing
                If Not ShiftSchedule.TryCreate(fromPanel.ShiftStart, fromPanel.ShiftEnd, fromPanel.BreakStart, fromPanel.BreakEnd, shift, ignored) Then Continue For
                area.DailyGoal = fromPanel.DailyGoal
                area.MonthlyGoal = fromPanel.MonthlyGoal
                area.ShiftStart = ShiftSchedule.Format(shift.Start)
                area.ShiftEnd = ShiftSchedule.Format(shift.End)
                area.BreakStart = If(shift.HasBreak, ShiftSchedule.Format(shift.BreakStart.Value), String.Empty)
                area.BreakEnd = If(shift.HasBreak, ShiftSchedule.Format(shift.BreakEnd.Value), String.Empty)
            Next
        End Sub
    End Class

    Public NotInheritable Class RemoteConfigCheck
        Public Property IsValid As Boolean
        Public Property [Error] As String = String.Empty
        Public Property Config As RemoteConfig
    End Class

    Public NotInheritable Class RemoteConfigSigning

        Private Sub New()
        End Sub

        ''' <summary>Signature (trusted key), contents, and a revision not older than <paramref name="minimumRevision"/>.</summary>
        Public Shared Function Verify(json As String, trustedKeys As IReadOnlyDictionary(Of String, Byte()), Optional minimumRevision As Long = 0) As RemoteConfigCheck
            Dim opened = AnnouncementSigning.OpenEnvelope(json, trustedKeys, RemoteConfig.Format, "de metas")
            If Not opened.Ok Then Return New RemoteConfigCheck With {.Error = opened.Error}
            Dim config As RemoteConfig
            Try
                config = JsonSerializer.Deserialize(Of RemoteConfig)(opened.Payload, AnnouncementJson.Options)
            Catch ex As JsonException
                Return New RemoteConfigCheck With {.Error = "El contenido firmado no es válido: " & ex.Message}
            End Try
            If config Is Nothing Then Return New RemoteConfigCheck With {.Error = "El contenido firmado está vacío."}
            Dim problems = config.Validate()
            If problems.Count > 0 Then Return New RemoteConfigCheck With {.Error = problems(0)}
            If config.Revision < minimumRevision Then
                Return New RemoteConfigCheck With {.Error = $"Revisión {config.Revision} más vieja que la ya aplicada ({minimumRevision}); se ignora."}
            End If
            Return New RemoteConfigCheck With {.IsValid = True, .Config = config}
        End Function

        Public Shared Function Sign(config As RemoteConfig, key As Security.Cryptography.ECDsa) As String
            Return AnnouncementSigning.Serialize(AnnouncementSigning.SignPayload(config, key, RemoteConfig.Format))
        End Function

    End Class

End Namespace
