Imports System.Globalization
Imports DashboardMetas.App.Services
Imports DashboardMetas.App.ViewModels

Namespace Converters

    Public MustInherit Class OneWayConverter
        Implements IValueConverter
        Public MustOverride Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        Public Function ConvertBack(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
            Throw New NotSupportedException()
        End Function
        Protected Shared Function Res(key As String) As Object
            Return Application.Current.TryFindResource(key)
        End Function
    End Class

    Public NotInheritable Class InverseBoolToVisibilityConverter
        Inherits OneWayConverter
        Public Overrides Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object
            Return If(TypeOf value Is Boolean AndAlso CBool(value), Visibility.Collapsed, Visibility.Visible)
        End Function
    End Class

    Public NotInheritable Class EmptyToCollapsedConverter
        Inherits OneWayConverter
        Public Overrides Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object
            Dim text = TryCast(value, String)
            If value Is Nothing OrElse (text IsNot Nothing AndAlso text.Length = 0) Then Return Visibility.Collapsed
            Dim list = TryCast(value, ICollection)
            If list IsNot Nothing AndAlso list.Count = 0 Then Return Visibility.Collapsed
            Return Visibility.Visible
        End Function
    End Class

    ''' <summary>Text colour of a KPI value or status line. Parameter "muted": Normal uses the secondary ink.</summary>
    Public NotInheritable Class ToneToTextBrushConverter
        Inherits OneWayConverter
        Public Overrides Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object
            Dim key = If(String.Equals(TryCast(parameter, String), "muted", StringComparison.OrdinalIgnoreCase), "MutedBrush", "InkBrush")
            If TypeOf value Is Tone Then
                Select Case DirectCast(value, Tone)
                    Case Tone.Good : key = "TealTextBrush"
                    Case Tone.Warn, Tone.Busy : key = "OrangeTextBrush"
                    Case Tone.Bad : key = "DangerBrush"
                End Select
            End If
            Return Res(key)
        End Function
    End Class

    ''' <summary>Colour of the connection dot: teal up to date, orange querying/warning, red no connection.</summary>
    Public NotInheritable Class ToneToDotBrushConverter
        Inherits OneWayConverter
        Public Overrides Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object
            Dim key = "TealBrush"
            If TypeOf value Is Tone Then
                Select Case DirectCast(value, Tone)
                    Case Tone.Warn, Tone.Busy : key = "OrangeBrush"
                    Case Tone.Bad : key = "DangerBrush"
                    Case Tone.Normal : key = "GreyBarBrush"
                End Select
            End If
            Return Res(key)
        End Function
    End Class

    Public NotInheritable Class BoolToBrushConverter
        Inherits OneWayConverter
        ''' <summary>Parameter "TrueKey|FalseKey".</summary>
        Public Overrides Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object
            Dim keys = System.Convert.ToString(parameter, CultureInfo.InvariantCulture).Split("|"c)
            Return Res(If(TypeOf value Is Boolean AndAlso CBool(value), keys(0), keys(keys.Length - 1)))
        End Function
    End Class

    ''' <summary>0..1 ratio to a star GridLength: parameter "left" = ratio, "right" = 1 − ratio.</summary>
    Public NotInheritable Class RatioToStarConverter
        Inherits OneWayConverter
        Public Overrides Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object
            Dim ratio = If(TypeOf value Is Double, Math.Clamp(CDbl(value), 0.0, 1.0), 0.0)
            Dim right = String.Equals(TryCast(parameter, String), "right", StringComparison.OrdinalIgnoreCase)
            Return New GridLength(Math.Max(0.0001, If(right, 1 - ratio, ratio)), GridUnitType.Star)
        End Function
    End Class

    Public NotInheritable Class LevelToBrushConverter
        Inherits OneWayConverter
        Public Overrides Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object
            Dim key = "InkBrush"
            If TypeOf value Is DiagnosticLevel Then
                Select Case DirectCast(value, DiagnosticLevel)
                    Case DiagnosticLevel.Error : key = "DangerBrush"
                    Case DiagnosticLevel.Ok : key = "TealTextBrush"
                    Case DiagnosticLevel.Warning : key = "WarnBrush"
                End Select
            End If
            Return Res(key)
        End Function
    End Class

    ''' <summary>Float line slot (Integer) to the same brush the float chart draws it with.</summary>
    Public NotInheritable Class LineSlotToBrushConverter
        Inherits OneWayConverter
        Public Overrides Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object
            Return Global.DashboardMetas.App.Controls.ChartPalette.LineBrush(If(TypeOf value Is Integer, CInt(value), -1))
        End Function
    End Class

    ''' <summary>
    ''' Colour of an announcement by its severity. Parameter: "accent" (band, icon, stripe), "soft" (strip
    ''' background), "onaccent" (text on the accent) or "text" (coloured text on white).
    ''' Info = navy, Warning = orange, Critical = red (the same meanings as the rest of the screen).
    ''' </summary>
    Public NotInheritable Class SeverityToBrushConverter
        Inherits OneWayConverter
        Public Overrides Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object
            Dim severity = If(TypeOf value Is Core.Announcements.AnnouncementSeverity, DirectCast(value, Core.Announcements.AnnouncementSeverity), Core.Announcements.AnnouncementSeverity.Info)
            Dim part = If(TryCast(parameter, String), "accent").ToLowerInvariant()
            Select Case severity
                Case Core.Announcements.AnnouncementSeverity.Critical
                    Return Res(If(part = "soft", "DangerBackBrush", If(part = "onaccent", "CardBrush", "DangerBrush")))
                Case Core.Announcements.AnnouncementSeverity.Warning
                    Return Res(If(part = "soft", "WarnBackBrush", If(part = "onaccent", "InkBrush", If(part = "text", "OrangeTextBrush", "OrangeBrush"))))
                Case Else
                    Return Res(If(part = "soft", "InfoBackBrush", If(part = "onaccent", "CardBrush", "NavyBrush")))
            End Select
        End Function
    End Class

    ''' <summary>Icon of an announcement (Segoe Fluent Icons / MDL2): info, warning or error.</summary>
    Public NotInheritable Class SeverityToGlyphConverter
        Inherits OneWayConverter
        Public Overrides Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object
            Select Case If(TypeOf value Is Core.Announcements.AnnouncementSeverity, DirectCast(value, Core.Announcements.AnnouncementSeverity), Core.Announcements.AnnouncementSeverity.Info)
                Case Core.Announcements.AnnouncementSeverity.Critical : Return ChrW(&HEA39)
                Case Core.Announcements.AnnouncementSeverity.Warning : Return ChrW(&HE7BA)
                Case Else : Return ChrW(&HE946)
            End Select
        End Function
    End Class

    Public NotInheritable Class DiagnosticGlyphConverter
        Inherits OneWayConverter
        Public Overrides Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object
            If TypeOf value Is DiagnosticLevel Then
                Select Case DirectCast(value, DiagnosticLevel)
                    Case DiagnosticLevel.Ok : Return "✓"
                    Case DiagnosticLevel.Warning : Return "!"
                    Case DiagnosticLevel.Error : Return "✕"
                End Select
            End If
            Return "·"
        End Function
    End Class

End Namespace
