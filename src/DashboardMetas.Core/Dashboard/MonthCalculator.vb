Imports DashboardMetas.Core.Models

Namespace Dashboard

    Public NotInheritable Class MonthDay
        Public Property [Date] As Date
        Public Property Value As Decimal
        ''' <summary>Accumulated value up to this day (only for days up to today).</summary>
        Public Property Cumulative As Decimal
        ''' <summary>Accumulated monthly goal at the end of this day (straight line over the working days).</summary>
        Public Property Target As Decimal
        Public Property IsToday As Boolean
        Public Property IsFuture As Boolean
    End Class

    ''' <summary>Month to date against the monthly goal (one axis: accumulated US$).</summary>
    Public NotInheritable Class MonthSnapshot
        Public Property Today As Date
        Public Property MonthStart As Date
        ''' <summary>Working days of the month (without Sundays when they are excluded), oldest first.</summary>
        Public Property Days As IReadOnlyList(Of MonthDay) = Array.Empty(Of MonthDay)()
        Public Property MonthlyGoal As Decimal
        ''' <summary>True when the goal is daily goal × working days (no monthly goal typed for the area).</summary>
        Public Property IsAutomaticGoal As Boolean
        Public Property MonthToDate As Decimal
        ''' <summary>Working days elapsed, with today counted by its shift progress (e.g. 12.5).</summary>
        Public Property ElapsedDays As Double
        Public Property ExpectedToDate As Decimal
        Public Property HasProjection As Boolean
        Public Property Projection As Decimal
        Public Property AxisStep As Double
        Public Property AxisMax As Double
        Public Property GridLines As Integer = DashboardCalculator.GridLines

        Public ReadOnly Property WorkingDays As Integer
            Get
                Return Days.Count
            End Get
        End Property

        Public ReadOnly Property Attainment As Double
            Get
                Return If(MonthlyGoal > 0D, CDbl(MonthToDate / MonthlyGoal), 0.0)
            End Get
        End Property

        Public ReadOnly Property IsGoalMet As Boolean
            Get
                Return MonthlyGoal > 0D AndAlso MonthToDate >= MonthlyGoal
            End Get
        End Property

        ''' <summary>True when the month to date is at or above what was expected by now.</summary>
        Public ReadOnly Property IsOnTrack As Boolean
            Get
                Return MonthToDate >= ExpectedToDate
            End Get
        End Property

        Public ReadOnly Property TodayIndex As Integer
            Get
                For i = 0 To Days.Count - 1
                    If Days(i).IsToday Then Return i
                Next
                Return -1
            End Get
        End Property

        ''' <summary>Index of the last day with an accumulated value (today, or the last working day before it).</summary>
        Public ReadOnly Property LastPastIndex As Integer
            Get
                Dim last = -1
                For i = 0 To Days.Count - 1
                    If Not Days(i).IsFuture Then last = i
                Next
                Return last
            End Get
        End Property
    End Class

    Public NotInheritable Class MonthCalculator

        Private Sub New()
        End Sub

        ''' <summary>Working days that must have passed before projecting the month end (earlier it jumps too much).</summary>
        Public Const MinDaysForProjection As Double = 3

        Public Shared Function FirstOfMonth(d As Date) As Date
            Return New Date(d.Year, d.Month, 1)
        End Function

        Public Shared Function WorkingDaysOfMonth(d As Date, excludeSundays As Boolean) As IReadOnlyList(Of Date)
            Dim first = FirstOfMonth(d)
            Return Enumerable.Range(0, Date.DaysInMonth(d.Year, d.Month)).
                Select(Function(i) first.AddDays(i)).
                Where(Function(x) Not (excludeSundays AndAlso x.DayOfWeek = DayOfWeek.Sunday)).ToList()
        End Function

        ''' <param name="monthlyGoal">Goal typed for the area; 0 = daily goal × working days of the month.</param>
        ''' <param name="now">Current date and time.</param>
        ''' <param name="shift">To count today by its shift progress; Nothing = today counts once it is over.</param>
        Public Shared Function Build(dailyGoal As Decimal, monthlyGoal As Decimal, rows As IEnumerable(Of DailyProduction), now As Date,
                                     excludeSundays As Boolean, Optional shift As ShiftSchedule = Nothing) As MonthSnapshot
            Dim today = now.Date
            Dim first = FirstOfMonth(today)
            Dim byDate = If(rows, Enumerable.Empty(Of DailyProduction)()).
                Where(Function(r) r IsNot Nothing AndAlso r.Date.Date >= first AndAlso r.Date.Date <= today).
                GroupBy(Function(r) r.Date.Date).
                ToDictionary(Function(g) g.Key, Function(g) g.Sum(Function(r) r.Value))

            Dim dates = WorkingDaysOfMonth(today, excludeSundays)
            Dim snap As New MonthSnapshot With {.Today = today, .MonthStart = first}
            snap.IsAutomaticGoal = monthlyGoal <= 0D
            snap.MonthlyGoal = If(snap.IsAutomaticGoal, dailyGoal * dates.Count, monthlyGoal)
            ' Values of an excluded Sunday still count in the month total
            snap.MonthToDate = byDate.Values.Sum()

            Dim days As New List(Of MonthDay)()
            Dim running = 0D
            Dim n = Math.Max(1, dates.Count)
            For k = 0 To dates.Count - 1
                Dim d = dates(k)
                running = byDate.Where(Function(p) p.Key <= d).Sum(Function(p) p.Value)
                days.Add(New MonthDay With {
                    .Date = d, .IsToday = (d = today), .IsFuture = d > today,
                    .Value = If(byDate.ContainsKey(d), byDate(d), 0D),
                    .Cumulative = If(d > today, 0D, running),
                    .Target = Math.Round(snap.MonthlyGoal * (k + 1) / n, 2)})
            Next
            snap.Days = days

            ' Working days elapsed: the closed ones plus today's share of its shift
            Dim closed = dates.Where(Function(d) d < today).Count()
            Dim todayShare = 0.0
            If dates.Contains(today) Then todayShare = If(shift Is Nothing, 0.0, shift.Progress(now.TimeOfDay))
            snap.ElapsedDays = closed + todayShare
            snap.ExpectedToDate = Math.Round(snap.MonthlyGoal * CDec(snap.ElapsedDays / n), 2)

            If snap.ElapsedDays >= MinDaysForProjection AndAlso snap.ElapsedDays < n Then
                snap.HasProjection = True
                snap.Projection = Math.Round(snap.MonthToDate / CDec(snap.ElapsedDays) * n, 2)
            End If

            Dim maxValue = Math.Max(CDbl(snap.MonthlyGoal), CDbl(snap.MonthToDate))
            If snap.HasProjection Then maxValue = Math.Max(maxValue, CDbl(snap.Projection))
            snap.AxisStep = DashboardCalculator.NiceStep(maxValue * 1.08 / snap.GridLines)
            snap.AxisMax = snap.AxisStep * snap.GridLines
            Return snap
        End Function

    End Class

End Namespace
