Imports System.Text.RegularExpressions
Imports DashboardMetas.Core.Dashboard

Namespace Configuration

    ''' <summary>
    ''' JDE connection and query settings ("Jde" section). The defaults here are placeholders: the real
    ''' values of the company go in appsettings.empresa.json next to the executable (not in source control).
    ''' </summary>
    Public NotInheritable Class JdeSettings

        Public Const SectionName As String = "Jde"
        Public Const BranchLength As Integer = 12

        Private Shared ReadOnly LibraryPattern As New Regex("^[A-Za-z0-9_#$@]{1,128}(\.[A-Za-z0-9_#$@]{1,128})?$", RegexOptions.CultureInvariant)
        Private Shared ReadOnly CodePattern As New Regex("^[A-Za-z0-9 ]{1,12}$", RegexOptions.CultureInvariant)

        Public Property Dsn As String = "JDE"
        Public Property User As String = "USUARIO"

        ''' <summary>
        ''' True = do not send a password: the IBM i Access driver signs on by itself (its cached sign-on,
        ''' like the Access version with SIGNON=1). False = the app asks for it and keeps it encrypted with DPAPI.
        ''' </summary>
        Public Property UseDriverSignOn As Boolean

        ''' <summary>Plant / branch (MCU) without padding, e.g. "001".</summary>
        Public Property Branch As String = "001"

        ''' <summary>"RDB.LIBRARY" with F31122, F4801 and F4211 (Y1 - deliveries to assembly).</summary>
        Public Property AssemblyLibrary As String = "MIRDB.MIBIBLIOTECA"

        ''' <summary>Library with F58C3120 (Station Activity: Y2, Y3, Y5, Y7).</summary>
        Public Property StationsLibrary As String = "MIBIBLIOTECA"

        ''' <summary>Library with F41D200 (price list).</summary>
        Public Property PricesLibrary As String = "MIBIBLIOTECA"

        ''' <summary>Library with DCTXF (dcLINK transactions, shipments).</summary>
        Public Property DcLinkLibrary As String = "MIBIBLIOTECA"

        ''' <summary>F41D200.PM@RTY used for the value of stations and shipments.</summary>
        Public Property PriceType As String = "W01"

        ''' <summary>F31122.WTOPSQ of the assembly delivery scan (510.00 is stored as 51000).</summary>
        Public Property AssemblyOperation As Integer = 51000

        ''' <summary>DCTXF.TXTXID of a confirmed shipment.</summary>
        Public Property ShipmentTransaction As String = "CS"

        ''' <summary>
        ''' F4801.WASRST of the custom float, separated by commas (same as avanceMeta.vbs). Read from the
        ''' stations library, where avanceMeta.vbs reads F4801 and F58C3120.
        ''' </summary>
        Public Property FloatStatuses As String = "Y1,Y2,Y3,Y5"

        Public Const MaxFloatStatuses As Integer = 12

        ''' <summary>The float statuses, trimmed, upper case and without repeats, in the configured order.</summary>
        Public Function FloatStatusList() As IReadOnlyList(Of String)
            Return If(FloatStatuses, String.Empty).Split({","c, ";"c}, StringSplitOptions.RemoveEmptyEntries Or StringSplitOptions.TrimEntries).
                Select(Function(s) s.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList()
        End Function

        ''' <summary>Divisor of the SRTL02/03/05/07 quantities (1 = as they come, like the Access version).</summary>
        Public Property StationsQuantityDivisor As Decimal = 1D

        ''' <summary>Divisor of the PM@RG$ price (1 = as it comes, like the Access version).</summary>
        Public Property PriceDivisor As Decimal = 1D

        Public Property CommandTimeoutSeconds As Integer = 300
        Public Property ConnectionTimeoutSeconds As Integer = 30

        ''' <summary>Branch padded on the left to 12 characters as JDE stores MCU ("001" → "         001").</summary>
        Public Shared Function PadBranch(value As String) As String
            Dim trimmed = If(value, String.Empty).Trim()
            Return If(trimmed.Length >= BranchLength, trimmed, trimmed.PadLeft(BranchLength))
        End Function

        Public Shared Function IsValidLibrary(value As String) As Boolean
            Return Not String.IsNullOrWhiteSpace(value) AndAlso LibraryPattern.IsMatch(value.Trim())
        End Function

        ''' <summary>Validation errors in Spanish (empty list = valid).</summary>
        Public Function Validate() As IReadOnlyList(Of String)
            Dim errors As New List(Of String)()
            If String.IsNullOrWhiteSpace(Dsn) Then errors.Add("El DSN no puede estar vacío.")
            If Dsn IsNot Nothing AndAlso (Dsn.Contains(";"c) OrElse Dsn.Contains("{"c) OrElse Dsn.Contains("}"c)) Then errors.Add("El DSN no puede contener ; { }.")
            If String.IsNullOrWhiteSpace(User) Then errors.Add("El usuario no puede estar vacío.")
            If User IsNot Nothing AndAlso (User.Contains(";"c) OrElse User.Contains("{"c) OrElse User.Contains("}"c)) Then errors.Add("El usuario no puede contener ; { }.")
            If String.IsNullOrWhiteSpace(Branch) OrElse Branch.Trim().Length > BranchLength Then errors.Add("La planta debe tener de 1 a 12 caracteres.")
            For Each pair In {("Ensamble (Y1)", AssemblyLibrary), ("Estaciones", StationsLibrary), ("Precios", PricesLibrary), ("dcLINK", DcLinkLibrary)}
                If Not IsValidLibrary(pair.Item2) Then errors.Add($"Biblioteca de {pair.Item1} no válida: solo letras, números, _ # $ @ y un punto.")
            Next
            If String.IsNullOrWhiteSpace(PriceType) OrElse Not CodePattern.IsMatch(PriceType) Then errors.Add("El tipo de precio solo puede tener letras y números.")
            If String.IsNullOrWhiteSpace(ShipmentTransaction) OrElse Not CodePattern.IsMatch(ShipmentTransaction) Then errors.Add("La transacción de embarque solo puede tener letras y números.")
            Dim statuses = FloatStatusList()
            If statuses.Count = 0 OrElse statuses.Count > MaxFloatStatuses OrElse statuses.Any(Function(s) s.Length > 2 OrElse Not CodePattern.IsMatch(s)) Then
                errors.Add($"Los estatus del Float deben ser de 1 a {MaxFloatStatuses} códigos de 1 o 2 letras o números, separados por comas (ej. Y1,Y2,Y3,Y5).")
            End If
            If StationsQuantityDivisor <= 0D OrElse PriceDivisor <= 0D Then errors.Add("Los divisores deben ser mayores que 0.")
            If CommandTimeoutSeconds < 1 Then errors.Add("El tiempo de espera de consulta debe ser mayor que 0.")
            Return errors
        End Function

    End Class

    ''' <summary>Company-wide behaviour of the dashboard ("Dashboard" section).</summary>
    Public NotInheritable Class DashboardSettings
        Public Const SectionName As String = "Dashboard"
        Public Const MinRefreshMinutes As Integer = 1
        Public Const MaxRefreshMinutes As Integer = 60

        ''' <summary>Minutes between automatic queries to JDE.</summary>
        Public Property RefreshMinutes As Integer = 5

        ''' <summary>True = Sundays are not shown in the chart.</summary>
        Public Property ExcludeSundays As Boolean = True

        ''' <summary>Initial daily goal of each area (then changed in "Configuración").</summary>
        Public Property DefaultDailyGoal As Decimal = 150000D

        ''' <summary>Starts in full screen (TV mode). F11 toggles it.</summary>
        Public Property StartFullScreen As Boolean = True

        ''' <summary>Default shift of every area "HH:mm" (each area can change it in "Configuración").</summary>
        Public Property ShiftStart As String = ShiftSchedule.DefaultStart
        Public Property ShiftEnd As String = ShiftSchedule.DefaultEnd
        ''' <summary>Default break; empty = none.</summary>
        Public Property BreakStart As String = String.Empty
        Public Property BreakEnd As String = String.Empty

        ''' <summary>Minutes the shift must run before projecting the end of the day (early projections jump too much).</summary>
        Public Property MinMinutesForProjection As Integer = 30

        Public Function DefaultShift() As ShiftSchedule
            Dim schedule As ShiftSchedule = Nothing
            Dim ignored As String = Nothing
            Return If(ShiftSchedule.TryCreate(ShiftStart, ShiftEnd, BreakStart, BreakEnd, schedule, ignored), schedule, ShiftSchedule.Default)
        End Function

        Public Function EffectiveRefreshMinutes() As Integer
            Return Math.Clamp(RefreshMinutes, MinRefreshMinutes, MaxRefreshMinutes)
        End Function
    End Class

    ''' <summary>Remote announcements ("Announcements" section).</summary>
    Public NotInheritable Class AnnouncementSettings
        Public Const SectionName As String = "Announcements"
        ''' <summary>The panel's control file. In code too, so a PC with an old appsettings.json still gets announcements.</summary>
        Public Const DefaultFeedUrl As String = "https://dashboard-metas-anuncios.vercel.app/control.json"
        Public Const MinPollSeconds As Integer = 30
        Public Const MaxPollSeconds As Integer = 3600

        Public Property Enabled As Boolean = True
        ''' <summary>
        ''' HTTPS address of the signed control file (e.g. https://…/control.json), or a file path / network share
        ''' (\\servidor\carpeta\control.json). Empty = announcements off.
        ''' </summary>
        Public Property FeedUrl As String = DefaultFeedUrl
        ''' <summary>
        ''' Seconds between checks of the control file. It also carries the panel's commands and the signals of new goals
        ''' or versions, so it sets how fast the screen reacts. An unchanged file costs the panel a 304.
        ''' </summary>
        Public Property PollSeconds As Integer = DefaultPollSeconds
        Public Const DefaultPollSeconds As Integer = 30
        ''' <summary>The default up to 1.2: settings saved by those versions carry it, and it means «the default».</summary>
        Private Const LegacyDefaultPollSeconds As Integer = 60

        Public Function EffectivePollSeconds() As Integer
            If PollSeconds = LegacyDefaultPollSeconds Then Return DefaultPollSeconds
            Return Math.Clamp(PollSeconds, MinPollSeconds, MaxPollSeconds)
        End Function

        Public ReadOnly Property IsConfigured As Boolean
            Get
                Return Enabled AndAlso Not String.IsNullOrWhiteSpace(FeedUrl)
            End Get
        End Property

        ''' <summary>Validation errors in Spanish (empty list = valid). An empty address is valid (= off).</summary>
        Public Function Validate() As IReadOnlyList(Of String)
            Dim errors As New List(Of String)()
            Dim url = If(FeedUrl, String.Empty).Trim()
            If url.Length > 0 Then
                Dim uri As Uri = Nothing
                If Not Uri.TryCreate(url, UriKind.Absolute, uri) Then
                    errors.Add("La dirección de anuncios debe ser https://… o una ruta de archivo (\\servidor\carpeta\control.json).")
                ElseIf uri.Scheme = Uri.UriSchemeHttp AndAlso Not uri.IsLoopback Then
                    errors.Add("La dirección de anuncios debe usar https:// (http solo se acepta en esta misma PC, para pruebas).")
                ElseIf uri.Scheme <> Uri.UriSchemeHttps AndAlso uri.Scheme <> Uri.UriSchemeHttp AndAlso uri.Scheme <> Uri.UriSchemeFile Then
                    errors.Add("La dirección de anuncios debe ser https://… o una ruta de archivo.")
                End If
            End If
            If PollSeconds < MinPollSeconds OrElse PollSeconds > MaxPollSeconds Then
                errors.Add($"La consulta de anuncios debe ser de {MinPollSeconds} a {MaxPollSeconds} segundos.")
            End If
            Return errors
        End Function
    End Class

    ''' <summary>Screen status reported to the announcements panel ("Status" section).</summary>
    Public NotInheritable Class StatusSettings
        Public Const SectionName As String = "Status"
        Public Const MinIntervalSeconds As Integer = 60
        Public Const MaxIntervalSeconds As Integer = 3600

        ''' <summary>True = this screen reports its status (and what it showed) to the panel.</summary>
        Public Property Enabled As Boolean = True
        ''' <summary>HTTPS address of the panel's heartbeat (…/api/heartbeat). Empty = off.</summary>
        Public Property Url As String = DefaultUrl

        Public Const DefaultUrl As String = "https://dashboard-metas-anuncios.vercel.app/api/heartbeat"
        ''' <summary>Seconds between reports when nothing changes (a change is reported within a minute).</summary>
        Public Property IntervalSeconds As Integer = 300

        Public Function EffectiveIntervalSeconds() As Integer
            Return Math.Clamp(IntervalSeconds, MinIntervalSeconds, MaxIntervalSeconds)
        End Function

        Public ReadOnly Property IsConfigured As Boolean
            Get
                Return Enabled AndAlso Not String.IsNullOrWhiteSpace(Url)
            End Get
        End Property

        ''' <summary>Validation errors in Spanish (empty = valid). An empty address is valid (= off).</summary>
        Public Function Validate() As IReadOnlyList(Of String)
            Dim errors As New List(Of String)()
            Dim value = If(Url, String.Empty).Trim()
            If value.Length > 0 Then
                Dim uri As Uri = Nothing
                If Not Uri.TryCreate(value, UriKind.Absolute, uri) OrElse Not (uri.Scheme = Uri.UriSchemeHttps OrElse (uri.Scheme = Uri.UriSchemeHttp AndAlso uri.IsLoopback)) Then
                    errors.Add("La dirección del panel debe ser https://… (http solo en esta misma PC, para pruebas).")
                End If
            End If
            If IntervalSeconds < MinIntervalSeconds OrElse IntervalSeconds > MaxIntervalSeconds Then
                errors.Add($"El reporte al panel debe ser cada {MinIntervalSeconds} a {MaxIntervalSeconds} segundos.")
            End If
            Return errors
        End Function
    End Class

    ''' <summary>Automatic updates ("Update" section). They arrive with the replies to the screen-status reports.</summary>
    Public NotInheritable Class UpdateSettings
        Public Const SectionName As String = "Update"
        ''' <summary>True = install the versions published in the panel (signed with the publisher's key).</summary>
        Public Property Enabled As Boolean = True
    End Class

    ''' <summary>Demo mode ("Demo" section). Also enabled with the --demo argument.</summary>
    Public NotInheritable Class DemoSettings
        Public Const SectionName As String = "Demo"
        Public Property Enabled As Boolean
        ''' <summary>Simulated query time per source.</summary>
        Public Property QueryDelayMilliseconds As Integer = 700
        ''' <summary>Source that always fails in demo ("", "Y1", "Estaciones" or "Embarques"), to see the red state.</summary>
        Public Property FailingSource As String = String.Empty
        ''' <summary>"HH:mm" or "yyyy-MM-dd HH:mm" = in demo the clock starts at this time (to see the pace at any hour). Empty = real time.</summary>
        Public Property SimulatedTime As String = String.Empty
        ''' <summary>True = in demo the announcements come from invented, locally signed examples (no address needed).</summary>
        Public Property SimulateAnnouncements As Boolean
    End Class

End Namespace
