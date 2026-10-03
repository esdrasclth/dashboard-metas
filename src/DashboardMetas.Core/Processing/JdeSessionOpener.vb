Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports Microsoft.Extensions.Logging

Namespace Processing

    ''' <summary>
    ''' Opens the JDE session. Uses the saved password (DPAPI) and only asks for it when
    ''' <c>allowPrompt</c> is True: automatic refreshes never open a dialog on an unattended screen.
    ''' On CWBSY0002 the saved password is deleted (same as Cíclicos JDE).
    ''' </summary>
    Public NotInheritable Class JdeSessionOpener

        Public Const MaxAttempts As Integer = 3

        Private ReadOnly _repository As IProductionRepository
        Private ReadOnly _store As ICredentialStore
        Private ReadOnly _prompt As IPasswordPrompt
        Private ReadOnly _logger As ILogger

        Public Sub New(repository As IProductionRepository, store As ICredentialStore, prompt As IPasswordPrompt, logger As ILogger(Of JdeSessionOpener))
            _repository = repository
            _store = store
            _prompt = prompt
            _logger = If(CType(logger, ILogger), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
        End Sub

        ''' <param name="useDriverSignOn">True = no password is sent; the IBM i Access driver signs on by itself.</param>
        ''' <param name="allowPrompt">False for automatic refreshes: fail with PasswordNotProvided instead of asking.</param>
        ''' <param name="savePassword">False for "Probar conexión": nothing is written to disk.</param>
        Public Async Function OpenAsync(user As String, useDriverSignOn As Boolean, allowPrompt As Boolean, savePassword As Boolean,
                                        cancellationToken As CancellationToken) As Task(Of IProductionSession)
            If useDriverSignOn Then
                Dim session = Await _repository.OpenSessionAsync(user, Nothing, cancellationToken).ConfigureAwait(False)
                _logger.LogInformation("Conectado a {Source} como {User} con el inicio de sesión del driver ({Driver})", _repository.SourceDescription, user, session.DriverInfo)
                Return session
            End If

            Dim lastError As JdeConnectionException = Nothing
            Dim promptMessage = $"Contraseña de JDE (AS400) para {user}"

            For attempt = 1 To MaxAttempts
                cancellationToken.ThrowIfCancellationRequested()

                Dim password As String = Nothing
                Dim fromStore = False
                Dim remember = False

                If attempt = 1 Then
                    password = _store.TryLoad()
                    fromStore = Not String.IsNullOrEmpty(password)
                End If

                If Not fromStore Then
                    If Not allowPrompt Then
                        Throw If(lastError, New JdeConnectionException(JdeConnectionErrorKind.PasswordNotProvided,
                            "Falta la contraseña de JDE. Presiona «Conectar» para escribirla."))
                    End If
                    Dim answer = Await _prompt.PromptAsync(user, promptMessage, attempt > 1, cancellationToken).ConfigureAwait(False)
                    If answer Is Nothing OrElse String.IsNullOrEmpty(answer.Password) Then
                        Throw New JdeConnectionException(JdeConnectionErrorKind.PasswordNotProvided, "No se ingresó la contraseña de JDE.")
                    End If
                    password = answer.Password
                    remember = answer.Remember
                End If

                Try
                    Dim session = Await _repository.OpenSessionAsync(user, password, cancellationToken).ConfigureAwait(False)
                    _logger.LogInformation("Conectado a {Source} como {User} ({Driver})", _repository.SourceDescription, user, session.DriverInfo)
                    If savePassword AndAlso Not fromStore AndAlso remember Then _store.Save(password)
                    Return session
                Catch ex As JdeConnectionException When ex.Kind = JdeConnectionErrorKind.InvalidPassword
                    lastError = ex
                    _logger.LogWarning("Contraseña incorrecta o vencida (intento {Attempt} de {Max})", attempt, MaxAttempts)
                    If fromStore Then _store.Delete()
                    promptMessage = "Contraseña incorrecta o vencida (CWBSY0002). Intenta de nuevo."
                Catch ex As JdeConnectionException When ex.Kind = JdeConnectionErrorKind.PasswordExpired
                    If fromStore Then _store.Delete()
                    Throw
                End Try
            Next

            Throw lastError
        End Function

    End Class

End Namespace
