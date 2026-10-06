Imports System.Security.Cryptography

Namespace Remote

    ''' <summary>
    ''' What a screen reports to the panel (…/api/heartbeat), as camelCase JSON. Only the state of the screen: no JDE
    ''' figures, no passwords. The panel keeps the latest report of each screen and a history of changes.
    ''' Same shape as web/anuncios/lib/devices.ts (DeviceReport).
    ''' </summary>
    Public NotInheritable Class ScreenReport
        ''' <summary>Fingerprint of this PC's key (see <see cref="Announcements.AnnouncementSigning.KeyIdOf"/>).</summary>
        Public Property DeviceId As String = String.Empty
        ''' <summary>SubjectPublicKeyInfo (base64) of this PC's key; the panel checks the signature with it.</summary>
        Public Property PublicKey As String = String.Empty
        Public Property SentAt As DateTimeOffset
        Public Property StartedAt As DateTimeOffset
        Public Property Machine As String = String.Empty
        Public Property Branch As String = String.Empty
        Public Property AppVersion As String = String.Empty
        Public Property Os As String = String.Empty
        ''' <summary>"real" or "demo".</summary>
        Public Property Mode As String = "real"
        Public Property Screen As New ScreenView()
        Public Property Jde As New JdeState()
        Public Property Announcements As New AnnouncementsState()
        Public Property Update As New UpdateState()
        ''' <summary>«Metas y turnos» from the panel on this screen.</summary>
        Public Property Config As New RemoteConfigState()
        ''' <summary>Goals and shifts this screen uses now (the panel starts its editor from them; never production figures).</summary>
        Public Property Areas As List(Of AreaSummary) = New List(Of AreaSummary)()
        ''' <summary>The last commands from the panel and what happened, newest first.</summary>
        Public Property Commands As List(Of CommandResult) = New List(Of CommandResult)()
    End Class

    Public NotInheritable Class RemoteConfigState
        ''' <summary>0 = none received.</summary>
        Public Property Revision As Long
        Public Property AppliedAt As DateTimeOffset?
        ''' <summary>Area codes the panel manages here.</summary>
        Public Property Managed As List(Of String) = New List(Of String)()
        Public Property [Error] As String
    End Class

    Public NotInheritable Class AreaSummary
        Public Property Code As String = String.Empty
        Public Property Name As String = String.Empty
        Public Property Active As Boolean
        Public Property DailyGoal As Decimal
        Public Property MonthlyGoal As Decimal
        Public Property ShiftStart As String = String.Empty
        Public Property ShiftEnd As String = String.Empty
        Public Property BreakStart As String = String.Empty
        Public Property BreakEnd As String = String.Empty
    End Class

    ''' <summary>Automatic update on this screen, for the panel ("Versiones").</summary>
    Public NotInheritable Class UpdateState
        Public Property Enabled As Boolean
        ''' <summary>"al-dia", "no-aplica", "descargando", "lista", "instalando", "error", "revertida".</summary>
        Public Property State As String = "al-dia"
        ''' <summary>The version it refers to (the one being downloaded or installed).</summary>
        Public Property Version As String = String.Empty
        Public Property Detail As String
        Public Property At As DateTimeOffset?
    End Class

    Public NotInheritable Class ScreenView
        ''' <summary>"area", "general" or "float".</summary>
        Public Property View As String = "area"
        Public Property Area As String = String.Empty
        Public Property AreaName As String = String.Empty
        Public Property FullScreen As Boolean
        Public Property Language As String = "ES"
        Public Property AutoRotate As Boolean
        Public Property Resolution As String = String.Empty
    End Class

    Public NotInheritable Class JdeState
        Public Property Sources As List(Of JdeSourceState) = New List(Of JdeSourceState)()
        Public Property NeedsPassword As Boolean
    End Class

    Public NotInheritable Class JdeSourceState
        Public Property Name As String = String.Empty
        Public Property LastSuccess As DateTimeOffset?
        Public Property [Error] As String
        Public Property ErrorAt As DateTimeOffset?
    End Class

    Public NotInheritable Class AnnouncementsState
        Public Property Enabled As Boolean
        Public Property FeedUrl As String = String.Empty
        Public Property Version As Long
        Public Property LastCheck As DateTimeOffset?
        Public Property LastError As String
        ''' <summary>Ids on screen right now (cards and strips).</summary>
        Public Property Active As List(Of String) = New List(Of String)()
        ''' <summary>Id → when it was first shown on this screen.</summary>
        Public Property Seen As Dictionary(Of String, DateTimeOffset) = New Dictionary(Of String, DateTimeOffset)()
        ''' <summary>Id → when someone closed it here with «Entendido».</summary>
        Public Property Dismissed As Dictionary(Of String, DateTimeOffset) = New Dictionary(Of String, DateTimeOffset)()
    End Class

    ''' <summary>Signs report bodies with this PC's key (ECDSA P-256 / SHA-256, IEEE P1363 — what the panel verifies).</summary>
    Public NotInheritable Class ReportSigning

        Private Sub New()
        End Sub

        Public Shared Function Sign(body As Byte(), key As ECDsa) As String
            Return Convert.ToBase64String(key.SignData(body, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        End Function

    End Class

End Namespace
