Imports System.Collections.ObjectModel
Imports System.Globalization
Imports CommunityToolkit.Mvvm.ComponentModel
Imports CommunityToolkit.Mvvm.Input
Imports DashboardMetas.App.Services
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Models
Imports Microsoft.Extensions.Options

Namespace ViewModels

    ''' <summary>An editable row of the areas table (name ES/EN, goal, shown or not).</summary>
    Public NotInheritable Class AreaRowViewModel
        Inherits ObservableObject

        Private ReadOnly _original As AreaDefinition

        Public Sub New(area As AreaDefinition)
            _original = area.Clone()
            Code = area.Code
            _name = area.Name
            _nameEn = area.NameEn
            _active = area.Active
            _goalText = area.DailyGoal.ToString("#,##0", CultureInfo.InvariantCulture)
            _monthlyGoalText = If(area.MonthlyGoal > 0D, area.MonthlyGoal.ToString("#,##0", CultureInfo.InvariantCulture), String.Empty)
            _shiftStart = area.ShiftStart
            _shiftEnd = area.ShiftEnd
            _breakStart = area.BreakStart
            _breakEnd = area.BreakEnd
        End Sub

        Public ReadOnly Property Code As String

        Public ReadOnly Property SourceText As String
            Get
                Select Case _original.Source()
                    Case ProductionSource.AssemblyDeliveries : Return "F31122 op. 51000"
                    Case ProductionSource.Shipments : Return "dcLINK DCTXF CS"
                    Case Else : Return "F58C3120 estaciones"
                End Select
            End Get
        End Property

        Private _name As String
        Public Property Name As String
            Get
                Return _name
            End Get
            Set(value As String)
                SetProperty(_name, value)
            End Set
        End Property

        Private _nameEn As String
        Public Property NameEn As String
            Get
                Return _nameEn
            End Get
            Set(value As String)
                SetProperty(_nameEn, value)
            End Set
        End Property

        Private _goalText As String
        Public Property GoalText As String
            Get
                Return _goalText
            End Get
            Set(value As String)
                SetProperty(_goalText, value)
            End Set
        End Property

        Private _shiftStart As String
        Public Property ShiftStart As String
            Get
                Return _shiftStart
            End Get
            Set(value As String)
                SetProperty(_shiftStart, value)
            End Set
        End Property

        Private _shiftEnd As String
        Public Property ShiftEnd As String
            Get
                Return _shiftEnd
            End Get
            Set(value As String)
                SetProperty(_shiftEnd, value)
            End Set
        End Property

        Private _breakStart As String
        Public Property BreakStart As String
            Get
                Return _breakStart
            End Get
            Set(value As String)
                SetProperty(_breakStart, value)
            End Set
        End Property

        Private _breakEnd As String
        Public Property BreakEnd As String
            Get
                Return _breakEnd
            End Get
            Set(value As String)
                SetProperty(_breakEnd, value)
            End Set
        End Property

        Private _monthlyGoalText As String
        ''' <summary>Empty = automatic (daily goal × working days of the month).</summary>
        Public Property MonthlyGoalText As String
            Get
                Return _monthlyGoalText
            End Get
            Set(value As String)
                SetProperty(_monthlyGoalText, value)
            End Set
        End Property

        Private _active As Boolean
        Public Property Active As Boolean
            Get
                Return _active
            End Get
            Set(value As Boolean)
                SetProperty(_active, value)
            End Set
        End Property

        Public Function TryBuild(ByRef area As AreaDefinition, ByRef [error] As String) As Boolean
            Dim goal As Decimal
            If Not SettingsViewModel.TryParseAmount(GoalText, goal) OrElse goal <= 0D Then
                [error] = $"La meta diaria de {Code} debe ser un número mayor que 0."
                Return False
            End If
            Dim monthly As Decimal = 0D
            If Not String.IsNullOrWhiteSpace(MonthlyGoalText) AndAlso (Not SettingsViewModel.TryParseAmount(MonthlyGoalText, monthly) OrElse monthly <= 0D) Then
                [error] = $"La meta mensual de {Code} debe ser un número mayor que 0 (o vacía = automática)."
                Return False
            End If
            Dim shift As ShiftSchedule = Nothing
            Dim shiftError As String = Nothing
            If Not ShiftSchedule.TryCreate(ShiftStart, ShiftEnd, BreakStart, BreakEnd, shift, shiftError) Then
                [error] = $"Horario de {Code}: {shiftError}"
                Return False
            End If
            area = _original.Clone()
            area.ShiftStart = ShiftSchedule.Format(shift.Start)
            area.ShiftEnd = ShiftSchedule.Format(shift.End)
            area.BreakStart = If(shift.HasBreak, ShiftSchedule.Format(shift.BreakStart.Value), String.Empty)
            area.BreakEnd = If(shift.HasBreak, ShiftSchedule.Format(shift.BreakEnd.Value), String.Empty)
            area.Name = If(Name, String.Empty).Trim()
            area.NameEn = If(NameEn, String.Empty).Trim()
            area.DailyGoal = goal
            area.MonthlyGoal = monthly
            area.Active = Active
            Return True
        End Function

    End Class

    ''' <summary>"Configuración": screen, areas and goals, JDE connection and demo mode.</summary>
    Public NotInheritable Class SettingsViewModel
        Inherits ObservableObject

        Private ReadOnly _store As PreferencesStore
        Private ReadOnly _writer As UserSettingsWriter
        Private ReadOnly _credentials As ICredentialStore
        Private ReadOnly _dialogs As IDialogService
        Private ReadOnly _mode As AppMode
        Private ReadOnly _jde As IOptionsMonitor(Of JdeSettings)
        Private ReadOnly _dashboard As IOptionsMonitor(Of DashboardSettings)
        Private ReadOnly _demo As IOptionsMonitor(Of DemoSettings)
        Private _prefs As DashboardPreferences

        Public Sub New(store As PreferencesStore, writer As UserSettingsWriter, credentials As ICredentialStore, dialogs As IDialogService, mode As AppMode,
                       jde As IOptionsMonitor(Of JdeSettings), dashboard As IOptionsMonitor(Of DashboardSettings), demo As IOptionsMonitor(Of DemoSettings))
            _store = store
            _writer = writer
            _credentials = credentials
            _dialogs = dialogs
            _mode = mode
            _jde = jde
            _dashboard = dashboard
            _demo = demo

            SaveCommand = New RelayCommand(AddressOf Save)
            DiagnosticsCommand = New RelayCommand(Sub() _dialogs.ShowDiagnostics())
            ForgetPasswordCommand = New RelayCommand(AddressOf ForgetPassword)
            RestoreCompanyCommand = New RelayCommand(AddressOf RestoreCompany)
            CopyShiftToAllCommand = New RelayCommand(AddressOf CopyShiftToAll)
            Load()
        End Sub

        Public Event Saved As EventHandler

        Public ReadOnly Property SaveCommand As IRelayCommand
        Public ReadOnly Property DiagnosticsCommand As IRelayCommand
        Public ReadOnly Property ForgetPasswordCommand As IRelayCommand
        Public ReadOnly Property RestoreCompanyCommand As IRelayCommand
        Public ReadOnly Property CopyShiftToAllCommand As IRelayCommand

        ''' <summary>Same shift hours as the first row in every area.</summary>
        Private Sub CopyShiftToAll()
            If Areas.Count = 0 Then Return
            Dim first = Areas(0)
            For Each row In Areas.Skip(1)
                row.ShiftStart = first.ShiftStart
                row.ShiftEnd = first.ShiftEnd
                row.BreakStart = first.BreakStart
                row.BreakEnd = first.BreakEnd
            Next
        End Sub

        Public ReadOnly Property Areas As New ObservableCollection(Of AreaRowViewModel)()
        Public ReadOnly Property FailingSourceOptions As IReadOnlyList(Of String) = {"", "Y1", "Estaciones", "Embarques"}

        Private Sub Load()
            _prefs = _store.Load()
            English = _prefs.IsEnglish()
            HistoryDays = _prefs.HistoryDays.ToString(CultureInfo.InvariantCulture)
            AutoRotate = _prefs.AutoRotate
            RotateSeconds = _prefs.RotateSeconds.ToString(CultureInfo.InvariantCulture)
            Areas.Clear()
            For Each area In _prefs.Areas
                Areas.Add(New AreaRowViewModel(area))
            Next
            LoadConnection(_jde.CurrentValue, _dashboard.CurrentValue, _demo.CurrentValue)
            ErrorText = String.Empty
            OnPropertyChanged(NameOf(HasSavedPassword))
        End Sub

        Private Sub LoadConnection(j As JdeSettings, d As DashboardSettings, demo As DemoSettings)
            Dsn = j.Dsn
            User = j.User
            UseDriverSignOn = j.UseDriverSignOn
            Branch = j.Branch.Trim()
            AssemblyLibrary = j.AssemblyLibrary
            StationsLibrary = j.StationsLibrary
            PricesLibrary = j.PricesLibrary
            DcLinkLibrary = j.DcLinkLibrary
            PriceType = j.PriceType
            AssemblyOperation = j.AssemblyOperation.ToString(CultureInfo.InvariantCulture)
            ShipmentTransaction = j.ShipmentTransaction
            StationsDivisor = j.StationsQuantityDivisor.ToString(CultureInfo.InvariantCulture)
            PriceDivisor = j.PriceDivisor.ToString(CultureInfo.InvariantCulture)
            RefreshMinutes = d.RefreshMinutes.ToString(CultureInfo.InvariantCulture)
            ExcludeSundays = d.ExcludeSundays
            StartFullScreen = d.StartFullScreen
            DemoEnabled = demo.Enabled OrElse _mode.ForcedByArgument
            FailingSource = If(demo.FailingSource, String.Empty)
            SimulatedTime = If(demo.SimulatedTime, String.Empty)
        End Sub

#Region "Fields"

        Private _english As Boolean
        Public Property English As Boolean
            Get
                Return _english
            End Get
            Set(value As Boolean)
                If SetProperty(_english, value) Then OnPropertyChanged(NameOf(Spanish))
            End Set
        End Property

        Public Property Spanish As Boolean
            Get
                Return Not _english
            End Get
            Set(value As Boolean)
                English = Not value
            End Set
        End Property

        Private _historyDays As String
        Public Property HistoryDays As String
            Get
                Return _historyDays
            End Get
            Set(value As String)
                SetProperty(_historyDays, value)
            End Set
        End Property

        Private _autoRotate As Boolean
        Public Property AutoRotate As Boolean
            Get
                Return _autoRotate
            End Get
            Set(value As Boolean)
                If SetProperty(_autoRotate, value) Then OnPropertyChanged(NameOf(NoRotate))
            End Set
        End Property

        Public Property NoRotate As Boolean
            Get
                Return Not _autoRotate
            End Get
            Set(value As Boolean)
                AutoRotate = Not value
            End Set
        End Property

        Private _rotateSeconds As String
        Public Property RotateSeconds As String
            Get
                Return _rotateSeconds
            End Get
            Set(value As String)
                SetProperty(_rotateSeconds, value)
            End Set
        End Property

        Private _refreshMinutes As String
        Public Property RefreshMinutes As String
            Get
                Return _refreshMinutes
            End Get
            Set(value As String)
                SetProperty(_refreshMinutes, value)
            End Set
        End Property

        Private _excludeSundays As Boolean
        Public Property ExcludeSundays As Boolean
            Get
                Return _excludeSundays
            End Get
            Set(value As Boolean)
                SetProperty(_excludeSundays, value)
            End Set
        End Property

        Private _startFullScreen As Boolean
        Public Property StartFullScreen As Boolean
            Get
                Return _startFullScreen
            End Get
            Set(value As Boolean)
                SetProperty(_startFullScreen, value)
            End Set
        End Property

        Private _dsn As String
        Public Property Dsn As String
            Get
                Return _dsn
            End Get
            Set(value As String)
                SetProperty(_dsn, value)
            End Set
        End Property

        Private _user As String
        Public Property User As String
            Get
                Return _user
            End Get
            Set(value As String)
                SetProperty(_user, value)
            End Set
        End Property

        Private _useDriverSignOn As Boolean
        Public Property UseDriverSignOn As Boolean
            Get
                Return _useDriverSignOn
            End Get
            Set(value As Boolean)
                SetProperty(_useDriverSignOn, value)
            End Set
        End Property

        Private _branch As String
        Public Property Branch As String
            Get
                Return _branch
            End Get
            Set(value As String)
                SetProperty(_branch, value)
            End Set
        End Property

        Private _assemblyLibrary As String
        Public Property AssemblyLibrary As String
            Get
                Return _assemblyLibrary
            End Get
            Set(value As String)
                SetProperty(_assemblyLibrary, value)
            End Set
        End Property

        Private _stationsLibrary As String
        Public Property StationsLibrary As String
            Get
                Return _stationsLibrary
            End Get
            Set(value As String)
                SetProperty(_stationsLibrary, value)
            End Set
        End Property

        Private _pricesLibrary As String
        Public Property PricesLibrary As String
            Get
                Return _pricesLibrary
            End Get
            Set(value As String)
                SetProperty(_pricesLibrary, value)
            End Set
        End Property

        Private _dcLinkLibrary As String
        Public Property DcLinkLibrary As String
            Get
                Return _dcLinkLibrary
            End Get
            Set(value As String)
                SetProperty(_dcLinkLibrary, value)
            End Set
        End Property

        Private _priceType As String
        Public Property PriceType As String
            Get
                Return _priceType
            End Get
            Set(value As String)
                SetProperty(_priceType, value)
            End Set
        End Property

        Private _assemblyOperation As String
        Public Property AssemblyOperation As String
            Get
                Return _assemblyOperation
            End Get
            Set(value As String)
                SetProperty(_assemblyOperation, value)
            End Set
        End Property

        Private _shipmentTransaction As String
        Public Property ShipmentTransaction As String
            Get
                Return _shipmentTransaction
            End Get
            Set(value As String)
                SetProperty(_shipmentTransaction, value)
            End Set
        End Property

        Private _stationsDivisor As String
        Public Property StationsDivisor As String
            Get
                Return _stationsDivisor
            End Get
            Set(value As String)
                SetProperty(_stationsDivisor, value)
            End Set
        End Property

        Private _priceDivisor As String
        Public Property PriceDivisor As String
            Get
                Return _priceDivisor
            End Get
            Set(value As String)
                SetProperty(_priceDivisor, value)
            End Set
        End Property

        Private _demoEnabled As Boolean
        Public Property DemoEnabled As Boolean
            Get
                Return _demoEnabled
            End Get
            Set(value As Boolean)
                SetProperty(_demoEnabled, value)
            End Set
        End Property

        Public ReadOnly Property DemoForced As Boolean
            Get
                Return _mode.ForcedByArgument
            End Get
        End Property

        Public ReadOnly Property DemoEditable As Boolean
            Get
                Return Not _mode.ForcedByArgument
            End Get
        End Property

        Private _failingSource As String = String.Empty
        Public Property FailingSource As String
            Get
                Return _failingSource
            End Get
            Set(value As String)
                SetProperty(_failingSource, value)
            End Set
        End Property

        Private _simulatedTime As String = String.Empty
        Public Property SimulatedTime As String
            Get
                Return _simulatedTime
            End Get
            Set(value As String)
                SetProperty(_simulatedTime, value)
            End Set
        End Property

        Public ReadOnly Property HasSavedPassword As Boolean
            Get
                Return _credentials.HasSaved
            End Get
        End Property

        Private _errorText As String = String.Empty
        Public Property ErrorText As String
            Get
                Return _errorText
            End Get
            Private Set(value As String)
                SetProperty(_errorText, value)
            End Set
        End Property

#End Region

        ''' <summary>Accepts "150000", "150,000" and "150000.50" (comma = thousands, like the screen shows it).</summary>
        Public Shared Function TryParseAmount(text As String, ByRef value As Decimal) As Boolean
            Dim clean = If(text, String.Empty).Trim().Replace("$", "").Replace("US", "").Replace(" ", "")
            Return Decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, value)
        End Function

        Private Shared Function TryParseInt(text As String, min As Integer, max As Integer, ByRef value As Integer) As Boolean
            Return Integer.TryParse(If(text, String.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, value) AndAlso value >= min AndAlso value <= max
        End Function

        Private Sub Save()
            Dim errors As New List(Of String)()

            Dim days, seconds, minutes, operation As Integer
            If Not TryParseInt(HistoryDays, DashboardPreferences.MinDays, DashboardPreferences.MaxDays, days) Then errors.Add($"Los días de histórico deben estar entre {DashboardPreferences.MinDays} y {DashboardPreferences.MaxDays}.")
            If Not TryParseInt(RotateSeconds, DashboardPreferences.MinRotateSeconds, DashboardPreferences.MaxRotateSeconds, seconds) Then errors.Add($"Los segundos del cambio automático deben estar entre {DashboardPreferences.MinRotateSeconds} y {DashboardPreferences.MaxRotateSeconds}.")
            If Not TryParseInt(RefreshMinutes, DashboardSettings.MinRefreshMinutes, DashboardSettings.MaxRefreshMinutes, minutes) Then errors.Add($"La actualización debe ser de {DashboardSettings.MinRefreshMinutes} a {DashboardSettings.MaxRefreshMinutes} minutos.")
            If Not TryParseInt(AssemblyOperation, 1, Integer.MaxValue, operation) Then errors.Add("La operación de Y1 debe ser un número entero.")
            Dim stationsDiv, priceDiv As Decimal
            If Not TryParseAmount(StationsDivisor, stationsDiv) OrElse stationsDiv <= 0D Then errors.Add("El divisor de cantidades debe ser mayor que 0.")
            If Not String.IsNullOrWhiteSpace(SimulatedTime) AndAlso Not AppClock.ParseSimulated(SimulatedTime, Date.Today).HasValue Then errors.Add("La hora simulada del modo demo debe ser HH:mm o aaaa-mm-dd HH:mm (o vacía).")
            If Not TryParseAmount(PriceDivisor, priceDiv) OrElse priceDiv <= 0D Then errors.Add("El divisor de precio debe ser mayor que 0.")

            Dim built As New List(Of AreaDefinition)()
            For Each row In Areas
                Dim area As AreaDefinition = Nothing
                Dim message As String = Nothing
                If row.TryBuild(area, message) Then built.Add(area) Else errors.Add(message)
            Next
            If built.Count > 0 AndAlso Not built.Any(Function(a) a.Active) Then errors.Add("Debe quedar al menos un área visible.")

            Dim current = _jde.CurrentValue
            Dim jde As New JdeSettings With {
                .Dsn = If(Dsn, String.Empty).Trim(), .User = If(User, String.Empty).Trim().ToUpperInvariant(), .UseDriverSignOn = UseDriverSignOn,
                .Branch = If(Branch, String.Empty).Trim(),
                .AssemblyLibrary = If(AssemblyLibrary, String.Empty).Trim().ToUpperInvariant(), .StationsLibrary = If(StationsLibrary, String.Empty).Trim().ToUpperInvariant(),
                .PricesLibrary = If(PricesLibrary, String.Empty).Trim().ToUpperInvariant(), .DcLinkLibrary = If(DcLinkLibrary, String.Empty).Trim().ToUpperInvariant(),
                .PriceType = If(PriceType, String.Empty).Trim().ToUpperInvariant(), .AssemblyOperation = operation,
                .ShipmentTransaction = If(ShipmentTransaction, String.Empty).Trim().ToUpperInvariant(),
                .StationsQuantityDivisor = If(stationsDiv > 0D, stationsDiv, 1D), .PriceDivisor = If(priceDiv > 0D, priceDiv, 1D),
                .CommandTimeoutSeconds = current.CommandTimeoutSeconds, .ConnectionTimeoutSeconds = current.ConnectionTimeoutSeconds}
            errors.AddRange(jde.Validate())

            If errors.Count > 0 Then
                ErrorText = String.Join(Environment.NewLine, errors.Distinct())
                Return
            End If

            Dim dashboard = _dashboard.CurrentValue
            Dim newDashboard As New DashboardSettings With {
                .RefreshMinutes = minutes, .ExcludeSundays = ExcludeSundays, .StartFullScreen = StartFullScreen, .DefaultDailyGoal = dashboard.DefaultDailyGoal,
                .ShiftStart = dashboard.ShiftStart, .ShiftEnd = dashboard.ShiftEnd, .BreakStart = dashboard.BreakStart, .BreakEnd = dashboard.BreakEnd,
                .MinMinutesForProjection = dashboard.MinMinutesForProjection}
            Dim demo = _demo.CurrentValue
            Dim newDemo As New DemoSettings With {
                .Enabled = If(_mode.ForcedByArgument, demo.Enabled, DemoEnabled), .QueryDelayMilliseconds = demo.QueryDelayMilliseconds, .FailingSource = If(FailingSource, String.Empty), .SimulatedTime = If(SimulatedTime, String.Empty).Trim()}

            If jde.User <> current.User OrElse jde.Dsn <> current.Dsn Then _credentials.Delete()

            _prefs.Language = If(English, "EN", "ES")
            _prefs.HistoryDays = days
            _prefs.AutoRotate = AutoRotate
            _prefs.RotateSeconds = seconds
            _prefs.Areas = built
            _prefs.Normalize(dashboard)
            _store.Save(_prefs)
            _writer.Save(jde, newDashboard, newDemo)
            ErrorText = String.Empty
            RaiseEvent Saved(Me, EventArgs.Empty)
        End Sub

        Private Sub ForgetPassword()
            If Not _credentials.HasSaved Then
                _dialogs.ShowInfo("No hay contraseña guardada.")
                Return
            End If
            If _dialogs.Confirm("¿Olvidar la contraseña de JDE guardada en esta PC? Se pedirá en la próxima actualización.") Then
                _credentials.Delete()
                OnPropertyChanged(NameOf(HasSavedPassword))
            End If
        End Sub

        Private Sub RestoreCompany()
            If Not _dialogs.Confirm("¿Volver a los valores de conexión de la empresa (appsettings.empresa.json)? Las metas y nombres de las áreas no cambian.") Then Return
            _writer.Reset()
            LoadConnection(_jde.CurrentValue, _dashboard.CurrentValue, _demo.CurrentValue)
        End Sub

    End Class

End Namespace
