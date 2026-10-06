Imports CommunityToolkit.Mvvm.ComponentModel
Imports DashboardMetas.Core.Formatting

Namespace ViewModels

    Public Enum Tone
        Normal
        Good
        Warn
        Bad
        Busy
    End Enum

    ''' <summary>One of the white KPI cards: title, big value and a line below.</summary>
    Public NotInheritable Class KpiCard
        Inherits ObservableObject

        Private _title As String = String.Empty
        Private _value As String = "—"
        Private _subtitle As String = String.Empty
        Private _tone As Tone = Tone.Normal

        Public Property Title As String
            Get
                Return _title
            End Get
            Set(value As String)
                SetProperty(_title, value)
            End Set
        End Property

        Public Property Value As String
            Get
                Return _value
            End Get
            Set(value As String)
                SetProperty(_value, value)
            End Set
        End Property

        Public Property Subtitle As String
            Get
                Return _subtitle
            End Get
            Set(value As String)
                SetProperty(_subtitle, value)
            End Set
        End Property

        ''' <summary>Colour of the value: normal ink, teal (goal met) or orange (below goal).</summary>
        Public Property Tone As Tone
            Get
                Return _tone
            End Get
            Set(value As Tone)
                SetProperty(_tone, value)
            End Set
        End Property

        Private _subtitleTone As Tone = Tone.Normal
        ''' <summary>Colour of the line below (muted by default).</summary>
        Public Property SubtitleTone As Tone
            Get
                Return _subtitleTone
            End Get
            Set(value As Tone)
                SetProperty(_subtitleTone, value)
            End Set
        End Property

        Public Sub Update(title As String, value As String, subtitle As String, Optional tone As Tone = Tone.Normal)
            Me.Title = title
            Me.Value = value
            Me.Subtitle = subtitle
            Me.Tone = tone
            SubtitleTone = Tone.Normal
        End Sub

    End Class

    ''' <summary>Fixed texts of the screen in the selected language.</summary>
    Public NotInheritable Class UiLabels

        Public Sub New(t As Texts)
            Area = t.L("Área", "Area")
            ChangeArea = t.L("Cambiar de área", "Change area")
            Settings = t.L("Configuración", "Settings")
            SettingsTip = t.L("Idioma, días, cambio automático, metas y conexión", "Language, days, auto-rotation, goals and connection")
            Refresh = t.L("Actualizar", "Refresh")
            ExitText = t.L("Salir", "Exit")
            Data = t.L("Datos", "Data")
            DataTip = t.L("Tabla con el detalle día por día", "Day by day table")
            Connect = t.L("Conectar", "Connect")
            FullScreenTip = t.L("Pantalla completa (F11)", "Full screen (F11)")
            AttainmentTitle = t.L("CUMPLIMIENTO DE HOY", "TODAY'S ATTAINMENT")
            Demo = t.L("MODO DEMO · datos inventados", "DEMO MODE · invented data")
            ByDay = t.L("Por día", "By day")
            Month = t.L("Mes", "Month")
            Float = "Float"
            FloatTip = t.L("Custom float: órdenes FIN abiertas por estatus (F)", "Custom float: open FIN orders by status (F)")
            FloatTotalTitle = t.L("FLOAT TOTAL", "TOTAL FLOAT")
            ByStatus = t.L("Por estatus", "By status")
            ByLine = t.L("Por línea", "By line")
        End Sub

        Public ReadOnly Property ByDay As String
        Public ReadOnly Property Month As String
        Public ReadOnly Property Float As String
        Public ReadOnly Property FloatTip As String
        Public ReadOnly Property FloatTotalTitle As String
        Public ReadOnly Property ByStatus As String
        Public ReadOnly Property ByLine As String

        Public ReadOnly Property Area As String
        Public ReadOnly Property ChangeArea As String
        Public ReadOnly Property Settings As String
        Public ReadOnly Property SettingsTip As String
        Public ReadOnly Property Refresh As String
        Public ReadOnly Property ExitText As String
        Public ReadOnly Property Data As String
        Public ReadOnly Property DataTip As String
        Public ReadOnly Property Connect As String
        Public ReadOnly Property FullScreenTip As String
        Public ReadOnly Property AttainmentTitle As String
        Public ReadOnly Property Demo As String

    End Class

    ''' <summary>One product line in the legend of the float chart (same colour slot as the chart).</summary>
    Public NotInheritable Class FloatLegendItem
        Public Property Name As String = String.Empty
        Public Property Slot As Integer
    End Class

    ''' <summary>A row of the float "Datos" window: one style in one status.</summary>
    Public NotInheritable Class FloatRow
        Public Property Status As String = String.Empty
        Public Property Style As String = String.Empty
        Public Property ProductLine As String = String.Empty
        Public Property Pieces As Decimal
        Public Property Value As Decimal
        Public Property Orders As Integer

        Public ReadOnly Property ValueText As String
            Get
                Return Money.Full(Value)
            End Get
        End Property

        Public ReadOnly Property PiecesText As String
            Get
                Return Money.Count(Pieces)
            End Get
        End Property

        ''' <summary>W01 price per piece; "—" without price.</summary>
        Public ReadOnly Property UnitText As String
            Get
                Return If(Pieces > 0D AndAlso Value > 0D, Money.Full(Value / Pieces), "—")
            End Get
        End Property
    End Class

    ''' <summary>A row of the "Datos" window.</summary>
    Public NotInheritable Class DayRow
        Public Property DateText As String = String.Empty
        Public Property DayName As String = String.Empty
        Public Property Value As Decimal
        Public Property Pieces As Decimal
        Public Property Styles As Integer
        Public Property PercentText As String = String.Empty
        Public Property StateText As String = String.Empty
        Public Property Tone As Tone

        Public ReadOnly Property ValueText As String
            Get
                Return Money.Full(Value)
            End Get
        End Property

        Public ReadOnly Property PiecesText As String
            Get
                Return Money.Count(Pieces)
            End Get
        End Property
    End Class

End Namespace
