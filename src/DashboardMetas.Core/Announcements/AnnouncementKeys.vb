Namespace Announcements

    ''' <summary>
    ''' Public keys whose signature the app accepts. They live in the code, not in the configuration, so editing
    ''' appsettings.json cannot make the app trust another key. To rotate: create a new key with the
    ''' DashboardMetas.Anuncios tool, add it here, publish this version everywhere, and only then sign with it
    ''' (and remove the old one in a later version).
    ''' </summary>
    Public NotInheritable Class AnnouncementKeys

        Private Sub New()
        End Sub

        ''' <summary>Key id → SubjectPublicKeyInfo (base64).</summary>
        Private Shared ReadOnly Production As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
            {"510704804fe45c35", "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE0Hq0u0UV/oYvpxoGYibg+Idvx6VRdK3BMK8KpD/Wq3ORAIn0k63CrwPcT4v16NMLcfKWsZzINQuSslgvexkFAg=="}}

        Public Shared Function Trusted() As IReadOnlyDictionary(Of String, Byte())
            Return Production.ToDictionary(Function(p) p.Key, Function(p) Convert.FromBase64String(p.Value), StringComparer.Ordinal)
        End Function

    End Class

End Namespace
