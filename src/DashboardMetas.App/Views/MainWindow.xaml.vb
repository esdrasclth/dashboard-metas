Imports System.Windows.Threading
Imports DashboardMetas.App.Services
Imports DashboardMetas.App.ViewModels
Imports DashboardMetas.Core.Configuration
Imports Microsoft.Extensions.Options

''' <summary>
''' Only visual behaviour here: full screen (F11 / Esc), scaling the 1080-high design to any screen, and
''' hiding the mouse pointer on the TV after a few seconds without moving it.
''' </summary>
Partial Class MainWindow

    Private Const DesignHeight As Double = 1080
    Private ReadOnly _viewModel As MainViewModel
    Private ReadOnly _startFullScreen As Boolean
    Private ReadOnly _cursorTimer As New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(4)}
    Private _isFullScreen As Boolean
    Private _restoreBounds As Rect
    Private _lastMouse As Point

    Public Sub New(viewModel As MainViewModel, args As CommandLineArgs, dashboard As IOptionsMonitor(Of DashboardSettings))
        InitializeComponent()
        _viewModel = viewModel
        DataContext = viewModel
        _startFullScreen = dashboard.CurrentValue.StartFullScreen AndAlso Not args.Windowed
        AddHandler viewModel.CloseRequested, Sub() Close()
        AddHandler _cursorTimer.Tick, Sub()
                                          _cursorTimer.Stop()
                                          If _isFullScreen Then Mouse.OverrideCursor = Cursors.None
                                      End Sub
    End Sub

    Protected Overrides Async Sub OnContentRendered(e As EventArgs)
        MyBase.OnContentRendered(e)
        If _startFullScreen Then SetFullScreen(True)
        Await _viewModel.InitializeAsync()
    End Sub

    ''' <summary>Keeps the design 1080 high and makes it as wide as the screen proportion, so nothing is cut or letterboxed.</summary>
    Protected Overrides Sub OnRenderSizeChanged(sizeInfo As SizeChangedInfo)
        MyBase.OnRenderSizeChanged(sizeInfo)
        Dim content = TryCast(Me.Content, FrameworkElement)
        If content Is Nothing OrElse content.ActualHeight <= 0 Then Return
        Dim ratio = content.ActualWidth / content.ActualHeight
        DesignRoot.Width = Math.Clamp(DesignHeight * ratio, 1440, 2600)
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.Key = Key.F11 Then
            SetFullScreen(Not _isFullScreen)
            e.Handled = True
        ElseIf e.Key = Key.Escape AndAlso _isFullScreen Then
            SetFullScreen(False)
            e.Handled = True
        End If
        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Sub OnPreviewMouseMove(e As MouseEventArgs)
        MyBase.OnPreviewMouseMove(e)
        Dim p = e.GetPosition(Me)
        If Math.Abs(p.X - _lastMouse.X) < 2 AndAlso Math.Abs(p.Y - _lastMouse.Y) < 2 Then Return
        _lastMouse = p
        If Mouse.OverrideCursor Is Cursors.None Then Mouse.OverrideCursor = Nothing
        _cursorTimer.Stop()
        If _isFullScreen Then _cursorTimer.Start()
    End Sub

    Private Sub OnToggleFullScreen(sender As Object, e As RoutedEventArgs)
        SetFullScreen(Not _isFullScreen)
    End Sub

    Private Sub SetFullScreen(value As Boolean)
        If value = _isFullScreen Then Return
        _isFullScreen = value
        If value Then
            _restoreBounds = New Rect(Left, Top, Width, Height)
            WindowState = WindowState.Normal       ' required so the maximized window covers the taskbar
            WindowStyle = WindowStyle.None
            ResizeMode = ResizeMode.NoResize
            WindowState = WindowState.Maximized
            _cursorTimer.Start()
        Else
            _cursorTimer.Stop()
            Mouse.OverrideCursor = Nothing
            WindowStyle = WindowStyle.SingleBorderWindow
            ResizeMode = ResizeMode.CanResize
            WindowState = WindowState.Normal
            If _restoreBounds.Width > 0 Then
                Left = _restoreBounds.Left
                Top = _restoreBounds.Top
                Width = _restoreBounds.Width
                Height = _restoreBounds.Height
            End If
        End If
    End Sub

End Class
