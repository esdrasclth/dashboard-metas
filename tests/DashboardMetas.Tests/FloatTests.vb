Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Models
Imports DashboardMetas.Core.Processing
Imports DashboardMetas.Data
Imports DashboardMetas.Data.Demo
Imports Xunit

Public Class FloatQueryTests

    Private Shared Function Company() As JdeSettings
        Return New JdeSettings With {.Dsn = "JDE", .User = "USER", .Branch = "027",
            .AssemblyLibrary = "MIRDB.PRODDTA", .StationsLibrary = "PRODDTA", .PricesLibrary = "PRECIOSDTA", .DcLinkLibrary = "DCLINKDTA"}
    End Function

    Private Shared Function Markers(sql As String) As Integer
        Return sql.Count(Function(c) c = "?"c)
    End Function

    <Fact>
    Public Sub Float_SameFiltersAsAvanceMeta()
        Dim q = JdeQueries.Float(Company(), #10/05/2025#)
        ' Same tables and library as avanceMeta.vbs
        Assert.Contains("FROM PRODDTA.F4801 W", q.Sql)
        Assert.Contains("FROM PRODDTA.F58C3120 S", q.Sql)
        Assert.Contains("FROM PRECIOSDTA.F41D200", q.Sql)
        Assert.Contains("W.WAMMCU = CAST(? AS CHAR(12))", q.Sql)
        Assert.Contains("UPPER(W.WALITM) LIKE '%FIN%'", q.Sql)
        Assert.Contains("P.PMLITM = W.WALITM", q.Sql)
        Assert.Contains("GROUP BY W.WASRST, TRIM(SUBSTR(W.WALITM, 1, 9))", q.Sql)
        ' SUM() of DB2 is DECIMAL(31,0): cast before dividing (SQL0419), like the shipments
        Assert.Contains("CAST(CAST(SUM(W.WAUORG) AS DECIMAL(17, 2)) / 100 AS DECIMAL(15, 2))", q.Sql)
        Assert.Contains("MAX(TRIM(S.SRSORT))", q.Sql)
        Assert.Equal(q.Parameters.Count, Markers(q.Sql))
        Assert.Equal({"priceType", "branchPadded", "status1", "status2", "status3", "status4", "branch", "classifyFromJulian"}, q.Parameters.Select(Function(p) p.Name))
        Assert.Equal("         027", q.Parameters(1).Value)
        Assert.Equal({"Y1", "Y2", "Y3", "Y5"}, q.Parameters.Skip(2).Take(4).Select(Function(p) CStr(p.Value)))
        Assert.Equal("027", q.Parameters(6).Value)
        Assert.Equal(125278D, q.Parameters(7).Value)
    End Sub

    <Fact>
    Public Sub Float_StatusesComeFromSettings()
        Dim settings = Company()
        settings.FloatStatuses = " y2 ; Y3,y2,, "
        Assert.Equal({"Y2", "Y3"}, settings.FloatStatusList())
        Dim q = JdeQueries.Float(settings, Date.Today)
        Assert.Equal(q.Parameters.Count, Markers(q.Sql))
        Assert.Equal(2, q.Parameters.Where(Function(p) p.Name.StartsWith("status", StringComparison.Ordinal)).Count())
    End Sub

    <Theory>
    <InlineData("Y1,Y2,Y3,Y5", True)>
    <InlineData("Y2", True)>
    <InlineData("", False)>
    <InlineData("Y1,ABC", False)>
    <InlineData("Y1,'X'", False)>
    Public Sub Float_StatusValidation(statuses As String, valid As Boolean)
        Dim settings = Company()
        settings.FloatStatuses = statuses
        Assert.Equal(valid, settings.Validate().Count = 0)
    End Sub

    <Fact>
    Public Sub Float_PriceDivisorApplies()
        Dim settings = Company()
        settings.PriceDivisor = 10D
        Dim item = JdeQueries.ScaleFloat(Float("Y2", "A", "L", 5D, 500D), settings)
        Assert.Equal(50D, item.Value)
        Assert.Equal(5D, item.Pieces)
    End Sub

End Class

Public Class FloatCalculatorTests

    Private Shared ReadOnly Statuses As String() = {"Y1", "Y2", "Y3", "Y5"}

    <Fact>
    Public Sub Totals_AndOneBarPerConfiguredStatus()
        Dim snap = FloatCalculator.Build({
            Float("Y1", "STYLE1", "CASE", 10D, 1000D, 2),
            Float("Y2", "STYLE1", "CASE", 5D, 500D),
            Float("Y2", "STYLE2", "UPH", 4D, 1500D)}, Statuses)
        Assert.Equal(3000D, snap.TotalValue)
        Assert.Equal(19D, snap.TotalPieces)
        Assert.Equal(2, snap.Styles)
        Assert.Equal(4, snap.Orders)
        ' Y3 and Y5 have nothing but keep their bar, in the configured order
        Assert.Equal(Statuses, snap.Statuses.Select(Function(s) s.Code))
        Assert.Equal(0D, snap.Statuses(2).Value)
        Assert.Equal(2000D, snap.Statuses(1).Value)
        Assert.Equal(2, snap.Statuses(1).Styles)
        Assert.Equal(2000D / 3000D, snap.Statuses(1).Share, 6)
        Assert.Equal("Y2", snap.Largest().Code)
    End Sub

    <Fact>
    Public Sub UnexpectedStatus_IsAddedAfterTheConfiguredOnes()
        Dim snap = FloatCalculator.Build({Float("y9", "A", "L", 1D, 1D), Float("Y1", "B", "L", 1D, 1D)}, {"Y1", "Y2"})
        Assert.Equal({"Y1", "Y2", "Y9"}, snap.Statuses.Select(Function(s) s.Code))
    End Sub

    <Fact>
    Public Sub Segments_AddUpToTheirStatus()
        Dim items As New List(Of FloatItem)()
        For i = 1 To 8
            items.Add(Float(If(i Mod 2 = 0, "Y1", "Y2"), "S" & i, "LINE" & i, i, i * 100D))
        Next
        Dim snap = FloatCalculator.Build(items, Statuses)
        For Each s In snap.Statuses
            Assert.Equal(s.Value, s.Segments.Sum(Function(x) x.Value))
            Assert.Equal(s.Pieces, s.Segments.Sum(Function(x) x.Pieces))
        Next
        Assert.Equal(snap.TotalValue, snap.Lines.Sum(Function(l) l.Value))
    End Sub

    <Fact>
    Public Sub OnlyTheBiggestLinesKeepAColour_TheRestFoldIntoOther()
        Dim items As New List(Of FloatItem)()
        For i = 1 To 7
            items.Add(Float("Y1", "S" & i, "LINE" & i, 1D, i * 1000D))
        Next
        items.Add(Float("Y2", "NOLINE", "", 3D, 50D))
        Dim snap = FloatCalculator.Build(items, Statuses)

        Assert.Equal(FloatCalculator.MaxLines + 1, snap.Lines.Count)
        Assert.Equal({"LINE7", "LINE6", "LINE5", "LINE4", "LINE3"}, snap.Lines.Take(5).Select(Function(l) l.Name))
        Dim other = snap.Lines.Last()
        Assert.True(other.IsOther)
        Assert.Equal(FloatCalculator.MaxLines, other.Slot)
        Assert.Equal(2, other.FoldedLines)
        Assert.Equal(1000D + 2000D + 50D, other.Value)
        Assert.Equal(1, snap.UnclassifiedStyles)
    End Sub

    <Fact>
    Public Sub LineColour_FollowsTheName_NotTheRank()
        Dim first = FloatCalculator.Build({Float("Y1", "A", "AAA", 1D, 900D), Float("Y1", "B", "BBB", 1D, 100D)}, Statuses)
        Dim swapped = FloatCalculator.Build({Float("Y1", "A", "AAA", 1D, 100D), Float("Y1", "B", "BBB", 1D, 900D)}, Statuses)
        Dim slotOf = Function(s As FloatSnapshot, name As String) s.Lines.Single(Function(l) l.Name = name).Slot
        Assert.Equal(slotOf(first, "AAA"), slotOf(swapped, "AAA"))
        Assert.Equal(slotOf(first, "BBB"), slotOf(swapped, "BBB"))
        Assert.Equal("BBB", swapped.Lines(0).Name)
    End Sub

    <Fact>
    Public Sub UnpricedStyles_AreCountedOnce()
        Dim snap = FloatCalculator.Build({
            Float("Y1", "NOPRICE", "L", 6D, 0D),
            Float("Y2", "NOPRICE", "L", 4D, 0D),
            Float("Y2", "PRICED", "L", 4D, 400D)}, Statuses)
        Assert.Equal(1, snap.UnpricedStyles)
        Assert.Equal(10D, snap.UnpricedPieces)
    End Sub

    <Fact>
    Public Sub Empty_IsEmptyWithAUsableAxis()
        Dim snap = FloatCalculator.Build(Array.Empty(Of FloatItem)(), Statuses)
        Assert.True(snap.IsEmpty)
        Assert.Equal(4, snap.Statuses.Count)
        Assert.Empty(snap.Lines)
        Assert.Null(snap.Largest())
        Assert.True(snap.StatusAxisMax > 0)
    End Sub

    <Fact>
    Public Sub Axis_UsesTheNiceStep()
        Dim snap = FloatCalculator.Build({Float("Y1", "A", "L", 1D, 1234567D)}, Statuses)
        Assert.True(snap.StatusAxisMax >= 1234567D * 1.1)
        Assert.Equal(snap.StatusAxisStep * snap.GridLines, snap.StatusAxisMax)
    End Sub

End Class

Public Class FloatRefreshTests

    Private Shared ReadOnly Now As Date = #10/05/2026 10:00#

    Private Shared Function Build(repo As FakeRepository) As ProductionRefresher
        Dim opener As New JdeSessionOpener(repo, New MemoryCredentialStore With {.Saved = "ok"}, New ScriptedPrompt(), Nothing)
        Return New ProductionRefresher(opener, New Monitor(Of JdeSettings)(New JdeSettings With {.User = "U"}), New FixedClock(Now), Nothing)
    End Function

    <Fact>
    Public Async Function FloatOnScreen_GoesFirst_AndIsStored() As Task
        Dim repo As New FakeRepository()
        repo.FloatItems.Add(Float("Y2", "A", "L", 3D, 300D))
        Dim refresher = Build(repo)
        Dim outcome = Await refresher.RefreshAsync(ProductionSource.Float, Now.Date, False, Nothing, CancellationToken.None)
        Assert.Equal(4, outcome.Succeeded)
        Assert.Equal(ProductionSource.Float, repo.QueryOrder.First())
        Assert.Equal(300D, refresher.GetFloat().Single().Value)
        Assert.Equal(Now, refresher.GetStatus(ProductionSource.Float).LastSuccess)
        ' The product line is looked up in the last year of F58C3120
        Assert.Equal(Now.Date.AddDays(-ProductionRefresher.FloatClassifyDays), repo.FloatClassifyFrom)
    End Function

    <Fact>
    Public Async Function FloatFailure_KeepsTheLastPicture_AndTheAreasUpdate() As Task
        Dim repo As New FakeRepository()
        repo.FloatItems.Add(Float("Y1", "A", "L", 1D, 100D))
        Dim refresher = Build(repo)
        Await refresher.RefreshAsync(ProductionSource.AssemblyDeliveries, Now.Date, False, Nothing, CancellationToken.None)

        repo.Failing.Add(ProductionSource.Float)
        repo.Rows(ProductionSource.Shipments) = New List(Of DailyProduction) From {Row("SHP", Now.Date, 9D)}
        Dim outcome = Await refresher.RefreshAsync(ProductionSource.AssemblyDeliveries, Now.Date, False, Nothing, CancellationToken.None)

        Assert.Equal(1, outcome.Failed)
        Assert.True(refresher.GetStatus(ProductionSource.Float).HasError)
        Assert.Equal(100D, refresher.GetFloat().Single().Value)
        Assert.Equal(9D, refresher.GetRows("SHP").Single().Value)
    End Function

    <Fact>
    Public Async Function Float_SurvivesTheCache() As Task
        Dim repo As New FakeRepository()
        repo.FloatItems.Add(Float("Y3", "A", "CASE", 2D, 250D, 3))
        Dim refresher = Build(repo)
        Await refresher.RefreshAsync(ProductionSource.Float, Now.Date, False, Nothing, CancellationToken.None)

        Dim json = Text.Json.JsonSerializer.Serialize(refresher.ExportCache())
        Dim other = Build(New FakeRepository())
        other.ImportCache(Text.Json.JsonSerializer.Deserialize(Of ProductionCache)(json))
        Dim item = other.GetFloat().Single()
        Assert.Equal(("Y3", "CASE", 250D, 3), (item.Status, item.ProductLine, item.Value, item.Orders))
        Assert.Equal(Now, other.GetStatus(ProductionSource.Float).LastSuccess)
    End Function

    <Fact>
    Public Sub QueryOrder_FloatLastUnlessOnScreen()
        Assert.Equal(ProductionSource.Float, ProductionRefresher.QueryOrder(ProductionSource.StationActivity).Last())
        Assert.Equal(ProductionSource.StationActivity, ProductionRefresher.QueryOrder(ProductionSource.StationActivity).First())
        Assert.Equal({ProductionSource.Float, ProductionSource.AssemblyDeliveries, ProductionSource.StationActivity, ProductionSource.Shipments},
                     ProductionRefresher.QueryOrder(ProductionSource.Float))
    End Sub

End Class

Public Class FloatDemoAndPreferencesTests

    Private Shared Function Repo(now As Date, Optional failing As String = "", Optional statuses As String = "Y1,Y2,Y3,Y5") As DemoProductionRepository
        Return New DemoProductionRepository(New Monitor(Of DemoSettings)(New DemoSettings With {.Enabled = True, .QueryDelayMilliseconds = 0, .FailingSource = failing}),
                                            New Monitor(Of DashboardSettings)(New DashboardSettings()), New FixedClock(now), Nothing,
                                            New Monitor(Of JdeSettings)(New JdeSettings With {.FloatStatuses = statuses}))
    End Function

    <Fact>
    Public Async Function DemoFloat_UsesTheConfiguredStatuses_AndIsStableWithinTheHour() As Task
        Dim a = Await (Await Repo(#10/05/2026 10:05#, statuses:="Y2,Y3").OpenSessionAsync("U", "x", CancellationToken.None)).GetFloatAsync(Date.Today, CancellationToken.None)
        Dim b = Await (Await Repo(#10/05/2026 10:50#, statuses:="Y2,Y3").OpenSessionAsync("U", "x", CancellationToken.None)).GetFloatAsync(Date.Today, CancellationToken.None)
        Assert.NotEmpty(a)
        Assert.All(a, Sub(i) Assert.Contains(i.Status, {"Y2", "Y3"}))
        Assert.Equal(a.Sum(Function(i) i.Value), b.Sum(Function(i) i.Value))
        ' Some styles without price or line, so those warnings can be seen in demo
        Assert.Contains(a, Function(i) i.Value = 0D)
        Assert.Contains(a, Function(i) i.ProductLine.Length = 0)
    End Function

    <Fact>
    Public Async Function DemoFloat_CanFail() As Task
        Dim session = Await Repo(Date.Now, "Float").OpenSessionAsync("U", Nothing, CancellationToken.None)
        Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() session.GetFloatAsync(Date.Today, CancellationToken.None))
    End Function

    <Fact>
    Public Sub Preferences_FloatAndOverviewNeverTogether()
        Dim prefs As New DashboardPreferences With {.ShowFloat = True, .ShowOverview = True, .FloatChartView = "algo"}
        prefs.Normalize(150000D)
        Assert.True(prefs.ShowFloat)
        Assert.False(prefs.ShowOverview)
        Assert.Equal("Status", prefs.FloatChartView)
        Assert.True(New DashboardPreferences().FloatInRotation)
    End Sub

End Class
