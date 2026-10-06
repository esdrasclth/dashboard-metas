Imports System.Threading
Imports System.Windows.Threading
Imports CommunityToolkit.Mvvm.ComponentModel
Imports CommunityToolkit.Mvvm.Input
Imports DashboardMetas.App.Services
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Formatting
Imports DashboardMetas.Core.Models
Imports DashboardMetas.Core.Processing
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options

Namespace ViewModels

    ''' <summary>
    ''' The dashboard screen. A 1-second clock checks when to query JDE (every N minutes) and when to rotate
    ''' to the next area. Changing area never queries JDE: the three sources are always loaded.
    ''' </summary>
    Public NotInheritable Class MainViewModel
        Inherits ObservableObject

        Private ReadOnly _refresher As ProductionRefresher
        Private ReadOnly _store As PreferencesStore
        Private ReadOnly _dashboard As IOptionsMonitor(Of DashboardSettings)
        Private ReadOnly _jde As IOptionsMonitor(Of JdeSettings)
        Private ReadOnly _mode As AppMode
        Private ReadOnly _dialogs As IDialogService
        Private ReadOnly _clock As IClock
        Private ReadOnly _logger As ILogger
        Private ReadOnly _timer As DispatcherTimer

        Private _prefs As DashboardPreferences
        Private _texts As Texts = Texts.Spanish
        Private _nextRefresh As Date = Date.MaxValue
        Private _nextRotate As Date = Date.MaxValue
        Private _renderedDay As Date
        Private _renderedMinute As Date
        Private _loadedDemo As Boolean
        Private _currentSourceDone As Boolean

        Public Sub New(refresher As ProductionRefresher, store As PreferencesStore, dashboard As IOptionsMonitor(Of DashboardSettings),
                       jde As IOptionsMonitor(Of JdeSettings), mode As AppMode, dialogs As IDialogService, clock As IClock,
                       announcements As AnnouncementsViewModel, announcementService As AnnouncementService, logger As ILogger(Of MainViewModel))
            Me.Announcements = announcements
            ' The service talks from a background thread: the screen is updated on the UI thread
            Dim ui = Dispatcher.CurrentDispatcher
            AddHandler announcementService.Changed, Sub() ui.BeginInvoke(Sub() announcements.Refresh(_texts))
            AddHandler announcementService.PreviewRequested, Sub(sender, a) ui.BeginInvoke(Sub() announcements.ShowPreview(a))
            _refresher = refresher
            _store = store
            _dashboard = dashboard
            _jde = jde
            _mode = mode
            _dialogs = dialogs
            _clock = clock
            _logger = logger

            RefreshCommand = New AsyncRelayCommand(Function() RefreshAsync(allowPrompt:=True))
            ConnectCommand = New AsyncRelayCommand(Function() RefreshAsync(allowPrompt:=True))
            ChangeAreaCommand = New RelayCommand(AddressOf ChangeArea)
            SettingsCommand = New AsyncRelayCommand(AddressOf OpenSettingsAsync)
            DataCommand = New RelayCommand(AddressOf ShowData)
            ExitCommand = New RelayCommand(Sub() RaiseEvent CloseRequested(Me, EventArgs.Empty))
            NextAreaCommand = New RelayCommand(Sub() RotateArea(+1))
            PreviousAreaCommand = New RelayCommand(Sub() RotateArea(-1))
            ToggleOverviewCommand = New RelayCommand(Sub() SetOverview(Not _prefs.ShowOverview))
            OpenAreaCommand = New RelayCommand(Of String)(AddressOf OpenArea)
            ShowDailyChartCommand = New RelayCommand(Sub() SetChartView(False))
            ShowMonthChartCommand = New RelayCommand(Sub() SetChartView(True))
            ToggleChartCommand = New RelayCommand(
                Sub()
                    If IsFloat Then
                        SetFloatChartView(Not FloatByLine)
                    ElseIf Not IsOverview Then
                        SetChartView(Not IsMonthChart)
                    End If
                End Sub)
            ToggleFloatCommand = New RelayCommand(Sub() SetFloat(Not _prefs.ShowFloat))
            ShowFloatByStatusCommand = New RelayCommand(Sub() SetFloatChartView(False))
            ShowFloatByLineCommand = New RelayCommand(Sub() SetFloatChartView(True))

            _timer = New DispatcherTimer(DispatcherPriority.Background) With {.Interval = TimeSpan.FromSeconds(1)}
            AddHandler _timer.Tick, AddressOf OnTick
        End Sub

        Public Event CloseRequested As EventHandler

        ''' <summary>Remote announcements on top of the dashboard (card and strip).</summary>
        Public ReadOnly Property Announcements As AnnouncementsViewModel

        ''' <summary>Set by the window (F11 / Esc); reported to the panel.</summary>
        Public Property IsFullScreen As Boolean

        ''' <summary>What this screen shows right now, for the status report to the panel (call on the UI thread).</summary>
        Public Function ScreenState() As Core.Remote.ScreenView
            Dim view As New Core.Remote.ScreenView With {
                .FullScreen = IsFullScreen,
                .Resolution = $"{SystemParameters.PrimaryScreenWidth:0}x{SystemParameters.PrimaryScreenHeight:0}"}
            If _prefs Is Nothing Then Return view
            view.Language = If(_prefs.IsEnglish(), "EN", "ES")
            view.AutoRotate = _prefs.AutoRotate
            If IsFloat Then
                view.View = "float"
                view.AreaName = "Custom Float"
            ElseIf IsOverview Then
                view.View = "general"
                view.AreaName = "Vista general"
            Else
                view.View = "area"
                view.Area = CurrentArea.Code
                view.AreaName = CurrentArea.DisplayName(False)
            End If
            Return view
        End Function

        Public ReadOnly Property RefreshCommand As IAsyncRelayCommand
        Public ReadOnly Property ConnectCommand As IAsyncRelayCommand
        Public ReadOnly Property ChangeAreaCommand As IRelayCommand
        Public ReadOnly Property SettingsCommand As IAsyncRelayCommand
        Public ReadOnly Property DataCommand As IRelayCommand
        Public ReadOnly Property ExitCommand As IRelayCommand
        Public ReadOnly Property NextAreaCommand As IRelayCommand
        Public ReadOnly Property PreviousAreaCommand As IRelayCommand
        Public ReadOnly Property ToggleOverviewCommand As IRelayCommand
        Public ReadOnly Property OpenAreaCommand As IRelayCommand(Of String)
        Public ReadOnly Property ShowDailyChartCommand As IRelayCommand
        Public ReadOnly Property ShowMonthChartCommand As IRelayCommand
        Public ReadOnly Property ToggleChartCommand As IRelayCommand
        Public ReadOnly Property ToggleFloatCommand As IRelayCommand
        Public ReadOnly Property ShowFloatByStatusCommand As IRelayCommand
        Public ReadOnly Property ShowFloatByLineCommand As IRelayCommand

        Public ReadOnly Property TodayCard As New KpiCard()
        Public ReadOnly Property GapCard As New KpiCard()
        Public ReadOnly Property WeekCard As New KpiCard()
        Public ReadOnly Property LastClosedCard As New KpiCard()
        Public ReadOnly Property AverageCard As New KpiCard()
        Public ReadOnly Property DaysOnGoalCard As New KpiCard()
        Public ReadOnly Property MonthCard As New KpiCard()

        ''' <summary>Tiles of the overview, one per active area.</summary>
        Public ReadOnly Property Tiles As New System.Collections.ObjectModel.ObservableCollection(Of AreaTileViewModel)()

        ' ---- Float screen
        Public ReadOnly Property FloatLargestCard As New KpiCard()
        Public ReadOnly Property FloatLineCard As New KpiCard()
        Public ReadOnly Property FloatUnpricedCard As New KpiCard()
        ''' <summary>One card per status under the float chart.</summary>
        Public ReadOnly Property FloatStatusCards As New System.Collections.ObjectModel.ObservableCollection(Of KpiCard)()

#Region "Bindable properties"

        Private _labels As New UiLabels(Texts.Spanish)
        Public Property Labels As UiLabels
            Get
                Return _labels
            End Get
            Private Set(value As UiLabels)
                SetProperty(_labels, value)
            End Set
        End Property

        Private _english As Boolean
        Public Property English As Boolean
            Get
                Return _english
            End Get
            Private Set(value As Boolean)
                SetProperty(_english, value)
            End Set
        End Property

        Private _isDemo As Boolean
        Public Property IsDemo As Boolean
            Get
                Return _isDemo
            End Get
            Private Set(value As Boolean)
                SetProperty(_isDemo, value)
            End Set
        End Property

        Private _title As String = String.Empty
        Public Property Title As String
            Get
                Return _title
            End Get
            Private Set(value As String)
                SetProperty(_title, value)
            End Set
        End Property

        Private _subtitle As String = String.Empty
        Public Property Subtitle As String
            Get
                Return _subtitle
            End Get
            Private Set(value As String)
                SetProperty(_subtitle, value)
            End Set
        End Property

        Private _dateText As String = String.Empty
        Public Property DateText As String
            Get
                Return _dateText
            End Get
            Private Set(value As String)
                SetProperty(_dateText, value)
            End Set
        End Property

        Private _clockText As String = String.Empty
        Public Property ClockText As String
            Get
                Return _clockText
            End Get
            Private Set(value As String)
                SetProperty(_clockText, value)
            End Set
        End Property

        Private _statusText As String = String.Empty
        Public Property StatusText As String
            Get
                Return _statusText
            End Get
            Private Set(value As String)
                SetProperty(_statusText, value)
            End Set
        End Property

        Private _statusTone As Tone = Tone.Busy
        Public Property StatusTone As Tone
            Get
                Return _statusTone
            End Get
            Private Set(value As Tone)
                SetProperty(_statusTone, value)
            End Set
        End Property

        Private _statusDetail As String
        Public Property StatusDetail As String
            Get
                Return _statusDetail
            End Get
            Private Set(value As String)
                SetProperty(_statusDetail, value)
            End Set
        End Property

        Private _needsPassword As Boolean
        Public Property NeedsPassword As Boolean
            Get
                Return _needsPassword
            End Get
            Private Set(value As Boolean)
                SetProperty(_needsPassword, value)
            End Set
        End Property

        Private _isBusy As Boolean
        Public Property IsBusy As Boolean
            Get
                Return _isBusy
            End Get
            Private Set(value As Boolean)
                SetProperty(_isBusy, value)
            End Set
        End Property

        Private _snapshot As DashboardSnapshot
        Public Property Snapshot As DashboardSnapshot
            Get
                Return _snapshot
            End Get
            Private Set(value As DashboardSnapshot)
                SetProperty(_snapshot, value)
            End Set
        End Property

        Private _attainmentText As String = "—"
        Public Property AttainmentText As String
            Get
                Return _attainmentText
            End Get
            Private Set(value As String)
                SetProperty(_attainmentText, value)
            End Set
        End Property

        ''' <summary>0 to 1, width of the progress bar of the navy card.</summary>
        Private _attainmentRatio As Double
        Public Property AttainmentRatio As Double
            Get
                Return _attainmentRatio
            End Get
            Private Set(value As Double)
                SetProperty(_attainmentRatio, value)
            End Set
        End Property

        Private _isGoalMet As Boolean
        Public Property IsGoalMet As Boolean
            Get
                Return _isGoalMet
            End Get
            Private Set(value As Boolean)
                SetProperty(_isGoalMet, value)
            End Set
        End Property

        Private _pillText As String = String.Empty
        Public Property PillText As String
            Get
                Return _pillText
            End Get
            Private Set(value As String)
                SetProperty(_pillText, value)
            End Set
        End Property

        Private _pace As PaceState = PaceState.NoShift
        ''' <summary>Colour and text of the pill of the navy card.</summary>
        Public Property Pace As PaceState
            Get
                Return _pace
            End Get
            Private Set(value As PaceState)
                SetProperty(_pace, value)
            End Set
        End Property

        Private _showPaceMarker As Boolean
        ''' <summary>Shows the tick of the value expected by now.</summary>
        Public Property ShowPaceMarker As Boolean
            Get
                Return _showPaceMarker
            End Get
            Private Set(value As Boolean)
                SetProperty(_showPaceMarker, value)
            End Set
        End Property

        Private _paceMarkerRatio As Double
        ''' <summary>0 to 1: share of the shift worked (= share of the goal expected by now).</summary>
        Public Property PaceMarkerRatio As Double
            Get
                Return _paceMarkerRatio
            End Get
            Private Set(value As Double)
                SetProperty(_paceMarkerRatio, value)
            End Set
        End Property

        Private _legendProjection As String = String.Empty
        Public Property LegendProjection As String
            Get
                Return _legendProjection
            End Get
            Private Set(value As String)
                SetProperty(_legendProjection, value)
            End Set
        End Property

        Private _showProjectionLegend As Boolean
        Public Property ShowProjectionLegend As Boolean
            Get
                Return _showProjectionLegend
            End Get
            Private Set(value As Boolean)
                SetProperty(_showProjectionLegend, value)
            End Set
        End Property

        Private _isOverview As Boolean
        ''' <summary>True = all areas at once (overview), False = one area in detail.</summary>
        Public Property IsOverview As Boolean
            Get
                Return _isOverview
            End Get
            Private Set(value As Boolean)
                SetProperty(_isOverview, value)
            End Set
        End Property

        Private _isMonthChart As Boolean
        ''' <summary>True = month to date chart, False = value per day.</summary>
        Public Property IsMonthChart As Boolean
            Get
                Return _isMonthChart
            End Get
            Private Set(value As Boolean)
                SetProperty(_isMonthChart, value)
            End Set
        End Property

        Private _viewToggleText As String = String.Empty
        Public Property ViewToggleText As String
            Get
                Return _viewToggleText
            End Get
            Private Set(value As String)
                SetProperty(_viewToggleText, value)
            End Set
        End Property

        Private _monthSnapshot As MonthSnapshot
        Public Property MonthSnapshot As MonthSnapshot
            Get
                Return _monthSnapshot
            End Get
            Private Set(value As MonthSnapshot)
                SetProperty(_monthSnapshot, value)
            End Set
        End Property

        Private _isFloat As Boolean
        ''' <summary>True = the custom float screen.</summary>
        Public Property IsFloat As Boolean
            Get
                Return _isFloat
            End Get
            Private Set(value As Boolean)
                SetProperty(_isFloat, value)
            End Set
        End Property

        Private _isDetail As Boolean = True
        ''' <summary>True = one area in detail (neither the overview nor the float).</summary>
        Public Property IsDetail As Boolean
            Get
                Return _isDetail
            End Get
            Private Set(value As Boolean)
                SetProperty(_isDetail, value)
            End Set
        End Property

        Private _floatSnapshot As FloatSnapshot
        Public Property FloatSnapshot As FloatSnapshot
            Get
                Return _floatSnapshot
            End Get
            Private Set(value As FloatSnapshot)
                SetProperty(_floatSnapshot, value)
            End Set
        End Property

        Private _floatByLine As Boolean
        ''' <summary>True = float chart by product line, False = by status.</summary>
        Public Property FloatByLine As Boolean
            Get
                Return _floatByLine
            End Get
            Private Set(value As Boolean)
                SetProperty(_floatByLine, value)
            End Set
        End Property

        Private _floatTotalText As String = "—"
        Public Property FloatTotalText As String
            Get
                Return _floatTotalText
            End Get
            Private Set(value As String)
                SetProperty(_floatTotalText, value)
            End Set
        End Property

        Private _floatTotalSubtitle As String = String.Empty
        Public Property FloatTotalSubtitle As String
            Get
                Return _floatTotalSubtitle
            End Get
            Private Set(value As String)
                SetProperty(_floatTotalSubtitle, value)
            End Set
        End Property

        Private _floatTakenText As String = String.Empty
        ''' <summary>"Foto de JDE de las 10:35" (the float has no date: it is what JDE had at that moment).</summary>
        Public Property FloatTakenText As String
            Get
                Return _floatTakenText
            End Get
            Private Set(value As String)
                SetProperty(_floatTakenText, value)
            End Set
        End Property

        Private _floatLegend As IReadOnlyList(Of FloatLegendItem) = Array.Empty(Of FloatLegendItem)()
        Public Property FloatLegend As IReadOnlyList(Of FloatLegendItem)
            Get
                Return _floatLegend
            End Get
            Private Set(value As IReadOnlyList(Of FloatLegendItem))
                SetProperty(_floatLegend, value)
            End Set
        End Property

        Private _tileColumns As Integer = 3
        Public Property TileColumns As Integer
            Get
                Return _tileColumns
            End Get
            Private Set(value As Integer)
                SetProperty(_tileColumns, value)
            End Set
        End Property

        Private _tileRows As Integer = 2
        Public Property TileRows As Integer
            Get
                Return _tileRows
            End Get
            Private Set(value As Integer)
                SetProperty(_tileRows, value)
            End Set
        End Property

        Private _legendMonthActual As String = String.Empty
        Public Property LegendMonthActual As String
            Get
                Return _legendMonthActual
            End Get
            Private Set(value As String)
                SetProperty(_legendMonthActual, value)
            End Set
        End Property

        Private _legendMonthTarget As String = String.Empty
        Public Property LegendMonthTarget As String
            Get
                Return _legendMonthTarget
            End Get
            Private Set(value As String)
                SetProperty(_legendMonthTarget, value)
            End Set
        End Property

        Private _legendMonthProjection As String = String.Empty
        Public Property LegendMonthProjection As String
            Get
                Return _legendMonthProjection
            End Get
            Private Set(value As String)
                SetProperty(_legendMonthProjection, value)
            End Set
        End Property

        Private _showMonthProjectionLegend As Boolean
        Public Property ShowMonthProjectionLegend As Boolean
            Get
                Return _showMonthProjectionLegend
            End Get
            Private Set(value As Boolean)
                SetProperty(_showMonthProjectionLegend, value)
            End Set
        End Property

        Private _attainmentSubtitle As String = String.Empty
        Public Property AttainmentSubtitle As String
            Get
                Return _attainmentSubtitle
            End Get
            Private Set(value As String)
                SetProperty(_attainmentSubtitle, value)
            End Set
        End Property

        Private _attainmentGoal As String = String.Empty
        Public Property AttainmentGoal As String
            Get
                Return _attainmentGoal
            End Get
            Private Set(value As String)
                SetProperty(_attainmentGoal, value)
            End Set
        End Property

        Private _chartTitle As String = String.Empty
        Public Property ChartTitle As String
            Get
                Return _chartTitle
            End Get
            Private Set(value As String)
                SetProperty(_chartTitle, value)
            End Set
        End Property

        Private _chartRange As String = String.Empty
        Public Property ChartRange As String
            Get
                Return _chartRange
            End Get
            Private Set(value As String)
                SetProperty(_chartRange, value)
            End Set
        End Property

        Private _legendMet As String = String.Empty
        Public Property LegendMet As String
            Get
                Return _legendMet
            End Get
            Private Set(value As String)
                SetProperty(_legendMet, value)
            End Set
        End Property

        Private _legendBelow As String = String.Empty
        Public Property LegendBelow As String
            Get
                Return _legendBelow
            End Get
            Private Set(value As String)
                SetProperty(_legendBelow, value)
            End Set
        End Property

        Private _legendToday As String = String.Empty
        Public Property LegendToday As String
            Get
                Return _legendToday
            End Get
            Private Set(value As String)
                SetProperty(_legendToday, value)
            End Set
        End Property

        Private _areaButtonText As String = String.Empty
        Public Property AreaButtonText As String
            Get
                Return _areaButtonText
            End Get
            Private Set(value As String)
                SetProperty(_areaButtonText, value)
            End Set
        End Property

        Private _footerGoal As String = String.Empty
        Public Property FooterGoal As String
            Get
                Return _footerGoal
            End Get
            Private Set(value As String)
                SetProperty(_footerGoal, value)
            End Set
        End Property

        Private _footerInfo As String = String.Empty
        Public Property FooterInfo As String
            Get
                Return _footerInfo
            End Get
            Private Set(value As String)
                SetProperty(_footerInfo, value)
            End Set
        End Property

        Private _areaDots As IReadOnlyList(Of Boolean) = Array.Empty(Of Boolean)()
        ''' <summary>One dot per active area (True = the one on screen), shown when rotation is on.</summary>
        Public Property AreaDots As IReadOnlyList(Of Boolean)
            Get
                Return _areaDots
            End Get
            Private Set(value As IReadOnlyList(Of Boolean))
                SetProperty(_areaDots, value)
            End Set
        End Property

#End Region

        Private ReadOnly Property RefreshMinutes As Integer
            Get
                Return _dashboard.CurrentValue.EffectiveRefreshMinutes()
            End Get
        End Property

        Private ReadOnly Property CurrentArea As AreaDefinition
            Get
                Return _prefs.FindArea(_prefs.CurrentArea)
            End Get
        End Property

        ''' <summary>Opens with the last saved data, then asks JDE.</summary>
        Public Async Function InitializeAsync() As Task
            _prefs = _store.Load()
            LoadCacheForMode()
            Render()
            _nextRotate = _clock.Now.AddSeconds(_prefs.RotateSeconds)
            _timer.Start()
            Await RefreshAsync(allowPrompt:=True)
        End Function

        Private Sub LoadCacheForMode()
            _loadedDemo = _mode.IsDemo
            _refresher.ImportCache(_store.LoadCache(_loadedDemo))
        End Sub

        Public Sub Shutdown()
            _timer.Stop()
            If _prefs IsNot Nothing Then _store.Save(_prefs)
        End Sub

        Private Sub OnTick(sender As Object, e As EventArgs)
            Dim now = _clock.Now
            ClockText = now.ToString("HH:mm", Globalization.CultureInfo.InvariantCulture)
            ' Start and end times of the announcements are checked every second
            Announcements.Refresh(_texts)
            If now.Date <> _renderedDay Then
                Render()   ' midnight: new day in the chart
            ElseIf now >= _renderedMinute.AddMinutes(1) AndAlso Not IsBusy Then
                Render()   ' "expected by now" and the projection move with the clock
            End If

            If now >= _nextRefresh AndAlso Not _refresher.IsBusy Then
                Dim ignored = RefreshAsync(allowPrompt:=False)
                Return
            End If
            ' The overview is the alternative to rotation: it does not rotate
            If _prefs.AutoRotate AndAlso Not IsOverview AndAlso Not IsBusy AndAlso now >= _nextRotate Then RotateArea(+1)
        End Sub

        ''' <summary>
        ''' Queries the three sources starting with the one on screen; the screen is repainted as soon as that
        ''' one arrives. <paramref name="allowPrompt"/> is False for automatic refreshes (no dialogs on a TV).
        ''' </summary>
        Public Async Function RefreshAsync(allowPrompt As Boolean) As Task
            If _refresher.IsBusy OrElse _prefs Is Nothing Then Return
            If _loadedDemo <> _mode.IsDemo Then LoadCacheForMode()
            IsBusy = True
            _currentSourceDone = False
            _nextRefresh = Date.MaxValue
            UpdateStatus()
            Dim first = If(IsFloat, ProductionSource.Float, CurrentArea.Source())
            Dim fromDate = DashboardCalculator.QueryStart(_clock.Now, _prefs.HistoryDays)
            Dim progress As New Progress(Of ProductionSource)(
                Sub(source)
                    Dim onScreen = If(IsFloat, source = ProductionSource.Float,
                                      If(IsOverview, source <> ProductionSource.Float, source = CurrentArea.Source()))
                    If onScreen Then
                        _currentSourceDone = True
                        Render()
                    Else
                        UpdateStatus()
                    End If
                End Sub)
            Try
                Dim outcome = Await _refresher.RefreshAsync(first, fromDate, allowPrompt, progress, CancellationToken.None)
                NeedsPassword = outcome.NeedsPassword
                _store.SaveCache(_loadedDemo, _refresher.ExportCache())
            Catch ex As Exception
                _logger.LogError(ex, "Error al actualizar")
            Finally
                IsBusy = False
                _nextRefresh = _clock.Now.AddMinutes(RefreshMinutes)
                ' Rotation waits for the data: the next area is shown for its full time
                _nextRotate = _clock.Now.AddSeconds(_prefs.RotateSeconds)
                Render()
            End Try
        End Function

        ''' <summary>Stop of the float screen in the rotation (after the last area).</summary>
        Private Const FloatStop As String = "*FLOAT*"

        ''' <summary>The active areas and, when it rotates too (or is on screen), the float.</summary>
        Private Function RotationStops() As List(Of String)
            Dim stops = _prefs.ActiveAreas().Select(Function(a) a.Code).ToList()
            If _prefs.FloatInRotation OrElse _prefs.ShowFloat Then stops.Add(FloatStop)
            Return stops
        End Function

        Private Sub RotateArea(direction As Integer)
            If IsOverview Then Return
            Dim stops = RotationStops()
            If stops.Count = 0 Then Return
            Dim current = If(_prefs.ShowFloat, FloatStop, _prefs.CurrentArea)
            Dim index = stops.FindIndex(Function(c) String.Equals(c, current, StringComparison.OrdinalIgnoreCase))
            Dim nextStop = stops(((index + direction) Mod stops.Count + stops.Count) Mod stops.Count)
            If nextStop = FloatStop Then
                SetFloat(True)
            Else
                SetArea(nextStop)
            End If
        End Sub

        Private Sub ChangeArea()
            _timer.Stop()
            Try
                Dim code = _dialogs.PickArea(_prefs.ActiveAreas(), _prefs.CurrentArea, _prefs.IsEnglish())
                If Not String.IsNullOrEmpty(code) Then SetArea(code)
            Finally
                _timer.Start()
            End Try
        End Sub

        ''' <summary>Uses the data already loaded: instant, no query.</summary>
        Private Sub SetArea(code As String)
            If _prefs.FindArea(code) Is Nothing Then Return
            _prefs.CurrentArea = _prefs.FindArea(code).Code
            _prefs.ShowFloat = False
            _nextRotate = _clock.Now.AddSeconds(_prefs.RotateSeconds)
            _store.Save(_prefs)
            Render()
        End Sub

        Private Async Function OpenSettingsAsync() As Task
            _timer.Stop()
            Dim saved As Boolean
            Try
                saved = _dialogs.ShowSettings()
            Finally
                _timer.Start()
            End Try
            If Not saved Then Return
            _prefs = _store.Load()
            _nextRotate = _clock.Now.AddSeconds(_prefs.RotateSeconds)
            If _loadedDemo <> _mode.IsDemo Then LoadCacheForMode()
            Render()
            ' Days, connection or mode may have changed: ask JDE again with the new settings
            Await RefreshAsync(allowPrompt:=True)
        End Function

        Private Sub ShowData()
            If IsFloat Then
                ShowFloatData()
                Return
            End If
            Dim snap = Snapshot
            If snap Is Nothing OrElse IsOverview Then Return
            Dim rows = snap.Days.Reverse().Select(
                Function(d) New DayRow With {
                    .DateText = _texts.DayMonth(d.Date) & "/" & d.Date.Year.ToString(Globalization.CultureInfo.InvariantCulture),
                    .DayName = _texts.DayLong(d.Date),
                    .Value = d.Value, .Pieces = d.Pieces, .Styles = d.Styles,
                    .PercentText = Money.Attainment(d.Value, snap.Goal),
                    .StateText = StateText(d.State),
                    .Tone = If(d.State = BarState.GoalMet, Tone.Good, If(d.State = BarState.InProgress, Tone.Normal, Tone.Warn))}).ToList()
            _timer.Stop()
            Try
                _dialogs.ShowData(CurrentArea.DisplayName(English) & Texts.Bullet & _texts.L("Meta diaria ", "Daily goal ") & Money.Full(snap.Goal), rows, English)
            Finally
                _timer.Start()
            End Try
        End Sub

        ''' <summary>The float style by style (what the chart adds up), by status in the configured order and by value.</summary>
        Private Sub ShowFloatData()
            Dim order = _jde.CurrentValue.FloatStatusList().ToList()
            Dim rows = _refresher.GetFloat().Select(Function(i) New FloatRow With {
                    .Status = i.Status.Trim().ToUpperInvariant(), .Style = i.Style, .ProductLine = i.ProductLine,
                    .Pieces = i.Pieces, .Value = i.Value, .Orders = i.Orders}).
                OrderBy(Function(r) If(order.Contains(r.Status), order.IndexOf(r.Status), order.Count)).ThenBy(Function(r) r.Status, StringComparer.Ordinal).
                ThenByDescending(Function(r) r.Value).ThenBy(Function(r) r.Style, StringComparer.Ordinal).ToList()
            Dim snap = FloatSnapshot
            Dim title = "Custom Float" & Texts.Bullet & If(snap Is Nothing, "", Money.Full(snap.TotalValue) & Texts.Bullet) & FloatTakenText
            _timer.Stop()
            Try
                _dialogs.ShowFloatData(title, rows, English)
            Finally
                _timer.Start()
            End Try
        End Sub

        Private Function StateText(state As BarState) As String
            Select Case state
                Case BarState.GoalMet : Return _texts.L("Meta alcanzada", "Goal met")
                Case BarState.InProgress : Return _texts.L("En curso", "In progress")
                Case Else : Return _texts.L("Bajo meta", "Below goal")
            End Select
        End Function

        ''' <summary>Recalculates and repaints the screen: the area in detail (Pintar() in the .vbs) or the overview.</summary>
        Private Sub Render()
            If _prefs Is Nothing Then Return
            Dim now = _clock.Now
            Dim t As New Texts(_prefs.IsEnglish())
            If t.English <> _texts.English OrElse Labels Is Nothing Then Labels = New UiLabels(t)
            _texts = t
            English = t.English
            IsDemo = _mode.IsDemo
            _renderedDay = now.Date
            _renderedMinute = New Date(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0)
            ClockText = now.ToString("HH:mm", Globalization.CultureInfo.InvariantCulture)
            DateText = t.LongDate(now).ToUpper(Globalization.CultureInfo.CurrentCulture)
            IsFloat = _prefs.ShowFloat
            IsOverview = _prefs.ShowOverview AndAlso Not IsFloat
            IsDetail = Not IsFloat AndAlso Not IsOverview
            IsMonthChart = String.Equals(_prefs.ChartView, MonthChartView, StringComparison.OrdinalIgnoreCase)
            FloatByLine = String.Equals(_prefs.FloatChartView, FloatLineView, StringComparison.OrdinalIgnoreCase)
            ViewToggleText = If(IsOverview, t.L("Ver detalle", "Detail view"), t.L("Vista general", "All areas"))

            Announcements.Refresh(t)
            If IsFloat Then
                RenderFloat(t, now)
            ElseIf IsOverview Then
                RenderOverview(t, now)
            Else
                RenderArea(t, now)
            End If
            UpdateStatus()
        End Sub

        ''' <summary>Daily and monthly numbers of one area.</summary>
        Private Function Calculate(area As AreaDefinition, now As Date) As (Day As DashboardSnapshot, Month As MonthSnapshot)
            Dim settings = _dashboard.CurrentValue
            Dim shift = area.Shift()
            Dim rows = _refresher.GetRows(area.Code)
            Dim day = DashboardCalculator.Build(area.DailyGoal, rows, now, _prefs.HistoryDays, settings.ExcludeSundays, shift, settings.MinMinutesForProjection)
            Dim month = MonthCalculator.Build(area.DailyGoal, area.MonthlyGoal, rows, now, settings.ExcludeSundays, shift)
            Return (day, month)
        End Function

        ''' <summary>Pill text and the line under the percentage for today's pace.</summary>
        Private Shared Function PaceTexts(t As Texts, snap As DashboardSnapshot, shift As ShiftSchedule, now As Date) As (Pill As String, Detail As String)
            Select Case snap.Pace
                Case PaceState.GoalMet
                    Return (t.L("META ALCANZADA", "GOAL MET"), t.L("turno ", "shift ") & shift.Describe())
                Case PaceState.OnTrack
                    Return (t.L("A TIEMPO", "ON TRACK"), ExpectedText(t, snap, now))
                Case PaceState.Behind
                    Return (t.L("ATRASADO ", "BEHIND ") & Money.Rounded(snap.PaceGap), ExpectedText(t, snap, now))
                Case PaceState.NotStarted
                    Dim nextStart = shift.NextStart(now.TimeOfDay)
                    Return (t.L("INICIA ", "STARTS ") & ShiftSchedule.Format(If(nextStart, shift.Start)), t.L("turno ", "shift ") & shift.Describe())
                Case PaceState.ShiftEnded
                    Return (t.L("TURNO CERRADO", "SHIFT CLOSED"), t.L("turno ", "shift ") & shift.Describe())
                Case Else
                    Return (t.L("SIN TURNO", "NO SHIFT"), t.L("sin turno", "no shift"))
            End Select
        End Function

        ''' <summary>"a esta hora: $64K", with "(pausa)" or "(fuera de turno)" when nobody is working now.</summary>
        Private Shared Function ExpectedText(t As Texts, snap As DashboardSnapshot, now As Date) As String
            Dim text = t.L("a esta hora: ", "by now: ") & Money.Rounded(snap.ExpectedNow)
            If snap.Shift IsNot Nothing AndAlso Not snap.Shift.IsWorking(now.TimeOfDay) Then
                text &= If(snap.Shift.IsOnBreak(now.TimeOfDay), t.L(" (pausa)", " (break)"), t.L(" (fuera de turno)", " (off shift)"))
            End If
            Return text
        End Function

        ''' <summary>"$1.24M de $3.90M · 32%" and its colour (on track / behind the month's straight line).</summary>
        Private Shared Function MonthTexts(t As Texts, month As MonthSnapshot) As (Text As String, Tone As Tone)
            Dim text = Money.Attainment(month.MonthToDate, month.MonthlyGoal) & t.L(" de ", " of ") & Money.Rounded(month.MonthlyGoal) &
                       Texts.Bullet & t.L("esperado ", "expected ") & Money.Rounded(month.ExpectedToDate)
            Dim lineTone = If(month.IsGoalMet OrElse month.IsOnTrack, Tone.Good, Tone.Warn)
            Return (text, lineTone)
        End Function

        Private Sub RenderArea(t As Texts, now As Date)
            Dim area = CurrentArea
            Dim goal = area.DailyGoal
            Dim shift = area.Shift()
            Dim calc = Calculate(area, now)
            Dim snap = calc.Day
            Dim month = calc.Month
            Dim verb = area.DisplayVerb(t.English)
            Dim name = area.DisplayName(t.English)

            ' ---- Header
            Title = name.ToUpper(Globalization.CultureInfo.CurrentCulture)
            Subtitle = area.DisplaySubtitle(t.English)
            AreaButtonText = name

            ' ---- Navy card: today's attainment and pace
            Dim todayText = t.DayLong(now) & " " & t.DayMonth(now)
            Dim pace = PaceTexts(t, snap, shift, now)
            AttainmentText = If(snap.HasShiftToday, Money.Attainment(snap.TodayValue, goal), "—")
            Me.Pace = snap.Pace
            PillText = pace.Pill
            AttainmentSubtitle = todayText & Texts.Bullet & pace.Detail
            IsGoalMet = snap.HasShiftToday AndAlso snap.IsGoalMet
            AttainmentRatio = Math.Clamp(snap.Attainment, 0, 1)
            ' White tick on the progress bar: where the value should be at this hour
            ShowPaceMarker = snap.Pace = PaceState.OnTrack OrElse snap.Pace = PaceState.Behind
            PaceMarkerRatio = Math.Clamp(snap.ShiftProgress, 0, 1)
            AttainmentGoal = t.L("Meta ", "Goal ") & Money.Abbreviated(CDbl(goal), axis:=True)

            ' ---- Today, gap and week
            TodayCard.Update(t.L("VALOR " & verb.ToUpperInvariant() & " HOY", "VALUE " & verb.ToUpperInvariant() & " TODAY"),
                             Money.Full(snap.TodayValue),
                             If(snap.HasShiftToday, t.PiecesAndStyles(snap.TodayPieces, snap.TodayStyles), t.L("Sin turno hoy", "No shift today")))
            If snap.TodayValue < goal Then
                If snap.HasProjection Then
                    ' At the current pace: where the day will close
                    GapCard.Update(t.L("FALTA PARA LA META", "LEFT TO GOAL"), Money.Full(snap.GapToGoal),
                                   t.L("Al ritmo actual cierra en ", "At this pace closes at ") & Money.Rounded(snap.Projection) &
                                   " (" & Money.Attainment(snap.Projection, goal) & ")")
                    GapCard.SubtitleTone = If(snap.Projection >= goal, Tone.Good, Tone.Warn)
                Else
                    GapCard.Update(t.L("FALTA PARA LA META", "LEFT TO GOAL"), Money.Full(snap.GapToGoal), t.L("Meta diaria: ", "Daily goal: ") & Money.Full(goal))
                End If
            Else
                GapCard.Update(t.L("SOBRE LA META", "ABOVE GOAL"), "+" & Money.Full(snap.GapToGoal), t.L("Meta diaria: ", "Daily goal: ") & Money.Full(goal), Tone.Good)
            End If
            WeekCard.Update(t.L("ACUMULADO SEMANAL", "WEEK TO DATE"), Money.Full(snap.WeekToDate), t.L("Desde lunes ", "Since Monday ") & t.DayMonth(snap.WeekStart))

            ' ---- Closed days and month
            Dim closed = snap.LastClosed
            Dim closedTone = If(goal > 0D AndAlso closed.Value >= goal, Tone.Good, If(closed.Value > 0D, Tone.Warn, Tone.Normal))
            LastClosedCard.Update(t.L("ÚLTIMO DÍA CERRADO", "LAST CLOSED DAY"), Money.Full(closed.Value),
                                  t.DayLong(closed.Date) & " " & t.DayMonth(closed.Date) & Texts.Bullet & t.PiecesAndStyles(closed.Pieces, closed.Styles), closedTone)
            If snap.ClosedDaysWithData > 0 Then
                AverageCard.Update(t.L("PROMEDIO DIARIO", "DAILY AVERAGE"), Money.Full(snap.ClosedAverage),
                                   t.Days(snap.ClosedDaysWithData) & t.L(" con escaneo", " with scans"))
                DaysOnGoalCard.Update(t.L("DÍAS EN META", "DAYS ON GOAL"), $"{snap.DaysOnGoal}{t.L(" de ", " of ")}{snap.ClosedDaysWithData}",
                                      Money.Percent(snap.DaysOnGoal / CDbl(snap.ClosedDaysWithData)) & t.L(" de los días cerrados", " of closed days"))
            Else
                AverageCard.Update(t.L("PROMEDIO DIARIO", "DAILY AVERAGE"), "—", t.L("Sin días con escaneo", "No days with scans"))
                DaysOnGoalCard.Update(t.L("DÍAS EN META", "DAYS ON GOAL"), "—", t.L("Sin días con escaneo", "No days with scans"))
            End If
            Dim monthLine = MonthTexts(t, month)
            MonthCard.Update(t.L("ACUMULADO DEL MES", "MONTH TO DATE"), Money.Full(month.MonthToDate), monthLine.Text, If(month.IsGoalMet, Tone.Good, Tone.Normal))
            MonthCard.SubtitleTone = monthLine.Tone

            ' ---- Chart (by day or month to date)
            Dim goalShort = Money.Abbreviated(CDbl(goal), axis:=True)
            If IsMonthChart Then
                ChartTitle = t.L("Acumulado " & verb & " del mes", "Month to date " & verb)
                ChartRange = t.MonthLong(now) & Texts.Bullet & t.L("meta ", "goal ") & Money.Rounded(month.MonthlyGoal) &
                             If(month.IsAutomaticGoal, t.L(" (", " (") & t.Days(month.WorkingDays) & " × " & goalShort & ")", "")
            Else
                ChartTitle = t.L("Valor " & verb & " por día", "Value " & verb & " per day")
                ChartRange = t.ShortDate(snap.FirstDate, False) & " — " & t.ShortDate(snap.LastDate, True)
            End If
            LegendMet = t.L("Meta alcanzada (≥ ", "Goal met (≥ ") & goalShort & ")"
            LegendBelow = t.L("Bajo meta (< ", "Below goal (< ") & goalShort & ")"
            LegendToday = t.L("Día en curso", "Current day")
            LegendProjection = t.L("Proyección al cierre", "End-of-shift projection")
            ShowProjectionLegend = snap.HasProjection AndAlso snap.Projection > snap.TodayValue
            LegendMonthActual = t.L("Acumulado real", "Actual to date")
            LegendMonthTarget = t.L("Meta acumulada", "Goal to date")
            LegendMonthProjection = t.L("Proyección a fin de mes", "Month-end projection")
            ShowMonthProjectionLegend = month.HasProjection
            Snapshot = snap
            MonthSnapshot = month

            ' ---- Footer
            FooterGoal = t.L("Meta diaria: US", "Daily goal: US") & Money.Full(goal) & "."
            Dim info = t.L("Turno ", "Shift ") & shift.Describe() &
                       If(shift.HasBreak, t.L(" (pausa ", " (break ") & ShiftSchedule.Format(shift.BreakStart.Value) & "–" & ShiftSchedule.Format(shift.BreakEnd.Value) & ")", "") &
                       Texts.Bullet & t.L("se actualiza cada ", "refreshes every ") & RefreshMinutes & " min"
            If _prefs.AutoRotate Then info &= Texts.Bullet & t.L("cambio de área cada ", "area changes every ") & _prefs.RotateSeconds & " s"
            FooterInfo = info
            AreaDots = RotationDots(area.Code)
        End Sub

        ''' <summary>One dot per stop of the rotation (True = the one on screen); none when rotation is off.</summary>
        Private Function RotationDots(current As String) As IReadOnlyList(Of Boolean)
            If Not _prefs.AutoRotate Then Return Array.Empty(Of Boolean)()
            Return RotationStops().Select(Function(c) String.Equals(c, current, StringComparison.OrdinalIgnoreCase)).ToList()
        End Function

        ''' <summary>
        ''' The custom float: the total and where it sits (by status and product line). It is a picture of what JDE
        ''' had at the last query, so the screen says from what time it is.
        ''' </summary>
        Private Sub RenderFloat(t As Texts, now As Date)
            Dim jde = _jde.CurrentValue
            Dim priceType = jde.PriceType.Trim()
            Dim snap = FloatCalculator.Build(_refresher.GetFloat(), jde.FloatStatusList())
            Dim taken = _refresher.GetStatus(ProductionSource.Float).LastSuccess
            Dim takenText = If(taken.HasValue,
                               t.L("Foto de JDE de las ", "JDE snapshot at ") & taken.Value.ToString("HH:mm", Globalization.CultureInfo.InvariantCulture) &
                               If(taken.Value.Date <> now.Date, " " & t.DayMonth(taken.Value), ""),
                               t.L("Sin datos de JDE todavía", "No JDE data yet"))
            Dim ofFloat = t.L(" del float", " of the float")

            ' ---- Header
            Title = "CUSTOM FLOAT"
            Subtitle = t.L("Órdenes FIN abiertas por estatus", "Open FIN orders by status") & Texts.Bullet &
                       "F4801 " & String.Join(", ", snap.Statuses.Select(Function(s) s.Code)) & Texts.Bullet &
                       t.L("Valor en US$ (", "Value in US$ (") & priceType & ")"
            AreaButtonText = CurrentArea.DisplayName(t.English)

            ' ---- Navy card: the whole float
            FloatTotalText = Money.Full(snap.TotalValue)
            FloatTotalSubtitle = t.PiecesAndStyles(snap.TotalPieces, snap.Styles) & Texts.Bullet &
                                 snap.Orders.ToString(Globalization.CultureInfo.InvariantCulture) & t.L(" órdenes", " orders")
            FloatTakenText = takenText

            ' ---- Where most of it sits, the main line and what has no price
            Dim largest = snap.Largest()
            Dim largestTitle = t.L("MAYOR ACUMULACIÓN", "LARGEST PILE-UP")
            If largest Is Nothing Then
                FloatLargestCard.Update(largestTitle, "—", t.L("Sin valor en el float", "No value in the float"))
            Else
                FloatLargestCard.Update(largestTitle, largest.Code & "  " & Money.Rounded(largest.Value),
                                        Money.Percent(largest.Share) & ofFloat & Texts.Bullet & t.PiecesAndStyles(largest.Pieces, largest.Styles))
            End If
            Dim mainLine = snap.Lines.Where(Function(l) Not l.IsOther AndAlso l.Value > 0D).OrderByDescending(Function(l) l.Value).FirstOrDefault()
            Dim lineTitle = t.L("LÍNEA PRINCIPAL", "MAIN PRODUCT LINE")
            If mainLine Is Nothing Then
                FloatLineCard.Update(lineTitle, "—", If(snap.IsEmpty, t.L("Sin órdenes en el float", "No orders in the float"),
                                                        t.L("Sin línea de producto en F58C3120", "No product line in F58C3120")))
            Else
                FloatLineCard.Update(lineTitle, mainLine.Name, Money.Rounded(mainLine.Value) & Texts.Bullet & Money.Percent(mainLine.Share) & ofFloat)
            End If
            Dim unpricedTitle = t.L("PIEZAS SIN PRECIO " & priceType, "PIECES WITHOUT " & priceType & " PRICE")
            If snap.UnpricedStyles > 0 Then
                FloatUnpricedCard.Update(unpricedTitle, Money.Count(snap.UnpricedPieces),
                                         snap.UnpricedStyles.ToString(Globalization.CultureInfo.InvariantCulture) &
                                         If(snap.UnpricedStyles = 1, t.L(" estilo suma piezas pero $0", " style adds pieces but $0"),
                                                                     t.L(" estilos suman piezas pero $0", " styles add pieces but $0")), Tone.Warn)
            Else
                FloatUnpricedCard.Update(unpricedTitle, "0", t.L("Todos los estilos tienen precio", "Every style has a price"), Tone.Good)
            End If

            ' ---- One card per status (the objects are kept: only the numbers change every minute)
            If FloatStatusCards.Count <> snap.Statuses.Count Then
                FloatStatusCards.Clear()
                For Each s In snap.Statuses
                    FloatStatusCards.Add(New KpiCard())
                Next
            End If
            For i = 0 To snap.Statuses.Count - 1
                Dim s = snap.Statuses(i)
                FloatStatusCards(i).Update(t.L("ESTATUS ", "STATUS ") & s.Code, Money.Full(s.Value),
                                           Money.Percent(s.Share) & Texts.Bullet & t.PiecesAndStyles(s.Pieces, s.Styles))
            Next

            ' ---- Chart
            ChartTitle = If(FloatByLine, t.L("Float por línea de producto", "Float by product line"), t.L("Float por estatus", "Float by status"))
            ChartRange = takenText & If(snap.UnclassifiedStyles > 0,
                Texts.Bullet & snap.UnclassifiedStyles.ToString(Globalization.CultureInfo.InvariantCulture) &
                If(snap.UnclassifiedStyles = 1, t.L(" estilo sin línea", " style without a line"), t.L(" estilos sin línea", " styles without a line")), "")
            FloatLegend = snap.Lines.Select(Function(l) New FloatLegendItem With {.Name = Global.DashboardMetas.App.Controls.FloatChart.LineName(l, t), .Slot = l.Slot}).ToList()
            FloatSnapshot = snap

            ' ---- Footer
            FooterGoal = "Custom Float."
            ' Short like the area's footer (the source is already under the title); the long one did not fit next to the buttons
            Dim info = t.L("Órdenes FIN abiertas", "Open FIN orders") &
                       Texts.Bullet & t.L("se actualiza cada ", "refreshes every ") & RefreshMinutes & " min"
            If _prefs.AutoRotate Then info &= Texts.Bullet & t.L("cambio cada ", "changes every ") & _prefs.RotateSeconds & " s"
            FooterInfo = info
            AreaDots = RotationDots(FloatStop)
        End Sub

        ''' <summary>All active areas at once: today's attainment, pace, month and a mini chart per tile.</summary>
        Private Sub RenderOverview(t As Texts, now As Date)
            Dim areas = _prefs.ActiveAreas()
            Title = t.L("VISTA GENERAL", "ALL AREAS")
            Subtitle = t.L("Cumplimiento de hoy por área", "Today's attainment by area") & Texts.Bullet &
                       areas.Count.ToString(Globalization.CultureInfo.InvariantCulture) & If(areas.Count = 1, t.L(" área", " area"), t.L(" áreas", " areas"))

            ' Keep the tile objects (only the numbers change every minute)
            If Tiles.Count <> areas.Count OrElse Not Tiles.Select(Function(x) x.Code).SequenceEqual(areas.Select(Function(a) a.Code)) Then
                Tiles.Clear()
                For Each area In areas
                    Tiles.Add(New AreaTileViewModel(area.Code))
                Next
            End If
            Dim count = Math.Max(1, areas.Count)
            TileColumns = If(count <= 3, count, If(count = 4, 2, If(count <= 6, 3, 4)))
            TileRows = CInt(Math.Ceiling(count / CDbl(TileColumns)))

            For i = 0 To areas.Count - 1
                Dim area = areas(i)
                Dim calc = Calculate(area, now)
                Dim snap = calc.Day
                Dim shift = area.Shift()
                Dim pace = PaceTexts(t, snap, shift, now)
                Dim status = _refresher.GetStatus(area.Source())
                Dim tile = Tiles(i)
                tile.Name = area.DisplayName(t.English)
                tile.AttainmentText = If(snap.HasShiftToday, Money.Attainment(snap.TodayValue, area.DailyGoal), "—")
                tile.Pace = snap.Pace
                tile.PillText = pace.Pill
                tile.IsGoalMet = snap.HasShiftToday AndAlso snap.IsGoalMet
                tile.ValueText = Money.Full(snap.TodayValue) & t.L(" de ", " of ") & Money.Rounded(area.DailyGoal)
                tile.AttainmentRatio = Math.Clamp(snap.Attainment, 0, 1)
                tile.ShowPaceMarker = snap.Pace = PaceState.OnTrack OrElse snap.Pace = PaceState.Behind
                tile.PaceMarkerRatio = Math.Clamp(snap.ShiftProgress, 0, 1)
                If snap.HasProjection AndAlso snap.TodayValue < area.DailyGoal Then
                    tile.DetailText = t.L("Al ritmo actual: ", "At this pace: ") & Money.Rounded(snap.Projection) & " (" & Money.Attainment(snap.Projection, area.DailyGoal) & ")"
                    tile.DetailTone = If(snap.Projection >= area.DailyGoal, Tone.Good, Tone.Warn)
                Else
                    tile.DetailText = pace.Detail
                    tile.DetailTone = Tone.Normal
                End If
                Dim monthLine = MonthTexts(t, calc.Month)
                tile.MonthText = t.L("Mes: ", "Month: ") & Money.Rounded(calc.Month.MonthToDate) & Texts.Bullet & monthLine.Text
                tile.MonthTone = monthLine.Tone
                tile.HasError = status.HasError
                tile.ErrorText = If(status.HasError, t.L("Sin conexión • datos de las ", "No connection • data from ") &
                                    If(status.LastSuccess.HasValue, status.LastSuccess.Value.ToString("HH:mm", Globalization.CultureInfo.InvariantCulture), "—"), "")
                tile.Snapshot = snap
            Next

            FooterGoal = t.L("Vista general.", "All areas.")
            FooterInfo = t.L("Clic en un área para ver su detalle", "Click an area to see its detail") & Texts.Bullet &
                         t.L("V cambia de vista", "V switches view") & Texts.Bullet &
                         t.L("se actualiza cada ", "refreshes every ") & RefreshMinutes & " min"
            AreaDots = Array.Empty(Of Boolean)()
        End Sub

        ''' <summary>
        ''' Status dot and text (EstadoArea() in the .vbs): of the source on screen, or of all three in the overview.
        ''' </summary>
        Private Sub UpdateStatus()
            If _prefs Is Nothing Then Return
            Dim t = _texts
            Dim retry = t.L("reintento en ", "retry in ") & RefreshMinutes & " min"
            Dim sources = If(IsFloat, New List(Of ProductionSource) From {ProductionSource.Float},
                             If(IsOverview, AreaCatalog.AllSources.ToList(), New List(Of ProductionSource) From {CurrentArea.Source()}))
            Dim statuses = sources.Select(Function(s) _refresher.GetStatus(s)).ToList()
            Dim failing = statuses.Where(Function(s) s.HasError).ToList()

            If IsBusy AndAlso Not _currentSourceDone Then
                StatusText = t.L("Consultando JDE…", "Querying JDE…")
                StatusTone = Tone.Busy
                StatusDetail = Nothing
            ElseIf failing.Count > 0 Then
                StatusDetail = String.Join(Environment.NewLine, failing.Select(Function(s) AreaCatalog.SourceName(s.Source) & ": Error " &
                    s.LastErrorAt.GetValueOrDefault().ToString("HH:mm:ss", Globalization.CultureInfo.InvariantCulture) & " " & s.LastError))
                Dim needsPassword = failing.Any(Function(s) s.NeedsPassword)
                Dim head = If(needsPassword, t.L("Falta la contraseña de JDE", "JDE password needed"), t.L("Sin conexión con JDE", "No connection to JDE"))
                If IsOverview AndAlso failing.Count < statuses.Count Then head &= " (" & String.Join(", ", failing.Select(Function(s) AreaCatalog.SourceName(s.Source))) & ")"
                Dim lastOk = failing.Where(Function(s) s.LastSuccess.HasValue).Select(Function(s) s.LastSuccess.Value).DefaultIfEmpty().Min()
                If lastOk <> Date.MinValue Then
                    StatusText = head & Texts.Bullet & t.L("datos de las ", "data from ") & lastOk.ToString("HH:mm", Globalization.CultureInfo.InvariantCulture) &
                                 If(lastOk.Date <> _clock.Now.Date, " " & t.DayMonth(lastOk), "") & Texts.Bullet & retry
                Else
                    StatusText = head & Texts.Bullet & retry
                End If
                StatusTone = Tone.Bad
            ElseIf statuses.All(Function(s) s.LastSuccess.HasValue) Then
                Dim ok = t.L("Actualizado ", "Updated ") & statuses.Min(Function(s) s.LastSuccess.Value).ToString("HH:mm", Globalization.CultureInfo.InvariantCulture)
                Dim snap = Snapshot
                If IsFloat AndAlso FloatSnapshot IsNot Nothing AndAlso FloatSnapshot.IsEmpty Then
                    StatusText = ok & Texts.Bullet & t.L("JDE no devolvió órdenes del float", "no float orders from JDE")
                    StatusDetail = t.L("JDE respondió, pero no hay órdenes FIN abiertas en los estatus del float.", "JDE answered, but there are no open FIN orders in the float statuses.")
                    StatusTone = Tone.Warn
                ElseIf IsFloat Then
                    StatusText = ok & Texts.Bullet & t.L("cada ", "every ") & RefreshMinutes & " min"
                    StatusDetail = Nothing
                    StatusTone = Tone.Good
                ElseIf Not IsOverview AndAlso snap IsNot Nothing AndAlso snap.TotalPieces = 0D AndAlso snap.TotalValue = 0D Then
                    StatusText = ok & Texts.Bullet & t.L("JDE no devolvió registros de esta área", "no records from JDE for this area")
                    StatusDetail = t.L("JDE respondió, pero sin registros de esta área en el rango de fechas.", "JDE answered, but there are no records for this area in the date range.")
                    StatusTone = Tone.Warn
                ElseIf Not IsOverview AndAlso snap IsNot Nothing AndAlso snap.TotalValue = 0D Then
                    StatusText = ok & Texts.Bullet & t.L("hay piezas pero sin precio W01", "pieces found but no W01 price")
                    StatusDetail = t.L("Hay piezas, pero ningún artículo encontró precio W01 en F41D200.", "There are pieces, but no item found a W01 price in F41D200.")
                    StatusTone = Tone.Warn
                Else
                    StatusText = ok & Texts.Bullet & t.L("cada ", "every ") & RefreshMinutes & " min"
                    StatusDetail = Nothing
                    StatusTone = Tone.Good
                End If
            Else
                StatusText = t.L("Consultando JDE…", "Querying JDE…")
                StatusTone = Tone.Busy
                StatusDetail = Nothing
            End If
            NeedsPassword = failing.Any(Function(s) s.NeedsPassword) AndAlso Not IsBusy
        End Sub

        ' ---- View switching (detail ↔ overview, by day ↔ month)

        Public Const MonthChartView As String = "Month"

        Public Const FloatLineView As String = "Line"

        Private Sub SetOverview(value As Boolean)
            _prefs.ShowOverview = value
            _prefs.ShowFloat = False
            _store.Save(_prefs)
            _nextRotate = _clock.Now.AddSeconds(_prefs.RotateSeconds)
            Render()
        End Sub

        Private Sub OpenArea(code As String)
            If _prefs.FindArea(code) Is Nothing Then Return
            _prefs.CurrentArea = _prefs.FindArea(code).Code
            SetOverview(False)
        End Sub

        Private Sub SetFloat(value As Boolean)
            _prefs.ShowFloat = value
            If value Then _prefs.ShowOverview = False
            _store.Save(_prefs)
            _nextRotate = _clock.Now.AddSeconds(_prefs.RotateSeconds)
            Render()
        End Sub

        Private Sub SetFloatChartView(byLine As Boolean)
            _prefs.FloatChartView = If(byLine, FloatLineView, "Status")
            _store.Save(_prefs)
            Render()
        End Sub

        Private Sub SetChartView(month As Boolean)
            _prefs.ChartView = If(month, MonthChartView, "Daily")
            _store.Save(_prefs)
            Render()
        End Sub

    End Class

End Namespace
