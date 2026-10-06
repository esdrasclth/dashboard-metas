Imports CommunityToolkit.Mvvm.ComponentModel
Imports CommunityToolkit.Mvvm.Input
Imports DashboardMetas.App.Services
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Formatting
Imports Microsoft.Extensions.Logging

Namespace ViewModels

    ''' <summary>One announcement ready to show (texts already in the screen language).</summary>
    Public NotInheritable Class AnnouncementItem
        Public Property Id As String = String.Empty
        Public Property Title As String = String.Empty
        Public Property Message As String = String.Empty
        Public Property Severity As AnnouncementSeverity
        ''' <summary>"ANUNCIO", "AVISO" or "URGENTE".</summary>
        Public Property Caption As String = String.Empty
        ''' <summary>"Visible hasta las 18:00".</summary>
        Public Property UntilText As String = String.Empty
        Public Property CanDismiss As Boolean
        Public Property DismissText As String = String.Empty
        Public Property IsPreview As Boolean

        ''' <summary>Same key = same content on screen (no flicker when nothing changed).</summary>
        Friend ReadOnly Property Key As String
            Get
                Return String.Join(ChrW(31), Id, Title, Message, Severity.ToString(), Caption, UntilText, CanDismiss.ToString(), DismissText)
            End Get
        End Property
    End Class

    ''' <summary>
    ''' The announcements on screen: «modal» ones as a card over the dashboard (one at a time, with pages) and
    ''' «banner» ones as a strip under the title. On a TV nobody closes them: they rotate by themselves and go away
    ''' at their end time. It is part of the window (not a Windows dialog), so automatic updates never block.
    ''' </summary>
    Public NotInheritable Class AnnouncementsViewModel
        Inherits ObservableObject

        Private Const ModalPageSeconds As Integer = 15
        Private Const BannerPageSeconds As Integer = 10

        Private ReadOnly _service As AnnouncementService
        Private ReadOnly _clock As IClock
        Private ReadOnly _logger As ILogger
        Private ReadOnly _previews As New List(Of Announcement)()
        Private ReadOnly _announced As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Private _modals As IReadOnlyList(Of AnnouncementItem) = Array.Empty(Of AnnouncementItem)()
        Private _banners As IReadOnlyList(Of AnnouncementItem) = Array.Empty(Of AnnouncementItem)()
        Private _modalIndex As Integer
        Private _bannerIndex As Integer
        Private _modalShownAt As Date
        Private _bannerShownAt As Date
        Private _texts As Texts = Texts.Spanish

        Public Sub New(service As AnnouncementService, clock As IClock, logger As ILogger(Of AnnouncementsViewModel))
            _service = service
            _clock = clock
            _logger = logger
            DismissModalCommand = New RelayCommand(AddressOf DismissModal)
            NextModalCommand = New RelayCommand(Sub() MoveModal(+1))
            PreviousModalCommand = New RelayCommand(Sub() MoveModal(-1))
            DismissBannerCommand = New RelayCommand(AddressOf DismissBanner)
            NextBannerCommand = New RelayCommand(Sub() MoveBanner(+1))
        End Sub

        Public ReadOnly Property DismissModalCommand As IRelayCommand
        Public ReadOnly Property NextModalCommand As IRelayCommand
        Public ReadOnly Property PreviousModalCommand As IRelayCommand
        Public ReadOnly Property DismissBannerCommand As IRelayCommand
        Public ReadOnly Property NextBannerCommand As IRelayCommand

#Region "Bindable properties"

        Private _modal As AnnouncementItem
        Public Property Modal As AnnouncementItem
            Get
                Return _modal
            End Get
            Private Set(value As AnnouncementItem)
                If SetProperty(_modal, value) Then OnPropertyChanged(NameOf(HasModal))
            End Set
        End Property

        Public ReadOnly Property HasModal As Boolean
            Get
                Return _modal IsNot Nothing
            End Get
        End Property

        Private _modalPage As String = String.Empty
        ''' <summary>"1 de 3" (empty with a single one).</summary>
        Public Property ModalPage As String
            Get
                Return _modalPage
            End Get
            Private Set(value As String)
                SetProperty(_modalPage, value)
            End Set
        End Property

        Private _hasManyModals As Boolean
        Public Property HasManyModals As Boolean
            Get
                Return _hasManyModals
            End Get
            Private Set(value As Boolean)
                SetProperty(_hasManyModals, value)
            End Set
        End Property

        Private _banner As AnnouncementItem
        Public Property Banner As AnnouncementItem
            Get
                Return _banner
            End Get
            Private Set(value As AnnouncementItem)
                If SetProperty(_banner, value) Then OnPropertyChanged(NameOf(HasBanner))
            End Set
        End Property

        Public ReadOnly Property HasBanner As Boolean
            Get
                Return _banner IsNot Nothing
            End Get
        End Property

        Private _bannerPage As String = String.Empty
        Public Property BannerPage As String
            Get
                Return _bannerPage
            End Get
            Private Set(value As String)
                SetProperty(_bannerPage, value)
            End Set
        End Property

        Private _hasManyBanners As Boolean
        Public Property HasManyBanners As Boolean
            Get
                Return _hasManyBanners
            End Get
            Private Set(value As Boolean)
                SetProperty(_hasManyBanners, value)
            End Set
        End Property

        Private _previousText As String = "‹"
        Public Property PreviousText As String
            Get
                Return _previousText
            End Get
            Private Set(value As String)
                SetProperty(_previousText, value)
            End Set
        End Property

        Private _nextText As String = "›"
        Public Property NextText As String
            Get
                Return _nextText
            End Get
            Private Set(value As String)
                SetProperty(_nextText, value)
            End Set
        End Property

        Private _closeTip As String = String.Empty
        Public Property CloseTip As String
            Get
                Return _closeTip
            End Get
            Private Set(value As String)
                SetProperty(_closeTip, value)
            End Set
        End Property

#End Region

        ''' <summary>Recomputes what is on screen (every second from the main clock and when the file changes).</summary>
        Public Sub Refresh(texts As Texts)
            _texts = texts
            Dim now = _clock.Now
            Dim active As IReadOnlyList(Of Announcement)
            Try
                active = _service.Active()
            Catch ex As Exception
                _logger.LogError(ex, "No se pudieron calcular los anuncios activos")
                active = Array.Empty(Of Announcement)()
            End Try

            PlaySoundForNew(active)
            Dim modals = _previews.Concat(active.Where(Function(a) a.Display = AnnouncementDisplay.Modal)).
                Select(Function(a) ToItem(a, _previews.Contains(a))).ToList()
            Dim banners = active.Where(Function(a) a.Display = AnnouncementDisplay.Banner).Select(Function(a) ToItem(a, False)).ToList()

            _modalIndex = KeepIndex(_modals, modals, _modalIndex)
            _bannerIndex = KeepIndex(_banners, banners, _bannerIndex)
            _modals = modals
            _banners = banners

            ' On a TV nobody turns the pages: they turn by themselves
            If modals.Count > 1 AndAlso (now - _modalShownAt).TotalSeconds >= ModalPageSeconds Then
                _modalIndex = (_modalIndex + 1) Mod modals.Count
                _modalShownAt = now
            End If
            If banners.Count > 1 AndAlso (now - _bannerShownAt).TotalSeconds >= BannerPageSeconds Then
                _bannerIndex = (_bannerIndex + 1) Mod banners.Count
                _bannerShownAt = now
            End If
            Publish()
        End Sub

        ''' <summary>Shows a sample right away (settings › Anuncios › Vista previa); closing it saves nothing.</summary>
        Public Sub ShowPreview(announcement As Announcement)
            If announcement Is Nothing Then Return
            _previews.RemoveAll(Function(p) p.Id = announcement.Id)
            _previews.Insert(0, announcement)
            _modalIndex = 0
            _modalShownAt = _clock.Now
            Refresh(_texts)
        End Sub

        ''' <summary>Keys while a card is open: Enter/Esc close it, ← → turn the pages. True = handled.</summary>
        Public Function HandleKey(key As Key) As Boolean
            If Not HasModal Then Return False
            Select Case key
                Case Key.Enter, Key.Escape
                    If Modal.CanDismiss Then DismissModal()
                    Return True
                Case Key.Left
                    MoveModal(-1)
                    Return True
                Case Key.Right
                    MoveModal(+1)
                    Return True
            End Select
            Return False
        End Function

        Private Sub DismissModal()
            Dim item = Modal
            If item Is Nothing OrElse Not item.CanDismiss Then Return
            If item.IsPreview Then
                _previews.RemoveAll(Function(p) p.Id = item.Id)
            Else
                _service.Dismiss(item.Id)
            End If
            _modalShownAt = _clock.Now
            Refresh(_texts)
        End Sub

        Private Sub DismissBanner()
            Dim item = Banner
            If item Is Nothing OrElse Not item.CanDismiss Then Return
            _service.Dismiss(item.Id)
            Refresh(_texts)
        End Sub

        Private Sub MoveModal(direction As Integer)
            If _modals.Count < 2 Then Return
            _modalIndex = ((_modalIndex + direction) Mod _modals.Count + _modals.Count) Mod _modals.Count
            _modalShownAt = _clock.Now
            Publish()
        End Sub

        Private Sub MoveBanner(direction As Integer)
            If _banners.Count < 2 Then Return
            _bannerIndex = ((_bannerIndex + direction) Mod _banners.Count + _banners.Count) Mod _banners.Count
            _bannerShownAt = _clock.Now
            Publish()
        End Sub

        Private Sub Publish()
            Dim t = _texts
            Modal = Same(Modal, If(_modals.Count = 0, Nothing, _modals(_modalIndex)))
            Banner = Same(Banner, If(_banners.Count = 0, Nothing, _banners(_bannerIndex)))
            ' What is actually on screen counts as «visto» in the panel (previews do not)
            Dim shown = {Modal, Banner}.Where(Function(i) i IsNot Nothing AndAlso Not i.IsPreview).Select(Function(i) i.Id).ToList()
            If shown.Count > 0 Then
                Try
                    _service.MarkSeen(shown)
                Catch ex As Exception
                    _logger.LogDebug(ex, "No se pudo registrar el anuncio como visto")
                End Try
            End If
            HasManyModals = _modals.Count > 1
            HasManyBanners = _banners.Count > 1
            ModalPage = If(_modals.Count > 1, $"{_modalIndex + 1}{t.L(" de ", " of ")}{_modals.Count}", String.Empty)
            BannerPage = If(_banners.Count > 1, $"{_bannerIndex + 1}/{_banners.Count}", String.Empty)
            CloseTip = t.L("Cerrar este aviso en esta PC", "Close this notice on this PC")
        End Sub

        ''' <summary>Keeps the instance when the content did not change (no animation restart, no flicker).</summary>
        Private Shared Function Same(current As AnnouncementItem, candidate As AnnouncementItem) As AnnouncementItem
            If current IsNot Nothing AndAlso candidate IsNot Nothing AndAlso current.Key = candidate.Key Then Return current
            Return candidate
        End Function

        ''' <summary>Stays on the same announcement when others come or go.</summary>
        Private Shared Function KeepIndex(before As IReadOnlyList(Of AnnouncementItem), after As IReadOnlyList(Of AnnouncementItem), index As Integer) As Integer
            If after.Count = 0 Then Return 0
            If index >= 0 AndAlso index < before.Count Then
                Dim id = before(index).Id
                Dim found = after.ToList().FindIndex(Function(i) i.Id = id)
                If found >= 0 Then Return found
            End If
            Return Math.Min(Math.Max(0, index), after.Count - 1)
        End Function

        Private Function ToItem(a As Announcement, isPreview As Boolean) As AnnouncementItem
            Dim t = _texts
            Return New AnnouncementItem With {
                .Id = a.Id,
                .Title = a.DisplayTitle(t.English),
                .Message = a.DisplayMessage(t.English),
                .Severity = a.Severity,
                .Caption = If(isPreview, t.L("VISTA PREVIA", "PREVIEW"), CaptionOf(a.Severity, t)),
                .UntilText = UntilText(a.EndsAt, t),
                .CanDismiss = a.Dismissible OrElse isPreview,
                .DismissText = t.L("Entendido", "Got it"),
                .IsPreview = isPreview}
        End Function

        Private Shared Function CaptionOf(severity As AnnouncementSeverity, t As Texts) As String
            Select Case severity
                Case AnnouncementSeverity.Critical : Return t.L("URGENTE", "URGENT")
                Case AnnouncementSeverity.Warning : Return t.L("AVISO", "NOTICE")
                Case Else : Return t.L("ANUNCIO", "ANNOUNCEMENT")
            End Select
        End Function

        ''' <summary>"Visible hasta las 18:00", "… hasta el vie 10/10 18:00".</summary>
        Private Function UntilText(endsAt As DateTimeOffset?, t As Texts) As String
            If Not endsAt.HasValue Then Return String.Empty
            Dim local = endsAt.Value.LocalDateTime
            Dim time = local.ToString("HH:mm", Globalization.CultureInfo.InvariantCulture)
            If local.Date = _clock.Now.Date Then Return t.L("Visible hasta las ", "Showing until ") & time
            Return t.L("Visible hasta el ", "Showing until ") & t.DayShort(local).ToLowerInvariant() & " " & t.DayMonth(local) & " " & time
        End Function

        ''' <summary>Windows sound the first time an announcement with Sound appears in this session.</summary>
        Private Sub PlaySoundForNew(active As IReadOnlyList(Of Announcement))
            Dim fresh = active.Where(Function(a) _announced.Add(a.Id)).ToList()
            Dim loud = fresh.Where(Function(a) a.Sound).OrderByDescending(Function(a) a.Severity).FirstOrDefault()
            If loud Is Nothing Then Return
            Try
                If loud.Severity = AnnouncementSeverity.Info Then
                    Global.System.Media.SystemSounds.Asterisk.Play()
                Else
                    Global.System.Media.SystemSounds.Exclamation.Play()
                End If
            Catch ex As Exception
                _logger.LogDebug(ex, "No se pudo reproducir el sonido del anuncio")
            End Try
        End Sub

    End Class

End Namespace
