Imports System.Data.Odbc
Imports DashboardMetas.Core.Abstractions

Namespace Odbc

    ''' <summary>Translates driver errors into the known failures of the target PC, with actionable messages.</summary>
    Public NotInheritable Class OdbcErrorTranslator

        Private Sub New()
        End Sub

        Public Shared Function Translate(ex As Exception, dsn As String, user As String) As JdeConnectionException
            Dim states As New List(Of String)()
            Dim text = If(ex?.Message, String.Empty)
            Dim odbc = TryCast(ex, OdbcException)
            If odbc IsNot Nothing Then
                For Each e As OdbcError In odbc.Errors
                    states.Add(If(e.SQLState, String.Empty).ToUpperInvariant())
                Next
            End If
            Return Translate(states, text, dsn, user, ex)
        End Function

        ''' <summary>Classification by SQLSTATE and message (testable without a driver).</summary>
        Public Shared Function Translate(sqlStates As IEnumerable(Of String), driverMessage As String, dsn As String, user As String,
                                         Optional inner As Exception = Nothing) As JdeConnectionException
            Dim states = If(sqlStates, Enumerable.Empty(Of String)()).ToList()
            Dim msg = If(driverMessage, String.Empty)
            Dim has = Function(s As String) msg.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0

            If has("CWBSY0003") Then
                Return New JdeConnectionException(JdeConnectionErrorKind.PasswordExpired,
                    $"La contraseña de {user} venció (CWBSY0003). Cámbiala en el AS400 (pantalla verde) y vuelve a intentar.", msg, inner)
            End If
            If has("CWBSY0002") OrElse (has("password") AndAlso Not has("CWBSY")) Then
                Return New JdeConnectionException(JdeConnectionErrorKind.InvalidPassword,
                    $"Contraseña incorrecta o vencida para {user} (CWBSY0002).", msg, inner)
            End If
            If states.Contains("IM002") Then
                Return New JdeConnectionException(JdeConnectionErrorKind.DataSourceNotFound,
                    $"No se encontró el origen de datos ODBC ""{dsn}"". La app es de 32 bits: revisa que el DSN exista en " &
                    "el Administrador ODBC de 32 bits (C:\Windows\SysWOW64\odbcad32.exe) y que el nombre en Configuración sea idéntico.", msg, inner)
            End If
            If states.Contains("IM003") OrElse has("could not be loaded") OrElse has("no se pudo cargar") Then
                Return New JdeConnectionException(JdeConnectionErrorKind.DriverNotInstalled,
                    "No se pudo cargar el driver ODBC de IBM i Access (32 bits). Verifica que IBM i Access Client Solutions " &
                    "(paquete ODBC de Windows) esté instalado en esta PC.", msg, inner)
            End If
            If states.Any(Function(s) s.StartsWith("08", StringComparison.Ordinal)) OrElse has("CWBCO") Then
                Return New JdeConnectionException(JdeConnectionErrorKind.HostUnreachable,
                    "No se pudo comunicar con el AS400. Revisa la red o la VPN; se reintentará solo.", msg, inner)
            End If
            Return New JdeConnectionException(JdeConnectionErrorKind.Other, $"No se pudo conectar a JDE ({dsn}): {msg}", msg, inner)
        End Function

    End Class

End Namespace
