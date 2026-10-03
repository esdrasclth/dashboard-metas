Imports System.Globalization

Namespace Dashboard

    ''' <summary>
    ''' Working hours of an area (e.g. 06:30 to 16:10, optional break). The end may be earlier than the start:
    ''' the shift then crosses midnight (22:00 to 06:00). JDE dates every scan with its calendar date, so the
    ''' pace of a day counts the productive time that falls on that calendar date: for 22:00–06:00 that is
    ''' 00:00–06:00 (the night that started the day before) plus 22:00–24:00.
    ''' </summary>
    Public NotInheritable Class ShiftSchedule

        Public Const DefaultStart As String = "06:30"
        Public Const DefaultEnd As String = "16:10"
        Private Const DayMinutes As Double = 1440

        Private ReadOnly _windows As IReadOnlyList(Of (From As Double, [To] As Double))

        Public Sub New(start As TimeSpan, [end] As TimeSpan, Optional breakStart As TimeSpan? = Nothing, Optional breakEnd As TimeSpan? = Nothing)
            Me.Start = start
            Me.End = [end]
            If breakStart.HasValue AndAlso breakEnd.HasValue Then
                Me.BreakStart = breakStart
                Me.BreakEnd = breakEnd
            End If
            _windows = BuildWindows()
        End Sub

        Public ReadOnly Property Start As TimeSpan
        Public ReadOnly Property [End] As TimeSpan
        Public ReadOnly Property BreakStart As TimeSpan?
        Public ReadOnly Property BreakEnd As TimeSpan?

        Public Shared ReadOnly Property [Default] As New ShiftSchedule(New TimeSpan(6, 30, 0), New TimeSpan(16, 10, 0))

        Public ReadOnly Property HasBreak As Boolean
            Get
                Return BreakStart.HasValue
            End Get
        End Property

        ''' <summary>True when the shift ends the next day (22:00 to 06:00).</summary>
        Public ReadOnly Property CrossesMidnight As Boolean
            Get
                Return [End] <= Start
            End Get
        End Property

        ''' <summary>Productive periods inside one calendar day (minutes from 00:00), in order.</summary>
        Public ReadOnly Property Windows As IReadOnlyList(Of (From As Double, [To] As Double))
            Get
                Return _windows
            End Get
        End Property

        ''' <summary>Productive minutes of a calendar day (shift without the break).</summary>
        Public ReadOnly Property WorkMinutes As Double
            Get
                Return _windows.Sum(Function(w) w.To - w.From)
            End Get
        End Property

        ''' <summary>Productive minutes of the calendar day worked up to <paramref name="time"/>.</summary>
        Public Function Worked(time As TimeSpan) As Double
            Dim t = time.TotalMinutes
            Return _windows.Sum(Function(w) Math.Max(0, Math.Min(t, w.To) - w.From))
        End Function

        ''' <summary>0 before the first productive minute of the day, 1 after the last; flat outside the shift.</summary>
        Public Function Progress(time As TimeSpan) As Double
            Dim total = WorkMinutes
            Return If(total <= 0, 0.0, Math.Clamp(Worked(time) / total, 0.0, 1.0))
        End Function

        ''' <summary>True inside a productive period.</summary>
        Public Function IsWorking(time As TimeSpan) As Boolean
            Dim t = time.TotalMinutes
            Return _windows.Any(Function(w) t >= w.From AndAlso t < w.To)
        End Function

        Public Function IsOnBreak(time As TimeSpan) As Boolean
            If Not HasBreak Then Return False
            Dim t = time.TotalMinutes
            Dim b0 = BreakStart.Value.TotalMinutes, b1 = BreakEnd.Value.TotalMinutes
            Return If(b1 > b0, t >= b0 AndAlso t < b1, t >= b0 OrElse t < b1)
        End Function

        ''' <summary>Next start of a productive period after <paramref name="time"/> (Nothing when there is none left today).</summary>
        Public Function NextStart(time As TimeSpan) As TimeSpan?
            Dim t = time.TotalMinutes
            For Each w In _windows
                If w.From > t Then Return TimeSpan.FromMinutes(w.From)
            Next
            Return Nothing
        End Function

        ''' <summary>"06:30–16:10" or "22:00–06:00 (+1)".</summary>
        Public Function Describe() As String
            Return Format(Start) & "–" & Format([End]) & If(CrossesMidnight, " (+1)", "")
        End Function

        ''' <summary>
        ''' Shift on a timeline from its start (end + 24 h when it crosses midnight), minus the break, then
        ''' folded onto a single calendar day.
        ''' </summary>
        Private Function BuildWindows() As IReadOnlyList(Of (From As Double, [To] As Double))
            Dim s = Start.TotalMinutes
            Dim e = [End].TotalMinutes
            If e <= s Then e += DayMinutes
            Dim timeline As New List(Of (Double, Double))()
            If HasBreak Then
                Dim b0 = OnTimeline(BreakStart.Value, Start), b1 = OnTimeline(BreakEnd.Value, Start)
                timeline.Add((s, b0))
                timeline.Add((b1, e))
            Else
                timeline.Add((s, e))
            End If
            Dim folded As New List(Of (From As Double, [To] As Double))()
            For Each piece In timeline
                Dim a = piece.Item1, b = piece.Item2
                If b <= a Then Continue For
                If a < DayMinutes Then folded.Add((a, Math.Min(b, DayMinutes)))
                If b > DayMinutes Then folded.Add((Math.Max(a, DayMinutes) - DayMinutes, b - DayMinutes))
            Next
            Return folded.Where(Function(w) w.To > w.From).OrderBy(Function(w) w.From).ToList()
        End Function

        ''' <summary>Minutes from the start day's 00:00; times before the shift start belong to the next day.</summary>
        Private Shared Function OnTimeline(time As TimeSpan, shiftStart As TimeSpan) As Double
            Return time.TotalMinutes + If(time < shiftStart, DayMinutes, 0)
        End Function

        ''' <summary>"6:30", "06:30", "16:10". Empty or invalid = Nothing.</summary>
        Public Shared Function ParseTime(text As String) As TimeSpan?
            Dim value As TimeSpan
            Dim clean = If(text, String.Empty).Trim()
            If clean.Length = 0 Then Return Nothing
            If TimeSpan.TryParseExact(clean, {"h\:mm", "hh\:mm"}, CultureInfo.InvariantCulture, value) AndAlso value < TimeSpan.FromDays(1) Then Return value
            Return Nothing
        End Function

        Public Shared Function Format(value As TimeSpan) As String
            Return value.ToString("hh\:mm", CultureInfo.InvariantCulture)
        End Function

        ''' <summary>Builds a schedule from text, or returns a Spanish error. Break is optional (both empty).</summary>
        Public Shared Function TryCreate(start As String, [end] As String, breakStart As String, breakEnd As String,
                                         ByRef schedule As ShiftSchedule, ByRef [error] As String) As Boolean
            schedule = Nothing
            Dim s = ParseTime(start), e = ParseTime([end])
            If Not s.HasValue OrElse Not e.HasValue Then
                [error] = "La hora de inicio y fin del turno deben tener el formato HH:mm (ej. 06:30)."
                Return False
            End If
            If e.Value = s.Value Then
                [error] = "El inicio y el fin del turno no pueden ser iguales (si termina al día siguiente, el fin es menor que el inicio, ej. 22:00 a 06:00)."
                Return False
            End If
            Dim hasBreakText = Not String.IsNullOrWhiteSpace(breakStart) OrElse Not String.IsNullOrWhiteSpace(breakEnd)
            If Not hasBreakText Then
                schedule = New ShiftSchedule(s.Value, e.Value)
                Return True
            End If
            Dim b0 = ParseTime(breakStart), b1 = ParseTime(breakEnd)
            If Not b0.HasValue OrElse Not b1.HasValue OrElse b0.Value = b1.Value Then
                [error] = "La pausa debe tener inicio y fin en formato HH:mm (o dejar ambos vacíos)."
                Return False
            End If
            ' On the shift timeline (from its start), the break must fit inside it
            Dim sm = s.Value.TotalMinutes
            Dim em = e.Value.TotalMinutes + If(e.Value <= s.Value, DayMinutes, 0)
            Dim bs = OnTimeline(b0.Value, s.Value)
            Dim be = OnTimeline(b1.Value, s.Value)
            If be <= bs OrElse bs < sm OrElse be > em OrElse be - bs >= em - sm Then
                [error] = "La pausa debe quedar dentro del turno."
                Return False
            End If
            schedule = New ShiftSchedule(s.Value, e.Value, b0.Value, b1.Value)
            Return True
        End Function

    End Class

End Namespace
