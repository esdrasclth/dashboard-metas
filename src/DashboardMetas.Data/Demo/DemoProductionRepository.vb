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

        Public Sub New(demo As IOptionsMonitor(Of DemoSettings), dashboard As IOptionsMonitor(Of DashboardSettings), clock As IClock,
                       Optional shifts As IAreaShiftProvider = Nothing)
            _demo = demo
            _dashboard = dashboard
            _clock = clock
            _shifts = shifts
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
            Return New DemoProductionSession(_demo.CurrentValue, _dashboard.CurrentValue.DefaultDailyGoal, shiftOf, _clock)
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

        Public Sub New(settings As DemoSettings, goal As Decimal, shiftOf As Func(Of String, ShiftSchedule), clock As IClock)
            _settings = settings
            _goal = If(goal > 0D, goal, 150000D)
            _shiftOf = shiftOf
            _clock = clock
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
