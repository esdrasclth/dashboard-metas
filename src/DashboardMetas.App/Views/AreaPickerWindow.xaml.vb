Imports DashboardMetas.Core.Models

''' <summary>"Cambiar de área": one big button per active area (same as frmAreas in the Access version).</summary>
Partial Class AreaPickerWindow

    Public Sub New(areas As IReadOnlyList(Of AreaDefinition), current As String, english As Boolean)
        InitializeComponent()
        If english Then
            Title = "Change area"
            TitleText.Text = "Change area"
            SubtitleText.Text = "Choose the area shown on screen."
            CancelButton.Content = "Cancel"
        End If
        AreaList.ItemsSource = areas.Select(Function(a) New With {
            .Code = a.Code, .Name = a.DisplayName(english),
            .IsCurrent = String.Equals(a.Code, current, StringComparison.OrdinalIgnoreCase)}).ToList()
    End Sub

    Public Property SelectedCode As String

    Private Sub OnPick(sender As Object, e As RoutedEventArgs)
        SelectedCode = CStr(DirectCast(sender, Button).Tag)
        DialogResult = True
    End Sub

End Class
