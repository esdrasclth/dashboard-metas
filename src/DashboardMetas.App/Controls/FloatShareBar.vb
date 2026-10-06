Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Formatting

Namespace Controls

    ''' <summary>
    ''' Thin bar of the navy float card: the share of each product line of the float value, in the same colours as
    ''' the float chart and its legend (the composition at a glance). Tooltip with the shares.
    ''' </summary>
    Public NotInheritable Class FloatShareBar
        Inherits FrameworkElement

        Private Shared ReadOnly Rail As Brush = MakeRail()

        Public Shared ReadOnly SnapshotProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(Snapshot), GetType(FloatSnapshot), GetType(FloatShareBar),
            New FrameworkPropertyMetadata(Nothing, FrameworkPropertyMetadataOptions.AffectsRender, AddressOf OnSnapshotChanged))

        Public Shared ReadOnly EnglishProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(English), GetType(Boolean), GetType(FloatShareBar),
            New FrameworkPropertyMetadata(False, FrameworkPropertyMetadataOptions.AffectsRender, AddressOf OnSnapshotChanged))

        Public Property Snapshot As FloatSnapshot
            Get
                Return DirectCast(GetValue(SnapshotProperty), FloatSnapshot)
            End Get
            Set(value As FloatSnapshot)
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

        Private Shared Sub OnSnapshotChanged(d As DependencyObject, e As DependencyPropertyChangedEventArgs)
            Dim bar = DirectCast(d, FloatShareBar)
            Dim snap = bar.Snapshot
            If snap Is Nothing OrElse snap.TotalValue <= 0D Then
                bar.ToolTip = Nothing
                Return
            End If
            Dim t As New Texts(bar.English)
            bar.ToolTip = String.Join(Environment.NewLine, snap.Lines.Where(Function(l) l.Value > 0D).
                Select(Function(l) FloatChart.LineName(l, t) & ": " & Money.Percent(l.Share) & " (" & Money.Rounded(l.Value) & ")"))
        End Sub

        Protected Overrides Sub OnRender(dc As DrawingContext)
            MyBase.OnRender(dc)
            Dim w = ActualWidth, h = ActualHeight
            If w <= 0 OrElse h <= 0 Then Return
            dc.DrawRoundedRectangle(Rail, Nothing, New Rect(0, 0, w, h), 2, 2)
            Dim snap = Snapshot
            If snap Is Nothing OrElse snap.TotalValue <= 0D Then Return

            ' Clip to the rounded rail; the segments are separated by a 2 px gap of the card's navy
            dc.PushClip(New RectangleGeometry(New Rect(0, 0, w, h), 2, 2))
            Dim x = 0.0
            Dim visible = snap.Lines.Where(Function(l) l.Share > 0).ToList()
            For i = 0 To visible.Count - 1
                Dim width = visible(i).Share * w
                Dim gap = If(i < visible.Count - 1, Math.Min(2.0, width / 3), 0.0)
                dc.DrawRectangle(ChartPalette.LineBrush(visible(i).Slot), Nothing, New Rect(x, 0, Math.Max(0, width - gap), h))
                x += width
            Next
            dc.Pop()
        End Sub

        Private Shared Function MakeRail() As Brush
            Dim b As New SolidColorBrush(Color.FromRgb(52, 75, 122))
            b.Freeze()
            Return b
        End Function

    End Class

End Namespace
