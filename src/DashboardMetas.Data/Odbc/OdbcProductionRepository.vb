Imports System.Data
Imports System.Data.Odbc
Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Models
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options

Namespace Odbc

    ''' <summary>Reads the daily production from JD Edwards World through the configured DSN (IBM i Access ODBC, 32 bits).</summary>
    Public NotInheritable Class OdbcProductionRepository
        Implements IProductionRepository

        Private ReadOnly _settings As IOptionsMonitor(Of JdeSettings)
        Private ReadOnly _logger As ILogger

        Public Sub New(settings As IOptionsMonitor(Of JdeSettings), logger As ILogger(Of OdbcProductionRepository))
            _settings = settings
            _logger = If(CType(logger, ILogger), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
        End Sub

        Public ReadOnly Property IsDemo As Boolean Implements IProductionRepository.IsDemo
            Get
                Return False
            End Get
        End Property

        Public ReadOnly Property SourceDescription As String Implements IProductionRepository.SourceDescription
            Get
                Return "DSN " & _settings.CurrentValue.Dsn
            End Get
        End Property

        Public Function OpenSessionAsync(user As String, password As String, cancellationToken As CancellationToken) As Task(Of IProductionSession) Implements IProductionRepository.OpenSessionAsync
            Dim settings = _settings.CurrentValue
            Dim errors = settings.Validate()
            If errors.Count > 0 Then Throw New JdeConnectionException(JdeConnectionErrorKind.Other, "Configuración de JDE no válida: " & String.Join(" ", errors))

            ' Connection string is never logged (it contains the password).
            Dim builder As New OdbcConnectionStringBuilder()
            builder.Dsn = settings.Dsn
            builder("UID") = user
            If password IsNot Nothing Then builder("PWD") = password

            Return Task.Run(Of IProductionSession)(
                Function()
                    cancellationToken.ThrowIfCancellationRequested()
                    Dim connection As New OdbcConnection(builder.ConnectionString)
                    Try
                        connection.ConnectionTimeout = Math.Max(1, settings.ConnectionTimeoutSeconds)
                        connection.Open()
                    Catch ex As Exception When TypeOf ex IsNot OperationCanceledException
                        connection.Dispose()
                        Dim translated = OdbcErrorTranslator.Translate(ex, settings.Dsn, user)
                        _logger.LogWarning("Conexión fallida a DSN {Dsn} como {User}: {Kind} - {DriverMessage}", settings.Dsn, user, translated.Kind, translated.DriverMessage)
                        Throw translated
                    End Try
                    Return New OdbcProductionSession(connection, settings, _logger)
                End Function, cancellationToken)
        End Function

    End Class

    Friend NotInheritable Class OdbcProductionSession
        Implements IProductionSession

        Private ReadOnly _connection As OdbcConnection
        Private ReadOnly _settings As JdeSettings
        Private ReadOnly _logger As ILogger

        Public Sub New(connection As OdbcConnection, settings As JdeSettings, logger As ILogger)
            _connection = connection
            _settings = settings
            _logger = logger
            DriverInfo = SafeDriverInfo(connection)
        End Sub

        Public ReadOnly Property DriverInfo As String Implements IProductionSession.DriverInfo

        Public Function GetDailyAsync(source As ProductionSource, fromDate As Date, cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of DailyProduction)) Implements IProductionSession.GetDailyAsync
            Dim query = JdeQueries.Build(source, _settings, fromDate)
            Return Execute(query, source, cancellationToken,
                Function(reader)
                    Dim iArea = reader.GetOrdinal("AREA"), iDate = reader.GetOrdinal("FECHA"), iPieces = reader.GetOrdinal("PIEZAS")
                    Dim iValue = reader.GetOrdinal("COSTO_TOTAL"), iStyles = reader.GetOrdinal("ESTILOS")
                    Return Function()
                               If reader.IsDBNull(iDate) Then Return Nothing
                               Dim row As New DailyProduction With {
                                   .Area = JdeValueConverter.ToText(reader.GetValue(iArea)).Trim(),
                                   .Date = JdeValueConverter.ToDate(reader.GetValue(iDate)),
                                   .Pieces = JdeValueConverter.ToDecimal(reader.GetValue(iPieces)),
                                   .Value = JdeValueConverter.ToDecimal(reader.GetValue(iValue)),
                                   .Styles = JdeValueConverter.ToInt32(reader.GetValue(iStyles))}
                               Return JdeQueries.Scale(source, row, _settings)
                           End Function
                End Function)
        End Function

        Public Function GetFloatAsync(classifyFrom As Date, cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of FloatItem)) Implements IProductionSession.GetFloatAsync
            Dim query = JdeQueries.Float(_settings, classifyFrom)
            Return Execute(query, ProductionSource.Float, cancellationToken,
                Function(reader)
                    Dim iStatus = reader.GetOrdinal("ESTATUS"), iStyle = reader.GetOrdinal("ESTILO"), iLine = reader.GetOrdinal("LINEA")
                    Dim iPieces = reader.GetOrdinal("PIEZAS"), iValue = reader.GetOrdinal("COSTO_TOTAL"), iOrders = reader.GetOrdinal("ORDENES")
                    Return Function()
                               Dim item As New FloatItem With {
                                   .Status = JdeValueConverter.ToText(reader.GetValue(iStatus)).Trim(),
                                   .Style = JdeValueConverter.ToText(reader.GetValue(iStyle)).Trim(),
                                   .ProductLine = JdeValueConverter.ToText(reader.GetValue(iLine)).Trim(),
                                   .Pieces = JdeValueConverter.ToDecimal(reader.GetValue(iPieces)),
                                   .Value = JdeValueConverter.ToDecimal(reader.GetValue(iValue)),
                                   .Orders = JdeValueConverter.ToInt32(reader.GetValue(iOrders))}
                               Return JdeQueries.ScaleFloat(item, _settings)
                           End Function
                End Function)
        End Function

        ''' <summary>
        ''' Runs <paramref name="query"/> on a worker thread and maps each row. <paramref name="mapper"/> receives the
        ''' open reader once (to look up the column ordinals) and returns the function that reads the current row
        ''' (Nothing = skip the row). Cancelling the token cancels the command on the AS400.
        ''' </summary>
        Private Function Execute(Of T As Class)(query As JdeQuery, source As ProductionSource, cancellationToken As CancellationToken,
                                               mapper As Func(Of OdbcDataReader, Func(Of T))) As Task(Of IReadOnlyList(Of T))
            Return Task.Run(Of IReadOnlyList(Of T))(
                Function()
                    Using command As New OdbcCommand(query.Sql, _connection)
                        command.CommandTimeout = _settings.CommandTimeoutSeconds
                        For Each p In query.Parameters
                            Dim parameter = command.Parameters.Add(p.Name, p.Type)
                            If p.Size > 0 Then parameter.Size = p.Size
                            parameter.Value = p.Value
                        Next

                        Using registration = cancellationToken.Register(Sub() TryCancel(command))
                            Dim rows As New List(Of T)()
                            Try
                                Using reader = command.ExecuteReader(CommandBehavior.SingleResult)
                                    Dim readRow = mapper(reader)
                                    While reader.Read()
                                        cancellationToken.ThrowIfCancellationRequested()
                                        Dim row = readRow()
                                        If row IsNot Nothing Then rows.Add(row)
                                    End While
                                End Using
                            Catch ex As OdbcException
                                cancellationToken.ThrowIfCancellationRequested()
                                Dim states = String.Join(", ", ex.Errors.Cast(Of OdbcError)().Select(Function(e) e.SQLState).Distinct())
                                Throw New InvalidOperationException($"Error de JDE en la consulta de {AreaCatalog.SourceName(source)} ({states}): {ex.Message}", ex)
                            End Try
                            cancellationToken.ThrowIfCancellationRequested()
                            Return rows.AsReadOnly()
                        End Using
                    End Using
                End Function, cancellationToken)
        End Function

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            _connection.Dispose()
            Return ValueTask.CompletedTask
        End Function

        Private Sub TryCancel(command As OdbcCommand)
            Try
                command.Cancel()
            Catch ex As Exception
                _logger.LogDebug(ex, "No se pudo cancelar la consulta")
            End Try
        End Sub

        Private Shared Function SafeDriverInfo(connection As OdbcConnection) As String
            Try
                Return $"{connection.Driver} {connection.ServerVersion}".Trim()
            Catch
                Return "IBM i Access ODBC"
            End Try
        End Function

    End Class

End Namespace
