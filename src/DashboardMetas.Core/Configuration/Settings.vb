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
    End Class

End Namespace
