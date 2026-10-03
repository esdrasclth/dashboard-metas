Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Data.Demo
Imports DashboardMetas.Data.Odbc
Imports Microsoft.Extensions.Options

Namespace Services

    ''' <summary>Demo mode: "Demo:Enabled" in configuration or the --demo argument.</summary>
    Public NotInheritable Class AppMode

        Private ReadOnly _demo As IOptionsMonitor(Of DemoSettings)

        Public Sub New(demo As IOptionsMonitor(Of DemoSettings), args As CommandLineArgs)
            _demo = demo
            ForcedByArgument = args.Demo
        End Sub

        ''' <summary>True when started with --demo (cannot be turned off from the settings screen).</summary>
        Public ReadOnly Property ForcedByArgument As Boolean

        Public ReadOnly Property IsDemo As Boolean
            Get
                Return ForcedByArgument OrElse _demo.CurrentValue.Enabled
            End Get
        End Property

    End Class

    Public NotInheritable Class CommandLineArgs

        Public Sub New(args As IEnumerable(Of String))
            Dim list = If(args, Enumerable.Empty(Of String)()).ToList()
            Demo = list.Any(Function(a) String.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(a, "/demo", StringComparison.OrdinalIgnoreCase))
            Windowed = list.Any(Function(a) String.Equals(a, "--ventana", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(a, "--windowed", StringComparison.OrdinalIgnoreCase))
        End Sub

        Public ReadOnly Property Demo As Boolean

        ''' <summary>--ventana: start in a normal window instead of full screen.</summary>
        Public ReadOnly Property Windowed As Boolean

    End Class

    ''' <summary>
    ''' Delegates to the ODBC or the demo repository depending on the current mode, so demo mode can be
    ''' switched from the settings screen without restarting. Business logic never knows which one is used.
    ''' </summary>
    Public NotInheritable Class ModeAwareProductionRepository
        Implements IProductionRepository

        Private ReadOnly _mode As AppMode
        Private ReadOnly _odbc As OdbcProductionRepository
        Private ReadOnly _demo As DemoProductionRepository

        Public Sub New(mode As AppMode, odbc As OdbcProductionRepository, demo As DemoProductionRepository)
            _mode = mode
            _odbc = odbc
            _demo = demo
        End Sub

        Private ReadOnly Property Current As IProductionRepository
            Get
                Return If(_mode.IsDemo, CType(_demo, IProductionRepository), _odbc)
            End Get
        End Property

        Public ReadOnly Property IsDemo As Boolean Implements IProductionRepository.IsDemo
            Get
                Return Current.IsDemo
            End Get
        End Property

        Public ReadOnly Property SourceDescription As String Implements IProductionRepository.SourceDescription
            Get
                Return Current.SourceDescription
            End Get
        End Property

        Public Function OpenSessionAsync(user As String, password As String, cancellationToken As CancellationToken) As Task(Of IProductionSession) Implements IProductionRepository.OpenSessionAsync
            Return Current.OpenSessionAsync(user, password, cancellationToken)
        End Function

    End Class

End Namespace
