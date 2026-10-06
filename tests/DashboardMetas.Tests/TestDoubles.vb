Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Models
Imports Microsoft.Extensions.Options

Friend NotInheritable Class FixedClock
    Implements IClock
    Public Sub New(now As Date)
        Me.Now = now
    End Sub
    Public Property Now As Date Implements IClock.Now
End Class

Friend NotInheritable Class Monitor(Of T)
    Implements IOptionsMonitor(Of T)
    Public Sub New(value As T)
        CurrentValue = value
    End Sub
    Public Property CurrentValue As T Implements IOptionsMonitor(Of T).CurrentValue
    Public Function [Get](name As String) As T Implements IOptionsMonitor(Of T).Get
        Return CurrentValue
    End Function
    Public Function OnChange(listener As Action(Of T, String)) As IDisposable Implements IOptionsMonitor(Of T).OnChange
        Return Nothing
    End Function
End Class

Friend NotInheritable Class MemoryCredentialStore
    Implements ICredentialStore
    Public Property Saved As String
    Public Property Deleted As Integer
    Public Function TryLoad() As String Implements ICredentialStore.TryLoad
        Return Saved
    End Function
    Public Sub Save(password As String) Implements ICredentialStore.Save
        Saved = password
    End Sub
    Public Sub Delete() Implements ICredentialStore.Delete
        Saved = Nothing
        Deleted += 1
    End Sub
    Public ReadOnly Property HasSaved As Boolean Implements ICredentialStore.HasSaved
        Get
            Return Saved IsNot Nothing
        End Get
    End Property
End Class

Friend NotInheritable Class ScriptedPrompt
    Implements IPasswordPrompt
    Private ReadOnly _answers As Queue(Of String)
    Public Property Calls As Integer
    Public Sub New(ParamArray answers As String())
        _answers = New Queue(Of String)(answers)
    End Sub
    Public Function PromptAsync(user As String, message As String, isRetry As Boolean, cancellationToken As CancellationToken) As Task(Of PasswordPromptResult) Implements IPasswordPrompt.PromptAsync
        Calls += 1
        If _answers.Count = 0 Then Return Task.FromResult(Of PasswordPromptResult)(Nothing)
        Return Task.FromResult(New PasswordPromptResult(_answers.Dequeue(), True))
    End Function
End Class

''' <summary>Repository with canned rows per source; a source can be told to fail.</summary>
Friend NotInheritable Class FakeRepository
    Implements IProductionRepository

    Public Property GoodPassword As String = "ok"
    Public Property Rows As New Dictionary(Of ProductionSource, List(Of DailyProduction))()
    Public Property Failing As New HashSet(Of ProductionSource)()
    Public Property Opened As Integer
    Public Property QueryOrder As New List(Of ProductionSource)()
    Public Property FloatItems As New List(Of FloatItem)()
    Public Property FloatClassifyFrom As Date?

    Public ReadOnly Property IsDemo As Boolean = True Implements IProductionRepository.IsDemo
    Public ReadOnly Property SourceDescription As String = "fake" Implements IProductionRepository.SourceDescription

    Public Function OpenSessionAsync(user As String, password As String, cancellationToken As CancellationToken) As Task(Of IProductionSession) Implements IProductionRepository.OpenSessionAsync
        If password IsNot Nothing AndAlso password <> GoodPassword Then
            Throw New JdeConnectionException(JdeConnectionErrorKind.InvalidPassword, "CWBSY0002")
        End If
        Opened += 1
        Return Task.FromResult(Of IProductionSession)(New FakeSession(Me))
    End Function

    Private NotInheritable Class FakeSession
        Implements IProductionSession
        Private ReadOnly _owner As FakeRepository
        Public Sub New(owner As FakeRepository)
            _owner = owner
        End Sub
        Public ReadOnly Property DriverInfo As String = "fake" Implements IProductionSession.DriverInfo
        Public Function GetDailyAsync(source As ProductionSource, fromDate As Date, cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of DailyProduction)) Implements IProductionSession.GetDailyAsync
            _owner.QueryOrder.Add(source)
            If _owner.Failing.Contains(source) Then Throw New InvalidOperationException("SQL0204 simulado")
            Dim list As List(Of DailyProduction) = Nothing
            If Not _owner.Rows.TryGetValue(source, list) Then list = New List(Of DailyProduction)()
            Return Task.FromResult(Of IReadOnlyList(Of DailyProduction))(list)
        End Function
        Public Function GetFloatAsync(classifyFrom As Date, cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of FloatItem)) Implements IProductionSession.GetFloatAsync
            _owner.QueryOrder.Add(ProductionSource.Float)
            _owner.FloatClassifyFrom = classifyFrom
            If _owner.Failing.Contains(ProductionSource.Float) Then Throw New InvalidOperationException("SQL0204 simulado")
            Return Task.FromResult(Of IReadOnlyList(Of FloatItem))(_owner.FloatItems)
        End Function
        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return ValueTask.CompletedTask
        End Function
    End Class

End Class

Friend Module Make
    Public Function Row(area As String, d As Date, value As Decimal, Optional pieces As Decimal = 10D, Optional styles As Integer = 2) As DailyProduction
        Return New DailyProduction With {.Area = area, .Date = d, .Value = value, .Pieces = pieces, .Styles = styles}
    End Function

    Public Function Float(status As String, style As String, line As String, pieces As Decimal, value As Decimal, Optional orders As Integer = 1) As FloatItem
        Return New FloatItem With {.Status = status, .Style = style, .ProductLine = line, .Pieces = pieces, .Value = value, .Orders = orders}
    End Function
End Module
