Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports DashboardMetas.Core.Abstractions
Imports Microsoft.Extensions.Logging

Namespace Services

    ''' <summary>
    ''' JDE password encrypted with DPAPI (CurrentUser): only this Windows user on this PC can read it.
    ''' Real and demo passwords use different files.
    ''' </summary>
    Public NotInheritable Class DpapiCredentialStore
        Implements ICredentialStore

        Private Shared ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("DashboardMetas/JDE-password/v1")

        Private ReadOnly _paths As AppPaths
        Private ReadOnly _mode As AppMode
        Private ReadOnly _logger As ILogger

        Public Sub New(paths As AppPaths, mode As AppMode, logger As ILogger(Of DpapiCredentialStore))
            _paths = paths
            _mode = mode
            _logger = logger
        End Sub

        Private ReadOnly Property FilePath As String
            Get
                Return _paths.CredentialFile(_mode.IsDemo)
            End Get
        End Property

        Public ReadOnly Property HasSaved As Boolean Implements ICredentialStore.HasSaved
            Get
                Return File.Exists(FilePath)
            End Get
        End Property

        Public Function TryLoad() As String Implements ICredentialStore.TryLoad
            Try
                If Not File.Exists(FilePath) Then Return Nothing
                Dim plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser)
                Return Encoding.UTF8.GetString(plain)
            Catch ex As Exception
                ' Corrupted, or encrypted by another user/PC: forget it and ask again.
                _logger.LogWarning("No se pudo leer la contraseña guardada ({Error}); se pedirá de nuevo.", ex.GetType().Name)
                Delete()
                Return Nothing
            End Try
        End Function

        Public Sub Save(password As String) Implements ICredentialStore.Save
            Dim cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser)
            File.WriteAllBytes(FilePath, cipher)
            _logger.LogInformation("Contraseña guardada (cifrada con DPAPI)")
        End Sub

        Public Sub Delete() Implements ICredentialStore.Delete
            Try
                If File.Exists(FilePath) Then
                    File.Delete(FilePath)
                    _logger.LogInformation("Contraseña guardada eliminada")
                End If
            Catch ex As IOException
                _logger.LogWarning(ex, "No se pudo borrar la contraseña guardada")
            End Try
        End Sub

    End Class

End Namespace
