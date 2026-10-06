Imports DashboardMetas.Core.Models

Namespace Dashboard

    ''' <summary>One product line of a status bar (a stacked segment).</summary>
    Public NotInheritable Class FloatSegment
        Public Property Line As FloatLine
        Public Property Value As Decimal
        Public Property Pieces As Decimal
    End Class

    ''' <summary>One status of the float (a bar of the "by status" chart and a card under it).</summary>
    Public NotInheritable Class FloatStatusGroup
        Public Property Code As String = String.Empty
        Public Property Value As Decimal
        Public Property Pieces As Decimal
        Public Property Styles As Integer
        Public Property Orders As Integer
        ''' <summary>0 to 1 of the total float value.</summary>
        Public Property Share As Double
        ''' <summary>In the order of <see cref="FloatSnapshot.Lines"/> (same colours top to bottom in every bar).</summary>
        Public Property Segments As IReadOnlyList(Of FloatSegment) = Array.Empty(Of FloatSegment)()
    End Class

    ''' <summary>
    ''' A product line. The biggest ones keep their own colour (<see cref="Slot"/> 0…MaxLines-1); the rest,
    ''' and styles without a line, fold into "Otras" (<see cref="IsOther"/>).
    ''' </summary>
    Public NotInheritable Class FloatLine
        ''' <summary>SRSORT; empty for "Otras".</summary>
        Public Property Name As String = String.Empty
        Public Property IsOther As Boolean
        ''' <summary>Colour slot: 0…MaxLines-1 by name (stable while the same lines lead), MaxLines for "Otras".</summary>
        Public Property Slot As Integer
        Public Property Value As Decimal
        Public Property Pieces As Decimal
        Public Property Styles As Integer
        Public Property Share As Double
        ''' <summary>Lines folded into "Otras" (0 for a named line).</summary>
        Public Property FoldedLines As Integer
        ''' <summary>Value per status, in status order.</summary>
        Public Property ByStatus As IReadOnlyList(Of (Code As String, Value As Decimal, Pieces As Decimal)) = Array.Empty(Of (String, Decimal, Decimal))()
    End Class

    ''' <summary>Everything the float screen shows, computed from the rows of the float query.</summary>
    Public NotInheritable Class FloatSnapshot
        Public Property TotalValue As Decimal
        Public Property TotalPieces As Decimal
        ''' <summary>Distinct base styles in the float.</summary>
        Public Property Styles As Integer
        Public Property Orders As Integer
        Public Property Statuses As IReadOnlyList(Of FloatStatusGroup) = Array.Empty(Of FloatStatusGroup)()
        ''' <summary>Named lines by value (largest first), then "Otras" when there is anything to fold.</summary>
        Public Property Lines As IReadOnlyList(Of FloatLine) = Array.Empty(Of FloatLine)()
        ''' <summary>Styles with pieces but no W01 price (they add pieces, not value).</summary>
        Public Property UnpricedStyles As Integer
        Public Property UnpricedPieces As Decimal
        ''' <summary>Styles whose product line was not found in F58C3120 (shown inside "Otras").</summary>
        Public Property UnclassifiedStyles As Integer
        Public Property GridLines As Integer = DashboardCalculator.GridLines
        Public Property StatusAxisStep As Double
        Public Property StatusAxisMax As Double
        Public Property LineAxisStep As Double
        Public Property LineAxisMax As Double

        Public ReadOnly Property IsEmpty As Boolean
            Get
                Return TotalPieces = 0D AndAlso TotalValue = 0D
            End Get
        End Property

        ''' <summary>The status with the most value (Nothing when the float is empty).</summary>
        Public Function Largest() As FloatStatusGroup
            Return Statuses.Where(Function(s) s.Value > 0D).OrderByDescending(Function(s) s.Value).FirstOrDefault()
        End Function
    End Class

    ''' <summary>Groups the float rows by status and product line (the screen and the tests use the same numbers).</summary>
    Public NotInheritable Class FloatCalculator

        ''' <summary>Lines with their own colour; the rest go to "Otras" (more hues would not be told apart).</summary>
        Public Const MaxLines As Integer = 5

        Private Sub New()
        End Sub

        ''' <param name="statusOrder">Configured statuses: they always get a bar, even with nothing in them.</param>
        Public Shared Function Build(items As IEnumerable(Of FloatItem), statusOrder As IEnumerable(Of String)) As FloatSnapshot
            Dim rows = If(items, Enumerable.Empty(Of FloatItem)()).Where(Function(i) i IsNot Nothing).
                Select(Function(i) New FloatItem With {
                    .Status = If(i.Status, String.Empty).Trim().ToUpperInvariant(),
                    .Style = If(i.Style, String.Empty).Trim(),
                    .ProductLine = If(i.ProductLine, String.Empty).Trim().ToUpperInvariant(),
                    .Pieces = i.Pieces, .Value = i.Value, .Orders = i.Orders}).ToList()

            Dim snap As New FloatSnapshot With {
                .TotalValue = rows.Sum(Function(r) r.Value),
                .TotalPieces = rows.Sum(Function(r) r.Pieces),
                .Styles = rows.Select(Function(r) r.Style).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                .Orders = rows.Sum(Function(r) r.Orders)}

            ' A style counts once even if it sits in several statuses
            Dim byStyle = rows.GroupBy(Function(r) r.Style, StringComparer.OrdinalIgnoreCase).ToList()
            Dim unpriced = byStyle.Where(Function(g) g.Sum(Function(r) r.Pieces) > 0D AndAlso g.Sum(Function(r) r.Value) = 0D).ToList()
            snap.UnpricedStyles = unpriced.Count
            snap.UnpricedPieces = unpriced.Sum(Function(g) g.Sum(Function(r) r.Pieces))
            snap.UnclassifiedStyles = byStyle.Where(Function(g) g.All(Function(r) r.ProductLine.Length = 0)).Count()

            ' ---- Lines: the biggest keep a colour, the rest fold into "Otras"
            Dim named = rows.Where(Function(r) r.ProductLine.Length > 0).GroupBy(Function(r) r.ProductLine, StringComparer.OrdinalIgnoreCase).
                OrderByDescending(Function(g) g.Sum(Function(r) r.Value)).ThenByDescending(Function(g) g.Sum(Function(r) r.Pieces)).
                ThenBy(Function(g) g.Key, StringComparer.Ordinal).ToList()
            Dim kept = named.Take(MaxLines).ToList()
            Dim keptNames As New HashSet(Of String)(kept.Select(Function(g) g.Key), StringComparer.OrdinalIgnoreCase)
            ' Colour by name, not by rank: two lines swapping places do not swap colours
            Dim slots = kept.Select(Function(g) g.Key).OrderBy(Function(k) k, StringComparer.Ordinal).
                Select(Function(k, i) (k, i)).ToDictionary(Function(x) x.k, Function(x) x.i, StringComparer.OrdinalIgnoreCase)

            Dim statusCodes = OrderStatuses(statusOrder, rows)
            Dim lines As New List(Of FloatLine)()
            For Each g In kept
                lines.Add(MakeLine(g.Key, False, slots(g.Key), g.ToList(), statusCodes, snap.TotalValue))
            Next
            Dim rest = rows.Where(Function(r) Not keptNames.Contains(r.ProductLine)).ToList()
            If rest.Count > 0 Then
                Dim other = MakeLine(String.Empty, True, MaxLines, rest, statusCodes, snap.TotalValue)
                other.FoldedLines = named.Count - kept.Count
                lines.Add(other)
            End If
            snap.Lines = lines

            ' ---- Statuses, each with one segment per line (same order in every bar)
            Dim groups As New List(Of FloatStatusGroup)()
            For Each code In statusCodes
                Dim inStatus = rows.Where(Function(r) r.Status = code).ToList()
                Dim group As New FloatStatusGroup With {
                    .Code = code,
                    .Value = inStatus.Sum(Function(r) r.Value),
                    .Pieces = inStatus.Sum(Function(r) r.Pieces),
                    .Styles = inStatus.Select(Function(r) r.Style).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    .Orders = inStatus.Sum(Function(r) r.Orders)}
                group.Share = If(snap.TotalValue > 0D, CDbl(group.Value / snap.TotalValue), 0.0)
                group.Segments = lines.Select(Function(line) New FloatSegment With {
                    .Line = line,
                    .Value = inStatus.Where(Function(r) BelongsTo(r, line, keptNames)).Sum(Function(r) r.Value),
                    .Pieces = inStatus.Where(Function(r) BelongsTo(r, line, keptNames)).Sum(Function(r) r.Pieces)}).ToList()
                groups.Add(group)
            Next
            snap.Statuses = groups

            Dim statusMax = If(groups.Count = 0, 0.0, groups.Max(Function(s) CDbl(s.Value)))
            snap.StatusAxisStep = DashboardCalculator.NiceStep(Math.Max(1.0, statusMax * 1.1) / snap.GridLines)
            snap.StatusAxisMax = snap.StatusAxisStep * snap.GridLines
            Dim lineMax = If(lines.Count = 0, 0.0, lines.Max(Function(l) CDbl(l.Value)))
            snap.LineAxisStep = DashboardCalculator.NiceStep(Math.Max(1.0, lineMax * 1.1) / snap.GridLines)
            snap.LineAxisMax = snap.LineAxisStep * snap.GridLines
            Return snap
        End Function

        ''' <summary>Configured statuses first (in their order), then any other status JDE returned.</summary>
        Private Shared Function OrderStatuses(statusOrder As IEnumerable(Of String), rows As IEnumerable(Of FloatItem)) As IReadOnlyList(Of String)
            Dim list As New List(Of String)()
            For Each code In If(statusOrder, Enumerable.Empty(Of String)())
                Dim c = If(code, String.Empty).Trim().ToUpperInvariant()
                If c.Length > 0 AndAlso Not list.Contains(c) Then list.Add(c)
            Next
            For Each code In rows.Select(Function(r) r.Status).Where(Function(c) c.Length > 0).Distinct().OrderBy(Function(c) c, StringComparer.Ordinal)
                If Not list.Contains(code) Then list.Add(code)
            Next
            Return list
        End Function

        Private Shared Function BelongsTo(row As FloatItem, line As FloatLine, keptNames As HashSet(Of String)) As Boolean
            Return If(line.IsOther, Not keptNames.Contains(row.ProductLine), String.Equals(row.ProductLine, line.Name, StringComparison.OrdinalIgnoreCase))
        End Function

        Private Shared Function MakeLine(name As String, isOther As Boolean, slot As Integer, rows As List(Of FloatItem), statusCodes As IReadOnlyList(Of String), total As Decimal) As FloatLine
            Dim value = rows.Sum(Function(r) r.Value)
            Return New FloatLine With {
                .Name = name, .IsOther = isOther, .Slot = slot, .Value = value,
                .Pieces = rows.Sum(Function(r) r.Pieces),
                .Styles = rows.Select(Function(r) r.Style).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                .Share = If(total > 0D, CDbl(value / total), 0.0),
                .ByStatus = statusCodes.Select(Function(c) (c, rows.Where(Function(r) r.Status = c).Sum(Function(r) r.Value), rows.Where(Function(r) r.Status = c).Sum(Function(r) r.Pieces))).ToList()}
        End Function

    End Class

End Namespace
