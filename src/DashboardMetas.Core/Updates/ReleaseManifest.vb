Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports DashboardMetas.Core.Announcements

Namespace Updates

    ''' <summary>The installer package of a version: a zip with DashboardMetas.exe and appsettings.json.</summary>
    Public NotInheritable Class ReleaseFile
        ''' <summary>Path of the zip in the panel's private storage (the panel turns it into a temporary download link).</summary>
        Public Property Pathname As String = String.Empty
        Public Property Size As Long
        ''' <summary>SHA-256 of the zip, lowercase hex: the app installs nothing that does not match.</summary>
        Public Property Sha256 As String = String.Empty
    End Class

    ''' <summary>
    ''' A published version (signed with the same key as the announcements, envelope «dashboardmetas-version/1»).
    ''' Same JSON as web/anuncios/public/releases.js.
    ''' </summary>
    Public NotInheritable Class ReleaseManifest
        Public Const Format As String = "dashboardmetas-version/1"

        ''' <summary>Grows with every publication (UTC seconds): an older manifest is never accepted again.</summary>
        Public Property Release As Long
        ''' <summary>"1.2.0".</summary>
        Public Property Version As String = String.Empty
        Public Property PublishedAt As DateTimeOffset
        Public Property Notes As String = String.Empty
        Public Property File As New ReleaseFile()
        ''' <summary>"fuera-de-turno" (default: when no area is in its shift) or "ahora".</summary>
        Public Property Install As String = InstallOffShift
        ''' <summary>Same targets as the announcements: empty or "*" = every PC; "planta:027"; "equipo:NAME".</summary>
        Public Property Targets As List(Of String) = New List(Of String)()
        ''' <summary>True = stop handing it out (PCs that have not installed it yet wait).</summary>
        Public Property Paused As Boolean
        ''' <summary>True = also install over a newer version (to go back to this one).</summary>
        Public Property AllowDowngrade As Boolean

        Public Const InstallOffShift As String = "fuera-de-turno"
        Public Const InstallNow As String = "ahora"
        Public Const MaxFileSize As Long = 200L * 1024 * 1024

        Private Shared ReadOnly VersionPattern As New Regex("^\d{1,4}\.\d{1,4}\.\d{1,6}$", RegexOptions.CultureInvariant)
        Private Shared ReadOnly HashPattern As New Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)

        ''' <summary>Problems in Spanish (empty = valid).</summary>
        Public Function Validate() As IReadOnlyList(Of String)
            Dim errors As New List(Of String)()
            If Release <= 0 Then errors.Add("Falta el número de publicación.")
            If Not VersionPattern.IsMatch(If(Version, String.Empty)) Then errors.Add("La versión debe ser como 1.2.0.")
            If File Is Nothing Then
                errors.Add("Falta el archivo.")
            Else
                If String.IsNullOrWhiteSpace(File.Pathname) OrElse Not File.Pathname.StartsWith("versiones/", StringComparison.Ordinal) OrElse Not File.Pathname.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then
                    errors.Add("El archivo debe ser un .zip dentro de «versiones/».")
                End If
                If File.Size <= 0 OrElse File.Size > MaxFileSize Then errors.Add($"El tamaño del archivo debe ser de 1 byte a {MaxFileSize \ 1024 \ 1024} MB.")
                If Not HashPattern.IsMatch(If(File.Sha256, String.Empty)) Then errors.Add("La huella SHA-256 no es válida.")
            End If
            If Install <> InstallOffShift AndAlso Install <> InstallNow Then errors.Add("La instalación debe ser «fuera-de-turno» o «ahora».")
            If If(Notes, String.Empty).Length > 2000 Then errors.Add("Las notas pasan de 2,000 caracteres.")
            If If(Targets, New List(Of String)()).Count > 100 OrElse If(Targets, New List(Of String)()).Any(Function(t) String.IsNullOrWhiteSpace(t)) Then errors.Add("Destinos no válidos.")
            Return errors
        End Function

        ''' <summary>"1.2.0" → comparable; Nothing when not a valid version.</summary>
        Public Shared Function ParseVersion(text As String) As Version
            Dim parts = If(text, String.Empty).Trim().Split("."c)
            If parts.Length < 3 Then Return Nothing
            Dim major, minor, build As Integer
            If Not Integer.TryParse(parts(0), major) OrElse Not Integer.TryParse(parts(1), minor) OrElse Not Integer.TryParse(parts(2), build) Then Return Nothing
            Return New Version(major, minor, build)
        End Function
    End Class

    Public NotInheritable Class ReleaseCheck
        Public Property IsValid As Boolean
        Public Property [Error] As String = String.Empty
        Public Property Manifest As ReleaseManifest
        Public Property KeyId As String = String.Empty
    End Class

    ''' <summary>Opens and checks a signed version manifest.</summary>
    Public NotInheritable Class ReleaseSigning

        Private Sub New()
        End Sub

        Public Shared Function Verify(json As String, trustedKeys As IReadOnlyDictionary(Of String, Byte()), Optional minimumRelease As Long = 0) As ReleaseCheck
            Dim opened = AnnouncementSigning.OpenEnvelope(json, trustedKeys, ReleaseManifest.Format, "de versión")
            If Not opened.Ok Then Return New ReleaseCheck With {.Error = opened.Error}
            Dim manifest As ReleaseManifest
            Try
                manifest = JsonSerializer.Deserialize(Of ReleaseManifest)(opened.Payload, AnnouncementJson.Options)
            Catch ex As JsonException
                Return New ReleaseCheck With {.Error = "El contenido firmado no es válido: " & ex.Message}
            End Try
            If manifest Is Nothing Then Return New ReleaseCheck With {.Error = "El contenido firmado está vacío."}
            Dim problems = manifest.Validate()
            If problems.Count > 0 Then Return New ReleaseCheck With {.Error = problems(0)}
            If manifest.Release < minimumRelease Then
                Return New ReleaseCheck With {.Error = $"Publicación {manifest.Release} más vieja que la ya recibida ({minimumRelease}); se ignora."}
            End If
            Return New ReleaseCheck With {.IsValid = True, .Manifest = manifest, .KeyId = opened.KeyId}
        End Function

        Public Shared Function Sign(manifest As ReleaseManifest, key As Security.Cryptography.ECDsa) As String
            Return AnnouncementSigning.Serialize(AnnouncementSigning.SignPayload(manifest, key, ReleaseManifest.Format))
        End Function

    End Class

    ''' <summary>Whether this PC should install a manifest, and when.</summary>
    Public NotInheritable Class UpdatePolicy

        Private Sub New()
        End Sub

        ''' <summary>Nothing = install it; otherwise why not (in Spanish, for the status).</summary>
        Public Shared Function WhyNot(manifest As ReleaseManifest, current As Version, audience As AnnouncementAudience, blocked As IEnumerable(Of String)) As String
            If manifest.Paused Then Return "Actualización en pausa."
            Dim target = ReleaseManifest.ParseVersion(manifest.Version)
            If target Is Nothing Then Return "Versión no válida."
            If target = current Then Return "Ya tiene esta versión."
            If target < current AndAlso Not manifest.AllowDowngrade Then Return $"Ya tiene una versión más nueva ({current.ToString(3)})."
            If If(blocked, Enumerable.Empty(Of String)()).Contains(manifest.Version, StringComparer.OrdinalIgnoreCase) Then
                Return $"La versión {manifest.Version} ya falló al instalarse en esta PC; no se reintenta."
            End If
            Dim probe As New Announcement With {.Targets = If(manifest.Targets, New List(Of String)())}
            If Not AnnouncementSelector.IsFor(probe, audience) Then Return "Esta versión no va dirigida a esta PC."
            Return Nothing
        End Function

    End Class

End Namespace
