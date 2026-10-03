Imports DashboardMetas.App.ViewModels

Partial Class SettingsWindow

    Public Sub New(viewModel As SettingsViewModel)
        InitializeComponent()
        DataContext = viewModel
        AddHandler viewModel.Saved, Sub() DialogResult = True
    End Sub

End Class
