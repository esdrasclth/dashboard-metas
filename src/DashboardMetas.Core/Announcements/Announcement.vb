Imports System.Text.Json
Imports System.Text.Json.Serialization

Namespace Announcements

    ''' <summary>How urgent an announcement is (colour, icon and order on screen).</summary>
    <JsonConverter(GetType(JsonStringEnumConverter(Of AnnouncementSeverity)))>
    Public Enum AnnouncementSeverity
        Info = 0
        Warning = 1
        Critical = 2
    End Enum

    ''' <summary>How it is shown: a card over the whole screen, or a strip under the title.</summary>
    <JsonConverter(GetType(JsonStringEnumConverter(Of AnnouncementDisplay)))>
    Public Enum AnnouncementDisplay
        Modal = 0
        Banner = 1
    End Enum

    ''' <summary>
    ''' One announcement of the remote control file. It is shown from <see cref="StartsAt"/> until
    ''' <see cref="EndsAt"/> on the PCs it targets, until someone dismisses it on that PC.
    ''' </summary>
    Public NotInheritable Class Announcement
        ''' <summary>Unique and stable: a PC that dismissed it will not show it again.</summary>
        Public Property Id As String = String.Empty
        Public Property Title As String = String.Empty
        Public Property Message As String = String.Empty
        ''' <summary>Optional English text, used when the screen is in English.</summary>
        Public Property TitleEn As String
        Public Property MessageEn As String
        Public Property Severity As AnnouncementSeverity = AnnouncementSeverity.Info
        Public Property Display As AnnouncementDisplay = AnnouncementDisplay.Modal
        ''' <summary>Empty = from the moment it is published.</summary>
        Public Property StartsAt As DateTimeOffset?
        Public Property EndsAt As DateTimeOffset?
        ''' <summary>
        ''' Empty or "*" = every PC. Otherwise PC names ("PLANTA-TV01" or "equipo:PLANTA-TV01") and/or plants
        ''' ("planta:027").
        ''' </summary>
        Public Property Targets As List(Of String) = New List(Of String)()
        ''' <summary>False = it cannot be closed: it stays until <see cref="EndsAt"/> (e.g. maintenance).</summary>
        Public Property Dismissible As Boolean = True
        ''' <summary>Plays the Windows notification sound when it appears.</summary>
        Public Property Sound As Boolean

        Public Function DisplayTitle(english As Boolean) As String
            Return If(english AndAlso Not String.IsNullOrWhiteSpace(TitleEn), TitleEn, Title).Trim()
        End Function

        Public Function DisplayMessage(english As Boolean) As String
            Return If(english AndAlso Not String.IsNullOrWhiteSpace(MessageEn), MessageEn, Message).Trim()
        End Function
    End Class

    ''' <summary>What the publisher signs: every announcement in force, with an ever-growing version.</summary>
    Public NotInheritable Class AnnouncementFeed
        ''' <summary>
        ''' Grows with every publication (the signing tool uses the UTC time). The app never accepts a version
        ''' lower than one it already accepted, so an old signed file cannot bring back retired announcements.
        ''' </summary>
        Public Property Version As Long
        Public Property IssuedAt As DateTimeOffset
        Public Property Announcements As List(Of Announcement) = New List(Of Announcement)()
    End Class

    ''' <summary>
    ''' The file the app downloads: the feed JSON (base64, so the signed bytes are exactly the ones checked) and
    ''' its ECDSA P-256 / SHA-256 signature by the key <see cref="KeyId"/>.
    ''' </summary>
    Public NotInheritable Class SignedFeed
        Public Const CurrentFormat As String = "dashboardmetas-anuncios/1"

        Public Property Format As String = CurrentFormat
        Public Property KeyId As String = String.Empty
        Public Property Payload As String = String.Empty
        Public Property Signature As String = String.Empty
    End Class

    ''' <summary>JSON options of the announcement files (camelCase, readable, dates with offset).</summary>
    Public NotInheritable Class AnnouncementJson

        Private Sub New()
        End Sub

        Public Shared ReadOnly Options As New JsonSerializerOptions With {
            .PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            .PropertyNameCaseInsensitive = True,
            .WriteIndented = True,
            .DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            .ReadCommentHandling = JsonCommentHandling.Skip,
            .AllowTrailingCommas = True,
            .Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}

    End Class

End Namespace
