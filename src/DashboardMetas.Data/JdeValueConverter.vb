Imports System.Globalization
Imports System.Text

''' <summary>
''' Converts the values returned by the IBM i Access driver into .NET types.
''' Tolerates DBNull, numeric types, invariant numeric text and EBCDIC bytes (CCSID 65535 columns).
''' </summary>
Public NotInheritable Class JdeValueConverter

    Private Shared ReadOnly Ebcdic As New Lazy(Of Encoding)(
        Function()
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
            Return Encoding.GetEncoding(37)
        End Function)

    Private Sub New()
    End Sub

    Public Shared Function ToText(value As Object) As String
        If value Is Nothing OrElse TypeOf value Is DBNull Then Return String.Empty
        Dim bytes = TryCast(value, Byte())
        If bytes IsNot Nothing Then Return Ebcdic.Value.GetString(bytes)
        Return Convert.ToString(value, CultureInfo.InvariantCulture)
    End Function

    Public Shared Function ToDecimal(value As Object) As Decimal
        If value Is Nothing OrElse TypeOf value Is DBNull Then Return 0D
        If TypeOf value Is Decimal Then Return DirectCast(value, Decimal)
        Dim text = TryCast(value, String)
        If text IsNot Nothing Then
            text = text.Trim()
            If text.Length = 0 Then Return 0D
            Return Decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
        End If
        If TypeOf value Is Byte() Then Return ToDecimal(ToText(value))
        Return Convert.ToDecimal(value, CultureInfo.InvariantCulture)
    End Function

    Public Shared Function ToInt32(value As Object) As Integer
        Return CInt(Math.Truncate(ToDecimal(value)))
    End Function

    ''' <summary>DATE columns come as DateTime; tolerates DateOnly and "yyyy-MM-dd" text.</summary>
    Public Shared Function ToDate(value As Object) As Date
        If value Is Nothing OrElse TypeOf value Is DBNull Then Throw New FormatException("Fecha vacía en el resultado de JDE.")
        If TypeOf value Is Date Then Return DirectCast(value, Date).Date
        If TypeOf value Is DateOnly Then Return DirectCast(value, DateOnly).ToDateTime(TimeOnly.MinValue)
        Dim text = ToText(value).Trim()
        Dim result As Date
        If Date.TryParseExact(text, {"yyyy-MM-dd", "yyyyMMdd", "yyyy-MM-dd HH:mm:ss", "MM/dd/yyyy"}, CultureInfo.InvariantCulture, DateTimeStyles.None, result) Then Return result.Date
        Throw New FormatException($"Fecha no reconocida en el resultado de JDE: '{text}'.")
    End Function

End Class
