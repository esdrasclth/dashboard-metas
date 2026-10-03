Imports System.Globalization
Imports System.Text
Imports DashboardMetas.App.ViewModels

''' <summary>"Datos": the numbers behind the chart, day by day (qryVerDiario in the Access version).</summary>
Partial Class DataWindow

    Private ReadOnly _rows As IReadOnlyList(Of DayRow)
    Private ReadOnly _english As Boolean

    Public Sub New(title As String, rows As IReadOnlyList(Of DayRow), english As Boolean)
        InitializeComponent()
        _rows = rows
        _english = english
        HeaderText.Text = title
        DayGrid.ItemsSource = rows
        If english Then
            Me.Title = "Data per day"
            ColDate.Header = "Date" : ColDay.Header = "Day" : ColValue.Header = "Value US$" : ColPieces.Header = "Pieces"
            ColStyles.Header = "Styles" : ColPercent.Header = "% goal" : ColState.Header = "Status"
            CopyButton.Content = "_Copy for Excel" : CloseButton.Content = "Close"
        End If
    End Sub

    ''' <summary>Tab-separated with plain numbers, so it pastes into Excel as numbers.</summary>
    Private Sub OnCopy(sender As Object, e As RoutedEventArgs)
        Dim sb As New StringBuilder()
        sb.AppendLine(String.Join(vbTab, {ColDate.Header, ColDay.Header, ColValue.Header, ColPieces.Header, ColStyles.Header, ColPercent.Header, ColState.Header}))
        For Each r In _rows
            sb.AppendLine(String.Join(vbTab, {r.DateText, r.DayName, r.Value.ToString("0.00", CultureInfo.CurrentCulture),
                                              r.Pieces.ToString("0", CultureInfo.CurrentCulture), r.Styles.ToString(CultureInfo.CurrentCulture), r.PercentText, r.StateText}))
        Next
        Try
            Clipboard.SetText(sb.ToString())
            CopiedText.Text = If(_english, "Copied", "Copiado")
        Catch
            CopiedText.Text = String.Empty
        End Try
    End Sub

End Class
