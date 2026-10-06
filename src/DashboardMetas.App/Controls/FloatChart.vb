Imports System.Windows.Media.Animation
Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Formatting

Namespace Controls

    ''' <summary>
    ''' Custom float chart, drawn like the daily chart (nice Y axis, rounded data ends, value on each bar with a
    ''' halo, tooltip, bars that grow from their previous height). Two views of the same snapshot:
    ''' «por estatus» = one bar per status stacked by product line, «por línea» = one bar per product line in
    ''' its own colour. A line keeps the same colour in both views and in the legend.
    ''' </summary>
    Public NotInheritable Class FloatChart
        Inherits FrameworkElement

        Private Const AxisWidth As Double = 84
        Private Const RightRoom As Double = 24
        Private Const TopRoom As Double = 64
        Private Const LabelsHeight As Double = 62
        ''' <summary>Surface gap between stacked segments.</summary>
        Private Const SegmentGap As Double = 2

        Private _from As New Dictionary(Of String, Double)(StringComparer.Ordinal)
        Private _hoverIndex As Integer = -1
        Private _appearing As Boolean

        Public Shared ReadOnly SnapshotProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(Snapshot), GetType(FloatSnapshot), GetType(FloatChart),
            New FrameworkPropertyMetadata(Nothing, FrameworkPropertyMetadataOptions.AffectsRender, AddressOf OnDataChanged))

        Public Shared ReadOnly ByLineProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(ByLine), GetType(Boolean), GetType(FloatChart),
            New FrameworkPropertyMetadata(False, FrameworkPropertyMetadataOptions.AffectsRender, AddressOf OnDataChanged))

        Public Shared ReadOnly EnglishProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(English), GetType(Boolean), GetType(FloatChart),
            New FrameworkPropertyMetadata(False, FrameworkPropertyMetadataOptions.AffectsRender))

        Public Shared ReadOnly ProgressProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(Progress), GetType(Double), GetType(FloatChart),
            New FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender))

        Public Property Snapshot As FloatSnapshot
            Get
                Return DirectCast(GetValue(SnapshotProperty), FloatSnapshot)
            End Get
            Set(value As FloatSnapshot)
                SetValue(SnapshotProperty, value)
            End Set
        End Property

        ''' <summary>False = bars per status (stacked by line), True = bars per product line.</summary>
        Public Property ByLine As Boolean
            Get
                Return CBool(GetValue(ByLineProperty))
            End Get
            Set(value As Boolean)
                SetValue(ByLineProperty, value)
            End Set
        End Property

        Public Property English As Boolean
            Get
                Return CBool(GetValue(EnglishProperty))
            End Get
            Set(value As Boolean)
                SetValue(EnglishProperty, value)
            End Set
        End Property

        ''' <summary>0 → 1 while the bars move from their previous height to the new one.</summary>
        Public Property Progress As Double
            Get
                Return CDbl(GetValue(ProgressProperty))
            End Get
            Set(value As Double)
                SetValue(ProgressProperty, value)
            End Set
        End Property

        Public Sub New()
            SnapsToDevicePixels = True
            ToolTipService.SetInitialShowDelay(Me, 150)
            ToolTipService.SetBetweenShowDelay(Me, 0)
            AddHandler IsVisibleChanged, AddressOf OnShown
        End Sub

        ''' <summary>
        ''' The float is a photo that usually has not changed when the rotation comes back to it, so no new data
        ''' animates the bars: grow them from zero each time the screen appears, like the area charts do.
        ''' </summary>
        Private Sub OnShown(sender As Object, e As DependencyPropertyChangedEventArgs)
            If Not CBool(e.NewValue) Then Return
            _from = New Dictionary(Of String, Double)(StringComparer.Ordinal)
            Animate()
            ' The view model may still set a newer snapshot while the screen comes up: that one grows from zero
            ' too, instead of morphing from the old, nearly equal bars (which looks static)
            _appearing = True
            Dispatcher.BeginInvoke(Threading.DispatcherPriority.Loaded, New Action(Sub() _appearing = False))
        End Sub

        Private Sub Animate()
            _hoverIndex = -1
            ToolTip = Nothing
            If SystemParameters.ClientAreaAnimation Then
                BeginAnimation(ProgressProperty, New DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(750)) With {
                    .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseOut}})
            Else
                BeginAnimation(ProgressProperty, Nothing)
                Progress = 1
            End If
        End Sub

        ' ---- What is drawn: one bar per status or per line, with its segments

        Private NotInheritable Class Bar
            Public Key As String = String.Empty
            Public Title As String = String.Empty
            Public Caption As String = String.Empty
            Public Total As Double
            Public Segments As New List(Of (Slot As Integer, Value As Double))()
        End Class

        Private Function Bars(snap As FloatSnapshot, byLine As Boolean, t As Texts) As List(Of Bar)
            Dim list As New List(Of Bar)()
            If byLine Then
                For Each line In snap.Lines
                    Dim bar As New Bar With {.Key = "L:" & If(line.IsOther, "*", line.Name), .Title = LineName(line, t), .Total = CDbl(line.Value),
                                             .Caption = Money.Percent(line.Share) & Texts.Bullet & line.Styles.ToString(Globalization.CultureInfo.InvariantCulture) & t.L(" estilos", " styles")}
                    bar.Segments.Add((line.Slot, CDbl(line.Value)))
                    list.Add(bar)
                Next
            Else
                For Each status In snap.Statuses
                    Dim bar As New Bar With {.Key = "S:" & status.Code, .Title = status.Code, .Total = CDbl(status.Value),
                                             .Caption = Money.Percent(status.Share) & Texts.Bullet & Money.Count(status.Pieces) & t.L(" piezas", " pieces")}
                    For Each segment In status.Segments
                        If segment.Value > 0D Then bar.Segments.Add((segment.Line.Slot, CDbl(segment.Value)))
                    Next
                    list.Add(bar)
                Next
            End If
            Return list
        End Function

        Friend Shared Function LineName(line As FloatLine, t As Texts) As String
            If Not line.IsOther Then Return line.Name
            Return If(line.FoldedLines > 0, t.L("Otras (", "Other (") & line.FoldedLines.ToString(Globalization.CultureInfo.InvariantCulture) & ")", t.L("Sin línea", "No line"))
        End Function

        Private Shared Sub OnDataChanged(d As DependencyObject, e As DependencyPropertyChangedEventArgs)
            Dim chart = DirectCast(d, FloatChart)
            ' Start from what is on screen right now (also mid-animation); switching view starts from zero
            Dim from As New Dictionary(Of String, Double)(StringComparer.Ordinal)
            Dim old = If(e.Property Is SnapshotProperty, TryCast(e.OldValue, FloatSnapshot), Nothing)
            If old IsNot Nothing AndAlso chart.IsVisible AndAlso Not chart._appearing Then
                For Each bar In chart.Bars(old, chart.ByLine, New Texts(chart.English))
                    from(bar.Key) = chart.Displayed(bar)
                Next
            End If
            chart._from = from
            chart.Animate()
        End Sub

        Private Function Displayed(bar As Bar) As Double
            Dim start As Double = 0
            _from.TryGetValue(bar.Key, start)
            Return start + (bar.Total - start) * Progress
        End Function

        Private Function PlotRect() As Rect
            Return New Rect(AxisWidth, TopRoom, Math.Max(10, ActualWidth - AxisWidth - RightRoom), Math.Max(10, ActualHeight - TopRoom - LabelsHeight))
        End Function

        Protected Overrides Sub OnRender(dc As DrawingContext)
            MyBase.OnRender(dc)
            dc.DrawRectangle(Brushes.Transparent, Nothing, New Rect(0, 0, ActualWidth, ActualHeight))
            Dim snap = Snapshot
            If snap Is Nothing OrElse ActualWidth < 100 OrElse ActualHeight < 100 Then Return

            Dim t As New Texts(English)
            Dim dpi = VisualTreeHelper.GetDpi(Me).PixelsPerDip
            If snap.IsEmpty Then
                Dim empty = ChartPalette.Text(t.L("No hay órdenes en el Float", "There are no orders in the float"), 24, ChartPalette.FaceBold, ChartPalette.Muted, dpi)
                dc.DrawText(empty, New Point((ActualWidth - empty.Width) / 2, (ActualHeight - empty.Height) / 2))
                Return
            End If

            Dim bars = Me.Bars(snap, ByLine, t)
            If bars.Count = 0 Then Return
            Dim plot = PlotRect()
            Dim baseY = plot.Bottom
            Dim slot = plot.Width / bars.Count
            Dim axisStep = If(ByLine, snap.LineAxisStep, snap.StatusAxisStep)
            Dim axisMax = If(ByLine, snap.LineAxisMax, snap.StatusAxisMax)
            If axisMax <= 0 Then axisMax = 1

            ' ---- Grid and Y axis
            For k = 0 To snap.GridLines
                Dim y = baseY - k / snap.GridLines * plot.Height
                dc.DrawLine(If(k = 0, ChartPalette.AxisPen, ChartPalette.GridPen), New Point(plot.Left, y), New Point(plot.Right, y))
                Dim label = ChartPalette.Text(Money.Abbreviated(axisStep * k, axis:=True), 16, ChartPalette.Face, ChartPalette.Muted, dpi)
                dc.DrawText(label, New Point(plot.Left - 14 - label.Width, y - label.Height / 2))
            Next

            Dim barWidth = Math.Min(slot * 0.56, 190)
            Dim valueSize = Math.Min(24, Math.Max(12, slot * 0.12))
            Dim labels As New List(Of (Text As FormattedText, At As Point))()
            For i = 0 To bars.Count - 1
                Dim bar = bars(i)
                Dim x = plot.Left + i * slot + (slot - barWidth) / 2
                Dim shown = Displayed(bar)
                Dim h = If(bar.Total > 0, Math.Max(3, Math.Min(plot.Height + TopRoom * 0.4, shown / axisMax * plot.Height)), 0.0)
                Dim scale = If(bar.Total > 0, h / bar.Total, 0.0)

                ' Segments bottom to top, separated by a thin gap of the surface; only the top one is rounded
                Dim y = baseY
                Dim drawable = bar.Segments.Where(Function(s) s.Value * scale >= 0.5).ToList()
                For s = 0 To drawable.Count - 1
                    Dim seg = drawable(s)
                    Dim segH = seg.Value * scale
                    Dim isTop = s = drawable.Count - 1
                    Dim gap = If(isTop, 0.0, Math.Min(SegmentGap, segH / 3))
                    Dim fill = ChartPalette.LineBrush(seg.Slot)
                    If i = _hoverIndex Then fill = Lighter(fill)
                    If isTop Then
                        dc.DrawGeometry(fill, Nothing, ChartPalette.TopRoundedBar(x, y, barWidth, segH, Math.Min(4, barWidth / 4)))
                    Else
                        dc.DrawRectangle(fill, Nothing, New Rect(x, y - segH + gap, barWidth, Math.Max(0, segH - gap)))
                    End If
                    ' Share of the bar inside the segment when it fits (stacked view only; the line view has it below)
                    If Not ByLine AndAlso bar.Total > 0 AndAlso segH >= 30 AndAlso barWidth >= 80 Then
                        Dim inside = ChartPalette.Text(Money.Percent(seg.Value / bar.Total), 15, ChartPalette.FaceBold, ChartPalette.OnLine(seg.Slot), dpi)
                        If inside.Width <= barWidth - 10 Then dc.DrawText(inside, New Point(x + (barWidth - inside.Width) / 2, y - segH / 2 - inside.Height / 2))
                    End If
                    y -= segH
                Next

                ' Value on the cap
                Dim value = ChartPalette.Text(Money.Abbreviated(shown, axis:=False), valueSize, ChartPalette.FaceBold, ChartPalette.Ink, dpi)
                ChartPalette.FitWidth(value, valueSize, slot * 0.96)
                labels.Add((value, New Point(x + barWidth / 2 - value.Width / 2, baseY - h - value.Height - 4)))

                ' Name and detail under the bar
                Dim cx = plot.Left + i * slot + slot / 2
                Dim titleSize = If(ByLine, Math.Min(18, Math.Max(11, slot * 0.1)), 20.0)
                Dim title = ChartPalette.Text(bar.Title, titleSize, ChartPalette.FaceBold, ChartPalette.Ink, dpi)
                ChartPalette.FitWidth(title, titleSize, slot * 0.96)
                Dim caption = ChartPalette.Text(bar.Caption, 15, ChartPalette.Face, ChartPalette.Muted, dpi)
                ChartPalette.FitWidth(caption, 15, slot * 0.96)
                dc.DrawText(title, New Point(cx - title.Width / 2, baseY + 8))
                dc.DrawText(caption, New Point(cx - caption.Width / 2, baseY + 10 + title.Height))
            Next

            For Each label In labels
                dc.DrawRoundedRectangle(Brushes.White, Nothing, New Rect(label.At.X - 4, label.At.Y + 1, label.Text.Width + 8, label.Text.Height - 2), 3, 3)
                dc.DrawText(label.Text, label.At)
            Next
        End Sub

        Private Shared Function Lighter(brush As Brush) As Brush
            Dim solid = TryCast(brush, SolidColorBrush)
            If solid Is Nothing Then Return brush
            Dim c = solid.Color
            Dim b As New SolidColorBrush(Color.FromRgb(CByte(c.R + (255 - c.R) \ 5), CByte(c.G + (255 - c.G) \ 5), CByte(c.B + (255 - c.B) \ 5)))
            b.Freeze()
            Return b
        End Function

        ' ---- Tooltip with the detail of the bar under the mouse

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim snap = Snapshot
            If snap Is Nothing OrElse snap.IsEmpty Then Return
            Dim count = If(ByLine, snap.Lines.Count, snap.Statuses.Count)
            If count = 0 Then Return
            Dim plot = PlotRect()
            Dim p = e.GetPosition(Me)
            Dim index = -1
            If p.X >= plot.Left AndAlso p.X < plot.Right AndAlso p.Y >= 0 AndAlso p.Y <= ActualHeight Then
                index = Math.Min(count - 1, CInt(Math.Floor((p.X - plot.Left) / (plot.Width / count))))
            End If
            If index = _hoverIndex Then Return
            _hoverIndex = index
            ToolTip = If(index < 0, Nothing, CType(Describe(snap, index), Object))
            InvalidateVisual()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As MouseEventArgs)
            MyBase.OnMouseLeave(e)
            _hoverIndex = -1
            ToolTip = Nothing
            InvalidateVisual()
        End Sub

        Private Function Describe(snap As FloatSnapshot, index As Integer) As String
            Dim t As New Texts(English)
            Dim lines As New List(Of String)()
            If ByLine Then
                Dim line = snap.Lines(index)
                lines.Add(LineName(line, t) & Texts.Bullet & Money.Percent(line.Share) & t.L(" del float", " of the float"))
                lines.Add(Money.Full(line.Value) & Texts.Bullet & t.PiecesAndStyles(line.Pieces, line.Styles))
                For Each s In line.ByStatus.Where(Function(x) x.Value > 0D OrElse x.Pieces > 0D)
                    lines.Add("  " & s.Code & ": " & Money.Full(s.Value) & Texts.Bullet & Money.Count(s.Pieces) & t.L(" piezas", " pieces"))
                Next
            Else
                Dim status = snap.Statuses(index)
                lines.Add(t.L("Estatus ", "Status ") & status.Code & Texts.Bullet & Money.Percent(status.Share) & t.L(" del float", " of the float"))
                lines.Add(Money.Full(status.Value) & Texts.Bullet & t.PiecesAndStyles(status.Pieces, status.Styles) & Texts.Bullet &
                          status.Orders.ToString(Globalization.CultureInfo.InvariantCulture) & t.L(" órdenes", " orders"))
                For Each segment In status.Segments.Where(Function(s) s.Value > 0D OrElse s.Pieces > 0D)
                    lines.Add("  " & LineName(segment.Line, t) & ": " & Money.Full(segment.Value) & " (" &
                              Money.Percent(If(status.Value > 0D, CDbl(segment.Value / status.Value), 0.0)) & ")")
                Next
            End If
            Return String.Join(Environment.NewLine, lines)
        End Function

    End Class

End Namespace
