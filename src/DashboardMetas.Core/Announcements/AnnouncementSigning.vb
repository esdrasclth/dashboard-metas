Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json

Namespace Announcements

    ''' <summary>Result of checking a downloaded control file.</summary>
    Public NotInheritable Class FeedVerification
        Public Property IsValid As Boolean
        ''' <summary>Why it was rejected, in Spanish (empty when valid).</summary>
        Public Property [Error] As String = String.Empty
        Public Property Feed As AnnouncementFeed
        Public Property KeyId As String = String.Empty
        ''' <summary>Announcements dropped because they break <see cref="AnnouncementRules"/> (the rest are used).</summary>
        Public Property Warnings As IReadOnlyList(Of String) = Array.Empty(Of String)()

        Friend Shared Function Fail(message As String) As FeedVerification
            Return New FeedVerification With {.IsValid = False, .Error = message}
        End Function
    End Class

    ''' <summary>
    ''' Signs and verifies control files with ECDSA P-256 / SHA-256 (built into .NET, no extra packages).
    ''' The private key stays with the publisher; the app only carries public keys (<see cref="AnnouncementKeys"/>),
    ''' so nobody at the plant can forge an announcement even if they edit the configuration or the DNS.
    ''' </summary>
    Public NotInheritable Class AnnouncementSigning

        Private Sub New()
        End Sub

        ''' <summary>Short fingerprint of a public key (first 8 bytes of the SHA-256 of its SubjectPublicKeyInfo).</summary>
        Public Shared Function KeyIdOf(subjectPublicKeyInfo As Byte()) As String
            Return Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo), 0, 8).ToLowerInvariant()
        End Function

        ''' <summary>A new P-256 key pair: (private key PKCS#8 PEM, public key SubjectPublicKeyInfo base64, key id).</summary>
        Public Shared Function CreateKey() As (PrivatePem As String, PublicKey As String, KeyId As String)
            Using key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
                Dim spki = key.ExportSubjectPublicKeyInfo()
                Return (key.ExportPkcs8PrivateKeyPem(), Convert.ToBase64String(spki), KeyIdOf(spki))
            End Using
        End Function

        ''' <summary>
        ''' Opens a signed file of any kind (announcements, versions…): size, format, trusted key and ECDSA P-256 / SHA-256
        ''' signature over the payload bytes. Returns the payload bytes only when everything checks out.
        ''' </summary>
        ''' <param name="what">"de anuncios", "de versión"… for the messages ("El archivo de anuncios está vacío").</param>
        Public Shared Function OpenEnvelope(json As String, trustedKeys As IReadOnlyDictionary(Of String, Byte()), format As String, what As String) As (Ok As Boolean, Payload As Byte(), KeyId As String, [Error] As String)
            If String.IsNullOrWhiteSpace(json) Then Return (False, Nothing, "", $"El archivo {what} está vacío.")
            If Encoding.UTF8.GetByteCount(json) > AnnouncementRules.MaxFileBytes Then
                Return (False, Nothing, "", $"El archivo {what} pasa de {AnnouncementRules.MaxFileBytes \ 1024} KB.")
            End If

            Dim signed As SignedFeed
            Try
                signed = JsonSerializer.Deserialize(Of SignedFeed)(json, AnnouncementJson.Options)
            Catch ex As JsonException
                Return (False, Nothing, "", $"El archivo {what} no es un JSON válido: " & ex.Message)
            End Try
            If signed Is Nothing Then Return (False, Nothing, "", $"El archivo {what} está vacío.")
            If Not String.Equals(signed.Format, format, StringComparison.Ordinal) Then
                Return (False, Nothing, "", $"Formato «{signed.Format}» no reconocido (se espera «{format}»).")
            End If

            Dim publicKey As Byte() = Nothing
            If trustedKeys Is Nothing OrElse String.IsNullOrEmpty(signed.KeyId) OrElse Not trustedKeys.TryGetValue(signed.KeyId, publicKey) Then
                Return (False, Nothing, "", $"Firmado con una clave que esta versión no reconoce ({If(signed.KeyId, "sin id")}).")
            End If

            Dim payload, signature As Byte()
            Try
                payload = Convert.FromBase64String(If(signed.Payload, String.Empty))
                signature = Convert.FromBase64String(If(signed.Signature, String.Empty))
            Catch ex As FormatException
                Return (False, Nothing, "", "El contenido o la firma no están en base64.")
            End Try

            Dim valid As Boolean
            Try
                Using key = ECDsa.Create()
                    key.ImportSubjectPublicKeyInfo(publicKey, Nothing)
                    valid = key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                End Using
            Catch ex As CryptographicException
                valid = False
            End Try
            If Not valid Then Return (False, Nothing, "", "La firma no corresponde: el archivo fue modificado o no lo firmó la clave indicada.")
            Return (True, payload, signed.KeyId, "")
        End Function

        ''' <summary>Signs any payload (serialized as camelCase JSON) in the envelope of <paramref name="format"/>.</summary>
        Public Shared Function SignPayload(Of T)(payloadObject As T, key As ECDsa, format As String) As SignedFeed
            If key.KeySize <> 256 Then Throw New ArgumentException("La clave debe ser ECDSA P-256.")
            Dim payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payloadObject, AnnouncementJson.Options))
            Dim signature = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            Return New SignedFeed With {
                .Format = format,
                .KeyId = KeyIdOf(key.ExportSubjectPublicKeyInfo()),
                .Payload = Convert.ToBase64String(payload),
                .Signature = Convert.ToBase64String(signature)}
        End Function

        ''' <summary>Signs <paramref name="feed"/> with a PKCS#8 PEM P-256 private key.</summary>
        Public Shared Function Sign(feed As AnnouncementFeed, privateKeyPem As String) As SignedFeed
            Using key = ECDsa.Create()
                key.ImportFromPem(privateKeyPem)
                Return Sign(feed, key)
            End Using
        End Function

        Public Shared Function Sign(feed As AnnouncementFeed, key As ECDsa) As SignedFeed
            Return SignPayload(feed, key, SignedFeed.CurrentFormat)
        End Function

        Public Shared Function Serialize(signed As SignedFeed) As String
            Return JsonSerializer.Serialize(signed, AnnouncementJson.Options)
        End Function

        ''' <summary>
        ''' Checks a downloaded control file: size, format, trusted key, signature, and that its version is not
        ''' older than <paramref name="minimumVersion"/> (anti-rollback). Invalid announcements inside a valid
        ''' file are dropped and reported in <see cref="FeedVerification.Warnings"/>.
        ''' </summary>
        ''' <param name="trustedKeys">Key id → SubjectPublicKeyInfo.</param>
        Public Shared Function Verify(json As String, trustedKeys As IReadOnlyDictionary(Of String, Byte()), Optional minimumVersion As Long = 0) As FeedVerification
            Dim opened = OpenEnvelope(json, trustedKeys, SignedFeed.CurrentFormat, "de anuncios")
            If Not opened.Ok Then Return FeedVerification.Fail(opened.Error)
            Dim payload = opened.Payload
            Dim signed As New SignedFeed With {.KeyId = opened.KeyId}

            Dim feed As AnnouncementFeed
            Try
                feed = JsonSerializer.Deserialize(Of AnnouncementFeed)(payload, AnnouncementJson.Options)
            Catch ex As JsonException
                Return FeedVerification.Fail("El contenido firmado no es válido: " & ex.Message)
            End Try
            If feed Is Nothing Then Return FeedVerification.Fail("El contenido firmado está vacío.")
            If feed.Version < minimumVersion Then
                Return FeedVerification.Fail($"Versión {feed.Version} más vieja que la ya recibida ({minimumVersion}); se ignora.")
            End If

            ' A valid signature with a bad entry: keep the good ones
            Dim warnings As New List(Of String)()
            Dim all = If(feed.Announcements, New List(Of Announcement)()).Where(Function(a) a IsNot Nothing).ToList()
            Dim kept As New List(Of Announcement)()
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each a In all.Take(AnnouncementRules.MaxAnnouncements)
                Dim problems = AnnouncementRules.Validate(a)
                If problems.Count > 0 Then
                    warnings.AddRange(problems)
                ElseIf Not seen.Add(a.Id) Then
                    warnings.Add($"El id «{a.Id}» está repetido; se usa el primero.")
                Else
                    kept.Add(a)
                End If
            Next
            If all.Count > AnnouncementRules.MaxAnnouncements Then warnings.Add($"Solo se usan los primeros {AnnouncementRules.MaxAnnouncements} anuncios.")
            feed.Announcements = kept
            Return New FeedVerification With {.IsValid = True, .Feed = feed, .KeyId = signed.KeyId, .Warnings = warnings}
        End Function

    End Class

End Namespace
