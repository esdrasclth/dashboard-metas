Imports System.Text.RegularExpressions

Namespace Announcements

    ''' <summary>
    ''' Limits of an announcement. The signing tool refuses to sign a file that breaks them; the app drops (and
    ''' logs) the announcements that break them, so one bad entry never hides the others.
    ''' </summary>
    Public NotInheritable Class AnnouncementRules

        Public Const MaxAnnouncements As Integer = 20
        Public Const MaxTitleLength As Integer = 120
        Public Const MaxMessageLength As Integer = 1500
        Public Const MaxTargets As Integer = 100
        ''' <summary>Longest time an announcement may stay on screen.</summary>
        Public Shared ReadOnly MaxDuration As TimeSpan = TimeSpan.FromDays(31)
        ''' <summary>Largest control file the app downloads.</summary>
        Public Const MaxFileBytes As Integer = 256 * 1024

        Private Shared ReadOnly IdPattern As New Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)

        Private Sub New()
        End Sub

        ''' <summary>Problems of one announcement, in Spanish (empty = valid).</summary>
        Public Shared Function Validate(a As Announcement) As IReadOnlyList(Of String)
            Dim errors As New List(Of String)()
            If a Is Nothing Then
                errors.Add("Anuncio vacío.")
                Return errors
            End If
            Dim name = If(String.IsNullOrWhiteSpace(a.Id), "(sin id)", a.Id)
            If String.IsNullOrWhiteSpace(a.Id) OrElse Not IdPattern.IsMatch(a.Id) Then
                errors.Add($"{name}: el id debe tener de 1 a 64 letras, números, punto, guion o guion bajo.")
            End If
            If String.IsNullOrWhiteSpace(a.Title) Then errors.Add($"{name}: falta el título.")
            If String.IsNullOrWhiteSpace(a.Message) Then errors.Add($"{name}: falta el mensaje.")
            CheckLength(errors, name, "título", a.Title, MaxTitleLength)
            CheckLength(errors, name, "título en inglés", a.TitleEn, MaxTitleLength)
            CheckLength(errors, name, "mensaje", a.Message, MaxMessageLength)
            CheckLength(errors, name, "mensaje en inglés", a.MessageEn, MaxMessageLength)
            If Not [Enum].IsDefined(a.Severity) Then errors.Add($"{name}: severidad no válida (info, warning o critical).")
            If Not [Enum].IsDefined(a.Display) Then errors.Add($"{name}: forma no válida (modal o banner).")
            If Not a.EndsAt.HasValue Then
                errors.Add($"{name}: falta endsAt (fecha y hora en que deja de mostrarse).")
            ElseIf a.StartsAt.HasValue Then
                If a.EndsAt.Value <= a.StartsAt.Value Then errors.Add($"{name}: endsAt debe ser posterior a startsAt.")
                If a.EndsAt.Value - a.StartsAt.Value > MaxDuration Then errors.Add($"{name}: no puede mostrarse más de {MaxDuration.TotalDays:0} días.")
            End If
            Dim targets = If(a.Targets, New List(Of String)())
            If targets.Count > MaxTargets Then errors.Add($"{name}: máximo {MaxTargets} destinos.")
            If targets.Any(Function(t) String.IsNullOrWhiteSpace(t)) Then errors.Add($"{name}: hay un destino vacío.")
            Return errors
        End Function

        ''' <summary>Problems of the whole feed (repeated ids, too many announcements) plus each announcement's.</summary>
        Public Shared Function Validate(feed As AnnouncementFeed) As IReadOnlyList(Of String)
            Dim errors As New List(Of String)()
            If feed Is Nothing Then
                errors.Add("El archivo no tiene anuncios.")
                Return errors
            End If
            Dim list = If(feed.Announcements, New List(Of Announcement)())
            If list.Count > MaxAnnouncements Then errors.Add($"Máximo {MaxAnnouncements} anuncios por archivo (hay {list.Count}).")
            For Each repeated In list.Where(Function(a) a IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(a.Id)).
                                     GroupBy(Function(a) a.Id, StringComparer.OrdinalIgnoreCase).Where(Function(g) g.Count() > 1)
                errors.Add($"El id «{repeated.Key}» está repetido.")
            Next
            For Each a In list
                errors.AddRange(Validate(a))
            Next
            Return errors
        End Function

        Private Shared Sub CheckLength(errors As List(Of String), name As String, what As String, value As String, max As Integer)
            If value IsNot Nothing AndAlso value.Length > max Then errors.Add($"{name}: el {what} tiene {value.Length} caracteres (máximo {max}).")
        End Sub

    End Class

End Namespace
