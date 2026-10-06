Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Models
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options

Namespace Processing

    ''' <summary>State of one of the queries (the three daily sources and the float).</summary>
    Public NotInheritable Class SourceStatus
        Public Property Source As ProductionSource
        ''' <summary>Last time JDE answered this query.</summary>
        Public Property LastSuccess As Date?
        ''' <summary>Last error (empty when the last attempt worked).</summary>
        Public Property LastError As String = String.Empty
        Public Property LastErrorAt As Date?
        ''' <summary>The last failure was a missing or wrong password.</summary>
        Public Property NeedsPassword As Boolean

        Public ReadOnly Property HasError As Boolean
            Get
                Return Not String.IsNullOrEmpty(LastError)
            End Get
        End Property

        Public Function Clone() As SourceStatus
            Return DirectCast(MemberwiseClone(), SourceStatus)
        End Function
    End Class

    ''' <summary>The last data received, saved to disk so the screen opens with numbers before JDE answers.</summary>
    Public NotInheritable Class ProductionCache
        Public Property SavedAt As Date
        Public Property Rows As List(Of DailyProduction) = New List(Of DailyProduction)()
        ''' <summary>Key: <see cref="ProductionSource"/> name.</summary>
        Public Property LastSuccess As Dictionary(Of String, Date) = New Dictionary(Of String, Date)()
        ''' <summary>Last custom float received (a picture of the moment in LastSuccess("Float")).</summary>
        Public Property FloatItems As List(Of FloatItem) = New List(Of FloatItem)()
    End Class

    Public NotInheritable Class RefreshOutcome
        Public Property Succeeded As Integer
        Public Property Failed As Integer
        Public Property NeedsPassword As Boolean
        Public Property ConnectionError As String = String.Empty
    End Class

    ''' <summary>
    ''' Runs the three daily queries and the float with one connection, starting with the one on screen.
    ''' If one fails the others are still updated, and the one that failed keeps its last data
    ''' (same behaviour as the Access version).
    ''' </summary>
    Public NotInheritable Class ProductionRefresher

        Private ReadOnly _opener As JdeSessionOpener
        Private ReadOnly _jde As IOptionsMonitor(Of JdeSettings)
        Private ReadOnly _clock As IClock
        Private ReadOnly _logger As ILogger
        Private ReadOnly _gate As New Object()
        Private ReadOnly _busy As New SemaphoreSlim(1, 1)
        Private ReadOnly _rows As New Dictionary(Of String, List(Of DailyProduction))(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly _status As New Dictionary(Of ProductionSource, SourceStatus)()
        Private _float As New List(Of FloatItem)()

        ''' <summary>Days of F58C3120 used to find the product line of each float style (same as avanceMeta.vbs).</summary>
        Public Const FloatClassifyDays As Integer = 365

        Public Sub New(opener As JdeSessionOpener, jde As IOptionsMonitor(Of JdeSettings), clock As IClock, logger As ILogger(Of ProductionRefresher))
            _opener = opener
            _jde = jde
            _clock = clock
            _logger = If(CType(logger, ILogger), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
            For Each source In AreaCatalog.AllQueries
                _status(source) = New SourceStatus With {.Source = source}
            Next
        End Sub

        Public ReadOnly Property IsBusy As Boolean
            Get
                Return _busy.CurrentCount = 0
            End Get
        End Property

        Public Function GetRows(area As String) As IReadOnlyList(Of DailyProduction)
            SyncLock _gate
                Dim list As List(Of DailyProduction) = Nothing
                Return If(_rows.TryGetValue(If(area, String.Empty), list), list.ToList(), New List(Of DailyProduction)())
            End SyncLock
        End Function

        ''' <summary>The last custom float received (empty until JDE answers once).</summary>
        Public Function GetFloat() As IReadOnlyList(Of FloatItem)
            SyncLock _gate
                Return _float.ToList()
            End SyncLock
        End Function

        Public Function GetStatus(source As ProductionSource) As SourceStatus
            SyncLock _gate
                Return _status(source).Clone()
            End SyncLock
        End Function

        Public Function ExportCache() As ProductionCache
            SyncLock _gate
                Dim cache As New ProductionCache With {.SavedAt = _clock.Now}
                cache.Rows = _rows.Values.SelectMany(Function(l) l).ToList()
                cache.FloatItems = _float.ToList()
                For Each pair In _status
                    If pair.Value.LastSuccess.HasValue Then cache.LastSuccess(pair.Key.ToString()) = pair.Value.LastSuccess.Value
                Next
                Return cache
            End SyncLock
        End Function

        ''' <summary>Replaces everything in memory (Nothing = start empty, e.g. when switching demo ↔ JDE).</summary>
        Public Sub ImportCache(cache As ProductionCache)
            If cache Is Nothing Then cache = New ProductionCache()
            SyncLock _gate
                _rows.Clear()
                _float = If(cache.FloatItems, New List(Of FloatItem)()).Where(Function(i) i IsNot Nothing).ToList()
                For Each source In AreaCatalog.AllQueries
                    _status(source) = New SourceStatus With {.Source = source}
                Next
                For Each group In If(cache.Rows, New List(Of DailyProduction)()).Where(Function(r) r IsNot Nothing).GroupBy(Function(r) r.Area, StringComparer.OrdinalIgnoreCase)
                    _rows(group.Key) = group.ToList()
                Next
                For Each pair In If(cache.LastSuccess, New Dictionary(Of String, Date)())
                    Dim source As ProductionSource
                    If [Enum].TryParse(pair.Key, source) AndAlso _status.ContainsKey(source) Then _status(source).LastSuccess = pair.Value
                Next
            End SyncLock
        End Sub

        ''' <summary>
        ''' The daily sources starting with <paramref name="first"/>, then the float (it does not change the areas'
        ''' numbers). When the float is on screen it goes first.
        ''' </summary>
        Public Shared Function QueryOrder(first As ProductionSource) As IReadOnlyList(Of ProductionSource)
            Dim all = AreaCatalog.AllSources.ToList()
            Dim start = Math.Max(0, all.IndexOf(first))
            Dim order = Enumerable.Range(0, all.Count).Select(Function(i) all((start + i) Mod all.Count)).ToList()
            If first = ProductionSource.Float Then order.Insert(0, ProductionSource.Float) Else order.Add(ProductionSource.Float)
            Return order
        End Function

        ''' <param name="progress">Reported after each source finishes (worked or failed).</param>
        Public Async Function RefreshAsync(first As ProductionSource, fromDate As Date, allowPrompt As Boolean,
                                           progress As IProgress(Of ProductionSource), cancellationToken As CancellationToken) As Task(Of RefreshOutcome)
            Dim outcome As New RefreshOutcome()
            If Not Await _busy.WaitAsync(0, cancellationToken).ConfigureAwait(False) Then Return outcome
            Try
                Dim settings = _jde.CurrentValue
                Dim order = QueryOrder(first)
                Dim session As IProductionSession
                Try
                    session = Await _opener.OpenAsync(settings.User, settings.UseDriverSignOn, allowPrompt, savePassword:=True, cancellationToken).ConfigureAwait(False)
                Catch ex As JdeConnectionException
                    _logger.LogWarning("Sin conexión con JDE: {Message}", ex.Message)
                    outcome.ConnectionError = ex.Message
                    outcome.NeedsPassword = ex.NeedsPassword
                    For Each source In order
                        MarkFailed(source, ex.Message, ex.NeedsPassword)
                        outcome.Failed += 1
                        progress?.Report(source)
                    Next
                    Return outcome
                End Try

                ' VB cannot Await inside Finally: capture the failure, close the connection, then rethrow
                Dim failure As Runtime.ExceptionServices.ExceptionDispatchInfo = Nothing
                Try
                    For Each source In order
                        cancellationToken.ThrowIfCancellationRequested()
                        Dim watch = Diagnostics.Stopwatch.StartNew()
                        Try
                            Dim count As Integer
                            If source = ProductionSource.Float Then
                                Dim items = Await session.GetFloatAsync(_clock.Now.Date.AddDays(-FloatClassifyDays), cancellationToken).ConfigureAwait(False)
                                StoreFloat(items)
                                count = items.Count
                            Else
                                Dim rows = Await session.GetDailyAsync(source, fromDate, cancellationToken).ConfigureAwait(False)
                                Store(source, rows)
                                count = rows.Count
                            End If
                            outcome.Succeeded += 1
                            _logger.LogInformation("{Source}: {Count} filas en {Seconds:0.0} s", AreaCatalog.SourceName(source), count, watch.Elapsed.TotalSeconds)
                        Catch ex As Exception When TypeOf ex IsNot OperationCanceledException
                            _logger.LogError(ex, "Falló la consulta de {Source}", AreaCatalog.SourceName(source))
                            MarkFailed(source, ex.Message, False)
                            outcome.Failed += 1
                        End Try
                        progress?.Report(source)
                    Next
                Catch ex As Exception
                    failure = Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex)
                End Try
                Try
                    Await session.DisposeAsync().ConfigureAwait(False)
                Catch ex As Exception
                    _logger.LogDebug(ex, "No se pudo cerrar la conexión")
                End Try
                failure?.Throw()
                Return outcome
            Finally
                _busy.Release()
            End Try
        End Function

        ''' <summary>Replaces the data of every area of <paramref name="source"/> (only after JDE answered).</summary>
        Private Sub Store(source As ProductionSource, rows As IReadOnlyList(Of DailyProduction))
            SyncLock _gate
                For Each code In AreaCatalog.CodesOf(source)
                    _rows(code) = rows.Where(Function(r) String.Equals(r.Area, code, StringComparison.OrdinalIgnoreCase)).
                                       OrderBy(Function(r) r.Date).ToList()
                Next
                MarkSucceeded(source)
            End SyncLock
        End Sub

        ''' <summary>Replaces the float (only after JDE answered; an empty answer means the float is empty).</summary>
        Private Sub StoreFloat(items As IReadOnlyList(Of FloatItem))
            SyncLock _gate
                _float = items.Where(Function(i) i IsNot Nothing).ToList()
                MarkSucceeded(ProductionSource.Float)
            End SyncLock
        End Sub

        ''' <summary>Call inside the lock.</summary>
        Private Sub MarkSucceeded(source As ProductionSource)
            Dim status = _status(source)
            status.LastSuccess = _clock.Now
            status.LastError = String.Empty
            status.LastErrorAt = Nothing
            status.NeedsPassword = False
        End Sub

        Private Sub MarkFailed(source As ProductionSource, message As String, needsPassword As Boolean)
            SyncLock _gate
                Dim status = _status(source)
                status.LastError = If(String.IsNullOrWhiteSpace(message), "Error desconocido", message.Trim())
                status.LastErrorAt = _clock.Now
                status.NeedsPassword = needsPassword
            End SyncLock
        End Sub

    End Class

End Namespace
