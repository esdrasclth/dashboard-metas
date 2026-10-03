Imports CommunityToolkit.Mvvm.ComponentModel
Imports DashboardMetas.Core.Dashboard

Namespace ViewModels

    ''' <summary>One area in the overview: today's attainment and pace, the month and a mini chart.</summary>
    Public NotInheritable Class AreaTileViewModel
        Inherits ObservableObject

        Public Sub New(code As String)
            Me.Code = code
        End Sub

        Public ReadOnly Property Code As String

        Private _name As String = String.Empty
        Public Property Name As String
            Get
                Return _name
            End Get
            Set(value As String)
                SetProperty(_name, value)
            End Set
        End Property

        Private _attainmentText As String = "—"
        Public Property AttainmentText As String
            Get
                Return _attainmentText
            End Get
            Set(value As String)
                SetProperty(_attainmentText, value)
            End Set
        End Property

        Private _pace As PaceState = PaceState.NoShift
        Public Property Pace As PaceState
            Get
                Return _pace
            End Get
            Set(value As PaceState)
                SetProperty(_pace, value)
            End Set
        End Property

        Private _pillText As String = String.Empty
        Public Property PillText As String
            Get
                Return _pillText
            End Get
            Set(value As String)
                SetProperty(_pillText, value)
            End Set
        End Property

        Private _isGoalMet As Boolean
        Public Property IsGoalMet As Boolean
            Get
                Return _isGoalMet
            End Get
            Set(value As Boolean)
                SetProperty(_isGoalMet, value)
            End Set
        End Property

        Private _valueText As String = String.Empty
        ''' <summary>"$82,413 of $150K".</summary>
        Public Property ValueText As String
            Get
                Return _valueText
            End Get
            Set(value As String)
                SetProperty(_valueText, value)
            End Set
        End Property

        Private _attainmentRatio As Double
        Public Property AttainmentRatio As Double
            Get
                Return _attainmentRatio
            End Get
            Set(value As Double)
                SetProperty(_attainmentRatio, value)
            End Set
        End Property

        Private _showPaceMarker As Boolean
        Public Property ShowPaceMarker As Boolean
            Get
                Return _showPaceMarker
            End Get
            Set(value As Boolean)
                SetProperty(_showPaceMarker, value)
            End Set
        End Property

        Private _paceMarkerRatio As Double
        Public Property PaceMarkerRatio As Double
            Get
                Return _paceMarkerRatio
            End Get
            Set(value As Double)
                SetProperty(_paceMarkerRatio, value)
            End Set
        End Property

        Private _detailText As String = String.Empty
        ''' <summary>Projection at the current pace, or the shift / expected-by-now line.</summary>
        Public Property DetailText As String
            Get
                Return _detailText
            End Get
            Set(value As String)
                SetProperty(_detailText, value)
            End Set
        End Property

        Private _detailTone As Tone = Tone.Normal
        Public Property DetailTone As Tone
            Get
                Return _detailTone
            End Get
            Set(value As Tone)
                SetProperty(_detailTone, value)
            End Set
        End Property

        Private _monthText As String = String.Empty
        Public Property MonthText As String
            Get
                Return _monthText
            End Get
            Set(value As String)
                SetProperty(_monthText, value)
            End Set
        End Property

        Private _monthTone As Tone = Tone.Normal
        Public Property MonthTone As Tone
            Get
                Return _monthTone
            End Get
            Set(value As Tone)
                SetProperty(_monthTone, value)
            End Set
        End Property

        Private _hasError As Boolean
        ''' <summary>The query of this area failed: the tile shows its last data with a red notice.</summary>
        Public Property HasError As Boolean
            Get
                Return _hasError
            End Get
            Set(value As Boolean)
                SetProperty(_hasError, value)
            End Set
        End Property

        Private _errorText As String = String.Empty
        Public Property ErrorText As String
            Get
                Return _errorText
            End Get
            Set(value As String)
                SetProperty(_errorText, value)
            End Set
        End Property

        Private _snapshot As DashboardSnapshot
        Public Property Snapshot As DashboardSnapshot
            Get
                Return _snapshot
            End Get
            Set(value As DashboardSnapshot)
                SetProperty(_snapshot, value)
            End Set
        End Property

    End Class

End Namespace
