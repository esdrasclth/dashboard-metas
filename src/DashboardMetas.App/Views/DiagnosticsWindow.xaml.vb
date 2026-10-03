Imports DashboardMetas.App.ViewModels

Partial Class DiagnosticsWindow

    Public Sub New(viewModel As DiagnosticsViewModel)
        InitializeComponent()
        DataContext = viewModel
        Dim view = CollectionViewSource.GetDefaultView(viewModel.Items)
        view.GroupDescriptions.Add(New PropertyGroupDescription("Group"))
        AddHandler Loaded, Async Sub() Await viewModel.RefreshAsync()
    End Sub

End Class
