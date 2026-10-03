Imports DashboardMetas.Core.Formatting
Imports DashboardMetas.Core.Models

Namespace Dashboard

    Public Enum BarState
        ''' <summary>Value ≥ goal (teal).</summary>
        GoalMet
        ''' <summary>Closed day below the goal (orange).</summary>
        BelowGoal
        ''' <summary>Today, still below the goal (grey).</summary>
        InProgress
    End Enum

    ''' <summary>How today is going against the goal at this hour of the shift.</summary>
    Public Enum PaceState
        ''' <summary>Today is not shown (Sunday) or there is no goal.</summary>
        NoShift
        ''' <summary>Nothing worked yet on this calendar day.</summary>
        NotStarted
        ''' <summary>At or above what should be done by now.</summary>
        OnTrack
        ''' <summary>Below what should be done by now.</summary>
        Behind
        ''' <summary>Daily goal reached.</summary>
        GoalMet
        ''' <summary>Shift over without reaching the goal.</summary>
        ShiftEnded
    End Enum

    Public NotInheritable Class DayBar
        Public Property [Date] As Date
        Public Property Value As Decimal
        Public Property Pieces As Decimal
        Public Property Styles As Integer
        Public Property IsToday As Boolean
        Public Property IsLastClosed As Boolean
        Public Property State As BarState
    End Class

    ''' <summary>Everything the screen shows for one area, already calculated (no UI code).</summary>
    Public NotInheritable Class DashboardSnapshot
        Public Property Today As Date
        Public Property Goal As Decimal
        Public Property Days As IReadOnlyList(Of DayBar) = Array.Empty(Of DayBar)()

        ''' <summary>Index of today in <see cref="Days"/>, -1 when today is not shown (Sunday).</summary>
        Public Property TodayIndex As Integer = -1
        Public Property LastClosedIndex As Integer

        Public Property AxisStep As Double
        Public Property AxisMax As Double
        Public Property GridLines As Integer = DashboardCalculator.GridLines

        ''' <summary>True with many days: values on the bars without decimals.</summary>
        Public Property Compact As Boolean

        Public ReadOnly Property HasShiftToday As Boolean
            Get
                Return TodayIndex >= 0
            End Get
        End Property

        Public Property TodayValue As Decimal
        Public Property TodayPieces As Decimal
        Public Property TodayStyles As Integer
        ''' <summary>Today's value ÷ goal (0 when there is no goal).</summary>
        Public Property Attainment As Double

        Public ReadOnly Property IsGoalMet As Boolean
            Get
                Return Goal > 0D AndAlso TodayValue >= Goal
            End Get
        End Property

        ''' <summary>Goal − today (≥ 0) when below the goal; otherwise today − goal, see <see cref="IsGoalMet"/>.</summary>
        Public ReadOnly Property GapToGoal As Decimal
            Get
                Return Math.Abs(Goal - TodayValue)
            End Get
        End Property

        Public Property WeekStart As Date
        Public Property WeekToDate As Decimal

        Public ReadOnly Property LastClosed As DayBar
            Get
                Return If(Days.Count = 0, Nothing, Days(LastClosedIndex))
            End Get
        End Property

        ''' <summary>Closed days (before today) with data.</summary>
        Public Property ClosedDaysWithData As Integer
        Public Property ClosedAverage As Decimal
        Public Property DaysOnGoal As Integer

        Public Property TotalValue As Decimal
        Public Property TotalPieces As Decimal

        ' ---- Pace and projection (today only)

        Public Property Shift As ShiftSchedule
        Public Property Pace As PaceState = PaceState.NoShift
        ''' <summary>Share of the shift's productive time already worked (0 to 1).</summary>
        Public Property ShiftProgress As Double
        ''' <summary>What should be done by now: goal × shift progress.</summary>
        Public Property ExpectedNow As Decimal
        ''' <summary>Expected − today when <see cref="PaceState.Behind"/>, today − expected when on track.</summary>
        Public ReadOnly Property PaceGap As Decimal
            Get
                Return Math.Abs(ExpectedNow - TodayValue)
            End Get
        End Property
        ''' <summary>True while the shift runs and there is enough time worked to project.</summary>
        Public Property HasProjection As Boolean
        ''' <summary>Value at the end of the shift if the current pace continues.</summary>
        Public Property Projection As Decimal

        Public ReadOnly Property FirstDate As Date
            Get
                Return If(Days.Count = 0, Today, Days(0).Date)
            End Get
        End Property

        Public ReadOnly Property LastDate As Date
            Get
                Return If(Days.Count = 0, Today, Days(Days.Count - 1).Date)
            End Get
        End Property
    End Class

    ''' <summary>The calculations of Pintar() in the Access version, testable.</summary>
    Public NotInheritable Class DashboardCalculator

        Public Const GridLines As Integer = 5
        ''' <summary>More days than this: bar values without decimals.</summary>
        Public Const CompactAfterDays As Integer = 20

        Private Sub New()
        End Sub

        ''' <summary>The last <paramref name="count"/> days up to today, oldest first (optionally without Sundays).</summary>
        Public Shared Function VisibleDays(today As Date, count As Integer, excludeSundays As Boolean) As IReadOnlyList(Of Date)
            Dim result As New List(Of Date)()
            Dim d = today.Date
            While result.Count < Math.Max(1, count)
                If Not (excludeSundays AndAlso d.DayOfWeek = DayOfWeek.Sunday) Then result.Add(d)
                d = d.AddDays(-1)
            End While
            result.Reverse()
            Return result
        End Function

        ''' <summary>
        ''' First calendar day to ask JDE for: the visible days + Sundays + margin (same as the .vbs), and never
        ''' after the 1st of the month (the month to date needs the whole month).
        ''' </summary>
        Public Shared Function QueryStart(today As Date, count As Integer) As Date
            Dim byDays = today.Date.AddDays(-(count + count \ 6 + 3))
            Dim first = New Date(today.Year, today.Month, 1)
            Return If(first < byDays, first, byDays)
        End Function

        ''' <summary>"Nice" axis step (1, 1.5, 2, 2.5, 3, 4, 5, 6, 8 × 10^n): the smallest that covers v.</summary>
        Public Shared Function NiceStep(v As Double) As Double
            If v <= 0 OrElse Double.IsNaN(v) OrElse Double.IsInfinity(v) Then Return 1
            Dim p = Math.Pow(10, Math.Floor(Math.Log10(v)))
            Dim f = v / p
            For Each candidate In {1.0, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0}
                If f <= candidate + 0.0000000001 Then Return candidate * p
            Next
            Return 10 * p
        End Function

        ''' <param name="today">Current date and time (the time is used for the pace).</param>
        ''' <param name="shift">Shift of the area; Nothing = no pace or projection.</param>
        Public Shared Function Build(goal As Decimal, rows As IEnumerable(Of DailyProduction), today As Date,
                                     historyDays As Integer, excludeSundays As Boolean,
                                     Optional shift As ShiftSchedule = Nothing, Optional minMinutesForProjection As Integer = 30) As DashboardSnapshot
            Dim now = today
            today = today.Date
            Dim byDate = If(rows, Enumerable.Empty(Of DailyProduction)()).
                Where(Function(r) r IsNot Nothing).
                GroupBy(Function(r) r.Date.Date).
                ToDictionary(Function(g) g.Key, Function(g) (Value:=g.Sum(Function(r) r.Value), Pieces:=g.Sum(Function(r) r.Pieces), Styles:=g.Sum(Function(r) r.Styles)))

            Dim dates = VisibleDays(today, historyDays, excludeSundays)
            Dim bars As New List(Of DayBar)()
            For Each d In dates
                Dim bar As New DayBar With {.Date = d, .IsToday = (d = today)}
                Dim data As (Value As Decimal, Pieces As Decimal, Styles As Integer) = Nothing
                If byDate.TryGetValue(d, data) Then
                    bar.Value = data.Value
                    bar.Pieces = data.Pieces
                    bar.Styles = data.Styles
                End If
                bars.Add(bar)
            Next

            Dim snap As New DashboardSnapshot With {.Today = today, .Goal = goal, .Days = bars, .Compact = bars.Count > CompactAfterDays}
            Dim last = bars.Count - 1
            snap.TodayIndex = If(bars(last).IsToday, last, -1)

            ' Last closed day: the most recent one before today with data
            Dim closed = -1
            For i = last To 0 Step -1
                If i <> snap.TodayIndex AndAlso bars(i).Value > 0D Then
                    closed = i
                    Exit For
                End If
            Next
            If closed < 0 Then closed = If(snap.TodayIndex = last, last - 1, last)
            snap.LastClosedIndex = Math.Max(0, closed)
            bars(snap.LastClosedIndex).IsLastClosed = True

            If snap.TodayIndex >= 0 Then
                Dim t = bars(snap.TodayIndex)
                snap.TodayValue = t.Value
                snap.TodayPieces = t.Pieces
                snap.TodayStyles = t.Styles
            End If
            ApplyPace(snap, goal, now, shift, minMinutesForProjection)

            ' Y axis: always shows the goal (and the projection) with some room above
            Dim maxValue = Math.Max(CDbl(goal), If(bars.Count = 0, 0.0, bars.Max(Function(b) CDbl(b.Value))))
            If snap.HasProjection Then maxValue = Math.Max(maxValue, CDbl(snap.Projection))
            snap.AxisStep = NiceStep(maxValue * 1.1 / GridLines)
            snap.AxisMax = snap.AxisStep * GridLines

            For i = 0 To last
                Dim bar = bars(i)
                If goal > 0D AndAlso bar.Value >= goal Then
                    bar.State = BarState.GoalMet
                ElseIf i = snap.TodayIndex Then
                    bar.State = BarState.InProgress
                Else
                    bar.State = BarState.BelowGoal
                End If
            Next

            snap.Attainment = If(goal > 0D, CDbl(snap.TodayValue / goal), 0.0)

            snap.WeekStart = today.AddDays(-Texts.MondayIndex(today))
            snap.WeekToDate = bars.Where(Function(b) b.Date >= snap.WeekStart).Sum(Function(b) b.Value)

            Dim closedDays = bars.Where(Function(b) b.Date < today AndAlso b.Value > 0D).ToList()
            snap.ClosedDaysWithData = closedDays.Count
            snap.ClosedAverage = If(closedDays.Count = 0, 0D, closedDays.Sum(Function(b) b.Value) / closedDays.Count)
            snap.DaysOnGoal = closedDays.Where(Function(b) goal > 0D AndAlso b.Value >= goal).Count()

            snap.TotalValue = bars.Sum(Function(b) b.Value)
            snap.TotalPieces = bars.Sum(Function(b) b.Pieces)
            Return snap
        End Function

        ''' <summary>Expected value at this hour, on track / behind, and the end-of-shift projection at the current pace.</summary>
        Private Shared Sub ApplyPace(snap As DashboardSnapshot, goal As Decimal, now As Date, shift As ShiftSchedule, minMinutes As Integer)
            snap.Shift = shift
            If shift Is Nothing OrElse snap.TodayIndex < 0 OrElse goal <= 0D Then
                snap.Pace = PaceState.NoShift
                Return
            End If
            Dim time = now.TimeOfDay
            snap.ShiftProgress = shift.Progress(time)
            snap.ExpectedNow = Math.Round(goal * CDec(snap.ShiftProgress), 2)

            Dim worked = shift.Worked(time)
            If snap.ShiftProgress > 0 AndAlso snap.ShiftProgress < 1 AndAlso worked >= Math.Max(1, minMinutes) Then
                snap.HasProjection = True
                snap.Projection = Math.Round(snap.TodayValue / CDec(snap.ShiftProgress), 2)
            End If

            If snap.TodayValue >= goal Then
                snap.Pace = PaceState.GoalMet
            ElseIf worked <= 0 Then
                snap.Pace = PaceState.NotStarted
            ElseIf snap.ShiftProgress >= 1 Then
                snap.Pace = PaceState.ShiftEnded
            ElseIf snap.TodayValue >= snap.ExpectedNow Then
                snap.Pace = PaceState.OnTrack
            Else
                snap.Pace = PaceState.Behind
            End If
        End Sub

    End Class

End Namespace
