Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Models
Imports Microsoft.Extensions.Options

Namespace Demo

    ''' <summary>
    ''' Invented data with the same shape JDE returns, to try the whole app without an AS400.
    ''' Each area and day always gives the same numbers (stable between refreshes); today's value grows
    ''' during the default shift (Dashboard:ShiftStart/ShiftEnd), some days ahead of pace and others behind. No production on Sundays.
    ''' Password "incorrecta" simulates CWBSY0002; Demo:FailingSource makes one query fail.
    ''' </summary>
    Public NotInheritable Class DemoProductionRepository
        Implements IProductionRepository

        Public Const WrongPassword As String = "incorrecta"

        Private ReadOnly _demo As IOptionsMonitor(Of DemoSettings)
        Private ReadOnly _dashboard As IOptionsMonitor(Of DashboardSettings)
        Private ReadOnly _clock As IClock
        Private ReadOnly _shifts As IAreaShiftProvider
        Private ReadOnly _jde As IOptionsMonitor(Of JdeSettings)

        Public Sub New(demo As IOptionsMonitor(Of DemoSettings), dashboard As IOptionsMonitor(Of DashboardSettings), clock As IClock,
                       Optional shifts As IAreaShiftProvider = Nothing, Optional jde As IOptionsMonitor(Of JdeSettings) = Nothing)
            _demo = demo
            _dashboard = dashboard
            _clock = clock
            _shifts = shifts
            _jde = jde
        End Sub

        Public ReadOnly Property IsDemo As Boolean Implements IProductionRepository.IsDemo
            Get
                Return True
            End Get
        End Property

        Public ReadOnly Property SourceDescription As String Implements IProductionRepository.SourceDescription
            Get
                Return "DEMO (datos inventados)"
            End Get
        End Property

        Public Async Function OpenSessionAsync(user As String, password As String, cancellationToken As CancellationToken) As Task(Of IProductionSession) Implements IProductionRepository.OpenSessionAsync
            Await Task.Delay(Math.Max(0, _demo.CurrentValue.QueryDelayMilliseconds \ 2), cancellationToken).ConfigureAwait(False)
            If String.Equals(password, WrongPassword, StringComparison.OrdinalIgnoreCase) Then
                Throw New JdeConnectionException(JdeConnectionErrorKind.InvalidPassword,
                    $"Contraseña incorrecta o vencida para {user} (CWBSY0002).", "CWBSY0002 - Password is incorrect (simulado en modo demo)")
            End If
            Dim fallback = _dashboard.CurrentValue.DefaultShift()
            Dim shiftOf = Function(code As String) If(_shifts?.ShiftOf(code), fallback)
            Dim statuses = If(_jde?.CurrentValue, New JdeSettings()).FloatStatusList()
            Return New DemoProductionSession(_demo.CurrentValue, _dashboard.CurrentValue.DefaultDailyGoal, shiftOf, _clock, statuses)
        End Function

    End Class

    Friend NotInheritable Class DemoProductionSession
        Implements IProductionSession

        ' Level of each area relative to the goal: some meet it most days, others rarely
        Private Shared ReadOnly Levels As New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase) From {
            {"Y1", 1.02}, {"Y2", 0.93}, {"Y3", 1.08}, {"Y5", 0.97}, {"Y7", 0.86}, {"SHP", 1.12}}

        Private ReadOnly _settings As DemoSettings
        Private ReadOnly _goal As Decimal
        Private ReadOnly _shiftOf As Func(Of String, ShiftSchedule)
        Private ReadOnly _clock As IClock
        Private ReadOnly _floatStatuses As IReadOnlyList(Of String)

        ' Product lines of the demo float: more than the chart colours, so "Otras" also shows up
        Private Shared ReadOnly DemoLines As String() = {"CASEGOODS", "UPHOLSTERY", "DINING", "BEDROOM", "OCCASIONAL", "LIGHTING", "OUTDOOR"}

        Public Sub New(settings As DemoSettings, goal As Decimal, shiftOf As Func(Of String, ShiftSchedule), clock As IClock,
                       Optional floatStatuses As IReadOnlyList(Of String) = Nothing)
            _settings = settings
            _goal = If(goal > 0D, goal, 150000D)
            _shiftOf = shiftOf
            _clock = clock
            _floatStatuses = If(floatStatuses IsNot Nothing AndAlso floatStatuses.Count > 0, floatStatuses, New JdeSettings().FloatStatusList())
        End Sub

        Public ReadOnly Property DriverInfo As String = "Demo" Implements IProductionSession.DriverInfo

        Public Async Function GetDailyAsync(source As ProductionSource, fromDate As Date, cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of DailyProduction)) Implements IProductionSession.GetDailyAsync
            Await Task.Delay(Math.Max(0, _settings.QueryDelayMilliseconds), cancellationToken).ConfigureAwait(False)

            Dim failing As ProductionSource
            If AreaCatalog.TryParseSource(_settings.FailingSource, failing) AndAlso failing = source Then
                Throw New InvalidOperationException($"Error de JDE en la consulta de {AreaCatalog.SourceName(source)} (42704): " &
                                                    "[IBM][System i Access ODBC Driver][DB2 for i5/OS]SQL0204 - Objeto no encontrado (simulado en modo demo).")
            End If

            Dim now = _clock.Now
            Dim rows As New List(Of DailyProduction)()
            For Each code In AreaCatalog.CodesOf(source)
                Dim d = fromDate.Date
                While d <= now.Date
                    Dim row = Generate(code, d, now)
                    If row IsNot Nothing Then rows.Add(row)
                    d = d.AddDays(1)
                End While
            Next
            Return rows
        End Function

        ''' <summary>
        ''' An invented float: about 90 styles spread over the statuses (more in the first ones), most with a W01
        ''' price and a product line, a few without either (to see those warnings). It moves a little every
        ''' hour, like a real float during the day.
        ''' </summary>
        Public Async Function GetFloatAsync(classifyFrom As Date, cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of FloatItem)) Implements IProductionSession.GetFloatAsync
            Await Task.Delay(Math.Max(0, _settings.QueryDelayMilliseconds), cancellationToken).ConfigureAwait(False)
            Dim failing As ProductionSource
            If AreaCatalog.TryParseSource(_settings.FailingSource, failing) AndAlso failing = ProductionSource.Float Then
                Throw New InvalidOperationException("Error de JDE en la consulta de Float (42704): " &
                                                    "[IBM][System i Access ODBC Driver][DB2 for i5/OS]SQL0204 - F4801 no encontrado (simulado en modo demo).")
            End If

            Dim now = _clock.Now
            Dim items As New List(Of FloatItem)()
            For s = 0 To 89
                Dim style = "DM" & (10000 + s * 37).ToString("00000", Globalization.CultureInfo.InvariantCulture) & "AB"
                Dim random As New Random(StableSeed(style))
                Dim line = If(random.Next(0, 14) = 0, String.Empty, DemoLines(Math.Min(DemoLines.Length - 1, CInt(Math.Floor(Math.Pow(random.NextDouble(), 1.6) * DemoLines.Length)))))
                Dim price = If(random.Next(0, 18) = 0, 0D, 240D + CDec(random.Next(0, 900)))
                ' Each style sits in one or two statuses; the first statuses hold more
                Dim homes = If(random.Next(0, 4) = 0, 2, 1)
                For h = 0 To homes - 1
                    Dim index = Math.Min(_floatStatuses.Count - 1, CInt(Math.Floor(Math.Pow(random.NextDouble(), 1.3) * _floatStatuses.Count)))
                    Dim hourly As New Random(StableSeed(style & h.ToString(Globalization.CultureInfo.InvariantCulture) & now.ToString("yyyyMMddHH", Globalization.CultureInfo.InvariantCulture)))
                    Dim pieces = CDec(4 + random.Next(0, 60) + hourly.Next(-3, 4))
                    If pieces <= 0D Then Continue For
                    items.Add(New FloatItem With {.Status = _floatStatuses(index), .Style = style, .ProductLine = line,
                                                  .Pieces = pieces, .Value = pieces * price, .Orders = 1 + random.Next(0, 4)})
                Next
            Next
            ' Same style twice in the same status = one row, like the GROUP BY of the query
            Return items.GroupBy(Function(i) (i.Status, i.Style)).
                Select(Function(g) New FloatItem With {.Status = g.Key.Status, .Style = g.Key.Style, .ProductLine = g.First().ProductLine,
                                                        .Pieces = g.Sum(Function(i) i.Pieces), .Value = g.Sum(Function(i) i.Value), .Orders = g.Sum(Function(i) i.Orders)}).
                ToList()
        End Function

        ''' <summary>Nothing = no record that day (Sunday, holiday or before the shift starts).</summary>
        Private Function Generate(code As String, d As Date, now As Date) As DailyProduction
            If d.DayOfWeek = DayOfWeek.Sunday Then Return Nothing
            Dim random As New Random(StableSeed(code & d.ToString("yyyyMMdd", Globalization.CultureInfo.InvariantCulture)))
            If random.Next(0, 23) = 0 Then Return Nothing  ' holiday / line stopped

            Dim level As Double = 1.0
            Levels.TryGetValue(code, level)
            Dim factor = level * (0.72 + random.NextDouble() * 0.55)
            If d.DayOfWeek = DayOfWeek.Saturday Then factor *= 0.45

            If d = now.Date Then
                Dim progress = _shiftOf(code).Progress(now.TimeOfDay)
                If progress <= 0 Then Return Nothing
                ' Exponent < 1: strong start (ahead of pace); > 1: slow start (behind)
                Dim curve = 0.8 + random.NextDouble() * 0.45
                factor *= Math.Pow(progress, curve)
            End If

            Dim value = Math.Round(_goal * CDec(factor), 2)
            Dim unitPrice = 380D + CDec(random.Next(0, 260))
            Dim pieces = Math.Max(1D, Math.Round(value / unitPrice, 0))
            Dim styles = Math.Max(1, CInt(pieces / CDec(6 + random.Next(0, 12))))
            Return New DailyProduction With {.Area = code, .Date = d, .Value = value, .Pieces = pieces, .Styles = styles}
        End Function

        ''' <summary>Same seed in every process (String.GetHashCode is randomized per process).</summary>
        Private Shared Function StableSeed(text As String) As Integer
            Dim hash As UInteger = 2166136261UI
            For Each ch In text
                hash = CUInt((CULng(hash Xor CUInt(AscW(ch))) * 16777619UL) And &HFFFFFFFFUL)
            Next
            Return CInt(hash And &H7FFFFFFFUI)
        End Function

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return ValueTask.CompletedTask
        End Function

    End Class

End Namespace
