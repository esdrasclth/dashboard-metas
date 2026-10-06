Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Data.Announcements
Imports DashboardMetas.Data.Demo
Imports Xunit

Friend Module AnnouncementMake

    Public ReadOnly Now As DateTimeOffset = New DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(-6))

    Public Function Item(id As String, Optional severity As AnnouncementSeverity = AnnouncementSeverity.Info,
                         Optional display As AnnouncementDisplay = AnnouncementDisplay.Modal, Optional startsIn As Double = -1,
                         Optional endsIn As Double = 8, Optional targets As String() = Nothing, Optional dismissible As Boolean = True) As Announcement
        Return New Announcement With {
            .Id = id, .Title = "Título " & id, .Message = "Mensaje " & id, .Severity = severity, .Display = display,
            .StartsAt = Now.AddHours(startsIn), .EndsAt = Now.AddHours(endsIn), .Dismissible = dismissible,
            .Targets = If(targets, Array.Empty(Of String)()).ToList()}
    End Function

    Public Function Feed(version As Long, ParamArray items As Announcement()) As AnnouncementFeed
        Return New AnnouncementFeed With {.Version = version, .IssuedAt = Now.AddHours(-2), .Announcements = items.ToList()}
    End Function

    Public Function Trust(key As ECDsa) As IReadOnlyDictionary(Of String, Byte())
        Dim spki = key.ExportSubjectPublicKeyInfo()
        Return New Dictionary(Of String, Byte())(StringComparer.Ordinal) From {{AnnouncementSigning.KeyIdOf(spki), spki}}
    End Function

    Public Function SignedJson(key As ECDsa, feed As AnnouncementFeed) As String
        Return AnnouncementSigning.Serialize(AnnouncementSigning.Sign(feed, key))
    End Function

End Module

Public Class AnnouncementSigningTests

    <Fact>
    Public Sub SignAndVerify_RoundTrip()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim check = AnnouncementSigning.Verify(SignedJson(key, Feed(5, Item("a"))), Trust(key))
            Assert.True(check.IsValid, check.Error)
            Assert.Equal(5L, check.Feed.Version)
            Assert.Equal("Título a", check.Feed.Announcements.Single().Title)
            Assert.Equal(Now.AddHours(8), check.Feed.Announcements.Single().EndsAt)
        End Using
    End Sub

    <Fact>
    Public Sub TamperedPayload_IsRejected()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim signed = AnnouncementSigning.Sign(Feed(5, Item("a")), key)
            Dim text = Encoding.UTF8.GetString(Convert.FromBase64String(signed.Payload)).Replace("Título a", "Título falso")
            signed.Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            Dim check = AnnouncementSigning.Verify(AnnouncementSigning.Serialize(signed), Trust(key))
            Assert.False(check.IsValid)
            Assert.Contains("firma", check.Error)
        End Using
    End Sub

    <Fact>
    Public Sub OtherKey_IsRejected_EvenWithTheSameKeyId()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256), attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            ' Unknown key id
            Assert.False(AnnouncementSigning.Verify(SignedJson(attacker, Feed(5, Item("a"))), Trust(key)).IsValid)
            ' Attacker claims the trusted key id
            Dim forged = AnnouncementSigning.Sign(Feed(5, Item("a")), attacker)
            forged.KeyId = Trust(key).Keys.Single()
            Assert.False(AnnouncementSigning.Verify(AnnouncementSigning.Serialize(forged), Trust(key)).IsValid)
        End Using
    End Sub

    <Fact>
    Public Sub OlderVersion_IsRejected_SameVersionIsAccepted()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim json = SignedJson(key, Feed(5, Item("a")))
            Assert.False(AnnouncementSigning.Verify(json, Trust(key), minimumVersion:=6).IsValid)
            Assert.True(AnnouncementSigning.Verify(json, Trust(key), minimumVersion:=5).IsValid)
        End Using
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("no es json")>
    <InlineData("{""format"":""otro/1"",""keyId"":""x"",""payload"":""e30="",""signature"":""AA==""}")>
    Public Sub Garbage_IsRejectedWithAMessage(json As String)
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim check = AnnouncementSigning.Verify(json, Trust(key))
            Assert.False(check.IsValid)
            Assert.False(String.IsNullOrWhiteSpace(check.Error))
        End Using
    End Sub

    <Fact>
    Public Sub TooBig_IsRejected()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Assert.False(AnnouncementSigning.Verify(New String("x"c, AnnouncementRules.MaxFileBytes + 1), Trust(key)).IsValid)
        End Using
    End Sub

    <Fact>
    Public Sub BadAnnouncement_IsDropped_TheOthersStay()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim bad = Item("malo")
            bad.EndsAt = Nothing
            Dim check = AnnouncementSigning.Verify(SignedJson(key, Feed(5, Item("bueno"), bad, Item("bueno"))), Trust(key))
            Assert.True(check.IsValid)
            Assert.Equal({"bueno"}, check.Feed.Announcements.Select(Function(a) a.Id))
            Assert.Equal(2, check.Warnings.Count)
        End Using
    End Sub

    <Fact>
    Public Sub ProductionKey_IsWellFormed()
        ' Guards a bad copy/paste in AnnouncementKeys.vb: the id must be the fingerprint of the key
        Dim trusted = AnnouncementKeys.Trusted()
        Assert.NotEmpty(trusted)
        For Each pair In trusted
            Assert.Equal(pair.Key, AnnouncementSigning.KeyIdOf(pair.Value))
            Using key = ECDsa.Create()
                key.ImportSubjectPublicKeyInfo(pair.Value, Nothing)
                Assert.Equal(256, key.KeySize)
            End Using
        Next
    End Sub

    <Fact>
    Public Sub CreatedKey_SignsFilesItsPublicKeyVerifies()
        Dim created = AnnouncementSigning.CreateKey()
        Dim json = AnnouncementSigning.Serialize(AnnouncementSigning.Sign(Feed(1, Item("a")), created.PrivatePem))
        Dim trusted As New Dictionary(Of String, Byte()) From {{created.KeyId, Convert.FromBase64String(created.PublicKey)}}
        Assert.True(AnnouncementSigning.Verify(json, trusted).IsValid)
    End Sub

End Class

Public Class AnnouncementRulesTests

    <Fact>
    Public Sub ValidAnnouncement_HasNoErrors()
        Assert.Empty(AnnouncementRules.Validate(Item("aviso-1.v2")))
    End Sub

    <Fact>
    Public Sub CommonMistakes_AreReported()
        Dim noEnd = Item("a") : noEnd.EndsAt = Nothing
        Assert.NotEmpty(AnnouncementRules.Validate(noEnd))
        Dim backwards = Item("a", startsIn:=5, endsIn:=1)
        Assert.NotEmpty(AnnouncementRules.Validate(backwards))
        Dim tooLong = Item("a", startsIn:=0, endsIn:=24 * 40)
        Assert.NotEmpty(AnnouncementRules.Validate(tooLong))
        Dim badId = Item("con espacios")
        Assert.NotEmpty(AnnouncementRules.Validate(badId))
        Dim longTitle = Item("a") : longTitle.Title = New String("x"c, AnnouncementRules.MaxTitleLength + 1)
        Assert.NotEmpty(AnnouncementRules.Validate(longTitle))
        Dim noMessage = Item("a") : noMessage.Message = " "
        Assert.NotEmpty(AnnouncementRules.Validate(noMessage))
    End Sub

    <Fact>
    Public Sub RepeatedIds_AreReported()
        Assert.Contains(AnnouncementRules.Validate(Feed(1, Item("a"), Item("A"))), Function(e) e.Contains("repetido"))
    End Sub

    <Theory>
    <InlineData("", True)>
    <InlineData("https://ejemplo.com/control.json", True)>
    <InlineData("http://localhost:8080/control.json", True)>
    <InlineData("\\servidor\carpeta\control.json", True)>
    <InlineData("http://ejemplo.com/control.json", False)>
    <InlineData("ftp://ejemplo.com/control.json", False)>
    <InlineData("no es una dirección", False)>
    Public Sub Settings_Address(url As String, valid As Boolean)
        Assert.Equal(valid, New AnnouncementSettings With {.FeedUrl = url}.Validate().Count = 0)
    End Sub

End Class

Public Class AnnouncementSelectorTests

    Private Shared ReadOnly Here As New AnnouncementAudience("PLANTA-TV01", "027")

    <Fact>
    Public Sub OnlyInsideTheirWindow()
        Dim f = Feed(1, Item("ya", startsIn:=-1, endsIn:=1), Item("luego", startsIn:=1, endsIn:=2), Item("paso", startsIn:=-3, endsIn:=-1), Item("justo", startsIn:=-1, endsIn:=0))
        Assert.Equal({"ya"}, AnnouncementSelector.Active(f, Now, Here, Nothing).Select(Function(a) a.Id))
    End Sub

    <Theory>
    <InlineData("*", True)>
    <InlineData("planta:027", True)>
    <InlineData("planta:099", False)>
    <InlineData("equipo:planta-tv01", True)>
    <InlineData("PLANTA-TV01", True)>
    <InlineData("OTRA-PC", False)>
    Public Sub Targets(target As String, shown As Boolean)
        Dim f = Feed(1, Item("a", targets:={target}))
        Assert.Equal(shown, AnnouncementSelector.Active(f, Now, Here, Nothing).Count = 1)
    End Sub

    <Fact>
    Public Sub Dismissed_IsHidden_UnlessItCannotBeClosed()
        Dim f = Feed(1, Item("cerrable"), Item("fijo", dismissible:=False))
        Assert.Equal({"fijo"}, AnnouncementSelector.Active(f, Now, Here, {"CERRABLE", "fijo"}).Select(Function(a) a.Id))
    End Sub

    <Fact>
    Public Sub MostUrgentFirst_ThenNewest()
        Dim f = Feed(1, Item("info-viejo", startsIn:=-5), Item("critico", AnnouncementSeverity.Critical, startsIn:=-6), Item("info-nuevo", startsIn:=-1))
        Assert.Equal({"critico", "info-nuevo", "info-viejo"}, AnnouncementSelector.Active(f, Now, Here, Nothing).Select(Function(a) a.Id))
    End Sub

End Class

''' <summary>Source with a scripted answer; counts the calls.</summary>
Friend NotInheritable Class ScriptedSource
    Implements IAnnouncementSource
    Public Property Answer As Func(Of AnnouncementFetch)
    Public Property Keys As IReadOnlyDictionary(Of String, Byte())
    Public Property Calls As Integer
    Public Property Configured As Boolean = True
    Public Function FetchAsync(cancellationToken As CancellationToken) As Task(Of AnnouncementFetch) Implements IAnnouncementSource.FetchAsync
        Calls += 1
        Return Task.FromResult(Answer.Invoke())
    End Function
    Public Function TrustedKeys() As IReadOnlyDictionary(Of String, Byte()) Implements IAnnouncementSource.TrustedKeys
        Return Keys
    End Function
    Public ReadOnly Property Description As String = "prueba" Implements IAnnouncementSource.Description
    Public ReadOnly Property IsConfigured As Boolean Implements IAnnouncementSource.IsConfigured
        Get
            Return Configured
        End Get
    End Property
End Class

Friend NotInheritable Class MemoryStateStore
    Implements IAnnouncementStateStore
    Public Property Saved As AnnouncementState
    Public Function Load() As AnnouncementState Implements IAnnouncementStateStore.Load
        Return Saved
    End Function
    Public Sub Save(state As AnnouncementState) Implements IAnnouncementStateStore.Save
        Saved = state
    End Sub
End Class

Public Class AnnouncementCenterTests

    Private Shared ReadOnly Here As New AnnouncementAudience("PC", "027")

    Private Shared Function Content(json As String) As Func(Of AnnouncementFetch)
        Return Function() New AnnouncementFetch With {.Content = json, .Source = "prueba"}
    End Function

    <Fact>
    Public Async Function ValidFile_IsShown_Saved_AndRestoredAfterARestart() As Task
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim store As New MemoryStateStore()
            Dim source As New ScriptedSource With {.Keys = Trust(key), .Answer = Content(SignedJson(key, Feed(7, Item("a"))))}
            Dim center As New AnnouncementCenter(source, store, Nothing)
            center.Load()
            Assert.True(Await center.CheckAsync(Now, CancellationToken.None))
            Assert.Equal({"a"}, center.Active(Now, Here).Select(Function(a) a.Id))
            Assert.Equal(7L, store.Saved.LastVersion)

            ' Restart without network: the saved file is verified again and shown
            Dim offline As New ScriptedSource With {.Keys = Trust(key), .Answer = Function() As AnnouncementFetch
                                                                                     Throw New InvalidOperationException("sin red")
                                                                                 End Function}
            Dim restarted As New AnnouncementCenter(offline, store, Nothing)
            restarted.Load()
            Assert.Equal({"a"}, restarted.Active(Now, Here).Select(Function(a) a.Id))
            Assert.False(Await restarted.CheckAsync(Now, CancellationToken.None))
            Assert.Equal({"a"}, restarted.Active(Now, Here).Select(Function(a) a.Id))
            Assert.Equal("sin red", restarted.Status().LastError)
        End Using
    End Function

    <Fact>
    Public Async Function SameFile_OrNotModified_IsNotAChange() As Task
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim json = SignedJson(key, Feed(7, Item("a")))
            Dim source As New ScriptedSource With {.Keys = Trust(key), .Answer = Content(json)}
            Dim center As New AnnouncementCenter(source, New MemoryStateStore(), Nothing)
            center.Load()
            Assert.True(Await center.CheckAsync(Now, CancellationToken.None))
            Assert.False(Await center.CheckAsync(Now, CancellationToken.None))
            source.Answer = Function() AnnouncementFetch.Unchanged
            Assert.False(Await center.CheckAsync(Now, CancellationToken.None))
            Assert.True(String.IsNullOrEmpty(center.Status().LastError))
        End Using
    End Function

    <Fact>
    Public Async Function OldOrForgedFile_KeepsWhatIsOnScreen() As Task
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256), attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim source As New ScriptedSource With {.Keys = Trust(key), .Answer = Content(SignedJson(key, Feed(9, Item("actual"))))}
            Dim center As New AnnouncementCenter(source, New MemoryStateStore(), Nothing)
            center.Load()
            Await center.CheckAsync(Now, CancellationToken.None)

            source.Answer = Content(SignedJson(key, Feed(8, Item("viejo"))))
            Assert.False(Await center.CheckAsync(Now, CancellationToken.None))
            Assert.Contains("más vieja", center.Status().LastError)

            source.Answer = Content(SignedJson(attacker, Feed(99, Item("falso"))))
            Assert.False(Await center.CheckAsync(Now, CancellationToken.None))
            Assert.Equal({"actual"}, center.Active(Now, Here).Select(Function(a) a.Id))
        End Using
    End Function

    <Fact>
    Public Sub EditedFileOnDisk_IsDiscardedOnLoad()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim signed = AnnouncementSigning.Sign(Feed(3, Item("a")), key)
            signed.Signature = Convert.ToBase64String(New Byte(63) {})
            Dim store As New MemoryStateStore With {.Saved = New AnnouncementState With {.LastVersion = 3, .SignedFile = AnnouncementSigning.Serialize(signed)}}
            Dim center As New AnnouncementCenter(New ScriptedSource With {.Keys = Trust(key)}, store, Nothing)
            center.Load()
            Assert.Empty(center.Active(Now, Here))
        End Using
    End Sub

    <Fact>
    Public Async Function Dismiss_IsRemembered_AndForgottenWhenTheAnnouncementIsRetired() As Task
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim store As New MemoryStateStore()
            Dim source As New ScriptedSource With {.Keys = Trust(key), .Answer = Content(SignedJson(key, Feed(1, Item("a"), Item("b"))))}
            Dim center As New AnnouncementCenter(source, store, Nothing)
            center.Load()
            Await center.CheckAsync(Now, CancellationToken.None)
            center.Dismiss("a", Now)
            Assert.Equal({"b"}, center.Active(Now, Here).Select(Function(a) a.Id))
            Assert.True(store.Saved.Dismissed.ContainsKey("a"))

            source.Answer = Content(SignedJson(key, Feed(2, Item("b"))))
            Await center.CheckAsync(Now, CancellationToken.None)
            Assert.False(store.Saved.Dismissed.ContainsKey("a"))

            center.Dismiss("b", Now)
            Assert.Empty(center.Active(Now, Here))
            center.RestoreDismissed()
            Assert.Equal({"b"}, center.Active(Now, Here).Select(Function(a) a.Id))
        End Using
    End Function

    <Fact>
    Public Async Function NotConfigured_DoesNotAsk() As Task
        Dim source As New ScriptedSource With {.Configured = False, .Answer = Function() As AnnouncementFetch
                                                                                  Throw New InvalidOperationException()
                                                                              End Function}
        Dim center As New AnnouncementCenter(source, New MemoryStateStore(), Nothing)
        center.Load()
        Assert.False(Await center.CheckAsync(Now, CancellationToken.None))
        Assert.Equal(0, source.Calls)
    End Function

End Class

''' <summary>Answers HTTP requests with a function, and keeps the last request.</summary>
Friend NotInheritable Class FakeHttp
    Inherits HttpMessageHandler
    Public Property Respond As Func(Of HttpRequestMessage, HttpResponseMessage)
    Public Property LastRequest As HttpRequestMessage
    Protected Overrides Function SendAsync(request As HttpRequestMessage, cancellationToken As CancellationToken) As Task(Of HttpResponseMessage)
        LastRequest = request
        Return Task.FromResult(Respond(request))
    End Function
End Class

Public Class AnnouncementFeedClientTests

    Private Shared Function Client(url As String, http As FakeHttp) As AnnouncementFeedClient
        Return New AnnouncementFeedClient(New Monitor(Of AnnouncementSettings)(New AnnouncementSettings With {.FeedUrl = url}), http)
    End Function

    <Fact>
    Public Async Function UsesTheETag_AndUnderstands304() As Task
        Dim http As New FakeHttp()
        http.Respond = Function(r)
                           If r.Headers.IfNoneMatch.Any() Then Return New HttpResponseMessage(HttpStatusCode.NotModified)
                           Dim ok As New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New StringContent("{""hola"":1}")}
                           ok.Headers.ETag = New Headers.EntityTagHeaderValue("""v1""")
                           Return ok
                       End Function
        Dim c = Client("https://ejemplo.com/control.json", http)
        Dim first = Await c.FetchAsync(CancellationToken.None)
        Assert.Equal("{""hola"":1}", first.Content)
        Dim second = Await c.FetchAsync(CancellationToken.None)
        Assert.True(second.NotModified)
        Assert.Equal("""v1""", http.LastRequest.Headers.IfNoneMatch.Single().Tag)
    End Function

    <Fact>
    Public Async Function HttpErrors_HaveAClearMessage() As Task
        Dim http As New FakeHttp With {.Respond = Function(r) New HttpResponseMessage(HttpStatusCode.NotFound)}
        Dim ex = Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() Client("https://ejemplo.com/control.json", http).FetchAsync(CancellationToken.None))
        Assert.Contains("404", ex.Message)
    End Function

    <Fact>
    Public Async Function TooBig_IsCutOff() As Task
        Dim huge = New String("x"c, AnnouncementRules.MaxFileBytes + 10)
        ' No Content-Length: the limit is enforced while reading
        Dim http As New FakeHttp With {.Respond = Function(r) New HttpResponseMessage(HttpStatusCode.OK) With {
            .Content = New StreamContent(New MemoryStream(Encoding.UTF8.GetBytes(huge)))}}
        Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() Client("https://ejemplo.com/control.json", http).FetchAsync(CancellationToken.None))
    End Function

    <Fact>
    Public Async Function ReadsAFilePath() As Task
        Dim path = IO.Path.Combine(IO.Path.GetTempPath(), "dm-anuncios-" & Guid.NewGuid().ToString("N") & ".json")
        Try
            File.WriteAllText(path, "{""a"":1}", New UTF8Encoding(True))
            Dim fetch = Await Client(path, New FakeHttp()).FetchAsync(CancellationToken.None)
            Assert.Equal("{""a"":1}", fetch.Content)
        Finally
            File.Delete(path)
        End Try
    End Function

    <Fact>
    Public Async Function PlainHttpToAnotherHost_IsRefused() As Task
        Dim http As New FakeHttp With {.Respond = Function(r) New HttpResponseMessage(HttpStatusCode.OK)}
        Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() Client("http://ejemplo.com/control.json", http).FetchAsync(CancellationToken.None))
        Assert.Null(http.LastRequest)
    End Function

End Class

Public Class DemoAnnouncementTests

    <Fact>
    Public Async Function Demo_GoesThroughTheRealVerification_AndTargets() As Task
        Dim demo As New DemoAnnouncementSource(New Monitor(Of DemoSettings)(New DemoSettings With {.SimulateAnnouncements = True}), New FixedClock(#10/06/2026 10:00#))
        Dim store As New MemoryStateStore()
        Dim center As New AnnouncementCenter(demo, store, Nothing)
        center.Load()
        Assert.True(Await center.CheckAsync(Now, CancellationToken.None))
        Dim active = center.Active(New DateTimeOffset(#10/06/2026 10:00#), New AnnouncementAudience("PC", "027"))
        Assert.Equal(2, active.Count)
        Assert.DoesNotContain(active, Function(a) a.Id = "demo-otra-planta")
        Assert.Contains(active, Function(a) a.Display = AnnouncementDisplay.Banner)
        ' Same day: same file, not a change
        Assert.False(Await center.CheckAsync(Now, CancellationToken.None))
    End Function

    <Fact>
    Public Sub Demo_IsOffUnlessAskedFor()
        Dim demo As New DemoAnnouncementSource(New Monitor(Of DemoSettings)(New DemoSettings()), New FixedClock(Date.Now))
        Assert.False(demo.IsConfigured)
        Assert.Empty(AnnouncementRules.Validate(DemoAnnouncementSource.Build(Date.Today)))
    End Sub

End Class
