Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Dashboard
Imports Microsoft.Extensions.Options

Namespace Services

    ''' <summary>
    ''' The real clock, except in demo mode with Demo:SimulatedTime ("11:20"): then the day starts at that
    ''' time (or "2026-10-20 11:20": that day and time) and runs in real time, to try the pace and the projection at any hour of the shift.
    ''' </summary>
    Public NotInheritable Class AppClock
        Implements IClock

        Private ReadOnly _mode As AppMode
        Private ReadOnly _demo As IOptionsMonitor(Of DemoSettings)
        Private ReadOnly _started As Date = Date.Now

        Public Sub New(mode As AppMode, demo As IOptionsMonitor(Of DemoSettings))
            _mode = mode
            _demo = demo
        End Sub

        Public ReadOnly Property Now As Date Implements IClock.Now
            Get
                Dim real = Date.Now
                If Not _mode.IsDemo Then Return real
                Dim start = ParseSimulated(_demo.CurrentValue.SimulatedTime, _started.Date)
                If Not start.HasValue Then Return real
                Return start.Value + (real - _started)
            End Get
        End Property

        ''' <summary>"11:20" (today at that time) or "2026-10-20 11:20" (that day and time). Nothing = invalid or empty.</summary>
        Public Shared Function ParseSimulated(text As String, today As Date) As Date?
            Dim clean = If(text, String.Empty).Trim()
            Dim time = ShiftSchedule.ParseTime(clean)
            If time.HasValue Then Return today.Date + time.Value
            Dim full As Date
            If Date.TryParseExact(clean, {"yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm"}, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None, full) Then Return full
            Return Nothing
        End Function

    End Class

End Namespace
