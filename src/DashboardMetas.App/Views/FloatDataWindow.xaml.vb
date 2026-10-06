Imports System.Globalization
Imports System.Text
Imports DashboardMetas.App.ViewModels

''' <summary>"Datos" on the float screen: the float style by style (what the chart adds up).</summary>
Partial Class FloatDataWindow

    Private ReadOnly _rows As IReadOnlyList(Of FloatRow)
    Private ReadOnly _english As Boolean

    Public Sub New(title As String, rows As IReadOnlyList(Of FloatRow), english As Boolean)
        InitializeComponent()
        _rows = rows
        _english = english
        HeaderText.Text = title
        FloatGrid.ItemsSource = rows
        If english Then
            Me.Title = "Float data"
            ColStatus.Header = "Status" : ColStyle.Header = "Style" : ColLine.Header = "Line" : ColPieces.Header = "Pieces"
            ColUnit.Header = "W01 price" : ColValue.Header = "Value US$" : ColOrders.Header = "Orders"
            CopyButton.Content = "_Copy for Excel" : CloseButton.Content = "Close"
        End If
    End Sub

    ''' <summary>Tab-separated with plain numbers, so it pastes into Excel as numbers.</summary>
    Private Sub OnCopy(sender As Object, e As RoutedEventArgs)
        Dim sb As New StringBuilder()
        sb.AppendLine(String.Join(vbTab, {ColStatus.Header, ColStyle.Header, ColLine.Header, ColPieces.Header, ColUnit.Header, ColValue.Header, ColOrders.Header}))
        For Each r In _rows
            Dim unit = If(r.Pieces > 0D, r.Value / r.Pieces, 0D)
            sb.AppendLine(String.Join(vbTab, {r.Status, r.Style, r.ProductLine, r.Pieces.ToString("0", CultureInfo.CurrentCulture),
                                              unit.ToString("0.00", CultureInfo.CurrentCulture), r.Value.ToString("0.00", CultureInfo.CurrentCulture),
                                              r.Orders.ToString(CultureInfo.CurrentCulture)}))
        Next
        Try
            Clipboard.SetText(sb.ToString())
            CopiedText.Text = If(_english, "Copied", "Copiado")
        Catch
            CopiedText.Text = String.Empty
        End Try
    End Sub

End Class
