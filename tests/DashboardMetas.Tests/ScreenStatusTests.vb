Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Remote
Imports Xunit

Public Class ScreenStatusTests

    <Fact>
    Public Async Function Seen_IsRecordedOnce_Saved_AndForgottenWhenRetired() As Task
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim store As New MemoryStateStore()
            Dim source As New ScriptedSource With {.Keys = Trust(key), .Answer = Function() New AnnouncementFetch With {.Content = SignedJson(key, Feed(1, Item("a"), Item("b")))}}
            Dim center As New AnnouncementCenter(source, store, Nothing)
            center.Load()
            Await center.CheckAsync(Now, CancellationToken.None)

            Assert.True(center.MarkSeen({"a"}, Now))
            Assert.False(center.MarkSeen({"a"}, Now.AddMinutes(5)))
            Assert.Equal(Now, center.Views().Seen("a"))
            Assert.True(store.Saved.Seen.ContainsKey("a"))

            ' Survives a restart
            Dim restarted As New AnnouncementCenter(source, store, Nothing)
            restarted.Load()
            Assert.True(restarted.Views().Seen.ContainsKey("A"))

            ' Retired from the file: no longer reported
            source.Answer = Function() New AnnouncementFetch With {.Content = SignedJson(key, Feed(2, Item("b")))}
            Await restarted.CheckAsync(Now, CancellationToken.None)
            Assert.False(restarted.Views().Seen.ContainsKey("a"))
        End Using
    End Function

    <Theory>
    <InlineData("", 300, True)>
    <InlineData("https://panel.example.com/api/heartbeat", 300, True)>
    <InlineData("http://localhost:3000/api/heartbeat", 60, True)>
    <InlineData("http://panel.example.com/api/heartbeat", 300, False)>
    <InlineData("https://panel.example.com/api/heartbeat", 10, False)>
    Public Sub StatusSettings_Validation(url As String, seconds As Integer, valid As Boolean)
        Assert.Equal(valid, New StatusSettings With {.Url = url, .IntervalSeconds = seconds}.Validate().Count = 0)
    End Sub

    <Fact>
    Public Sub Report_HasTheJsonThePanelExpects_AndASignatureItCanCheck()
        Dim report As New ScreenReport With {
            .DeviceId = "0123456789abcdef", .PublicKey = "AAAA", .SentAt = Now, .StartedAt = Now.AddHours(-1),
            .Machine = "TV-01", .Branch = "027", .AppVersion = "1.0.0", .Os = "Windows", .Mode = "real"}
        report.Jde.Sources.Add(New JdeSourceState With {.Name = "Y1", .Error = "sin red", .ErrorAt = Now})
        report.Announcements.Seen("aviso-1") = Now
        Dim json = JsonSerializer.Serialize(report, AnnouncementJson.Options)
        Using doc = JsonDocument.Parse(json)
            Dim root = doc.RootElement
            For Each name In {"deviceId", "publicKey", "sentAt", "startedAt", "machine", "branch", "appVersion", "os", "mode", "screen", "jde", "announcements"}
                Assert.True(root.TryGetProperty(name, Nothing), name)
            Next
            Assert.Equal("sin red", root.GetProperty("jde").GetProperty("sources")(0).GetProperty("error").GetString())
            Assert.True(root.GetProperty("announcements").GetProperty("seen").TryGetProperty("aviso-1", Nothing))
            Assert.Equal("area", root.GetProperty("screen").GetProperty("view").GetString())
        End Using

        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim body = Encoding.UTF8.GetBytes(json)
            Dim signature = Convert.FromBase64String(ReportSigning.Sign(body, key))
            Assert.Equal(64, signature.Length) ' IEEE P1363 r||s, what the panel verifies
            Assert.True(key.VerifyData(body, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        End Using
    End Sub

End Class
