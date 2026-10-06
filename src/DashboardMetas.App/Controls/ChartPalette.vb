Imports System.Globalization

Namespace Controls

    ''' <summary>Colours, pens and text helpers shared by the hand-drawn charts (same palette as Theme.xaml).</summary>
    Friend NotInheritable Class ChartPalette

        Private Sub New()
        End Sub

        Public Shared ReadOnly Face As New Typeface(New FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
        Public Shared ReadOnly FaceBold As New Typeface(New FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal)
        Public Shared ReadOnly FaceHeavy As New Typeface(New FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal)

        Public Shared ReadOnly Ink As Brush = Frozen(Color.FromRgb(14, 30, 70))
        Public Shared ReadOnly Muted As Brush = Frozen(Color.FromRgb(96, 110, 136))
        Public Shared ReadOnly Teal As Brush = Frozen(Color.FromRgb(0, 163, 150))
        Public Shared ReadOnly Orange As Brush = Frozen(Color.FromRgb(246, 160, 40))
        Public Shared ReadOnly Grey As Brush = Frozen(Color.FromRgb(165, 174, 190))
        Public Shared ReadOnly Band As Brush = Frozen(Color.FromRgb(229, 238, 252))
        Public Shared ReadOnly Goal As Brush = Frozen(Color.FromRgb(28, 45, 150))
        Public Shared ReadOnly TealWash As Brush = Frozen(Color.FromArgb(34, 0, 163, 150))
        Public Shared ReadOnly OrangeWash As Brush = Frozen(Color.FromArgb(40, 246, 160, 40))
        Public Shared ReadOnly ProjectionMetFill As Brush = Frozen(Color.FromRgb(217, 241, 239))
        Public Shared ReadOnly ProjectionBelowFill As Brush = Frozen(Color.FromRgb(254, 241, 223))

        ''' <summary>
        ''' Categorical colours of the float's product lines, in fixed order (validated for colour blindness and
        ''' contrast on white). Teal and orange are left out on purpose: on this screen they mean goal met / below.
        ''' </summary>
        Private Shared ReadOnly LineBrushes As Brush() = {
            Frozen(Color.FromRgb(42, 120, 214)), Frozen(Color.FromRgb(213, 81, 129)), Frozen(Color.FromRgb(0, 131, 0)),
            Frozen(Color.FromRgb(138, 92, 208)), Frozen(Color.FromRgb(160, 82, 45))}
        ''' <summary>"Otras" (lines folded together and styles without a line).</summary>
        Public Shared ReadOnly OtherLine As Brush = Frozen(Color.FromRgb(184, 192, 206))

        ''' <summary>Colour of a float line slot; the last slot (and anything out of range) is "Otras".</summary>
        Public Shared Function LineBrush(slot As Integer) As Brush
            Return If(slot >= 0 AndAlso slot < LineBrushes.Length, LineBrushes(slot), OtherLine)
        End Function

        ''' <summary>Text on top of a line colour: white on the saturated ones, ink on the light grey of "Otras".</summary>
        Public Shared Function OnLine(slot As Integer) As Brush
            Return If(slot >= 0 AndAlso slot < LineBrushes.Length, Brushes.White, Ink)
        End Function

        Public Shared ReadOnly GridPen As Pen = FrozenPen(Frozen(Color.FromRgb(231, 235, 242)), 1)
        Public Shared ReadOnly AxisPen As Pen = FrozenPen(Frozen(Color.FromRgb(150, 160, 180)), 1.5)

        Public Shared Function DashedPen(brush As Brush, thickness As Double) As Pen
            Return FrozenPen(brush, thickness, {thickness * 1.6, thickness * 1.2})
        End Function

        Public Shared Function Text(value As String, size As Double, face As Typeface, brush As Brush, pixelsPerDip As Double) As FormattedText
            Return New FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, pixelsPerDip)
        End Function

        ''' <summary>Shrinks the text until it fits (never clipped).</summary>
        Public Shared Sub FitWidth(text As FormattedText, size As Double, maxWidth As Double)
            If text.Width > maxWidth AndAlso maxWidth > 0 Then text.SetFontSize(Math.Max(7, size * maxWidth / text.Width))
        End Sub

        ''' <summary>Square at the baseline, rounded data end.</summary>
        Public Shared Function TopRoundedBar(x As Double, baseY As Double, width As Double, height As Double, radius As Double) As Geometry
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

        Private Shared Function Frozen(c As Color) As Brush
            Dim b As New SolidColorBrush(c)
            b.Freeze()
            Return b
        End Function

        Private Shared Function FrozenPen(brush As Brush, thickness As Double, Optional dashes As Double() = Nothing) As Pen
            Dim p As New Pen(brush, thickness) With {.LineJoin = PenLineJoin.Round, .StartLineCap = PenLineCap.Round, .EndLineCap = PenLineCap.Round}
            If dashes IsNot Nothing Then
                p.DashStyle = New DashStyle(dashes.Select(Function(d) d / thickness), 0)
                p.DashCap = PenLineCap.Flat
                p.StartLineCap = PenLineCap.Flat
                p.EndLineCap = PenLineCap.Flat
            End If
            p.Freeze()
            Return p
        End Function

    End Class

End Namespace
