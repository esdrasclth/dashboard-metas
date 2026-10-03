Imports System.Globalization
Imports System.Windows.Media.Animation
Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Formatting

Namespace Controls

    ''' <summary>
    ''' Daily value chart: nice Y axis, band on the last closed day, bars coloured by goal attainment, value
    ''' on each bar, dashed goal line with its label in the right margin (never collides with the bars).
    ''' Drawn by hand so it looks the same on any PC and scales with the 1920x1080 design of the screen.
    ''' When the data changes the bars grow from their previous height.
    ''' </summary>
    Public NotInheritable Class DailyBarChart
        Inherits FrameworkElement

        Private Shared ReadOnly Typeface As New Typeface(New FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
        Private Shared ReadOnly TypefaceBold As New Typeface(New FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal)
        Private Shared ReadOnly TypefaceHeavy As New Typeface(New FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal)

        Private Shared ReadOnly InkBrush As Brush = Frozen(New SolidColorBrush(Color.FromRgb(14, 30, 70)))
        Private Shared ReadOnly MutedBrush As Brush = Frozen(New SolidColorBrush(Color.FromRgb(96, 110, 136)))
        Private Shared ReadOnly TealBrush As Brush = Frozen(New SolidColorBrush(Color.FromRgb(0, 163, 150)))
        Private Shared ReadOnly OrangeBrush As Brush = Frozen(New SolidColorBrush(Color.FromRgb(246, 160, 40)))
        Private Shared ReadOnly GreyBrush As Brush = Frozen(New SolidColorBrush(Color.FromRgb(165, 174, 190)))
        Private Shared ReadOnly BandBrush As Brush = Frozen(New SolidColorBrush(Color.FromRgb(229, 238, 252)))
        Private Shared ReadOnly GoalBrush As Brush = Frozen(New SolidColorBrush(Color.FromRgb(28, 45, 150)))
        Private Shared ReadOnly GridPen As Pen = FrozenPen(New Pen(Frozen(New SolidColorBrush(Color.FromRgb(231, 235, 242))), 1))
        Private Shared ReadOnly AxisPen As Pen = FrozenPen(New Pen(Frozen(New SolidColorBrush(Color.FromRgb(150, 160, 180))), 1.5))
        ' Projection of today: dashed outline + a light opaque wash (opaque so labels can sit on it)
        Private Shared ReadOnly ProjectionMetFill As Brush = Frozen(New SolidColorBrush(Color.FromRgb(217, 241, 239)))
        Private Shared ReadOnly ProjectionBelowFill As Brush = Frozen(New SolidColorBrush(Color.FromRgb(254, 241, 223)))
        Private Shared ReadOnly ProjectionMetPen As Pen = FrozenPen(New Pen(TealBrush, 2) With {.DashStyle = New DashStyle({3.0, 2.0}, 0)})
        Private Shared ReadOnly ProjectionBelowPen As Pen = FrozenPen(New Pen(OrangeBrush, 2) With {.DashStyle = New DashStyle({3.0, 2.0}, 0)})
        Private Shared ReadOnly GoalPen As Pen = FrozenPen(New Pen(GoalBrush, 2.5) With {.DashStyle = New DashStyle({4.0, 3.0}, 0), .DashCap = PenLineCap.Flat})

        ' Margins of the plot (design units: the whole screen is a 1920x1080 design scaled by a Viewbox)
        Private Const AxisWidth As Double = 84
        Private Const GoalLabelWidth As Double = 116
        Private Const TopRoom As Double = 64
        Private Const DayLabelsHeight As Double = 58

        Private _from As New Dictionary(Of Date, Double)()
        Private _hoverIndex As Integer = -1

        Public Shared ReadOnly SnapshotProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(Snapshot), GetType(DashboardSnapshot), GetType(DailyBarChart),
            New FrameworkPropertyMetadata(Nothing, FrameworkPropertyMetadataOptions.AffectsRender, AddressOf OnSnapshotChanged))

        Public Shared ReadOnly EnglishProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(English), GetType(Boolean), GetType(DailyBarChart),
            New FrameworkPropertyMetadata(False, FrameworkPropertyMetadataOptions.AffectsRender))

        Public Shared ReadOnly ProgressProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(Progress), GetType(Double), GetType(DailyBarChart),
            New FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender))

        Public Property Snapshot As DashboardSnapshot
            Get
                Return DirectCast(GetValue(SnapshotProperty), DashboardSnapshot)
            End Get
            Set(value As DashboardSnapshot)
                SetValue(SnapshotProperty, value)
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
        End Sub

        Private Shared Sub OnSnapshotChanged(d As DependencyObject, e As DependencyPropertyChangedEventArgs)
            Dim chart = DirectCast(d, DailyBarChart)
            Dim old = TryCast(e.OldValue, DashboardSnapshot)
            ' Start from what is on screen right now (also mid-animation)
            Dim from As New Dictionary(Of Date, Double)()
            If old IsNot Nothing Then
                For Each bar In old.Days
                    from(bar.Date) = chart.Displayed(bar)
                Next
            End If
            chart._from = from
            chart._hoverIndex = -1
            chart.ToolTip = Nothing
            If SystemParameters.ClientAreaAnimation Then
                chart.BeginAnimation(ProgressProperty, New DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(750)) With {
                    .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseOut}})
            Else
                chart.BeginAnimation(ProgressProperty, Nothing)
                chart.Progress = 1
            End If
        End Sub

        Private Function Displayed(bar As DayBar) As Double
            Dim start As Double = 0
            _from.TryGetValue(bar.Date, start)
            Return start + (CDbl(bar.Value) - start) * Progress
        End Function

        Private Function PlotRect() As Rect
            Return New Rect(AxisWidth, TopRoom,
                            Math.Max(10, ActualWidth - AxisWidth - GoalLabelWidth),
                            Math.Max(10, ActualHeight - TopRoom - DayLabelsHeight))
        End Function

        Protected Overrides Sub OnRender(dc As DrawingContext)
            MyBase.OnRender(dc)
            ' Transparent background so the whole area receives the mouse (tooltip)
            dc.DrawRectangle(Brushes.Transparent, Nothing, New Rect(0, 0, ActualWidth, ActualHeight))
            Dim snap = Snapshot
            If snap Is Nothing OrElse snap.Days.Count = 0 OrElse ActualWidth < 100 OrElse ActualHeight < 100 Then Return

            Dim texts As New Texts(English)
            Dim dpi = VisualTreeHelper.GetDpi(Me).PixelsPerDip
            Dim plot = PlotRect()
            Dim baseY = plot.Bottom
            Dim n = snap.Days.Count
            Dim slot = plot.Width / n
            Dim axisMax = If(snap.AxisMax > 0, snap.AxisMax, 1.0)

            ' ---- Band on the last closed day
            Dim bandX = plot.Left + snap.LastClosedIndex * slot
            dc.DrawRoundedRectangle(BandBrush, Nothing, New Rect(bandX + slot * 0.04, plot.Top - 18, slot * 0.92, plot.Height + 18 + DayLabelsHeight - 4), 4, 4)

            ' ---- Grid and Y axis
            For k = 0 To snap.GridLines
                Dim y = baseY - k / snap.GridLines * plot.Height
                dc.DrawLine(If(k = 0, AxisPen, GridPen), New Point(plot.Left, y), New Point(plot.Right, y))
                Dim label = Text(Money.Abbreviated(snap.AxisStep * k, axis:=True), 16, Typeface, MutedBrush, dpi)
                dc.DrawText(label, New Point(plot.Left - 14 - label.Width, y - label.Height / 2))
            Next

            ' ---- Bars, values and day labels
            Dim barWidth = Math.Min(slot * 0.6, 104)
            Dim valueSize = Math.Min(21, Math.Max(10, slot * 0.2))
            ' Labels are drawn after the goal line, on a halo of the background, so the line never strikes them
            Dim labels As New List(Of (Text As FormattedText, At As Point, Halo As Brush))()
            For i = 0 To n - 1
                Dim bar = snap.Days(i)
                Dim x = plot.Left + i * slot + (slot - barWidth) / 2
                Dim h = Math.Max(3, Math.Min(plot.Height + TopRoom * 0.4, Displayed(bar) / axisMax * plot.Height))
                Dim fill = If(bar.State = BarState.GoalMet, TealBrush, If(bar.State = BarState.InProgress, GreyBrush, OrangeBrush))
                If i = _hoverIndex Then fill = Lighter(fill)
                Dim halo = If(i = snap.LastClosedIndex, BandBrush, Brushes.White)

                ' Today: where the shift closes at the current pace (grows with the bars)
                Dim projection = bar.IsToday AndAlso snap.HasProjection AndAlso snap.Projection > bar.Value
                Dim projectionTop = 0.0
                If projection Then
                    Dim reaches = snap.Goal > 0D AndAlso snap.Projection >= snap.Goal
                    Dim fullH = Math.Min(plot.Height + TopRoom * 0.4, CDbl(snap.Projection) / axisMax * plot.Height)
                    Dim projH = h + Math.Max(0, fullH - h) * Progress
                    projectionTop = baseY - projH
                    Dim r = Math.Min(4, barWidth / 4)
                    dc.DrawGeometry(If(reaches, ProjectionMetFill, ProjectionBelowFill), If(reaches, ProjectionMetPen, ProjectionBelowPen),
                                    TopRoundedBar(x + 1, baseY - h + r, barWidth - 2, projH - h + r, r))
                    halo = If(reaches, ProjectionMetFill, ProjectionBelowFill)
                End If
                dc.DrawGeometry(fill, Nothing, TopRoundedBar(x, baseY, barWidth, h, Math.Min(4, barWidth / 4)))

                ' Value on the cap (shrinks to fit the slot, never clipped)
                Dim value = Text(Money.Abbreviated(Displayed(bar), axis:=False, compact:=snap.Compact), valueSize, TypefaceBold, InkBrush, dpi)
                FitWidth(value, valueSize, slot * 0.96)
                Dim valueY = baseY - h - value.Height - 4
                labels.Add((value, New Point(x + barWidth / 2 - value.Width / 2, valueY), halo))

                If projection Then
                    Dim projSize = Math.Min(16, valueSize * 0.82)
                    Dim projLabel = Text(texts.L("Proy. ", "Proj. ") & Money.Rounded(snap.Projection), projSize, TypefaceBold, MutedBrush, dpi)
                    FitWidth(projLabel, projSize, slot * 1.1)
                    ' Above the dashed box; if the value label is already there, above the value label
                    Dim projY = Math.Min(projectionTop - projLabel.Height - 2, valueY - projLabel.Height)
                    labels.Add((projLabel, New Point(x + barWidth / 2 - projLabel.Width / 2, projY), Brushes.White))
                ElseIf bar.IsToday Then
                    Dim inProgressSize = Math.Min(15, valueSize * 0.8)
                    Dim inProgress = Text(texts.L("En curso", "In progress"), inProgressSize, Typeface, MutedBrush, dpi)
                    FitWidth(inProgress, inProgressSize, slot * 1.1)
                    labels.Add((inProgress, New Point(x + barWidth / 2 - inProgress.Width / 2, valueY - inProgress.Height + 2), halo))
                End If

                Dim dayBrush = If(bar.IsToday, InkBrush, MutedBrush)
                Dim dayFace = If(bar.IsToday, TypefaceBold, Typeface)
                Dim daySize = Math.Min(17, Math.Max(10, slot * 0.2))
                Dim dayName = Text(texts.DayShort(bar.Date), daySize, dayFace, dayBrush, dpi)
                Dim dayDate = Text(texts.DayMonth(bar.Date), daySize, dayFace, dayBrush, dpi)
                FitWidth(dayDate, daySize, slot * 0.96)
                Dim cx = plot.Left + i * slot + slot / 2
                dc.DrawText(dayName, New Point(cx - dayName.Width / 2, baseY + 8))
                dc.DrawText(dayDate, New Point(cx - dayDate.Width / 2, baseY + 8 + dayName.Height))
            Next

            ' ---- Goal line, label in the right margin
            If snap.Goal > 0D Then
                Dim goalY = baseY - CDbl(snap.Goal) / axisMax * plot.Height
                dc.DrawLine(GoalPen, New Point(plot.Left, goalY), New Point(plot.Right + 14, goalY))
                Dim title = Text(texts.L("META", "GOAL"), 14, TypefaceHeavy, GoalBrush, dpi)
                Dim amount = Text(Money.Abbreviated(CDbl(snap.Goal), axis:=True), 19, TypefaceHeavy, GoalBrush, dpi)
                FitWidth(amount, 19, GoalLabelWidth - 26)
                Dim top = goalY - (title.Height + amount.Height) / 2
                dc.DrawText(title, New Point(plot.Right + 24, top))
                dc.DrawText(amount, New Point(plot.Right + 24, top + title.Height - 2))
            End If

            For Each label In labels
                dc.DrawRoundedRectangle(label.Halo, Nothing, New Rect(label.At.X - 4, label.At.Y + 1, label.Text.Width + 8, label.Text.Height - 2), 3, 3)
                dc.DrawText(label.Text, label.At)
            Next
        End Sub

        ''' <summary>Square at the baseline, rounded data end.</summary>
        Private Shared Function TopRoundedBar(x As Double, baseY As Double, width As Double, height As Double, radius As Double) As Geometry
            Dim r = Math.Max(0, Math.Min(radius, Math.Min(width / 2, height)))
            Dim top = baseY - height
            Dim geometry As New StreamGeometry()
            Using ctx = geometry.Open()
                ctx.BeginFigure(New Point(x, baseY), True, True)
                ctx.LineTo(New Point(x, top + r), False, False)
                ctx.ArcTo(New Point(x + r, top), New Size(r, r), 0, False, SweepDirection.Clockwise, False, False)
                ctx.LineTo(New Point(x + width - r, top), False, False)
                ctx.ArcTo(New Point(x + width, top + r), New Size(r, r), 0, False, SweepDirection.Clockwise, False, False)
                ctx.LineTo(New Point(x + width, baseY), False, False)
            End Using
            geometry.Freeze()
            Return geometry
        End Function

        Private Shared Function Text(value As String, size As Double, face As Typeface, brush As Brush, pixelsPerDip As Double) As FormattedText
            Return New FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, pixelsPerDip)
        End Function

        Private Shared Sub FitWidth(text As FormattedText, size As Double, maxWidth As Double)
            If text.Width > maxWidth AndAlso maxWidth > 0 Then text.SetFontSize(Math.Max(7, size * maxWidth / text.Width))
        End Sub

        Private Shared Function Lighter(brush As Brush) As Brush
            Dim solid = TryCast(brush, SolidColorBrush)
            If solid Is Nothing Then Return brush
            Dim c = solid.Color
            Return Frozen(New SolidColorBrush(Color.FromRgb(CByte(c.R + (255 - c.R) \ 5), CByte(c.G + (255 - c.G) \ 5), CByte(c.B + (255 - c.B) \ 5))))
        End Function

        ' ---- Tooltip with the detail of the day under the mouse

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim snap = Snapshot
            If snap Is Nothing OrElse snap.Days.Count = 0 Then Return
            Dim plot = PlotRect()
            Dim p = e.GetPosition(Me)
            Dim index = -1
            If p.X >= plot.Left AndAlso p.X < plot.Right AndAlso p.Y >= 0 AndAlso p.Y <= ActualHeight Then
                index = Math.Min(snap.Days.Count - 1, CInt(Math.Floor((p.X - plot.Left) / (plot.Width / snap.Days.Count))))
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

        Private Function Describe(snap As DashboardSnapshot, index As Integer) As String
            Dim t As New Texts(English)
            Dim bar = snap.Days(index)
            Dim lines As New List(Of String) From {
                t.DayLong(bar.Date) & " " & t.DayMonth(bar.Date) & If(bar.IsToday, t.L(" (en curso)", " (in progress)"), ""),
                Money.Full(bar.Value) & Texts.Bullet & t.PiecesAndStyles(bar.Pieces, bar.Styles)}
            If snap.Goal > 0D Then lines.Add(Money.Attainment(bar.Value, snap.Goal) & t.L(" de la meta (", " of goal (") & Money.Full(snap.Goal) & ")")
            If bar.IsToday AndAlso snap.Pace <> PaceState.NoShift AndAlso snap.Pace <> PaceState.NotStarted Then
                lines.Add(t.L("Esperado a esta hora: ", "Expected by now: ") & Money.Full(snap.ExpectedNow))
            End If
            If bar.IsToday AndAlso snap.HasProjection Then
                lines.Add(t.L("Al ritmo actual cierra en ", "At this pace closes at ") & Money.Full(snap.Projection) & " (" & Money.Attainment(snap.Projection, snap.Goal) & ")")
            End If
            Return String.Join(Environment.NewLine, lines)
        End Function

        Private Shared Function Frozen(brush As Brush) As Brush
            brush.Freeze()
            Return brush
        End Function

        Private Shared Function FrozenPen(pen As Pen) As Pen
            pen.Freeze()
            Return pen
        End Function

    End Class

End Namespace
