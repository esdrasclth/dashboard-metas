Namespace Announcements

    ''' <summary>Where the app is running, to decide which announcements are for this PC.</summary>
    Public NotInheritable Class AnnouncementAudience
        Public Sub New(machineName As String, branch As String)
            Me.MachineName = If(machineName, String.Empty).Trim()
            Me.Branch = If(branch, String.Empty).Trim()
        End Sub
        Public ReadOnly Property MachineName As String
        Public ReadOnly Property Branch As String
    End Class

    ''' <summary>Which announcements of a feed must be on screen right now on this PC, most urgent first.</summary>
    Public NotInheritable Class AnnouncementSelector

        Private Sub New()
        End Sub

        Public Shared Function Active(feed As AnnouncementFeed, now As DateTimeOffset, audience As AnnouncementAudience,
                                      dismissed As IReadOnlyCollection(Of String)) As IReadOnlyList(Of Announcement)
            If feed Is Nothing OrElse feed.Announcements Is Nothing Then Return Array.Empty(Of Announcement)()
            Dim closed As New HashSet(Of String)(If(dismissed, Array.Empty(Of String)()), StringComparer.OrdinalIgnoreCase)
            Return feed.Announcements.
                Where(Function(a) a IsNot Nothing AndAlso IsInWindow(a, now) AndAlso IsFor(a, audience)).
                Where(Function(a) Not (a.Dismissible AndAlso closed.Contains(a.Id))).
                OrderByDescending(Function(a) a.Severity).
                ThenByDescending(Function(a) If(a.StartsAt, feed.IssuedAt)).
                ThenBy(Function(a) a.Id, StringComparer.Ordinal).
                ToList()
        End Function

        Public Shared Function IsInWindow(a As Announcement, now As DateTimeOffset) As Boolean
            If a.StartsAt.HasValue AndAlso now < a.StartsAt.Value Then Return False
            If a.EndsAt.HasValue AndAlso now >= a.EndsAt.Value Then Return False
            Return True
        End Function

        ''' <summary>"*" or nothing = every PC; "equipo:NAME" / "NAME" = that PC; "planta:027" = every PC of that plant.</summary>
        Public Shared Function IsFor(a As Announcement, audience As AnnouncementAudience) As Boolean
            Dim targets = If(a.Targets, New List(Of String)()).Where(Function(t) Not String.IsNullOrWhiteSpace(t)).Select(Function(t) t.Trim()).ToList()
            If targets.Count = 0 OrElse targets.Contains("*") Then Return True
            If audience Is Nothing Then Return False
            For Each target In targets
                Dim colon = target.IndexOf(":"c)
                Dim kind = If(colon > 0, target.Substring(0, colon).Trim().ToLowerInvariant(), "equipo")
                Dim value = If(colon > 0, target.Substring(colon + 1).Trim(), target)
                Select Case kind
                    Case "planta", "plant"
                        If Same(value, audience.Branch) Then Return True
                    Case "equipo", "pc", "machine"
                        If Same(value, audience.MachineName) Then Return True
                End Select
            Next
            Return False
        End Function

        Private Shared Function Same(a As String, b As String) As Boolean
            Return a.Length > 0 AndAlso String.Equals(a, b, StringComparison.OrdinalIgnoreCase)
        End Function

    End Class

End Namespace
