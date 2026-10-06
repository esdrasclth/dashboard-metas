Imports System.IO
Imports System.Text.Json
Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Data.Announcements
Imports DashboardMetas.Data.Demo
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options

Namespace Services

    ''' <summary>anuncios.json / anuncios_demo.json in the user data folder (written through a temp file).</summary>
    Public NotInheritable Class FileAnnouncementStateStore
        Implements IAnnouncementStateStore

        Private ReadOnly _path As String

        Public Sub New(path As String)
            _path = path
        End Sub

        Public Function Load() As AnnouncementState Implements IAnnouncementStateStore.Load
            If Not File.Exists(_path) Then Return Nothing
            Return JsonSerializer.Deserialize(Of AnnouncementState)(File.ReadAllText(_path), AnnouncementJson.Options)
        End Function

        Public Sub Save(state As AnnouncementState) Implements IAnnouncementStateStore.Save
            Dim temp = _path & ".tmp"
            File.WriteAllText(temp, JsonSerializer.Serialize(state, AnnouncementJson.Options))
            File.Move(temp, _path, overwrite:=True)
        End Sub

    End Class

    ''' <summary>
    ''' Checks the control file every Announcements:PollSeconds in the background (and right away on start,
    ''' when the settings change and on "Consultar ahora"). Real and demo mode keep separate state, and switching
    ''' mode from the settings takes effect on the next check. Never opens dialogs: the screen shows what is active.
    ''' </summary>
    Public NotInheritable Class AnnouncementService
        Inherits BackgroundService

        Private ReadOnly _settings As IOptionsMonitor(Of AnnouncementSettings)
        Private ReadOnly _jde As IOptionsMonitor(Of JdeSettings)
        Private ReadOnly _mode As AppMode
        Private ReadOnly _clock As IClock
        Private ReadOnly _paths As AppPaths
        Private ReadOnly _feedClient As AnnouncementFeedClient
        Private ReadOnly _demoSource As DemoAnnouncementSource
        Private ReadOnly _logger As ILogger
        Private ReadOnly _wake As New SemaphoreSlim(0, 1)
        Private ReadOnly _gate As New Object()
        Private _center As AnnouncementCenter
        Private _centerIsDemo As Boolean

        Public Sub New(settings As IOptionsMonitor(Of AnnouncementSettings), jde As IOptionsMonitor(Of JdeSettings), mode As AppMode, clock As IClock,
                       paths As AppPaths, feedClient As AnnouncementFeedClient, demoSource As DemoAnnouncementSource, logger As ILogger(Of AnnouncementService))
            _settings = settings
            _jde = jde
            _mode = mode
            _clock = clock
            _paths = paths
            _feedClient = feedClient
            _demoSource = demoSource
            _logger = logger
            _settings.OnChange(Sub(s, name) Wake())
        End Sub

        ''' <summary>Something changed (file, dismissals, status, mode). Raised on a background thread.</summary>
        Public Event Changed As EventHandler

        ''' <summary>"Vista previa" from the settings: show this one now, without saving anything.</summary>
        Public Event PreviewRequested As EventHandler(Of Announcement)

        Private ReadOnly Property Now As DateTimeOffset
            Get
                Return New DateTimeOffset(_clock.Now)
            End Get
        End Property

        ''' <summary>The center of the current mode (created and loaded on first use or when the mode changes).</summary>
        Private Function Center() As AnnouncementCenter
            SyncLock _gate
                Dim demo = _mode.IsDemo
                If _center Is Nothing OrElse _centerIsDemo <> demo Then
                    If _center IsNot Nothing Then RemoveHandler _center.Changed, AddressOf OnCenterChanged
                    Dim source As IAnnouncementSource = If(demo, CType(_demoSource, IAnnouncementSource), _feedClient)
                    Dim store As New FileAnnouncementStateStore(Path.Combine(_paths.DataFolder, If(demo, "anuncios_demo.json", "anuncios.json")))
                    _center = New AnnouncementCenter(source, store, _logger)
                    _centerIsDemo = demo
                    AddHandler _center.Changed, AddressOf OnCenterChanged
                    _center.Load()
                End If
                Return _center
            End SyncLock
        End Function

        Private Sub OnCenterChanged(sender As Object, e As EventArgs)
            RaiseEvent Changed(Me, EventArgs.Empty)
        End Sub

        Public Function Audience() As AnnouncementAudience
            Return New AnnouncementAudience(Environment.MachineName, _jde.CurrentValue.Branch)
        End Function

        ''' <summary>What must be on screen right now on this PC, most urgent first.</summary>
        Public Function Active() As IReadOnlyList(Of Announcement)
            If Not _settings.CurrentValue.Enabled Then Return Array.Empty(Of Announcement)()
            Return Center().Active(Now, Audience())
        End Function

        Public Sub Dismiss(id As String)
            Center().Dismiss(id, Now)
        End Sub

        Public ReadOnly Property IsEnabled As Boolean
            Get
                Return _settings.CurrentValue.Enabled
            End Get
        End Property

        ''' <summary>These announcements are on screen now (the panel counts them as «visto» on this screen).</summary>
        Public Sub MarkSeen(ids As IEnumerable(Of String))
            Center().MarkSeen(ids, Now)
        End Sub

        Public Function Views() As (Seen As IReadOnlyDictionary(Of String, DateTimeOffset), Dismissed As IReadOnlyDictionary(Of String, DateTimeOffset))
            Return Center().Views()
        End Function

        Public Sub RestoreDismissed()
            Center().RestoreDismissed()
        End Sub

        Public Function Status() As AnnouncementStatus
            Return Center().Status()
        End Function

        Public Sub Preview(announcement As Announcement)
            RaiseEvent PreviewRequested(Me, announcement)
        End Sub

        ''' <summary>Checks now (used by "Consultar ahora"); returns when the check is done.</summary>
        Public Function CheckNowAsync(cancellationToken As CancellationToken) As Task(Of Boolean)
            Return Center().CheckAsync(Now, cancellationToken)
        End Function

        ''' <summary>Cuts the wait short: the loop checks again right away.</summary>
        Public Sub Wake()
            Try
                If _wake.CurrentCount = 0 Then _wake.Release()
            Catch ex As SemaphoreFullException
                ' already awake
            End Try
        End Sub

        Protected Overrides Async Function ExecuteAsync(stoppingToken As CancellationToken) As Task
            ' Let the window open first; the saved announcements are already on screen from Load()
            Try
                Await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return
            End Try
            Do While Not stoppingToken.IsCancellationRequested
                Try
                    If _settings.CurrentValue.Enabled Then Await Center().CheckAsync(Now, stoppingToken).ConfigureAwait(False)
                Catch ex As OperationCanceledException When stoppingToken.IsCancellationRequested
                    Exit Do
                Catch ex As Exception
                    _logger.LogError(ex, "Error al consultar los anuncios")
                End Try
                Try
                    Await _wake.WaitAsync(TimeSpan.FromSeconds(_settings.CurrentValue.EffectivePollSeconds()), stoppingToken).ConfigureAwait(False)
                Catch ex As OperationCanceledException
                    Exit Do
                End Try
            Loop
        End Function

    End Class

End Namespace
