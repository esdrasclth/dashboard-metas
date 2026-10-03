Imports System.Globalization

Namespace Formatting

    ''' <summary>
    ''' Texts of the dashboard screen in Spanish or English, with day and month names that do not depend on
    ''' the Windows regional settings (same wording as the Access version).
    ''' </summary>
    Public NotInheritable Class Texts

        Public Const Bullet As String = " • "

        Private Shared ReadOnly DaysShortEs As String() = {"Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom"}
        Private Shared ReadOnly DaysShortEn As String() = {"Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"}
        Private Shared ReadOnly DaysLongEs As String() = {"Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"}
        Private Shared ReadOnly DaysLongEn As String() = {"Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"}
        Private Shared ReadOnly MonthsEs As String() = {"ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"}
        Private Shared ReadOnly MonthsEn As String() = {"Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"}
        Private Shared ReadOnly MonthsLongEs As String() = {"enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"}
        Private Shared ReadOnly MonthsLongEn As String() = {"January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"}

        Public Sub New(english As Boolean)
            Me.English = english
        End Sub

        Public ReadOnly Property English As Boolean

        Public Shared ReadOnly Property Spanish As New Texts(False)

        Public Function L(es As String, en As String) As String
            Return If(English, en, es)
        End Function

        ''' <summary>Monday = 0 … Sunday = 6.</summary>
        Public Shared Function MondayIndex(d As Date) As Integer
            Return (CInt(d.DayOfWeek) + 6) Mod 7
        End Function

        Public Function DayShort(d As Date) As String
            Return If(English, DaysShortEn, DaysShortEs)(MondayIndex(d))
        End Function

        Public Function DayLong(d As Date) As String
            Return If(English, DaysLongEn, DaysLongEs)(MondayIndex(d))
        End Function

        Public Function MonthShort(d As Date) As String
            Return If(English, MonthsEn, MonthsEs)(d.Month - 1)
        End Function

        ''' <summary>"octubre 2026" / "October 2026".</summary>
        Public Function MonthLong(d As Date) As String
            Return If(English, MonthsLongEn, MonthsLongEs)(d.Month - 1) & " " & d.Year.ToString(CultureInfo.InvariantCulture)
        End Function

        ''' <summary>"01/10" in Spanish, "10/01" in English.</summary>
        Public Function DayMonth(d As Date) As String
            Return If(English, $"{d.Month:00}/{d.Day:00}", $"{d.Day:00}/{d.Month:00}")
        End Function

        ''' <summary>"jueves 01 oct 2026" / "Thursday, Oct 01 2026".</summary>
        Public Function LongDate(d As Date) As String
            Return If(English, $"{DayLong(d)}, {MonthShort(d)} {d.Day:00} {d.Year}", $"{DayLong(d).ToLowerInvariant()} {d.Day:00} {MonthShort(d)} {d.Year}")
        End Function

        ''' <summary>"16 sep" / "Sep 16", optionally with the year.</summary>
        Public Function ShortDate(d As Date, withYear As Boolean) As String
            Dim text = If(English, $"{MonthShort(d)} {d.Day:00}", $"{d.Day:00} {MonthShort(d)}")
            Return If(withYear, text & " " & d.Year.ToString(CultureInfo.InvariantCulture), text)
        End Function

        Public Function PiecesAndStyles(pieces As Decimal, styles As Integer) As String
            Return Money.Count(pieces) & L(" piezas", " pieces") & Bullet & styles.ToString(CultureInfo.InvariantCulture) & L(" estilos", " styles")
        End Function

        Public Function Days(count As Integer) As String
            Return count.ToString(CultureInfo.InvariantCulture) & If(count = 1, L(" día", " day"), L(" días", " days"))
        End Function

    End Class

    ''' <summary>Money and number formats of the screen, independent of the regional settings.</summary>
    Public NotInheritable Class Money

        Private Sub New()
        End Sub

        ''' <summary>"$123,456".</summary>
        Public Shared Function Full(value As Decimal) As String
            Dim text = Math.Abs(Math.Round(value, 0, MidpointRounding.AwayFromZero)).ToString("#,##0", CultureInfo.InvariantCulture)
            Return If(value < 0D AndAlso text <> "0", "-$" & text, "$" & text)
        End Function

        ''' <summary>"1,234".</summary>
        Public Shared Function Count(value As Decimal) As String
            Return Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("#,##0", CultureInfo.InvariantCulture)
        End Function

        ''' <summary>"87%".</summary>
        Public Shared Function Percent(ratio As Double) As String
            Return Math.Round(ratio * 100, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) & "%"
        End Function

        ''' <summary>Rounded for sentences: "$142K", "$4K", "$3.5K", "$1.25M", "$850".</summary>
        Public Shared Function Rounded(value As Decimal) As String
            Dim sign = If(value < 0D, "-", "")
            Dim v = Math.Abs(value)
            If v >= 1000000D Then Return sign & "$" & Math.Round(v / 1000000D, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture) & "M"
            If v >= 10000D Then Return sign & "$" & Math.Round(v / 1000D, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) & "K"
            If v >= 1000D Then Return sign & "$" & Math.Round(v / 1000D, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture) & "K"
            Return sign & "$" & Math.Round(v, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
        End Function

        ''' <summary>Value ÷ goal as a percentage; below the goal it never rounds up to "100%".</summary>
        Public Shared Function Attainment(value As Decimal, goal As Decimal) As String
            If goal <= 0D Then Return "—"
            Dim ratio = CDbl(value / goal)
            Return Percent(If(value >= goal, ratio, Math.Floor(ratio * 100) / 100))
        End Function

        ''' <summary>
        ''' "$150K", "$1.25M", "$850". On the axis decimals are dropped when the number is exact; on the bars
        ''' they are dropped when <paramref name="compact"/> (many narrow bars). Same as Corto() in the .vbs.
        ''' </summary>
        Public Shared Function Abbreviated(value As Double, axis As Boolean, Optional compact As Boolean = False) As String
            Dim sign = If(value < 0, "-", "")
            Dim v = Math.Abs(value)
            If v >= 1000000 Then Return sign & "$" & Number(v / 1000000, axis, 2, compact) & "M"
            If v >= 1000 Then Return sign & "$" & Number(v / 1000, axis, 1, compact) & "K"
            Return sign & "$" & Math.Round(v, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
        End Function

        Private Shared Function Number(x As Double, axis As Boolean, decimals As Integer, compact As Boolean) As String
            If (axis AndAlso Math.Abs(x - Math.Round(x, 0)) < 0.001) OrElse (Not axis AndAlso compact) Then
                Return Math.Round(x, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
            End If
            Return Math.Round(x, decimals, MidpointRounding.AwayFromZero).ToString("0." & New String("0"c, decimals), CultureInfo.InvariantCulture)
        End Function

    End Class

End Namespace
