Namespace Controls

    ''' <summary>
    ''' One row of cards with a fixed gap. With four cards it uses the same columns as the area screen's bottom row
    ''' (1, 1, 1, 1.25), so the cards do not change width when the rotation moves between an area and the Float.
    ''' Any other count: equal widths.
    ''' </summary>
    Public NotInheritable Class CardRowPanel
        Inherits Panel

        Public Shared ReadOnly SpacingProperty As DependencyProperty = DependencyProperty.Register(
            NameOf(Spacing), GetType(Double), GetType(CardRowPanel),
            New FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsMeasure))

        Public Property Spacing As Double
            Get
                Return CDbl(GetValue(SpacingProperty))
            End Get
            Set(value As Double)
                SetValue(SpacingProperty, value)
            End Set
        End Property

        ''' <summary>Width of the last card relative to the others when there are four (as the area screen).</summary>
        Public Const LastOfFourWeight As Double = 1.25

        Private Function Widths(total As Double) As Double()
            Dim n = InternalChildren.Count
            If n = 0 Then Return Array.Empty(Of Double)()
            Dim weights = Enumerable.Repeat(1.0, n).ToArray()
            If n = 4 Then weights(3) = LastOfFourWeight
            Dim free = Math.Max(0, total - Spacing * (n - 1))
            Dim sum = weights.Sum()
            Return weights.Select(Function(w) free * w / sum).ToArray()
        End Function

        Protected Overrides Function MeasureOverride(availableSize As Size) As Size
            Dim width = If(Double.IsInfinity(availableSize.Width), 0, availableSize.Width)
            Dim columns = Widths(width)
            Dim height = 0.0
            For i = 0 To InternalChildren.Count - 1
                Dim child = InternalChildren(i)
                child.Measure(New Size(If(width > 0, columns(i), Double.PositiveInfinity), availableSize.Height))
                height = Math.Max(height, child.DesiredSize.Height)
            Next
            Return New Size(width, height)
        End Function

        Protected Overrides Function ArrangeOverride(finalSize As Size) As Size
            Dim columns = Widths(finalSize.Width)
            Dim x = 0.0
            For i = 0 To InternalChildren.Count - 1
                InternalChildren(i).Arrange(New Rect(x, 0, columns(i), finalSize.Height))
                x += columns(i) + Spacing
            Next
            Return finalSize
        End Function

    End Class

End Namespace
