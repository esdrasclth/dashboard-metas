Imports System.Collections.ObjectModel
Imports System.Threading
Imports CommunityToolkit.Mvvm.ComponentModel
Imports CommunityToolkit.Mvvm.Input
Imports DashboardMetas.App.Services
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Dashboard
Imports DashboardMetas.Core.Models
Imports DashboardMetas.Core.Processing
Imports Microsoft.Extensions.Options

Namespace ViewModels

    ''' <summary>"Diagnóstico": environment checks and a connection + query test that saves nothing.</summary>
    Public NotInheritable Class DiagnosticsViewModel
        Inherits ObservableObject

        Private ReadOnly _diagnostics As DiagnosticsService
        Private ReadOnly _opener As JdeSessionOpener
        Private ReadOnly _jde As IOptionsMonitor(Of JdeSettings)
        Private ReadOnly _clock As IClock

        Public Sub New(diagnostics As DiagnosticsService, opener As JdeSessionOpener, jde As IOptionsMonitor(Of JdeSettings), clock As IClock)
            _diagnostics = diagnostics
            _opener = opener
            _jde = jde
            _clock = clock
            RefreshCommand = New AsyncRelayCommand(AddressOf RefreshAsync)
            TestConnectionCommand = New AsyncRelayCommand(AddressOf TestConnectionAsync)
            CopyCommand = New RelayCommand(AddressOf CopyToClipboard)
        End Sub

        Public ReadOnly Property Items As New ObservableCollection(Of DiagnosticItem)()
        Public ReadOnly Property RefreshCommand As IAsyncRelayCommand
        Public ReadOnly Property TestConnectionCommand As IAsyncRelayCommand
        Public ReadOnly Property CopyCommand As IRelayCommand

        Private _connectionResult As String = "Sin probar. La prueba conecta y corre las 3 consultas de hoy; no guarda la contraseña."
        Public Property ConnectionResult As String
            Get
                Return _connectionResult
            End Get
            Private Set(value As String)
                SetProperty(_connectionResult, value)
            End Set
        End Property

        Private _connectionLevel As DiagnosticLevel = DiagnosticLevel.Info
        Public Property ConnectionLevel As DiagnosticLevel
            Get
                Return _connectionLevel
            End Get
            Private Set(value As DiagnosticLevel)
                SetProperty(_connectionLevel, value)
            End Set
        End Property

        Public Async Function RefreshAsync() As Task
            Dim result = Await Task.Run(Function() _diagnostics.Collect())
            Items.Clear()
            For Each item In result
                Items.Add(item)
            Next
        End Function

        Private Async Function TestConnectionAsync() As Task
            ConnectionResult = "Conectando…"
            ConnectionLevel = DiagnosticLevel.Info
            Dim watch = Diagnostics.Stopwatch.StartNew()
            Try
                Dim settings = _jde.CurrentValue
                Dim session = Await Task.Run(Function() _opener.OpenAsync(settings.User, settings.UseDriverSignOn, allowPrompt:=True, savePassword:=False, CancellationToken.None))
                Dim parts As New List(Of String) From {$"Conexión correcta como {settings.User} en {watch.Elapsed.TotalSeconds:0.0} s ({session.DriverInfo})."}
                Dim anyError = False
                Try
                    For Each source In AreaCatalog.AllSources
                        Dim queryWatch = Diagnostics.Stopwatch.StartNew()
                        Try
                            Dim rows = Await session.GetDailyAsync(source, _clock.Now.Date.AddDays(-2), CancellationToken.None)
                            parts.Add($"{AreaCatalog.SourceName(source)}: {rows.Count} filas en {queryWatch.Elapsed.TotalSeconds:0.0} s.")
                        Catch ex As Exception
                            anyError = True
                            parts.Add($"{AreaCatalog.SourceName(source)}: ERROR {ex.Message}")
                        End Try
                    Next
                Finally
                    session.DisposeAsync().AsTask().Wait()
                End Try
                ConnectionResult = String.Join(Environment.NewLine, parts)
                ConnectionLevel = If(anyError, DiagnosticLevel.Warning, DiagnosticLevel.Ok)
            Catch ex As JdeConnectionException
                ConnectionResult = ex.Message & If(String.IsNullOrEmpty(ex.DriverMessage), "", "  [Driver: " & ex.DriverMessage & "]")
                ConnectionLevel = DiagnosticLevel.Error
            Catch ex As Exception
                ConnectionResult = ex.Message
                ConnectionLevel = DiagnosticLevel.Error
            End Try
        End Function

        Private Sub CopyToClipboard()
            Dim text = DiagnosticsService.ToText(Items) & Environment.NewLine & "Prueba de conexión:" & Environment.NewLine & ConnectionResult
            Try
                Clipboard.SetText(text)
            Catch
                ' clipboard busy: ignore
            End Try
        End Sub

    End Class

End Namespace
