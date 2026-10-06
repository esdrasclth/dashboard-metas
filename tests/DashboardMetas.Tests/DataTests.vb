Imports System.Data.Odbc
Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Models
Imports DashboardMetas.Core.Processing
Imports DashboardMetas.Data
Imports DashboardMetas.Data.Demo
Imports DashboardMetas.Data.Odbc
Imports Xunit

Public Class QueryTests

    Private Shared Function Company() As JdeSettings
        Return New JdeSettings With {.Dsn = "JDE", .User = "USER", .Branch = "001",
            .AssemblyLibrary = "MIRDB.PRODDTA", .StationsLibrary = "PRODDTA", .PricesLibrary = "PRECIOSDTA", .DcLinkLibrary = "DCLINKDTA"}
    End Function

    Private Shared Function Markers(sql As String) As Integer
        Return sql.Count(Function(c) c = "?"c)
    End Function

    <Fact>
    Public Sub Julian()
        Assert.Equal(126275, JdeQueries.ToJulian(#10/02/2026#))
        Assert.Equal(126001, JdeQueries.ToJulian(#01/01/2026#))
        Assert.Equal(20261002, JdeQueries.ToYmd(#10/02/2026#))
    End Sub

    <Fact>
    Public Sub Assembly_SameFiltersAsVbs()
        Dim q = JdeQueries.Assembly(Company(), #09/18/2026#)
        Assert.Contains("FROM MIRDB.PRODDTA.F31122 WT", q.Sql)
        Assert.Contains("UPPER(WT.WTKITL) LIKE '%FIN%'", q.Sql)
        Assert.Contains("F4801.WASRST <> '98'", q.Sql)
        Assert.Contains("/ 10000 AS DECIMAL(15, 2)", q.Sql)
        Assert.Equal(q.Parameters.Count, Markers(q.Sql))
        Assert.Equal({"branch", "operation", "branchPadded", "fromJulian"}, q.Parameters.Select(Function(p) p.Name))
        Assert.Equal("         001", q.Parameters(2).Value)
        Assert.Equal(51000D, q.Parameters(1).Value)
        Assert.Equal(126261D, q.Parameters(3).Value)
    End Sub

    <Fact>
    Public Sub Stations_OneSelectPerStation()
        Dim q = JdeQueries.Stations(Company(), #09/18/2026#)
        Assert.Equal(4, (q.Sql.Length - q.Sql.Replace("UNION ALL", "").Length) \ "UNION ALL".Length + 1)
        For Each code In {"'Y2'", "'Y3'", "'Y5'", "'Y7'"}
            Assert.Contains(code & " AS AREA", q.Sql)
        Next
        Assert.Contains("S.SRTL07 AS Q4", q.Sql)
        Assert.Contains("FROM PRECIOSDTA.F41D200", q.Sql)
        Assert.Contains("SUBSTR(S.SRLITM, 1, 9) || 'FIN'", q.Sql)
        Assert.Equal(q.Parameters.Count, Markers(q.Sql))
        Assert.Equal({"priceType", "branch", "fromJulian"}, q.Parameters.Select(Function(p) p.Name))
    End Sub

    <Fact>
    Public Sub Shipments_ExactPriceThenBasePlusFin()
        Dim q = JdeQueries.Shipments(Company(), #09/18/2026#)
        Assert.Contains("FROM DCLINKDTA.DCTXF T", q.Sql)
        Assert.Contains("COALESCE(P1.RG, P2.RG, 0)", q.Sql)
        Assert.Contains("CAST(CAST(SUM(T.TXQTY1) AS DECIMAL(17, 2)) / 100 AS DECIMAL(15, 2))", q.Sql)
        Assert.Equal(q.Parameters.Count, Markers(q.Sql))
        Assert.Equal({"priceType", "transaction", "branch", "fromYmd"}, q.Parameters.Select(Function(p) p.Name))
        Assert.Equal(20260918D, q.Parameters(3).Value)
    End Sub

    <Fact>
    Public Sub InvalidLibrary_IsRejected()
        Dim settings = Company()
        settings.DcLinkLibrary = "X; DROP TABLE"
        Assert.Throws(Of ArgumentException)(Function() JdeQueries.Shipments(settings, Date.Today))
        Assert.NotEmpty(settings.Validate())
    End Sub

    <Fact>
    Public Sub Divisors_AppliedInDotNet()
        Dim settings = Company()
        settings.StationsQuantityDivisor = 100D
        settings.PriceDivisor = 10D
        Dim station = JdeQueries.Scale(ProductionSource.StationActivity, Row("Y2", Date.Today, 50000D, 1200D), settings)
        Assert.Equal(12D, station.Pieces)
        Assert.Equal(50D, station.Value)
        Dim ship = JdeQueries.Scale(ProductionSource.Shipments, Row("SHP", Date.Today, 50000D, 30D), settings)
        Assert.Equal(30D, ship.Pieces)
        Assert.Equal(5000D, ship.Value)
        Dim y1 = JdeQueries.Scale(ProductionSource.AssemblyDeliveries, Row("Y1", Date.Today, 50000D, 30D), settings)
        Assert.Equal(50000D, y1.Value)
    End Sub

    <Fact>
    Public Sub Converter_Dates()
        Assert.Equal(#10/02/2026#, JdeValueConverter.ToDate(#10/02/2026#))
        Assert.Equal(#10/02/2026#, JdeValueConverter.ToDate("2026-10-02"))
        Assert.Equal(#10/02/2026#, JdeValueConverter.ToDate(New DateOnly(2026, 10, 2)))
        Assert.Throws(Of FormatException)(Function() JdeValueConverter.ToDate(DBNull.Value))
    End Sub

    <Theory>
    <InlineData("IM002", "", JdeConnectionErrorKind.DataSourceNotFound)>
    <InlineData("", "CWBSY0002 - Password is incorrect", JdeConnectionErrorKind.InvalidPassword)>
    <InlineData("", "CWBSY0003 - Password has expired", JdeConnectionErrorKind.PasswordExpired)>
    <InlineData("08001", "CWBCO1048", JdeConnectionErrorKind.HostUnreachable)>
    <InlineData("IM003", "", JdeConnectionErrorKind.DriverNotInstalled)>
    Public Sub ErrorTranslator(state As String, message As String, expected As JdeConnectionErrorKind)
        Dim result = OdbcErrorTranslator.Translate({state}, message, "JDE", "USER")
        Assert.Equal(expected, result.Kind)
    End Sub

End Class

Public Class DemoTests

    Private Shared Function Repo(now As Date, Optional failing As String = "") As DemoProductionRepository
        Return New DemoProductionRepository(New Monitor(Of DemoSettings)(New DemoSettings With {.Enabled = True, .QueryDelayMilliseconds = 0, .FailingSource = failing}),
                                            New Monitor(Of DashboardSettings)(New DashboardSettings()), New FixedClock(now))
    End Function

    <Fact>
    Public Async Function Demo_StableNoSundaysAndGrowsDuringShift() As Task
        Dim morning = Await (Await Repo(#10/02/2026 08:00#).OpenSessionAsync("U", "x", CancellationToken.None)).GetDailyAsync(ProductionSource.StationActivity, #09/14/2026#, CancellationToken.None)
        Dim afternoon = Await (Await Repo(#10/02/2026 15:00#).OpenSessionAsync("U", "x", CancellationToken.None)).GetDailyAsync(ProductionSource.StationActivity, #09/14/2026#, CancellationToken.None)
        Assert.DoesNotContain(morning, Function(r) r.Date.DayOfWeek = DayOfWeek.Sunday)
        Assert.Equal({"Y2", "Y3", "Y5", "Y7"}, morning.Select(Function(r) r.Area).Distinct().OrderBy(Function(a) a))
        ' Past days identical in both runs, today bigger in the afternoon
        Dim pastMorning = morning.Where(Function(r) r.Date < #10/02/2026#).Sum(Function(r) r.Value)
        Dim pastAfternoon = afternoon.Where(Function(r) r.Date < #10/02/2026#).Sum(Function(r) r.Value)
        Assert.Equal(pastMorning, pastAfternoon)
        Dim todayMorning = morning.Where(Function(r) r.Date = #10/02/2026#).Sum(Function(r) r.Value)
        Dim todayAfternoon = afternoon.Where(Function(r) r.Date = #10/02/2026#).Sum(Function(r) r.Value)
        Assert.True(todayAfternoon > todayMorning)
    End Function

    <Fact>
    Public Async Function Demo_WrongPasswordAndFailingSource() As Task
        Dim ex = Await Assert.ThrowsAsync(Of JdeConnectionException)(Function() Repo(Date.Now).OpenSessionAsync("U", DemoProductionRepository.WrongPassword, CancellationToken.None))
        Assert.Equal(JdeConnectionErrorKind.InvalidPassword, ex.Kind)
        Dim session = Await Repo(Date.Now, "Embarques").OpenSessionAsync("U", Nothing, CancellationToken.None)
        Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() session.GetDailyAsync(ProductionSource.Shipments, Date.Today.AddDays(-3), CancellationToken.None))
    End Function

End Class

Public Class RefresherTests

    Private Shared ReadOnly Now As Date = #10/02/2026 10:00#

    Private Shared Function Build(repo As FakeRepository, store As MemoryCredentialStore, prompt As ScriptedPrompt, Optional signOn As Boolean = False) As ProductionRefresher
        Dim opener As New JdeSessionOpener(repo, store, prompt, Nothing)
        Return New ProductionRefresher(opener, New Monitor(Of JdeSettings)(New JdeSettings With {.User = "U", .UseDriverSignOn = signOn}), New FixedClock(Now), Nothing)
    End Function

    <Fact>
    Public Async Function StartsWithAreaOnScreen_AndStoresRows() As Task
        Dim repo As New FakeRepository()
        repo.Rows(ProductionSource.Shipments) = New List(Of DailyProduction) From {Row("SHP", #10/01/2026#, 5D)}
        repo.Rows(ProductionSource.StationActivity) = New List(Of DailyProduction) From {Row("Y2", #10/01/2026#, 1D), Row("Y7", #10/01/2026#, 7D)}
        Dim store As New MemoryCredentialStore With {.Saved = "ok"}
        Dim refresher = Build(repo, store, New ScriptedPrompt())
        Dim outcome = Await refresher.RefreshAsync(ProductionSource.Shipments, #09/18/2026#, allowPrompt:=False, Nothing, CancellationToken.None)
        Assert.Equal(4, outcome.Succeeded)
        Assert.Equal({ProductionSource.Shipments, ProductionSource.AssemblyDeliveries, ProductionSource.StationActivity, ProductionSource.Float}, repo.QueryOrder)
        Assert.Equal(1, repo.Opened)
        Assert.Equal(7D, refresher.GetRows("Y7").Single().Value)
        Assert.Equal(Now, refresher.GetStatus(ProductionSource.Shipments).LastSuccess)
    End Function

    <Fact>
    Public Async Function FailingSource_KeepsItsLastData_OthersUpdate() As Task
        Dim repo As New FakeRepository()
        repo.Rows(ProductionSource.AssemblyDeliveries) = New List(Of DailyProduction) From {Row("Y1", #10/01/2026#, 100D)}
        Dim refresher = Build(repo, New MemoryCredentialStore With {.Saved = "ok"}, New ScriptedPrompt())
        Await refresher.RefreshAsync(ProductionSource.AssemblyDeliveries, #09/18/2026#, False, Nothing, CancellationToken.None)

        repo.Failing.Add(ProductionSource.AssemblyDeliveries)
        repo.Rows(ProductionSource.Shipments) = New List(Of DailyProduction) From {Row("SHP", #10/01/2026#, 9D)}
        Dim outcome = Await refresher.RefreshAsync(ProductionSource.AssemblyDeliveries, #09/18/2026#, False, Nothing, CancellationToken.None)

        Assert.Equal(1, outcome.Failed)
        Assert.Equal(100D, refresher.GetRows("Y1").Single().Value)
        Assert.True(refresher.GetStatus(ProductionSource.AssemblyDeliveries).HasError)
        Assert.Equal(Now, refresher.GetStatus(ProductionSource.AssemblyDeliveries).LastSuccess)
        Assert.Equal(9D, refresher.GetRows("SHP").Single().Value)
    End Function

    <Fact>
    Public Async Function AutomaticRefresh_NeverPrompts() As Task
        Dim prompt As New ScriptedPrompt("ok")
        Dim refresher = Build(New FakeRepository(), New MemoryCredentialStore(), prompt)
        Dim outcome = Await refresher.RefreshAsync(ProductionSource.AssemblyDeliveries, Now.Date, allowPrompt:=False, Nothing, CancellationToken.None)
        Assert.True(outcome.NeedsPassword)
        Assert.Equal(0, prompt.Calls)
        Assert.Equal(4, outcome.Failed)
        Assert.True(refresher.GetStatus(ProductionSource.Shipments).NeedsPassword)
    End Function

    <Fact>
    Public Async Function WrongSavedPassword_IsDeleted_ThenPromptedAndSaved() As Task
        Dim store As New MemoryCredentialStore With {.Saved = "old"}
        Dim prompt As New ScriptedPrompt("bad", "ok")
        Dim refresher = Build(New FakeRepository(), store, prompt)
        Dim outcome = Await refresher.RefreshAsync(ProductionSource.AssemblyDeliveries, Now.Date, allowPrompt:=True, Nothing, CancellationToken.None)
        Assert.Equal(4, outcome.Succeeded)
        Assert.Equal(2, prompt.Calls)
        Assert.Equal("ok", store.Saved)
        Assert.Equal(1, store.Deleted)
    End Function

    <Fact>
    Public Async Function DriverSignOn_SendsNoPassword() As Task
        Dim prompt As New ScriptedPrompt()
        Dim refresher = Build(New FakeRepository(), New MemoryCredentialStore(), prompt, signOn:=True)
        Dim outcome = Await refresher.RefreshAsync(ProductionSource.AssemblyDeliveries, Now.Date, allowPrompt:=False, Nothing, CancellationToken.None)
        Assert.Equal(4, outcome.Succeeded)
        Assert.Equal(0, prompt.Calls)
    End Function

    <Fact>
    Public Async Function Cache_RoundTrip() As Task
        Dim repo As New FakeRepository()
        repo.Rows(ProductionSource.StationActivity) = New List(Of DailyProduction) From {Row("Y3", #10/01/2026#, 3D)}
        Dim refresher = Build(repo, New MemoryCredentialStore With {.Saved = "ok"}, New ScriptedPrompt())
        Await refresher.RefreshAsync(ProductionSource.StationActivity, Now.Date, False, Nothing, CancellationToken.None)
        Dim cache = refresher.ExportCache()

        Dim other = Build(New FakeRepository(), New MemoryCredentialStore(), New ScriptedPrompt())
        other.ImportCache(cache)
        Assert.Equal(3D, other.GetRows("Y3").Single().Value)
        Assert.Equal(Now, other.GetStatus(ProductionSource.StationActivity).LastSuccess)
        other.ImportCache(Nothing)
        Assert.Empty(other.GetRows("Y3"))
        Assert.Null(other.GetStatus(ProductionSource.StationActivity).LastSuccess)
    End Function

End Class
