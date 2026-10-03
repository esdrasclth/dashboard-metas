Imports DashboardMetas.Core.Dashboard

Namespace Controls

    ''' <summary>
    ''' Small version of the daily chart for the overview tiles: bars coloured by goal attainment, the goal
    ''' line and today's projection. No labels (the tile shows the numbers); tooltip with the day's value.
    ''' </summary>
    Public NotInheritable Class MiniBarChart
        Inherits FrameworkElement

        Public Shared ReadOnly SnapshotProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(Snapshot), GetType(DashboardSnapshot), GetType(MiniBarChart),
            New FrameworkPropertyMetadata(Nothing, FrameworkPropertyMetadataOptions.AffectsRender))

        Public Property Snapshot As DashboardSnapshot
            Get
                Return DirectCast(GetValue(SnapshotProperty), DashboardSnapshot)
            End Get
            Set(value As DashboardSnapshot)
                SetValue(SnapshotProperty, value)
            End Set
        End Property

        Protected Overrides Sub OnRender(dc As DrawingContext)
            MyBase.OnRender(dc)
            Dim snap = Snapshot
            If snap Is Nothing OrElse snap.Days.Count = 0 OrElse ActualWidth < 20 OrElse ActualHeight < 20 Then Return
            Dim n = snap.Days.Count
            Dim slot = ActualWidth / n
            Dim baseY = ActualHeight - 1
            Dim height = ActualHeight - 4
            Dim axisMax = If(snap.AxisMax > 0, snap.AxisMax, 1.0)
            Dim barWidth = Math.Max(2, slot * 0.62)

            For i = 0 To n - 1
                Dim bar = snap.Days(i)
                Dim x = i * slot + (slot - barWidth) / 2
                Dim h = Math.Max(2, CDbl(bar.Value) / axisMax * height)
                If bar.IsToday AndAlso snap.HasProjection AndAlso snap.Projection > bar.Value Then
                    Dim reaches = snap.Goal > 0D AndAlso snap.Projection >= snap.Goal
                    Dim projH = Math.Min(height, CDbl(snap.Projection) / axisMax * height)
                    dc.DrawGeometry(If(reaches, ChartPalette.ProjectionMetFill, ChartPalette.ProjectionBelowFill),
                                    ChartPalette.DashedPen(If(reaches, ChartPalette.Teal, ChartPalette.Orange), 1.5),
                                    ChartPalette.TopRoundedBar(x + 0.75, baseY - h + 2, barWidth - 1.5, projH - h + 2, 2))
                End If
                Dim fill = If(bar.State = BarState.GoalMet, ChartPalette.Teal, If(bar.State = BarState.InProgress, ChartPalette.Grey, ChartPalette.Orange))
                dc.DrawGeometry(fill, Nothing, ChartPalette.TopRoundedBar(x, baseY, barWidth, h, 2))
            Next

            If snap.Goal > 0D Then
                Dim goalY = baseY - CDbl(snap.Goal) / axisMax * height
                dc.DrawLine(ChartPalette.DashedPen(ChartPalette.Goal, 1.5), New Point(0, goalY), New Point(ActualWidth, goalY))
            End If
            dc.DrawLine(ChartPalette.AxisPen, New Point(0, baseY), New Point(ActualWidth, baseY))
        End Sub

    End Class

End Namespace
