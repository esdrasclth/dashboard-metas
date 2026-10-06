Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Dashboard

Namespace Models

    ''' <summary>Where the numbers of an area come from in JDE.</summary>
    Public Enum ProductionSource
        ''' <summary>Y1: F31122 operation 51000 (SMP scan → Assembly).</summary>
        AssemblyDeliveries = 1
        ''' <summary>Y2, Y3, Y5, Y7: F58C3120 Station Activity × W01 price.</summary>
        StationActivity = 2
        ''' <summary>SHP: dcLINK DCTXF transaction CS × W01 price.</summary>
        Shipments = 3
        ''' <summary>Custom float: open FIN orders of F4801 by status, right now (not per day).</summary>
        Float = 4
    End Enum

    ''' <summary>
    ''' One style in one status of the custom float (F4801 open FIN orders), as the float query returns it.
    ''' It is a picture of right now: the float has no date.
    ''' </summary>
    Public NotInheritable Class FloatItem
        ''' <summary>F4801.WASRST (Y1, Y2, Y3, Y5…).</summary>
        Public Property Status As String = String.Empty
        ''' <summary>Base style (first 9 characters of the item).</summary>
        Public Property Style As String = String.Empty
        ''' <summary>F58C3120.SRSORT of the style in the last year; empty = not classified.</summary>
        Public Property ProductLine As String = String.Empty
        Public Property Pieces As Decimal
        ''' <summary>Pieces × W01 price, in US$ (0 when the item has no W01 price).</summary>
        Public Property Value As Decimal
        ''' <summary>Open orders of the style in that status.</summary>
        Public Property Orders As Integer
    End Class

    ''' <summary>One day of one area, as returned by any of the three queries.</summary>
    Public NotInheritable Class DailyProduction
        Public Property Area As String = String.Empty
        Public Property [Date] As Date
        Public Property Pieces As Decimal
        ''' <summary>Value in US$.</summary>
        Public Property Value As Decimal
        ''' <summary>Styles (or items) with movement that day.</summary>
        Public Property Styles As Integer
    End Class

    ''' <summary>An area shown on the dashboard, with its names (ES/EN) and daily goal.</summary>
    Public NotInheritable Class AreaDefinition
        Public Property Code As String = String.Empty
        Public Property Name As String = String.Empty
        Public Property Subtitle As String = String.Empty
        ''' <summary>"entregado", "embarcado"… used in "Valor entregado por día".</summary>
        Public Property Verb As String = "entregado"
        Public Property NameEn As String = String.Empty
        Public Property SubtitleEn As String = String.Empty
        Public Property VerbEn As String = "delivered"
        Public Property DailyGoal As Decimal
        ''' <summary>Monthly goal in US$; 0 = automatic (daily goal × working days of the month).</summary>
        Public Property MonthlyGoal As Decimal
        Public Property Order As Integer
        Public Property Active As Boolean = True

        ''' <summary>Shift hours "HH:mm". Empty = the company default (Dashboard:ShiftStart / ShiftEnd).</summary>
        Public Property ShiftStart As String = String.Empty
        Public Property ShiftEnd As String = String.Empty
        ''' <summary>Optional break (lunch) "HH:mm"; both empty = no break.</summary>
        Public Property BreakStart As String = String.Empty
        Public Property BreakEnd As String = String.Empty

        ''' <summary>The area's shift; falls back to the default when the saved hours are not valid.</summary>
        Public Function Shift() As ShiftSchedule
            Dim schedule As ShiftSchedule = Nothing
            Dim ignored As String = Nothing
            Return If(ShiftSchedule.TryCreate(ShiftStart, ShiftEnd, BreakStart, BreakEnd, schedule, ignored), schedule, ShiftSchedule.Default)
        End Function

        Public Function Source() As ProductionSource
            Return AreaCatalog.SourceOf(Code)
        End Function

        Public Function DisplayName(english As Boolean) As String
            Dim value = If(english, NameEn, Name)
            If String.IsNullOrWhiteSpace(value) Then value = If(english, Name, NameEn)
            Return If(String.IsNullOrWhiteSpace(value), Code, value.Trim())
        End Function

        Public Function DisplaySubtitle(english As Boolean) As String
            Return If(If(english, SubtitleEn, Subtitle), String.Empty)
        End Function

        Public Function DisplayVerb(english As Boolean) As String
            Dim value = If(english, VerbEn, Verb)
            Return If(String.IsNullOrWhiteSpace(value), If(english, "delivered", "entregado"), value.Trim())
        End Function

        Public Function Clone() As AreaDefinition
            Return DirectCast(MemberwiseClone(), AreaDefinition)
        End Function
    End Class

    ''' <summary>The six areas of the plant and which query feeds each one.</summary>
    Public NotInheritable Class AreaCatalog

        Public Const AssemblyCode As String = "Y1"
        Public Const ShipmentsCode As String = "SHP"

        ''' <summary>Station codes and their F58C3120 quantity column, in query order.</summary>
        Public Shared ReadOnly Property Stations As IReadOnlyList(Of (Code As String, Column As String)) =
            {("Y2", "SRTL02"), ("Y3", "SRTL03"), ("Y5", "SRTL05"), ("Y7", "SRTL07")}

        ''' <summary>The three daily sources (one row per area and day).</summary>
        Public Shared ReadOnly Property AllSources As IReadOnlyList(Of ProductionSource) =
            {ProductionSource.AssemblyDeliveries, ProductionSource.StationActivity, ProductionSource.Shipments}

        ''' <summary>Everything a refresh asks JDE: the three daily sources and the custom float.</summary>
        Public Shared ReadOnly Property AllQueries As IReadOnlyList(Of ProductionSource) =
            {ProductionSource.AssemblyDeliveries, ProductionSource.StationActivity, ProductionSource.Shipments, ProductionSource.Float}

        Private Sub New()
        End Sub

        Public Shared Function SourceOf(code As String) As ProductionSource
            Select Case If(code, String.Empty).Trim().ToUpperInvariant()
                Case AssemblyCode : Return ProductionSource.AssemblyDeliveries
                Case ShipmentsCode : Return ProductionSource.Shipments
                Case Else : Return ProductionSource.StationActivity
            End Select
        End Function

        Public Shared Function CodesOf(source As ProductionSource) As IReadOnlyList(Of String)
            Select Case source
                Case ProductionSource.AssemblyDeliveries : Return {AssemblyCode}
                Case ProductionSource.Shipments : Return {ShipmentsCode}
                Case ProductionSource.Float : Return Array.Empty(Of String)()
                Case Else : Return Stations.Select(Function(s) s.Code).ToArray()
            End Select
        End Function

        ''' <summary>Name shown in status messages and logs.</summary>
        Public Shared Function SourceName(source As ProductionSource) As String
            Select Case source
                Case ProductionSource.AssemblyDeliveries : Return "Y1"
                Case ProductionSource.Shipments : Return "Embarques"
                Case ProductionSource.Float : Return "Float"
                Case Else : Return "Estaciones"
            End Select
        End Function

        Public Shared Function TryParseSource(text As String, ByRef source As ProductionSource) As Boolean
            Select Case If(text, String.Empty).Trim().ToUpperInvariant()
                Case "Y1", "ENSAMBLE", "ASSEMBLY", "ASSEMBLYDELIVERIES" : source = ProductionSource.AssemblyDeliveries
                Case "ESTACIONES", "STATIONS", "STATIONACTIVITY", "Y2", "Y3", "Y5", "Y7" : source = ProductionSource.StationActivity
                Case "EMBARQUES", "SHP", "SHIPMENTS" : source = ProductionSource.Shipments
                Case "FLOAT", "CUSTOM FLOAT", "CUSTOMFLOAT" : source = ProductionSource.Float
                Case Else : Return False
            End Select
            Return True
        End Function

        ''' <summary>The same six areas the Access version creates (names can be changed in "Configuración").</summary>
        Public Shared Function Defaults(dailyGoal As Decimal) As List(Of AreaDefinition)
            Const sep = " • "
            Dim list As New List(Of AreaDefinition) From {
                New AreaDefinition With {.Code = AssemblyCode, .Order = 1, .Name = "Entregas a Ensamble", .Subtitle = "SMP → Ensamble" & sep & "Escaneos Y1" & sep & "Valor en US$", .Verb = "entregado",
                                         .NameEn = "Deliveries to Assembly", .SubtitleEn = "SMP → Assembly" & sep & "Y1 scans" & sep & "Value in US$", .VerbEn = "delivered"}
            }
            Dim order = 2
            For Each station In Stations
                list.Add(New AreaDefinition With {.Code = station.Code, .Order = order,
                    .Name = "Estación " & station.Code, .Subtitle = "Station Activity" & sep & "Estación " & station.Code & sep & "Valor en US$", .Verb = "entregado",
                    .NameEn = "Station " & station.Code, .SubtitleEn = "Station Activity" & sep & "Station " & station.Code & sep & "Value in US$", .VerbEn = "delivered"})
                order += 1
            Next
            list.Add(New AreaDefinition With {.Code = ShipmentsCode, .Order = order, .Name = "Embarques", .Subtitle = "Shipped" & sep & "dcLINK CS" & sep & "Valor en US$", .Verb = "embarcado",
                                              .NameEn = "Shipments", .SubtitleEn = "Shipped" & sep & "dcLINK CS" & sep & "Value in US$", .VerbEn = "shipped"})
            For Each area In list
                area.DailyGoal = dailyGoal
            Next
            Return list
        End Function

    End Class

    ''' <summary>What the user changes in "Configuración" for the screen (saved per Windows user).</summary>
    Public NotInheritable Class DashboardPreferences

        Public Const MinDays As Integer = 5
        Public Const MaxDays As Integer = 31
        Public Const DefaultDays As Integer = 10
        Public Const MinRotateSeconds As Integer = 5
        Public Const MaxRotateSeconds As Integer = 3600

        ''' <summary>"ES" or "EN".</summary>
        Public Property Language As String = "ES"
        Public Property HistoryDays As Integer = DefaultDays
        Public Property AutoRotate As Boolean
        Public Property RotateSeconds As Integer = 30
        Public Property CurrentArea As String = AreaCatalog.AssemblyCode
        ''' <summary>Last view used: True = all areas at once.</summary>
        Public Property ShowOverview As Boolean
        ''' <summary>"Daily" (value per day) or "Month" (month to date).</summary>
        Public Property ChartView As String = "Daily"
        ''' <summary>Last view used: True = the custom float screen.</summary>
        Public Property ShowFloat As Boolean
        ''' <summary>True = the automatic rotation also shows the float screen after the last area.</summary>
        Public Property FloatInRotation As Boolean = True
        ''' <summary>"Status" (bars per status, stacked by product line) or "Line" (bars per product line).</summary>
        Public Property FloatChartView As String = "Status"
        Public Property Areas As List(Of AreaDefinition) = New List(Of AreaDefinition)()

        Public Function IsEnglish() As Boolean
            Return String.Equals(Language, "EN", StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' Fixes out-of-range values and makes sure the six known areas exist (keeping the user's names and
        ''' goals). Unknown codes are dropped: each area needs its query.
        ''' </summary>
        Public Sub Normalize(defaultGoal As Decimal)
            Normalize(New DashboardSettings With {.DefaultDailyGoal = defaultGoal})
        End Sub

        Public Sub Normalize(defaults As DashboardSettings)
            Dim defaultGoal = defaults.DefaultDailyGoal
            Language = If(IsEnglish(), "EN", "ES")
            HistoryDays = Math.Clamp(HistoryDays, MinDays, MaxDays)
            RotateSeconds = Math.Clamp(RotateSeconds, MinRotateSeconds, MaxRotateSeconds)
            ' One screen at a time: the float wins over the overview
            If ShowFloat Then ShowOverview = False
            FloatChartView = If(String.Equals(FloatChartView, "Line", StringComparison.OrdinalIgnoreCase), "Line", "Status")

            Dim saved As New Dictionary(Of String, AreaDefinition)(StringComparer.OrdinalIgnoreCase)
            For Each area In If(Areas, New List(Of AreaDefinition)())
                If area IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(area.Code) AndAlso Not saved.ContainsKey(area.Code.Trim()) Then
                    saved(area.Code.Trim()) = area
                End If
            Next

            Dim merged As New List(Of AreaDefinition)()
            For Each def In AreaCatalog.Defaults(defaultGoal)
                Dim area As AreaDefinition = Nothing
                If saved.TryGetValue(def.Code, area) Then
                    area.Code = def.Code
                    If String.IsNullOrWhiteSpace(area.Name) Then area.Name = def.Name
                    If String.IsNullOrWhiteSpace(area.NameEn) Then area.NameEn = def.NameEn
                    If area.Subtitle Is Nothing Then area.Subtitle = def.Subtitle
                    If area.SubtitleEn Is Nothing Then area.SubtitleEn = def.SubtitleEn
                    If String.IsNullOrWhiteSpace(area.Verb) Then area.Verb = def.Verb
                    If String.IsNullOrWhiteSpace(area.VerbEn) Then area.VerbEn = def.VerbEn
                    If area.DailyGoal <= 0D Then area.DailyGoal = defaultGoal
                    NormalizeShift(area, defaults)
                    merged.Add(area)
                Else
                    NormalizeShift(def, defaults)
                    merged.Add(def)
                End If
            Next
            Areas = merged.OrderBy(Function(a) a.Order).ThenBy(Function(a) a.Code, StringComparer.Ordinal).ToList()
            If Not Areas.Any(Function(a) a.Active) Then Areas(0).Active = True
            Dim current = FindArea(CurrentArea)
            If current Is Nothing OrElse Not current.Active Then CurrentArea = Areas.First(Function(a) a.Active).Code
            CurrentArea = FindArea(CurrentArea).Code
        End Sub

        ''' <summary>Fills empty or invalid hours with the company default (files saved by older versions have none).</summary>
        Private Shared Sub NormalizeShift(area As AreaDefinition, defaults As DashboardSettings)
            Dim schedule As ShiftSchedule = Nothing
            Dim ignored As String = Nothing
            If ShiftSchedule.TryCreate(area.ShiftStart, area.ShiftEnd, area.BreakStart, area.BreakEnd, schedule, ignored) Then Return
            Dim fallback = defaults.DefaultShift()
            area.ShiftStart = ShiftSchedule.Format(fallback.Start)
            area.ShiftEnd = ShiftSchedule.Format(fallback.End)
            area.BreakStart = If(fallback.HasBreak, ShiftSchedule.Format(fallback.BreakStart.Value), String.Empty)
            area.BreakEnd = If(fallback.HasBreak, ShiftSchedule.Format(fallback.BreakEnd.Value), String.Empty)
        End Sub

        Public Function FindArea(code As String) As AreaDefinition
            Return If(Areas, New List(Of AreaDefinition)()).FirstOrDefault(Function(a) String.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase))
        End Function

        Public Function ActiveAreas() As IReadOnlyList(Of AreaDefinition)
            Return Areas.Where(Function(a) a.Active).OrderBy(Function(a) a.Order).ToList()
        End Function

        ''' <summary>Next active area after <paramref name="code"/> (wraps around), for automatic rotation.</summary>
        Public Function NextActiveArea(code As String) As AreaDefinition
            Dim active = ActiveAreas().ToList()
            If active.Count = 0 Then Return Nothing
            Dim index = active.FindIndex(Function(a) String.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase))
            Return active((index + 1) Mod active.Count)
        End Function

        Public Function Clone() As DashboardPreferences
            Dim copy = DirectCast(MemberwiseClone(), DashboardPreferences)
            copy.Areas = If(Areas, New List(Of AreaDefinition)()).Select(Function(a) a.Clone()).ToList()
            Return copy
        End Function

    End Class

End Namespace
