Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Formatting
Imports DashboardMetas.Core.Models
Imports Xunit

' 2026-10-02 is a Friday; 2026-10-04 a Sunday; 2026-09-28 a Monday.
Public Class CalendarTests

    <Fact>
    Public Sub VisibleDays_EndToday_SkipSundays()
        Dim days = DashboardCalculator.VisibleDays(#10/02/2026#, 10, excludeSundays:=True)
        Assert.Equal(10, days.Count)
        Assert.Equal(#10/02/2026#, days.Last())
        Assert.DoesNotContain(days, Function(d) d.DayOfWeek = DayOfWeek.Sunday)
        Assert.Equal(#09/22/2026#, days.First())   ' 10 working days: 22..26 and 28..02 (27 is Sunday)
    End Sub

    <Fact>
    Public Sub VisibleDays_OnSunday_TodayIsNotShown()
        Dim days = DashboardCalculator.VisibleDays(#10/04/2026#, 5, excludeSundays:=True)
        Assert.Equal(#10/03/2026#, days.Last())
    End Sub

    <Fact>
    Public Sub VisibleDays_WithSundays()
        Dim days = DashboardCalculator.VisibleDays(#10/04/2026#, 5, excludeSundays:=False)
        Assert.Equal({#09/30/2026#, #10/01/2026#, #10/02/2026#, #10/03/2026#, #10/04/2026#}, days)
    End Sub

    <Fact>
    Public Sub QueryStart_SameMarginAsVbs()
        ' N + N \ 6 + 3 calendar days back
        Assert.Equal(#10/02/2026#.AddDays(-14), DashboardCalculator.QueryStart(#10/02/2026 15:30#, 10))
        Assert.Equal(#10/02/2026#.AddDays(-39), DashboardCalculator.QueryStart(#10/02/2026#, 31))
    End Sub

    <Theory>
    <InlineData(0.0, 1.0)>
    <InlineData(33000.0, 40000.0)>
    <InlineData(15000.0, 15000.0)>
    <InlineData(15001.0, 20000.0)>
    <InlineData(26000.0, 30000.0)>
    <InlineData(700.0, 800.0)>
    <InlineData(90000.0, 100000.0)>
    Public Sub NiceStep(value As Double, expected As Double)
        Assert.Equal(expected, DashboardCalculator.NiceStep(value), 6)
    End Sub

End Class

Public Class CalculatorTests

    Private Shared ReadOnly Today As Date = #10/02/2026 11:00#

    Private Shared Function Build(goal As Decimal, ParamArray rows As DailyProduction()) As DashboardSnapshot
        Return DashboardCalculator.Build(goal, rows, Today, 10, excludeSundays:=True)
    End Function

    <Fact>
    Public Sub TodayKpis()
        Dim s = Build(100000D, Row("Y1", #10/02/2026#, 45000D, 120D, 9), Row("Y1", #10/01/2026#, 120000D))
        Assert.True(s.HasShiftToday)
        Assert.Equal(45000D, s.TodayValue)
        Assert.Equal(0.45, s.Attainment, 6)
        Assert.False(s.IsGoalMet)
        Assert.Equal(55000D, s.GapToGoal)
        Assert.Equal(BarState.InProgress, s.Days.Last().State)
    End Sub

    <Fact>
    Public Sub GoalMetToday_IsTealAndAboveGoal()
        Dim s = Build(100000D, Row("Y1", #10/02/2026#, 130000D))
        Assert.True(s.IsGoalMet)
        Assert.Equal(30000D, s.GapToGoal)
        Assert.Equal(BarState.GoalMet, s.Days.Last().State)
    End Sub

    <Fact>
    Public Sub LastClosedDay_IsMostRecentBeforeTodayWithData()
        Dim s = Build(100000D, Row("Y1", #10/02/2026#, 5000D), Row("Y1", #09/30/2026#, 90000D))
        Assert.Equal(#09/30/2026#, s.LastClosed.Date)
        Assert.True(s.LastClosed.IsLastClosed)
        Assert.Equal(BarState.BelowGoal, s.LastClosed.State)
    End Sub

    <Fact>
    Public Sub LastClosedDay_WithoutData_IsYesterday()
        Dim s = Build(100000D)
        Assert.Equal(#10/01/2026#, s.LastClosed.Date)
    End Sub

    <Fact>
    Public Sub WeekToDate_FromMonday()
        Dim s = Build(100000D, Row("Y1", #09/26/2026#, 999D), Row("Y1", #09/28/2026#, 10D), Row("Y1", #10/01/2026#, 20D), Row("Y1", #10/02/2026#, 30D))
        Assert.Equal(#09/28/2026#, s.WeekStart)
        Assert.Equal(60D, s.WeekToDate)
    End Sub

    <Fact>
    Public Sub AverageAndDaysOnGoal_OnlyClosedDaysWithData()
        Dim s = Build(100000D, Row("Y1", #09/29/2026#, 150000D), Row("Y1", #09/30/2026#, 50000D), Row("Y1", #10/01/2026#, 100000D), Row("Y1", #10/02/2026#, 200000D))
        Assert.Equal(3, s.ClosedDaysWithData)
        Assert.Equal(100000D, s.ClosedAverage)
        Assert.Equal(2, s.DaysOnGoal)
    End Sub

    <Fact>
    Public Sub Axis_AlwaysShowsGoalWithRoom()
        Dim s = Build(150000D, Row("Y1", #10/01/2026#, 50000D))
        Assert.True(s.AxisMax >= 150000 * 1.1)
        Assert.Equal(s.AxisStep * 5, s.AxisMax, 6)
        Dim big = Build(150000D, Row("Y1", #10/01/2026#, 400000D))
        Assert.True(big.AxisMax >= 400000 * 1.1)
    End Sub

    <Fact>
    Public Sub Sunday_NoShift()
        Dim s = DashboardCalculator.Build(100000D, {Row("Y1", #10/03/2026#, 80000D)}, #10/04/2026 10:00#, 10, excludeSundays:=True)
        Assert.False(s.HasShiftToday)
        Assert.Equal(0D, s.TodayValue)
        Assert.Equal(#10/03/2026#, s.LastClosed.Date)
        Assert.Equal(#10/03/2026#, s.WeekStart.AddDays(5))  ' week starts Monday 09/28
    End Sub

    <Fact>
    Public Sub DuplicatedRowsOfADay_AreAdded()
        Dim s = Build(100000D, Row("Y1", #10/01/2026#, 10D, 1D, 1), Row("Y1", #10/01/2026#, 15D, 2D, 3))
        Dim day = s.Days.Single(Function(d) d.Date = #10/01/2026#)
        Assert.Equal(25D, day.Value)
        Assert.Equal(3D, day.Pieces)
        Assert.Equal(4, day.Styles)
    End Sub

End Class

Public Class FormattingTests

    <Theory>
    <InlineData(150000.0, True, "$150K")>
    <InlineData(37500.0, True, "$37.5K")>
    <InlineData(1250000.0, True, "$1.25M")>
    <InlineData(2000000.0, True, "$2M")>
    <InlineData(850.0, True, "$850")>
    <InlineData(152340.0, False, "$152.3K")>
    <InlineData(0.0, True, "$0")>
    Public Sub Abbreviated(value As Double, axis As Boolean, expected As String)
        Assert.Equal(expected, Money.Abbreviated(value, axis))
    End Sub

    <Theory>
    <InlineData(75012.4, "$75K")>
    <InlineData(141987.0, "$142K")>
    <InlineData(3982.0, "$4K")>
    <InlineData(3450.0, "$3.5K")>
    <InlineData(1250000.0, "$1.25M")>
    <InlineData(850.0, "$850")>
    Public Sub Rounded(value As Double, expected As String)
        Assert.Equal(expected, Money.Rounded(CDec(value)))
    End Sub

    <Fact>
    Public Sub Attainment_NeverRoundsUpBelowGoal()
        Assert.Equal("99%", Money.Attainment(149832D, 150000D))
        Assert.Equal("100%", Money.Attainment(150000D, 150000D))
        Assert.Equal("119%", Money.Attainment(178955D, 150000D))
        Assert.Equal("—", Money.Attainment(5D, 0D))
    End Sub

    <Fact>
    Public Sub Abbreviated_CompactBarsDropDecimals()
        Assert.Equal("$152K", Money.Abbreviated(152340, axis:=False, compact:=True))
    End Sub

    <Fact>
    Public Sub Full_IndependentOfCulture()
        Dim previous = Globalization.CultureInfo.CurrentCulture
        Try
            Globalization.CultureInfo.CurrentCulture = New Globalization.CultureInfo("es-HN")
            Assert.Equal("$1,234,568", Money.Full(1234567.89D))
            Assert.Equal("-$500", Money.Full(-500D))
            Assert.Equal("87%", Money.Percent(0.8749))
        Finally
            Globalization.CultureInfo.CurrentCulture = previous
        End Try
    End Sub

    <Fact>
    Public Sub Texts_SpanishAndEnglish()
        Dim es As New Texts(False)
        Dim en As New Texts(True)
        Assert.Equal("viernes 02 oct 2026", es.LongDate(#10/02/2026#))
        Assert.Equal("Friday, Oct 02 2026", en.LongDate(#10/02/2026#))
        Assert.Equal("02/10", es.DayMonth(#10/02/2026#))
        Assert.Equal("10/02", en.DayMonth(#10/02/2026#))
        Assert.Equal("Sáb", es.DayShort(#10/03/2026#))
        Assert.Equal("1 día", es.Days(1))
        Assert.Equal("3 days", en.Days(3))
    End Sub

End Class

Public Class PreferencesTests

    <Fact>
    Public Sub Normalize_FillsMissingAreasAndClamps()
        Dim prefs As New DashboardPreferences With {
            .Language = "fr", .HistoryDays = 99, .RotateSeconds = 1, .CurrentArea = "XX",
            .Areas = New List(Of AreaDefinition) From {New AreaDefinition With {.Code = "y3", .Name = "Tapicería", .DailyGoal = 80000D, .Order = 3}}}
        prefs.Normalize(150000D)
        Assert.Equal("ES", prefs.Language)
        Assert.Equal(31, prefs.HistoryDays)
        Assert.Equal(5, prefs.RotateSeconds)
        Assert.Equal(6, prefs.Areas.Count)
        Assert.Equal("Tapicería", prefs.FindArea("Y3").Name)
        Assert.Equal(80000D, prefs.FindArea("Y3").DailyGoal)
        Assert.Equal(150000D, prefs.FindArea("SHP").DailyGoal)
        Assert.Equal("Y1", prefs.CurrentArea)
    End Sub

    <Fact>
    Public Sub Normalize_KeepsAtLeastOneActiveArea()
        Dim prefs As New DashboardPreferences With {.Areas = AreaCatalog.Defaults(1D)}
        prefs.Areas.ForEach(Sub(a) a.Active = False)
        prefs.Normalize(1D)
        Assert.Single(prefs.ActiveAreas())
    End Sub

    <Fact>
    Public Sub NextActiveArea_WrapsAndSkipsHidden()
        Dim prefs As New DashboardPreferences()
        prefs.Normalize(1D)
        prefs.FindArea("Y2").Active = False
        Assert.Equal("Y3", prefs.NextActiveArea("Y1").Code)
        Assert.Equal("Y1", prefs.NextActiveArea("SHP").Code)
    End Sub

    <Fact>
    Public Sub Sources()
        Assert.Equal(ProductionSource.AssemblyDeliveries, AreaCatalog.SourceOf("Y1"))
        Assert.Equal(ProductionSource.StationActivity, AreaCatalog.SourceOf("Y5"))
        Assert.Equal(ProductionSource.Shipments, AreaCatalog.SourceOf("shp"))
        Assert.Equal({"Y2", "Y3", "Y5", "Y7"}, AreaCatalog.CodesOf(ProductionSource.StationActivity))
    End Sub

End Class
