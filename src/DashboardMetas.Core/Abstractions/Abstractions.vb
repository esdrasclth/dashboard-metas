Imports System.Threading
Imports DashboardMetas.Core.Models

Namespace Abstractions

    ''' <summary>Source of the daily numbers. The ODBC (JDE) and the demo repositories implement it.</summary>
    Public Interface IProductionRepository

        ''' <summary>True for the demo repository (shown in the UI).</summary>
        ReadOnly Property IsDemo As Boolean

        ''' <summary>Text shown to the user describing where the data comes from (DSN or demo).</summary>
        ReadOnly Property SourceDescription As String

        ''' <summary>Opens one connection used for the three queries of a refresh.</summary>
        ''' <param name="password">Nothing = let the driver sign on by itself.</param>
        ''' <exception cref="JdeConnectionException">When the connection fails.</exception>
        Function OpenSessionAsync(user As String, password As String, cancellationToken As CancellationToken) As Task(Of IProductionSession)

    End Interface

    Public Interface IProductionSession
        Inherits IAsyncDisposable

        ''' <summary>Driver name and version, for logs and diagnostics.</summary>
        ReadOnly Property DriverInfo As String

        ''' <summary>One row per area and day from <paramref name="fromDate"/> (inclusive) to today.</summary>
        Function GetDailyAsync(source As ProductionSource, fromDate As Date, cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of DailyProduction))

        ''' <summary>
        ''' The custom float right now: one row per status and base style of the open FIN orders.
        ''' <paramref name="classifyFrom"/> = first day of F58C3120 used to find each style's product line.
        ''' </summary>
        Function GetFloatAsync(classifyFrom As Date, cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of FloatItem))

    End Interface

    ''' <summary>Answer of the announcement source: the file, or "unchanged since last time" (HTTP 304).</summary>
    Public NotInheritable Class AnnouncementFetch
        Public Shared ReadOnly Property Unchanged As New AnnouncementFetch With {.NotModified = True}
        Public Property NotModified As Boolean
        Public Property Content As String = String.Empty
        ''' <summary>Where it came from, for logs and diagnostics.</summary>
        Public Property Source As String = String.Empty
    End Class

    ''' <summary>Where the signed control file of the announcements comes from (HTTPS, a file, or the demo).</summary>
    Public Interface IAnnouncementSource
        ''' <summary>Throws when the file cannot be read (no network, 404, too big…); the message is in Spanish.</summary>
        Function FetchAsync(cancellationToken As CancellationToken) As Task(Of AnnouncementFetch)
        ''' <summary>Public keys this source's files may be signed with (production keys, or the demo key).</summary>
        Function TrustedKeys() As IReadOnlyDictionary(Of String, Byte())
        ''' <summary>Text for diagnostics ("https://…", "demo"…); empty = not configured.</summary>
        ReadOnly Property Description As String
        ReadOnly Property IsConfigured As Boolean
    End Interface

    ''' <summary>Saved JDE password (encrypted with DPAPI in the app).</summary>
    Public Interface ICredentialStore
        Function TryLoad() As String
        Sub Save(password As String)
        Sub Delete()
        ReadOnly Property HasSaved As Boolean
    End Interface

    ''' <summary>Asks the user for the JDE password. Returns Nothing when the user cancels.</summary>
    Public Interface IPasswordPrompt
        Function PromptAsync(user As String, message As String, isRetry As Boolean, cancellationToken As CancellationToken) As Task(Of PasswordPromptResult)
    End Interface

    Public NotInheritable Class PasswordPromptResult

        Public Sub New(password As String, remember As Boolean)
            Me.Password = password
            Me.Remember = remember
        End Sub

        Public ReadOnly Property Password As String
        Public ReadOnly Property Remember As Boolean

    End Class

    ''' <summary>Shift hours of each area (the demo uses it to make today's value grow during the right hours).</summary>
    Public Interface IAreaShiftProvider
        Function ShiftOf(areaCode As String) As Dashboard.ShiftSchedule
    End Interface

    ''' <summary>Current time, replaceable in tests.</summary>
    Public Interface IClock
        ReadOnly Property Now As Date
    End Interface

    Public NotInheritable Class SystemClock
        Implements IClock
        Public ReadOnly Property Now As Date Implements IClock.Now
            Get
                Return Date.Now
            End Get
        End Property
    End Class

    Public Enum JdeConnectionErrorKind
        Other
        ''' <summary>CWBSY0002: wrong or expired password.</summary>
        InvalidPassword
        ''' <summary>IM002: DSN not found (usually 32/64-bit mismatch).</summary>
        DataSourceNotFound
        ''' <summary>IM003 / driver could not be loaded.</summary>
        DriverNotInstalled
        ''' <summary>Network / host not reachable.</summary>
        HostUnreachable
        ''' <summary>CWBSY0003: password expired, it must be changed on the AS400.</summary>
        PasswordExpired
        ''' <summary>There is no saved password and the app could not ask for it (automatic refresh) or the user cancelled.</summary>
        PasswordNotProvided
    End Enum

    ''' <summary>A connection failure translated into a clear, actionable message in Spanish.</summary>
    Public Class JdeConnectionException
        Inherits Exception

        Public Sub New(kind As JdeConnectionErrorKind, message As String, Optional driverMessage As String = Nothing, Optional inner As Exception = Nothing)
            MyBase.New(message, inner)
            Me.Kind = kind
            Me.DriverMessage = If(driverMessage, String.Empty)
        End Sub

        Public ReadOnly Property Kind As JdeConnectionErrorKind

        ''' <summary>Original text from the driver (never contains the password).</summary>
        Public ReadOnly Property DriverMessage As String

        ''' <summary>True when the user has to type the password (the "Conectar" button is shown).</summary>
        Public ReadOnly Property NeedsPassword As Boolean
            Get
                Return Kind = JdeConnectionErrorKind.PasswordNotProvided OrElse Kind = JdeConnectionErrorKind.InvalidPassword
            End Get
        End Property

    End Class

End Namespace
