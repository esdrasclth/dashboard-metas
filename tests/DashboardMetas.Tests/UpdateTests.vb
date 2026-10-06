Imports System.Security.Cryptography
Imports System.Text
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Updates
Imports Xunit

Public Class UpdateTests

    Private Shared Function Manifest(version As String, Optional release As Long = 100, Optional targets As String() = Nothing,
                                     Optional paused As Boolean = False, Optional downgrade As Boolean = False) As ReleaseManifest
        Return New ReleaseManifest With {
            .Release = release, .Version = version, .PublishedAt = AnnouncementMake.Now, .Notes = "notas",
            .File = New ReleaseFile With {.Pathname = $"versiones/paquetes/{version}-abcd.zip", .Size = 1234, .Sha256 = New String("a"c, 64)},
            .Install = ReleaseManifest.InstallOffShift, .Targets = If(targets, Array.Empty(Of String)()).ToList(),
            .Paused = paused, .AllowDowngrade = downgrade}
    End Function

    Private Shared ReadOnly Here As New AnnouncementAudience("TV-01", "027")

    <Fact>
    Public Sub SignedManifest_RoundTrip()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim check = ReleaseSigning.Verify(ReleaseSigning.Sign(Manifest("1.2.0"), key), Trust(key))
            Assert.True(check.IsValid, check.Error)
            Assert.Equal("1.2.0", check.Manifest.Version)
            Assert.Equal(1234L, check.Manifest.File.Size)
        End Using
    End Sub

    <Fact>
    Public Sub TamperedOrOld_IsRejected()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim json = ReleaseSigning.Sign(Manifest("1.2.0", release:=100), key)
            Dim signed = Text.Json.JsonSerializer.Deserialize(Of SignedFeed)(json, AnnouncementJson.Options)
            signed.Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Convert.FromBase64String(signed.Payload)).Replace(New String("a"c, 64), New String("b"c, 64))))
            Assert.False(ReleaseSigning.Verify(AnnouncementSigning.Serialize(signed), Trust(key)).IsValid)
            Assert.False(ReleaseSigning.Verify(json, Trust(key), minimumRelease:=101).IsValid)
            Assert.True(ReleaseSigning.Verify(json, Trust(key), minimumRelease:=100).IsValid)
        End Using
    End Sub

    <Fact>
    Public Sub AnAnnouncementsFile_IsNotAVersion_AndViceVersa()
        Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Assert.Contains("Formato", ReleaseSigning.Verify(SignedJson(key, Feed(1, Item("a"))), Trust(key)).Error)
            Assert.False(AnnouncementSigning.Verify(ReleaseSigning.Sign(Manifest("1.2.0"), key), Trust(key)).IsValid)
        End Using
    End Sub

    <Theory>
    <InlineData("1.2", "versiones/paquetes/x.zip", 64)>
    <InlineData("1.2.0", "otra/x.zip", 64)>
    <InlineData("1.2.0", "versiones/paquetes/x.exe", 64)>
    <InlineData("1.2.0", "versiones/paquetes/x.zip", 10)>
    Public Sub InvalidManifest_IsRejected(version As String, pathname As String, hashLength As Integer)
        Dim m = Manifest(version)
        m.File.Pathname = pathname
        m.File.Sha256 = New String("a"c, hashLength)
        Assert.NotEmpty(m.Validate())
    End Sub

    <Fact>
    Public Sub Policy_InstallsOnlyANewerVersionForThisPc()
        Dim current As New Version(1, 1, 0)
        Assert.Null(UpdatePolicy.WhyNot(Manifest("1.2.0"), current, Here, Nothing))
        Assert.NotNull(UpdatePolicy.WhyNot(Manifest("1.1.0"), current, Here, Nothing))                      ' same
        Assert.NotNull(UpdatePolicy.WhyNot(Manifest("1.0.9"), current, Here, Nothing))                      ' older
        Assert.Null(UpdatePolicy.WhyNot(Manifest("1.0.9", downgrade:=True), current, Here, Nothing))        ' going back on purpose
        Assert.NotNull(UpdatePolicy.WhyNot(Manifest("1.2.0", paused:=True), current, Here, Nothing))
        Assert.NotNull(UpdatePolicy.WhyNot(Manifest("1.2.0", targets:={"equipo:OTRA"}), current, Here, Nothing))
        Assert.Null(UpdatePolicy.WhyNot(Manifest("1.2.0", targets:={"planta:027"}), current, Here, Nothing))
        Assert.Contains("ya falló", UpdatePolicy.WhyNot(Manifest("1.2.0"), current, Here, {"1.2.0"}))
    End Sub

    <Fact>
    Public Sub Versions_CompareAsNumbers()
        Assert.True(ReleaseManifest.ParseVersion("1.10.0") > ReleaseManifest.ParseVersion("1.9.5"))
        Assert.Null(ReleaseManifest.ParseVersion("uno"))
    End Sub

End Class
