''' <summary>
''' Password dialog. PasswordBox cannot be data-bound by design, so the value is read here
''' and handed to <see cref="Services.PasswordPrompt"/>; it is never logged or stored by this window.
''' </summary>
Partial Class PasswordWindow

    Public Sub New(user As String, message As String, isRetry As Boolean)
        InitializeComponent()
        UserText.Text = user
        MessageText.Text = message
        If isRetry Then MessageText.Foreground = DirectCast(FindResource("DangerBrush"), Brush)
    End Sub

    Public ReadOnly Property Password As String
        Get
            Return PasswordInput.Password
        End Get
    End Property

    Public ReadOnly Property Remember As Boolean
        Get
            Return RememberCheck.IsChecked.GetValueOrDefault()
        End Get
    End Property

    Private Sub OnAccept(sender As Object, e As RoutedEventArgs)
        If PasswordInput.Password.Length = 0 Then
            PasswordInput.Focus()
            Return
        End If
        DialogResult = True
    End Sub

End Class
