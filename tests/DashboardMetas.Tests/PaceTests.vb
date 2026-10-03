Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Models
Imports Xunit

Public Class ShiftScheduleTests

    ' 06:30 to 16:10 = 580 productive minutes
    Private Shared ReadOnly Shift As ShiftSchedule = ShiftSchedule.Default

    <Fact>
    Public Sub Default_Is0630To1610()
        Assert.Equal(New TimeSpan(6, 30, 0), Shift.Start)
        Assert.Equal(New TimeSpan(16, 10, 0), Shift.End)
        Assert.Equal(580.0, Shift.WorkMinutes, 6)
    End Sub

    <Theory>
    <InlineData(5, 0, 0.0)>
    <InlineData(6, 30, 0.0)>
    <InlineData(11, 20, 0.5)>
    <InlineData(16, 10, 1.0)>
    <InlineData(20, 0, 1.0)>
    Public Sub Progress(hour As Integer, minute As Integer, expected As Double)
        Assert.Equal(expected, Shift.Progress(New TimeSpan(hour, minute, 0)), 6)
    End Sub

    <Fact>
    Public Sub Break_DoesNotCount_AndProgressIsFlatDuringIt()
        Dim withLunch As ShiftSchedule = Nothing
        Dim err As String = Nothing
        Assert.True(ShiftSchedule.TryCreate("06:30", "16:10", "12:00", "12:30", withLunch, err))
        Assert.Equal(550.0, withLunch.WorkMinutes, 6)
        Assert.Equal(withLunch.Progress(New TimeSpan(12, 0, 0)), withLunch.Progress(New TimeSpan(12, 25, 0)), 6)
        Assert.True(withLunch.IsOnBreak(New TimeSpan(12, 10, 0)))
        Assert.Equal(330.0 / 550.0, withLunch.Progress(New TimeSpan(12, 30, 0)), 6)
    End Sub

    <Theory>
    <InlineData("6:30", "16:10", "", "", True)>
    <InlineData("06:30", "06:30", "", "", False)>
    <InlineData("22:00", "06:00", "", "", True)>
    <InlineData("22:00", "06:00", "02:00", "02:30", True)>
    <InlineData("22:00", "06:00", "12:00", "12:30", False)>
    <InlineData("6.30", "16:10", "", "", False)>
    <InlineData("06:30", "16:10", "12:00", "", False)>
    <InlineData("06:30", "16:10", "05:00", "05:30", False)>
    <InlineData("06:30", "16:10", "12:30", "12:00", False)>
    Public Sub TryCreate_Validates(start As String, [end] As String, b0 As String, b1 As String, ok As Boolean)
        Dim schedule As ShiftSchedule = Nothing
        Dim err As String = Nothing
        Assert.Equal(ok, ShiftSchedule.TryCreate(start, [end], b0, b1, schedule, err))
        If Not ok Then Assert.False(String.IsNullOrEmpty(err))
    End Sub

    <Fact>
    Public Sub Normalize_GivesEveryAreaTheDefaultShift_KeepsCustomOnes()
        Dim prefs As New DashboardPreferences With {
            .Areas = New List(Of AreaDefinition) From {New AreaDefinition With {.Code = "SHP", .ShiftStart = "07:00", .ShiftEnd = "17:00", .BreakStart = "12:00", .BreakEnd = "13:00"}}}
        prefs.Normalize(New DashboardSettings())
        Assert.Equal("06:30", prefs.FindArea("Y1").ShiftStart)
        Assert.Equal("16:10", prefs.FindArea("Y1").ShiftEnd)
        Assert.Equal("", prefs.FindArea("Y1").BreakStart)
        Assert.Equal("07:00", prefs.FindArea("SHP").ShiftStart)
        Assert.True(prefs.FindArea("SHP").Shift().HasBreak)
    End Sub

    <Fact>
    Public Sub InvalidSavedShift_FallsBackToDefault()
        Dim area As New AreaDefinition With {.Code = "Y1", .ShiftStart = "xx", .ShiftEnd = "16:10"}
        Assert.Equal(ShiftSchedule.Default.Start, area.Shift().Start)
    End Sub

End Class

Public Class PaceTests

    ' Friday 2026-10-02, shift 06:30–16:10, goal $100,000. At 11:20 half the shift has been worked.
    Private Shared Function Build(time As Date, todayValue As Decimal, Optional minMinutes As Integer = 30) As DashboardSnapshot
        Return DashboardCalculator.Build(100000D, {Row("Y1", #10/02/2026#, todayValue), Row("Y1", #10/01/2026#, 90000D)},
                                         time, 10, True, ShiftSchedule.Default, minMinutes)
    End Function

    <Fact>
    Public Sub OnTrack_WhenAboveExpectedByNow()
        Dim s = Build(#10/02/2026 11:20#, 55000D)
        Assert.Equal(PaceState.OnTrack, s.Pace)
        Assert.Equal(0.5, s.ShiftProgress, 6)
        Assert.Equal(50000D, s.ExpectedNow)
        Assert.Equal(5000D, s.PaceGap)
    End Sub

    <Fact>
    Public Sub Behind_WithGapAndProjection()
        Dim s = Build(#10/02/2026 11:20#, 40000D)
        Assert.Equal(PaceState.Behind, s.Pace)
        Assert.Equal(10000D, s.PaceGap)
        Assert.True(s.HasProjection)
        Assert.Equal(80000D, s.Projection)
    End Sub

    <Fact>
    Public Sub Projection_ExtendsTheAxis()
        Dim s = Build(#10/02/2026 08:00#, 60000D)   ' 90 of 580 minutes → projection ≈ $386,667
        Assert.True(s.Projection > 380000D)
        Assert.True(s.AxisMax >= CDbl(s.Projection))
    End Sub

    <Fact>
    Public Sub NoProjection_InTheFirstMinutes()
        Dim s = Build(#10/02/2026 06:50#, 3000D)
        Assert.False(s.HasProjection)
        Assert.NotEqual(PaceState.NotStarted, s.Pace)
    End Sub

    <Fact>
    Public Sub NotStarted_BeforeShift()
        Dim s = Build(#10/02/2026 06:00#, 0D)
        Assert.Equal(PaceState.NotStarted, s.Pace)
        Assert.Equal(0D, s.ExpectedNow)
        Assert.False(s.HasProjection)
    End Sub

    <Fact>
    Public Sub GoalMet_WinsOverEverything()
        Assert.Equal(PaceState.GoalMet, Build(#10/02/2026 09:00#, 100000D).Pace)
        Assert.Equal(PaceState.GoalMet, Build(#10/02/2026 18:00#, 120000D).Pace)
    End Sub

    <Fact>
    Public Sub ShiftEnded_BelowGoal_NoProjection()
        Dim s = Build(#10/02/2026 17:00#, 70000D)
        Assert.Equal(PaceState.ShiftEnded, s.Pace)
        Assert.False(s.HasProjection)
        Assert.Equal(100000D, s.ExpectedNow)
    End Sub

    <Fact>
    Public Sub Sunday_NoPace()
        Dim s = DashboardCalculator.Build(100000D, Array.Empty(Of DailyProduction)(), #10/04/2026 11:00#, 10, True, ShiftSchedule.Default)
        Assert.Equal(PaceState.NoShift, s.Pace)
    End Sub

    <Fact>
    Public Sub WithoutShift_NoPace()
        Dim s = DashboardCalculator.Build(100000D, {Row("Y1", #10/02/2026#, 5D)}, #10/02/2026 11:00#, 10, True)
        Assert.Equal(PaceState.NoShift, s.Pace)
        Assert.False(s.HasProjection)
    End Sub

End Class
