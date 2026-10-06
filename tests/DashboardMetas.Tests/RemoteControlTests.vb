Imports System.Security.Cryptography
Imports DashboardMetas.Core.Models
Imports DashboardMetas.Core.Remote
Imports Xunit

Public Class RemoteControlTests

    Private Shared Function Area(code As String, Optional goal As Decimal = 150000D, Optional start As String = "07:00", Optional [end] As String = "17:00") As RemoteAreaConfig
        Return New RemoteAreaConfig With {.Code = code, .DailyGoal = goal, .MonthlyGoal = 3_000_000D, .ShiftStart = start, .ShiftEnd = [end], .BreakStart = "12:00", .BreakEnd = "12:30"}
    End Function

    Private Shared Function Config(revision As Long, ParamArray areas As RemoteAreaConfig()) As RemoteConfig
        Return New RemoteConfig With {.Revision = revision, .PublishedAt = DateTimeOffset.Now, .Areas = areas.ToList()}
    End Function

    Private Shared Function Command(Optional action As String = RemoteCommand.ActionShow, Optional view As String = RemoteCommand.ViewFloat,
                                    Optional targets As String() = Nothing, Optional issued As DateTimeOffset? = Nothing) As RemoteCommand
        Dim at = If(issued, DateTimeOffset.Now)
        Return New RemoteCommand With {.Id = Guid.NewGuid().ToString("N"), .IssuedAt = at, .ExpiresAt = at.AddMinutes(10),
                                       .Targets = If(targets, Array.Empty(Of String)()).ToList(), .Action = action, .View = view, .HoldMinutes = 30}
    End Function

    ' ---- Goals and shifts

    <Fact>
    Public Sub Config_SignedRoundTrip_AndAntiRollback()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim json = RemoteConfigSigning.Sign(Config(5, Area("Y1"), Area("SHP")), key)
            Dim check = RemoteConfigSigning.Verify(json, Trust(key))
            Assert.True(check.IsValid, check.Error)
            Assert.Equal({"Y1", "SHP"}, check.Config.Managed())
            Assert.False(RemoteConfigSigning.Verify(json, Trust(key), minimumRevision:=6).IsValid)
            Using other = ECDsa.Create(ECCurve.NamedCurves.nistP256)
                Assert.False(RemoteConfigSigning.Verify(json, Trust(other)).IsValid)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Config_IsNotAnAnnouncementOrACommand()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim json = RemoteConfigSigning.Sign(Config(5, Area("Y1")), key)
            Assert.False(RemoteCommandSigning.Verify(json, Trust(key)).Ok)
            Assert.False(Core.Announcements.AnnouncementSigning.Verify(json, Trust(key)).IsValid)
        End Using
    End Sub

    <Fact>
    Public Sub Config_RejectsUnknownAreasBadGoalsAndShifts()
        Assert.Empty(Config(1, Area("Y1")).Validate())
        Assert.Empty(Config(1).Validate()) ' empty = hand everything back to the screens
        Assert.NotEmpty(Config(0, Area("Y1")).Validate())
        Assert.NotEmpty(Config(1, Area("Z9")).Validate())
        Assert.NotEmpty(Config(1, Area("Y1", goal:=0D)).Validate())
        Assert.NotEmpty(Config(1, Area("Y1", start:="25:00")).Validate())
        Assert.NotEmpty(Config(1, Area("Y1"), Area("y1")).Validate())
    End Sub

    <Fact>
    Public Sub Config_OverridesGoalsAndShiftOnlyOfManagedAreas()
        Dim prefs As New DashboardPreferences()
        prefs.Normalize(100000D)
        prefs.FindArea("Y1").Name = "Mi nombre"
        Dim y2Before = prefs.FindArea("Y2").DailyGoal
        Dim night = Area("Y1", goal:=222000D, start:="22:00", [end]:="06:00")
        night.BreakStart = String.Empty
        night.BreakEnd = String.Empty
        Dim c = Config(3, night)
        Assert.Empty(c.Validate())
        c.ApplyTo(prefs)

        Dim y1 = prefs.FindArea("Y1")
        Assert.Equal(222000D, y1.DailyGoal)
        Assert.Equal(3_000_000D, y1.MonthlyGoal)
        Assert.Equal("22:00", y1.ShiftStart)
        Assert.Equal("06:00", y1.ShiftEnd)
        Assert.Equal("Mi nombre", y1.Name)              ' names stay local
        Assert.Equal(y2Before, prefs.FindArea("Y2").DailyGoal)
    End Sub

    <Fact>
    Public Sub ControlFile_IsCheckedEvery30Seconds_EvenWithTheOldSavedDefault()
        Assert.Equal(30, New Core.Configuration.AnnouncementSettings().EffectivePollSeconds())
        Assert.Equal(30, New Core.Configuration.AnnouncementSettings With {.PollSeconds = 60}.EffectivePollSeconds()) ' saved by 1.2
        Assert.Equal(120, New Core.Configuration.AnnouncementSettings With {.PollSeconds = 120}.EffectivePollSeconds())
        Assert.Equal(30, New Core.Configuration.AnnouncementSettings With {.PollSeconds = 5}.EffectivePollSeconds())
    End Sub

    ' ---- Commands

    <Fact>
    Public Sub Command_SignedRoundTrip()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim check = RemoteCommandSigning.Verify(RemoteCommandSigning.Sign(Command(), key), Trust(key))
            Assert.True(check.Ok, check.Error)
            Assert.Equal("Mostrar el Float durante 30 min", check.Command.Describe())
        End Using
    End Sub

    <Fact>
    Public Sub Command_RunsOnceOnlyOnItsScreensAndInDate()
        Dim now = DateTimeOffset.Now
        Dim c = Command(targets:={"aaaa1111bbbb2222"})
        Assert.Null(RemoteCommandSigning.WhyNot(c, now, "aaaa1111bbbb2222", New List(Of String)()))
        Assert.NotNull(RemoteCommandSigning.WhyNot(c, now, "otra000000000000", New List(Of String)()))
        Assert.NotNull(RemoteCommandSigning.WhyNot(c, now, "aaaa1111bbbb2222", New List(Of String) From {c.Id}))
        Assert.NotNull(RemoteCommandSigning.WhyNot(c, now.AddMinutes(30), "aaaa1111bbbb2222", Nothing))  ' expired
        Assert.NotNull(RemoteCommandSigning.WhyNot(Command(issued:=now.AddHours(1)), now, "x", Nothing))  ' from the future
        Assert.Null(RemoteCommandSigning.WhyNot(Command(), now, "cualquiera", Nothing))                     ' no targets = all
    End Sub

    <Fact>
    Public Sub Command_RejectsUnknownActionsViewsAreasAndLongLives()
        Assert.Empty(Command(RemoteCommand.ActionRefresh, "").Validate())
        Assert.Empty(Command(RemoteCommand.ActionRestart, "").Validate())
        Assert.NotEmpty(Command("borrar-todo", "").Validate())
        Assert.NotEmpty(Command(view:="otra").Validate())
        Dim area = Command(view:=RemoteCommand.ViewArea)
        area.Area = "Y5"
        Assert.Empty(area.Validate())
        area.Area = "ZZ"
        Assert.NotEmpty(area.Validate())
        Dim forever = Command()
        forever.ExpiresAt = forever.IssuedAt.AddDays(1)
        Assert.NotEmpty(forever.Validate())
    End Sub

End Class
