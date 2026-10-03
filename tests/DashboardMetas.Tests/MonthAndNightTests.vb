Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Models
Imports Xunit

Public Class NightShiftTests

    Private Shared Function Create(start As String, [end] As String, Optional b0 As String = "", Optional b1 As String = "") As ShiftSchedule
        Dim schedule As ShiftSchedule = Nothing
        Dim err As String = Nothing
        Assert.True(ShiftSchedule.TryCreate(start, [end], b0, b1, schedule, err), err)
        Return schedule
    End Function

    <Fact>
    Public Sub CrossesMidnight_CountsBothPartsOfTheCalendarDay()
        Dim night = Create("22:00", "06:00")
        Assert.True(night.CrossesMidnight)
        Assert.Equal(480.0, night.WorkMinutes, 6)                  ' 00:00–06:00 + 22:00–24:00
        Assert.Equal(2, night.Windows.Count)
        Assert.Equal(0.0, night.Progress(New TimeSpan(0, 0, 0)), 6)
        Assert.Equal(0.75, night.Progress(New TimeSpan(6, 0, 0)), 6)  ' 360 of 480
        Assert.Equal(0.75, night.Progress(New TimeSpan(15, 0, 0)), 6) ' flat during the day
        Assert.Equal(1.0, night.Progress(New TimeSpan(23, 59, 59)), 2)
        Assert.False(night.IsWorking(New TimeSpan(12, 0, 0)))
        Assert.True(night.IsWorking(New TimeSpan(23, 0, 0)))
        Assert.Equal(New TimeSpan(22, 0, 0), night.NextStart(New TimeSpan(7, 0, 0)))
        Assert.Equal("22:00–06:00 (+1)", night.Describe())
    End Sub

    <Fact>
    Public Sub SecondShiftEndingAfterMidnight()
        Dim second = Create("15:00", "01:00")
        Assert.Equal(600.0, second.WorkMinutes, 6)                  ' 00:00–01:00 + 15:00–24:00
        Assert.Equal(60.0 / 600.0, second.Progress(New TimeSpan(9, 0, 0)), 6)
    End Sub

    <Fact>
    Public Sub BreakAfterMidnight()
        Dim night = Create("22:00", "06:00", "02:00", "02:30")
        Assert.Equal(450.0, night.WorkMinutes, 6)
        Assert.True(night.IsOnBreak(New TimeSpan(2, 10, 0)))
        Assert.Equal(night.Progress(New TimeSpan(2, 0, 0)), night.Progress(New TimeSpan(2, 29, 0)), 6)
    End Sub

    <Fact>
    Public Sub Pace_OnNightShift()
        ' 03:00: 180 of 480 productive minutes of the calendar day → expected 37.5% of the goal
        Dim s = DashboardCalculator.Build(80000D, {Row("Y1", #10/02/2026#, 20000D)}, #10/02/2026 03:00#, 10, True, Create("22:00", "06:00"))
        Assert.Equal(30000D, s.ExpectedNow)
        Assert.Equal(PaceState.Behind, s.Pace)
        Assert.True(s.HasProjection)
        ' Before anything is worked on the calendar day (00:00 exactly) it has not started
        Dim early = DashboardCalculator.Build(80000D, Array.Empty(Of DailyProduction)(), #10/02/2026 00:00#, 10, True, Create("22:00", "06:00"))
        Assert.Equal(PaceState.NotStarted, early.Pace)
    End Sub

End Class

Public Class MonthTests

    ' October 2026: 31 days, 4 Sundays → 27 working days.
    <Fact>
    Public Sub AutomaticGoal_IsDailyGoalTimesWorkingDays()
        Dim m = MonthCalculator.Build(100000D, 0D, Array.Empty(Of DailyProduction)(), #10/15/2026 12:00#, True)
        Assert.Equal(27, m.WorkingDays)
        Assert.True(m.IsAutomaticGoal)
        Assert.Equal(2700000D, m.MonthlyGoal)
    End Sub

    <Fact>
    Public Sub TypedGoal_AndAccumulated()
        Dim rows = {Row("Y1", #09/30/2026#, 999D), Row("Y1", #10/01/2026#, 100D), Row("Y1", #10/02/2026#, 50D), Row("Y1", #10/03/2026#, 25D)}
        Dim m = MonthCalculator.Build(100000D, 5000D, rows, #10/03/2026 23:00#, True)
        Assert.False(m.IsAutomaticGoal)
        Assert.Equal(5000D, m.MonthlyGoal)
        Assert.Equal(175D, m.MonthToDate)                   ' September is not counted
        Assert.Equal(150D, m.Days(1).Cumulative)
        Assert.Equal(0D, m.Days.Last().Cumulative)          ' future days have no accumulated value
        Assert.Equal(5000D, m.Days.Last().Target)
    End Sub

    <Fact>
    Public Sub Expected_UsesTodaysShiftProgress()
        ' Oct 15 at 11:20: 12 closed working days (1–14 without Sundays 4 and 11) + half of today
        Dim m = MonthCalculator.Build(100000D, 0D, Array.Empty(Of DailyProduction)(), #10/15/2026 11:20#, True, ShiftSchedule.Default)
        Assert.Equal(12.5, m.ElapsedDays, 6)
        Assert.Equal(1250000D, m.ExpectedToDate)
    End Sub

    <Fact>
    Public Sub Projection_FromTheThirdDay()
        Dim rows = Enumerable.Range(1, 14).Select(Function(d) New Date(2026, 10, d)).Where(Function(d) d.DayOfWeek <> DayOfWeek.Sunday).Select(Function(d) Row("Y1", d, 90000D)).ToArray()
        Dim m = MonthCalculator.Build(100000D, 0D, rows, #10/15/2026 00:00#, True, ShiftSchedule.Default)
        Assert.True(m.HasProjection)
        Assert.False(m.IsOnTrack)
        Assert.True(m.Projection < m.MonthlyGoal)
        Dim early = MonthCalculator.Build(100000D, 0D, rows, #10/02/2026 10:00#, True, ShiftSchedule.Default)
        Assert.False(early.HasProjection)
    End Sub

    <Fact>
    Public Sub QueryStart_CoversTheWholeMonth()
        Assert.Equal(#10/01/2026#, DashboardCalculator.QueryStart(#10/28/2026#, 10))
        Assert.Equal(#09/18/2026#, DashboardCalculator.QueryStart(#10/02/2026#, 10))
    End Sub

End Class
