Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports Microsoft.Extensions.Logging

Namespace Announcements

    ''' <summary>What the app remembers between runs (per mode: real and demo are separate).</summary>
    Public NotInheritable Class AnnouncementState
        ''' <summary>Highest version accepted: an older file is never accepted again (anti-rollback).</summary>
        Public Property LastVersion As Long
        ''' <summary>The last valid signed file, verified again on load (so editing it on disk does nothing).</summary>
        Public Property SignedFile As String = String.Empty
        ''' <summary>Announcement id → when it was dismissed on this PC.</summary>
        Public Property Dismissed As Dictionary(Of String, DateTimeOffset) = New Dictionary(Of String, DateTimeOffset)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>Announcement id → when it was first shown on this screen (reported to the panel as «visto»).</summary>
        Public Property Seen As Dictionary(Of String, DateTimeOffset) = New Dictionary(Of String, DateTimeOffset)(StringComparer.OrdinalIgnoreCase)
        Public Property LastCheck As DateTimeOffset?
        Public Property LastSuccess As DateTimeOffset?
        Public Property LastError As String = String.Empty
    End Class

    Public Interface IAnnouncementStateStore
        ''' <summary>Nothing when there is nothing saved (or it cannot be read).</summary>
        Function Load() As AnnouncementState
        Sub Save(state As AnnouncementState)
    End Interface

    ''' <summary>Status for the settings screen and diagnostics.</summary>
    Public NotInheritable Class AnnouncementStatus
        Public Property IsConfigured As Boolean
        Public Property Source As String = String.Empty
        Public Property LastCheck As DateTimeOffset?
        Public Property LastSuccess As DateTimeOffset?
        Public Property LastError As String = String.Empty
        Public Property Version As Long
        Public Property IssuedAt As DateTimeOffset?
        Public Property KeyId As String = String.Empty
        ''' <summary>Announcements in the file (any date or target).</summary>
        Public Property InFile As Integer
        Public Property Dismissed As Integer
        Public Property Warnings As IReadOnlyList(Of String) = Array.Empty(Of String)()
    End Class

    ''' <summary>
    ''' Keeps the announcements of one source: downloads, verifies (signature and anti-rollback), keeps the
    ''' last valid file on disk so they show without network after a restart, and remembers what was dismissed
    ''' on this PC. A failed check never removes what is on screen. Thread-safe; UI-agnostic.
    ''' </summary>
    Public NotInheritable Class AnnouncementCenter

        Private ReadOnly _source As IAnnouncementSource
        Private ReadOnly _store As IAnnouncementStateStore
        Private ReadOnly _logger As ILogger
        Private ReadOnly _gate As New Object()
        Private ReadOnly _checking As New SemaphoreSlim(1, 1)
        Private _state As New AnnouncementState()
        Private _feed As AnnouncementFeed
        Private _keyId As String = String.Empty
        Private _warnings As IReadOnlyList(Of String) = Array.Empty(Of String)()

        Public Sub New(source As IAnnouncementSource, store As IAnnouncementStateStore, logger As ILogger)
            _source = source
            _store = store
            _logger = If(logger, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
        End Sub

        ''' <summary>The file, the dismissed list or the status changed (raised on the calling thread).</summary>
        Public Event Changed As EventHandler

        ''' <summary>A new control file arrived (raised before checking its announcements, on a background thread).</summary>
        Public Event FileReceived As EventHandler(Of String)

        ''' <summary>False for a control file that only carries commands (no announcements were ever published).</summary>
        Private Shared Function CarriesAnnouncements(content As String) As Boolean
            Try
                Using doc = Text.Json.JsonDocument.Parse(content)
                    Dim ignored As Text.Json.JsonElement
                    Return doc.RootElement.ValueKind <> Text.Json.JsonValueKind.Object OrElse
                           doc.RootElement.TryGetProperty("payload", ignored) OrElse doc.RootElement.TryGetProperty("format", ignored)
                End Using
            Catch ex As Text.Json.JsonException
                Return True ' let the normal check report it
            End Try
        End Function

        ''' <summary>Restores the last valid file and the dismissed list saved on this PC.</summary>
        Public Sub Load()
            Dim saved As AnnouncementState = Nothing
            Try
                saved = _store.Load()
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudo leer el estado de los anuncios")
            End Try
            SyncLock _gate
                _state = If(saved, New AnnouncementState())
                If _state.Dismissed Is Nothing Then _state.Dismissed = New Dictionary(Of String, DateTimeOffset)(StringComparer.OrdinalIgnoreCase)
                _state.Dismissed = New Dictionary(Of String, DateTimeOffset)(_state.Dismissed, StringComparer.OrdinalIgnoreCase)
                _state.Seen = New Dictionary(Of String, DateTimeOffset)(If(_state.Seen, New Dictionary(Of String, DateTimeOffset)()), StringComparer.OrdinalIgnoreCase)
                _feed = Nothing
                If Not String.IsNullOrEmpty(_state.SignedFile) Then
                    Dim check = AnnouncementSigning.Verify(_state.SignedFile, _source.TrustedKeys())
                    If check.IsValid Then
                        _feed = check.Feed
                        _keyId = check.KeyId
                        _warnings = check.Warnings
                    Else
                        _logger.LogWarning("Se descarta el archivo de anuncios guardado: {Error}", check.Error)
                        _state.SignedFile = String.Empty
                    End If
                End If
            End SyncLock
            RaiseEvent Changed(Me, EventArgs.Empty)
        End Sub

        ''' <summary>
        ''' Asks the source once. Returns True when the announcements changed. Never throws (except for
        ''' cancellation): errors are kept in the status and the last valid announcements stay.
        ''' </summary>
        Public Async Function CheckAsync(now As DateTimeOffset, cancellationToken As CancellationToken) As Task(Of Boolean)
            If Not _source.IsConfigured Then Return False
            If Not Await _checking.WaitAsync(0, cancellationToken).ConfigureAwait(False) Then Return False
            Try
                Dim fetch As AnnouncementFetch
                Try
                    fetch = Await _source.FetchAsync(cancellationToken).ConfigureAwait(False)
                Catch ex As Exception When TypeOf ex IsNot OperationCanceledException OrElse Not cancellationToken.IsCancellationRequested
                    Fail(now, ex.Message)
                    Return False
                End Try

                If fetch.NotModified Then
                    Succeed(now, Nothing, Nothing)
                    Return False
                End If
                ' The same file carries the panel's signed commands (checked by whoever handles them)
                RaiseEvent FileReceived(Me, fetch.Content)
                If Not CarriesAnnouncements(fetch.Content) Then
                    ' Only commands so far: nothing published yet is not an error
                    Succeed(now, Nothing, Nothing)
                    Return False
                End If

                Dim minimum As Long
                Dim currentFile As String
                SyncLock _gate
                    minimum = _state.LastVersion
                    currentFile = _state.SignedFile
                End SyncLock
                If String.Equals(fetch.Content, currentFile, StringComparison.Ordinal) Then
                    Succeed(now, Nothing, Nothing)
                    Return False
                End If

                Dim check = AnnouncementSigning.Verify(fetch.Content, _source.TrustedKeys(), minimum)
                If Not check.IsValid Then
                    Fail(now, check.Error)
                    Return False
                End If
                For Each warning In check.Warnings
                    _logger.LogWarning("Anuncio descartado: {Warning}", warning)
                Next
                _logger.LogInformation("Anuncios: versión {Version} con {Count} anuncio(s) de {Source}", check.Feed.Version, check.Feed.Announcements.Count, fetch.Source)
                Succeed(now, check, fetch.Content)
                Return True
            Finally
                _checking.Release()
            End Try
        End Function

        ''' <summary>The announcements for this PC right now, most urgent first.</summary>
        Public Function Active(now As DateTimeOffset, audience As AnnouncementAudience) As IReadOnlyList(Of Announcement)
            SyncLock _gate
                Return AnnouncementSelector.Active(_feed, now, audience, _state.Dismissed.Keys.ToList())
            End SyncLock
        End Function

        ''' <summary>Closed on this PC: it will not show again (unless "show dismissed again").</summary>
        Public Sub Dismiss(id As String, now As DateTimeOffset)
            If String.IsNullOrWhiteSpace(id) Then Return
            SyncLock _gate
                _state.Dismissed(id) = now
            End SyncLock
            Persist()
            RaiseEvent Changed(Me, EventArgs.Empty)
        End Sub

        ''' <summary>
        ''' Records that these announcements are on screen now (first time only). Returns True when one is new, so the
        ''' panel can be told soon.
        ''' </summary>
        Public Function MarkSeen(ids As IEnumerable(Of String), now As DateTimeOffset) As Boolean
            Dim added = False
            SyncLock _gate
                For Each id In If(ids, Enumerable.Empty(Of String)())
                    If Not String.IsNullOrWhiteSpace(id) AndAlso Not _state.Seen.ContainsKey(id) Then
                        _state.Seen(id) = now
                        added = True
                    End If
                Next
            End SyncLock
            If added Then Persist()
            Return added
        End Function

        ''' <summary>Copies of «seen» and «dismissed» on this PC, for the report to the panel.</summary>
        Public Function Views() As (Seen As IReadOnlyDictionary(Of String, DateTimeOffset), Dismissed As IReadOnlyDictionary(Of String, DateTimeOffset))
            SyncLock _gate
                Return (New Dictionary(Of String, DateTimeOffset)(_state.Seen, StringComparer.OrdinalIgnoreCase),
                        New Dictionary(Of String, DateTimeOffset)(_state.Dismissed, StringComparer.OrdinalIgnoreCase))
            End SyncLock
        End Function

        ''' <summary>Forgets every dismissal on this PC (support: "I closed it by mistake").</summary>
        Public Sub RestoreDismissed()
            SyncLock _gate
                _state.Dismissed.Clear()
            End SyncLock
            Persist()
            RaiseEvent Changed(Me, EventArgs.Empty)
        End Sub

        Public Function Status() As AnnouncementStatus
            SyncLock _gate
                Return New AnnouncementStatus With {
                    .IsConfigured = _source.IsConfigured,
                    .Source = _source.Description,
                    .LastCheck = _state.LastCheck,
                    .LastSuccess = _state.LastSuccess,
                    .LastError = If(_state.LastError, String.Empty),
                    .Version = If(_feed Is Nothing, 0L, _feed.Version),
                    .IssuedAt = If(_feed Is Nothing, CType(Nothing, DateTimeOffset?), _feed.IssuedAt),
                    .KeyId = _keyId,
                    .InFile = If(_feed?.Announcements Is Nothing, 0, _feed.Announcements.Count),
                    .Dismissed = _state.Dismissed.Count,
                    .Warnings = _warnings}
            End SyncLock
        End Function

        Private Sub Succeed(now As DateTimeOffset, check As FeedVerification, content As String)
            SyncLock _gate
                _state.LastCheck = now
                _state.LastSuccess = now
                _state.LastError = String.Empty
                If check IsNot Nothing Then
                    _feed = check.Feed
                    _keyId = check.KeyId
                    _warnings = check.Warnings
                    _state.SignedFile = content
                    _state.LastVersion = Math.Max(_state.LastVersion, check.Feed.Version)
                    ' Dismissals of announcements no longer in the file are not needed any more
                    Dim ids As New HashSet(Of String)(check.Feed.Announcements.Select(Function(a) a.Id), StringComparer.OrdinalIgnoreCase)
                    For Each gone In _state.Dismissed.Keys.Where(Function(k) Not ids.Contains(k)).ToList()
                        _state.Dismissed.Remove(gone)
                    Next
                    For Each gone In _state.Seen.Keys.Where(Function(k) Not ids.Contains(k)).ToList()
                        _state.Seen.Remove(gone)
                    Next
                End If
            End SyncLock
            Persist()
            RaiseEvent Changed(Me, EventArgs.Empty)
        End Sub

        Private Sub Fail(now As DateTimeOffset, message As String)
            Dim text = If(String.IsNullOrWhiteSpace(message), "Error desconocido", message.Trim())
            Dim repeated As Boolean
            SyncLock _gate
                repeated = String.Equals(_state.LastError, text, StringComparison.Ordinal)
                _state.LastCheck = now
                _state.LastError = text
            End SyncLock
            ' Same error every minute: log it once, not 1,440 times a day
            If Not repeated Then _logger.LogWarning("Anuncios: {Error}", text)
            Persist()
            RaiseEvent Changed(Me, EventArgs.Empty)
        End Sub

        Private Sub Persist()
            Dim copy As AnnouncementState
            SyncLock _gate
                copy = New AnnouncementState With {
                    .LastVersion = _state.LastVersion, .SignedFile = _state.SignedFile, .LastCheck = _state.LastCheck,
                    .LastSuccess = _state.LastSuccess, .LastError = _state.LastError,
                    .Dismissed = New Dictionary(Of String, DateTimeOffset)(_state.Dismissed, StringComparer.OrdinalIgnoreCase),
                    .Seen = New Dictionary(Of String, DateTimeOffset)(_state.Seen, StringComparer.OrdinalIgnoreCase)}
            End SyncLock
            Try
                _store.Save(copy)
            Catch ex As Exception
                _logger.LogWarning(ex, "No se pudo guardar el estado de los anuncios")
            End Try
        End Sub

    End Class

End Namespace
