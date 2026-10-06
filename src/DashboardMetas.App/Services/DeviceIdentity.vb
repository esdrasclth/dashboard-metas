Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports DashboardMetas.Core.Announcements
Imports Microsoft.Extensions.Logging

Namespace Services

    ''' <summary>
    ''' This PC's own key for the panel (ECDSA P-256), created on first use and kept in equipo.key encrypted with DPAPI
    ''' (CurrentUser): it signs every status report, and its fingerprint is the screen's id in the panel. A report
    ''' therefore can only update its own screen. If the file is lost or unreadable a new key is made and the screen
    ''' shows up in the panel as a new one.
    ''' </summary>
    Public NotInheritable Class DeviceIdentity
        Implements IDisposable

        Private Shared ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("DashboardMetas/device-key/v1")

        Private ReadOnly _key As ECDsa

        Public Sub New(paths As AppPaths, logger As ILogger(Of DeviceIdentity))
            Dim file = Path.Combine(paths.DataFolder, "equipo.key")
            _key = ECDsa.Create(ECCurve.NamedCurves.nistP256)
            Dim loaded = False
            Try
                If IO.File.Exists(file) Then
                    _key.ImportPkcs8PrivateKey(ProtectedData.Unprotect(IO.File.ReadAllBytes(file), Entropy, DataProtectionScope.CurrentUser), Nothing)
                    loaded = True
                End If
            Catch ex As Exception
                logger.LogWarning("No se pudo leer la clave de este equipo ({Error}); se crea una nueva.", ex.GetType().Name)
            End Try
            If Not loaded Then
                Try
                    IO.File.WriteAllBytes(file, ProtectedData.Protect(_key.ExportPkcs8PrivateKey(), Entropy, DataProtectionScope.CurrentUser))
                Catch ex As Exception
                    logger.LogWarning(ex, "No se pudo guardar la clave de este equipo; se usará solo en esta sesión")
                End Try
            End If
            Dim spki = _key.ExportSubjectPublicKeyInfo()
            PublicKey = Convert.ToBase64String(spki)
            Id = AnnouncementSigning.KeyIdOf(spki)
        End Sub

        ''' <summary>Fingerprint of the key: the id of this screen in the panel.</summary>
        Public ReadOnly Property Id As String
        Public ReadOnly Property PublicKey As String

        Public Function Sign(body As Byte()) As String
            Return Core.Remote.ReportSigning.Sign(body, _key)
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            _key.Dispose()
        End Sub

    End Class

End Namespace
