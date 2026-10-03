Imports System.Windows.Media.Animation
Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Formatting

Namespace Controls

    ''' <summary>
    ''' Month to date on a single axis (accumulated US$): the actual accumulated line against the accumulated
    ''' goal (a straight dashed line over the working days) and, from today, the dashed projection to the end
    ''' of the month. Coloured teal when on track, orange when behind.
    ''' </summary>
    Public NotInheritable Class MonthChart
        Inherits FrameworkElement

        Private Const AxisWidth As Double = 96
        Private Const RightLabelWidth As Double = 150
        Private Const TopRoom As Double = 40
        Private Const DayLabelsHeight As Double = 58

        Private _hoverIndex As Integer = -1

        Public Shared ReadOnly SnapshotProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(Snapshot), GetType(MonthSnapshot), GetType(MonthChart),
            New FrameworkPropertyMetadata(Nothing, FrameworkPropertyMetadataOptions.AffectsRender, AddressOf OnSnapshotChanged))

        Public Shared ReadOnly EnglishProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(English), GetType(Boolean), GetType(MonthChart),
            New FrameworkPropertyMetadata(False, FrameworkPropertyMetadataOptions.AffectsRender))

        Public Shared ReadOnly ProgressProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(Progress), GetType(Double), GetType(MonthChart),
            New FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender))

        Public Property Snapshot As MonthSnapshot
            Get
                Return DirectCast(GetValue(SnapshotProperty), MonthSnapshot)
            End Get
            Set(value As MonthSnapshot)
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
            Dim chart = DirectCast(d, MonthChart)
            Dim old = TryCast(e.OldValue, MonthSnapshot)
            Dim fresh = TryCast(e.NewValue, MonthSnapshot)
            ' Draw the line in only when the month or the area changes, not on every minute repaint
            Dim same = old IsNot Nothing AndAlso fresh IsNot Nothing AndAlso old.MonthStart = fresh.MonthStart AndAlso old.MonthlyGoal = fresh.MonthlyGoal
            If same OrElse Not SystemParameters.ClientAreaAnimation Then
                chart.BeginAnimation(ProgressProperty, Nothing)
                chart.Progress = 1
            Else
                chart.BeginAnimation(ProgressProperty, New DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) With {
                    .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseOut}})
            End If
        End Sub

        Private Function PlotRect() As Rect
            Return New Rect(AxisWidth, TopRoom, Math.Max(10, ActualWidth - AxisWidth - RightLabelWidth), Math.Max(10, ActualHeight - TopRoom - DayLabelsHeight))
        End Function

        Protected Overrides Sub OnRender(dc As DrawingContext)
            MyBase.OnRender(dc)
            dc.DrawRectangle(Brushes.Transparent, Nothing, New Rect(0, 0, ActualWidth, ActualHeight))
            Dim snap = Snapshot
            If snap Is Nothing OrElse snap.Days.Count = 0 OrElse ActualWidth < 100 OrElse ActualHeight < 100 Then Return

            Dim t As New Texts(English)
            Dim dpi = VisualTreeHelper.GetDpi(Me).PixelsPerDip
            Dim plot = PlotRect()
            Dim baseY = plot.Bottom
            Dim n = snap.Days.Count
            Dim slot = plot.Width / n
            Dim axisMax = If(snap.AxisMax > 0, snap.AxisMax, 1.0)
            Dim y = Function(v As Double) baseY - v / axisMax * plot.Height
            Dim x = Function(i As Integer) plot.Left + (i + 0.5) * slot
            Dim todayIndex = snap.TodayIndex
            Dim lastPast = snap.LastPastIndex
            Dim onTrack = snap.IsGoalMet OrElse snap.IsOnTrack
            Dim lineBrush = If(onTrack, ChartPalette.Teal, ChartPalette.Orange)

            ' ---- Today's column
            If todayIndex >= 0 Then
                dc.DrawRoundedRectangle(ChartPalette.Band, Nothing, New Rect(plot.Left + todayIndex * slot + slot * 0.08, plot.Top - 10, slot * 0.84, plot.Height + 10 + DayLabelsHeight - 4), 4, 4)
            End If

            ' ---- Grid and Y axis
            For k = 0 To snap.GridLines
                Dim gy = baseY - k / snap.GridLines * plot.Height
                dc.DrawLine(If(k = 0, ChartPalette.AxisPen, ChartPalette.GridPen), New Point(plot.Left, gy), New Point(plot.Right, gy))
                Dim label = ChartPalette.Text(Money.Abbreviated(snap.AxisStep * k, axis:=True), 16, ChartPalette.Face, ChartPalette.Muted, dpi)
                dc.DrawText(label, New Point(plot.Left - 14 - label.Width, gy - label.Height / 2))
            Next

            ' ---- Accumulated goal: straight dashed line from 0 to the monthly goal
            Dim target As New StreamGeometry()
            Using ctx = target.Open()
                ctx.BeginFigure(New Point(plot.Left, baseY), False, False)
                For i = 0 To n - 1
                    ctx.LineTo(New Point(x(i), y(CDbl(snap.Days(i).Target))), True, True)
                Next
            End Using
            target.Freeze()
            dc.DrawGeometry(Nothing, ChartPalette.DashedPen(ChartPalette.Goal, 2.5), target)

            ' ---- Actual accumulated: area wash + line (grows with the animation)
            Dim p = Progress
            If lastPast >= 0 Then
                Dim points As New List(Of Point) From {New Point(plot.Left, baseY)}
                For i = 0 To lastPast
                    points.Add(New Point(x(i), y(CDbl(snap.Days(i).Cumulative) * p)))
                Next
                Dim area As New StreamGeometry()
                Using ctx = area.Open()
                    ctx.BeginFigure(points(0), True, True)
                    For Each pt In points.Skip(1)
                        ctx.LineTo(pt, False, False)
                    Next
                    ctx.LineTo(New Point(points.Last().X, baseY), False, False)
                End Using
                area.Freeze()
                dc.DrawGeometry(If(onTrack, ChartPalette.TealWash, ChartPalette.OrangeWash), Nothing, area)

                Dim line As New StreamGeometry()
                Using ctx = line.Open()
                    ctx.BeginFigure(points(0), False, False)
                    For Each pt In points.Skip(1)
                        ctx.LineTo(pt, True, True)
                    Next
                End Using
                line.Freeze()
                dc.DrawGeometry(Nothing, New Pen(lineBrush, 3.5) With {.LineJoin = PenLineJoin.Round}, line)

                ' Projection to the end of the month
                Dim last = points.Last()
                If snap.HasProjection AndAlso lastPast < n - 1 Then
                    Dim reaches = snap.Projection >= snap.MonthlyGoal
                    Dim endPoint As New Point(x(n - 1), y(CDbl(snap.Projection) * p))
                    dc.DrawLine(ChartPalette.DashedPen(If(reaches, ChartPalette.Teal, ChartPalette.Orange), 2.5), last, endPoint)
                    dc.DrawEllipse(Brushes.White, New Pen(If(reaches, ChartPalette.Teal, ChartPalette.Orange), 2.5), endPoint, 5, 5)
                    Dim projText = ChartPalette.Text(t.L("Proy. ", "Proj. ") & Money.Rounded(snap.Projection), 16, ChartPalette.FaceBold, ChartPalette.Muted, dpi)
                    ChartPalette.FitWidth(projText, 16, RightLabelWidth - 22)
                    dc.DrawText(projText, New Point(plot.Right + 18, endPoint.Y - projText.Height / 2))
                End If

                ' End dot with the month to date
                dc.DrawEllipse(lineBrush, New Pen(Brushes.White, 2.5), last, 7, 7)
                Dim mtd = ChartPalette.Text(Money.Rounded(snap.MonthToDate), 20, ChartPalette.FaceBold, ChartPalette.Ink, dpi)
                Dim labelPos As New Point(last.X - mtd.Width / 2, last.Y - mtd.Height - 12)
                dc.DrawRoundedRectangle(Brushes.White, Nothing, New Rect(labelPos.X - 5, labelPos.Y + 1, mtd.Width + 10, mtd.Height - 2), 3, 3)
                dc.DrawText(mtd, labelPos)
            End If

            ' ---- Goal label at the end of the target line
            Dim goalY = y(CDbl(snap.MonthlyGoal))
            Dim title = ChartPalette.Text(t.L("META MES", "MONTH GOAL"), 13, ChartPalette.FaceHeavy, ChartPalette.Goal, dpi)
            Dim amount = ChartPalette.Text(Money.Rounded(snap.MonthlyGoal), 19, ChartPalette.FaceHeavy, ChartPalette.Goal, dpi)
            ChartPalette.FitWidth(title, 13, RightLabelWidth - 22)
            Dim top = goalY - (title.Height + amount.Height) / 2
            ' Keep it apart from the projection label when both are close
            If snap.HasProjection AndAlso Math.Abs(y(CDbl(snap.Projection)) - goalY) < title.Height + amount.Height Then
                top = If(snap.Projection > snap.MonthlyGoal, goalY + 6, goalY - title.Height - amount.Height - 6)
            End If
            dc.DrawText(title, New Point(plot.Right + 18, top))
            dc.DrawText(amount, New Point(plot.Right + 18, top + title.Height - 2))

            ' ---- Day labels
            Dim size = Math.Min(16, Math.Max(9, slot * 0.34))
            For i = 0 To n - 1
                Dim d = snap.Days(i)
                Dim brush = If(d.IsToday, ChartPalette.Ink, If(d.IsFuture, ChartPalette.Grey, ChartPalette.Muted))
                Dim face = If(d.IsToday, ChartPalette.FaceBold, ChartPalette.Face)
                Dim initial = ChartPalette.Text(t.DayShort(d.Date).Substring(0, 1), size * 0.85, face, brush, dpi)
                Dim number = ChartPalette.Text(d.Date.Day.ToString(Globalization.CultureInfo.InvariantCulture), size, face, brush, dpi)
                Dim cx = x(i)
                dc.DrawText(initial, New Point(cx - initial.Width / 2, baseY + 8))
                dc.DrawText(number, New Point(cx - number.Width / 2, baseY + 8 + initial.Height))
            Next

            ' ---- Hover marker
            If _hoverIndex >= 0 AndAlso _hoverIndex < n Then
                dc.DrawLine(ChartPalette.GridPen, New Point(x(_hoverIndex), plot.Top), New Point(x(_hoverIndex), baseY))
                If Not snap.Days(_hoverIndex).IsFuture Then dc.DrawEllipse(lineBrush, New Pen(Brushes.White, 2), New Point(x(_hoverIndex), y(CDbl(snap.Days(_hoverIndex).Cumulative))), 5, 5)
            End If
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim snap = Snapshot
            If snap Is Nothing OrElse snap.Days.Count = 0 Then Return
            Dim plot = PlotRect()
            Dim pos = e.GetPosition(Me)
            Dim index = -1
            If pos.X >= plot.Left AndAlso pos.X < plot.Right Then index = Math.Min(snap.Days.Count - 1, CInt(Math.Floor((pos.X - plot.Left) / (plot.Width / snap.Days.Count))))
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

        Private Function Describe(snap As MonthSnapshot, index As Integer) As String
            Dim t As New Texts(English)
            Dim d = snap.Days(index)
            Dim lines As New List(Of String) From {t.DayLong(d.Date) & " " & t.DayMonth(d.Date)}
            If d.IsFuture Then
                lines.Add(t.L("Meta acumulada: ", "Goal to date: ") & Money.Full(d.Target))
            Else
                lines.Add(t.L("Del día: ", "That day: ") & Money.Full(d.Value))
                lines.Add(t.L("Acumulado: ", "To date: ") & Money.Full(d.Cumulative) & " (" & Money.Attainment(d.Cumulative, snap.MonthlyGoal) & t.L(" del mes)", " of month)"))
                Dim diff = d.Cumulative - d.Target
                lines.Add(t.L("Meta acumulada: ", "Goal to date: ") & Money.Full(d.Target) & Texts.Bullet & If(diff >= 0D, "+", "") & Money.Full(diff))
            End If
            Return String.Join(Environment.NewLine, lines)
        End Function

    End Class

End Namespace
