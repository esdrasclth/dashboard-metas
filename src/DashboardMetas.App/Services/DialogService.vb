Imports System.Threading
Imports DashboardMetas.App.ViewModels
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Models
Imports Microsoft.Extensions.DependencyInjection

Namespace Services

    ''' <summary>Opens windows and message boxes so view models never touch views directly.</summary>
    Public Interface IDialogService
        Sub ShowInfo(message As String, Optional title As String = "Dashboard Metas")
        Sub ShowError(message As String, Optional title As String = "Dashboard Metas")
        Function Confirm(message As String, Optional title As String = "Dashboard Metas") As Boolean
        ''' <summary>Returns True when settings were saved.</summary>
        Function ShowSettings() As Boolean
        Sub ShowDiagnostics()
        ''' <summary>Returns the chosen area code, or Nothing.</summary>
        Function PickArea(areas As IReadOnlyList(Of AreaDefinition), current As String, english As Boolean) As String
        Sub ShowData(title As String, rows As IReadOnlyList(Of DayRow), english As Boolean)
        Sub ShowFloatData(title As String, rows As IReadOnlyList(Of FloatRow), english As Boolean)
    End Interface

    Public NotInheritable Class DialogService
        Implements IDialogService

        Private ReadOnly _services As IServiceProvider

        Public Sub New(services As IServiceProvider)
            _services = services
        End Sub

        Friend Shared Function Owner() As Window
            Dim app = Application.Current
            If app Is Nothing Then Return Nothing
            Return If(app.Windows.OfType(Of Window)().FirstOrDefault(Function(w) w.IsActive), app.MainWindow)
        End Function

        Public Sub ShowInfo(message As String, Optional title As String = "Dashboard Metas") Implements IDialogService.ShowInfo
            Show(message, title, MessageBoxImage.Information)
        End Sub

        Public Sub ShowError(message As String, Optional title As String = "Dashboard Metas") Implements IDialogService.ShowError
            Show(message, title, MessageBoxImage.Error)
        End Sub

        Public Function Confirm(message As String, Optional title As String = "Dashboard Metas") As Boolean Implements IDialogService.Confirm
            Dim o = Owner()
            Dim result = If(o Is Nothing,
                MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No),
                MessageBox.Show(o, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No))
            Return result = MessageBoxResult.Yes
        End Function

        Public Function ShowSettings() As Boolean Implements IDialogService.ShowSettings
            Dim window = _services.GetRequiredService(Of SettingsWindow)()
            window.Owner = Owner()
            Return window.ShowDialog().GetValueOrDefault()
        End Function

        Public Sub ShowDiagnostics() Implements IDialogService.ShowDiagnostics
            Dim window = _services.GetRequiredService(Of DiagnosticsWindow)()
            window.Owner = Owner()
            window.ShowDialog()
        End Sub

        Public Function PickArea(areas As IReadOnlyList(Of AreaDefinition), current As String, english As Boolean) As String Implements IDialogService.PickArea
            Dim window As New AreaPickerWindow(areas, current, english) With {.Owner = Owner()}
            Return If(window.ShowDialog().GetValueOrDefault(), window.SelectedCode, Nothing)
        End Function

        Public Sub ShowData(title As String, rows As IReadOnlyList(Of DayRow), english As Boolean) Implements IDialogService.ShowData
            Dim window As New DataWindow(title, rows, english) With {.Owner = Owner()}
            window.ShowDialog()
        End Sub

        Public Sub ShowFloatData(title As String, rows As IReadOnlyList(Of FloatRow), english As Boolean) Implements IDialogService.ShowFloatData
            Dim window As New FloatDataWindow(title, rows, english) With {.Owner = Owner()}
            window.ShowDialog()
        End Sub

        Private Shared Sub Show(message As String, title As String, image As MessageBoxImage)
            Dim o = Owner()
            If o Is Nothing Then
                MessageBox.Show(message, title, MessageBoxButton.OK, image)
            Else
                MessageBox.Show(o, message, title, MessageBoxButton.OK, image)
            End If
        End Sub

    End Class

    ''' <summary>Asks for the JDE password in the app's own dialog (on the UI thread).</summary>
    Public NotInheritable Class PasswordPrompt
        Implements IPasswordPrompt

        Private ReadOnly _mode As AppMode

        Public Sub New(mode As AppMode)
            _mode = mode
        End Sub

        Public Function PromptAsync(user As String, message As String, isRetry As Boolean, cancellationToken As CancellationToken) As Task(Of PasswordPromptResult) Implements IPasswordPrompt.PromptAsync
            Return Application.Current.Dispatcher.InvokeAsync(
                Function() As PasswordPromptResult
                    If _mode.IsDemo Then message &= Environment.NewLine & "Modo demo: sirve cualquier contraseña, menos «incorrecta» (simula CWBSY0002)."
                    Dim window As New PasswordWindow(user, message, isRetry) With {.Owner = DialogService.Owner()}
                    If Not window.ShowDialog().GetValueOrDefault() Then Return Nothing
                    Return New PasswordPromptResult(window.Password, window.Remember)
                End Function).Task
        End Function

    End Class

End Namespace
