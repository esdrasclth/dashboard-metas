Imports System.Security.Cryptography
Imports System.Threading
Imports DashboardMetas.Core.Abstractions
Imports DashboardMetas.Core.Announcements
Imports DashboardMetas.Core.Configuration
Imports Microsoft.Extensions.Options

Namespace Demo

    ''' <summary>
    ''' Invented announcements for demo mode (Demo:SimulateAnnouncements). They go through the same path as the
    ''' real ones: signed with a key created when the app starts (and trusted only in demo), verified, and
    ''' subject to the version check. The file changes once a day, so it is stable between checks.
    ''' </summary>
    Public NotInheritable Class DemoAnnouncementSource
        Implements IAnnouncementSource, IDisposable

        Private ReadOnly _demo As IOptionsMonitor(Of DemoSettings)
        Private ReadOnly _clock As IClock
        Private ReadOnly _key As ECDsa = ECDsa.Create(ECCurve.NamedCurves.nistP256)
        Private ReadOnly _trusted As IReadOnlyDictionary(Of String, Byte())
        Private _signedFor As Date = Date.MinValue
        Private _signed As String = String.Empty

        Public Sub New(demo As IOptionsMonitor(Of DemoSettings), clock As IClock)
            _demo = demo
            _clock = clock
            Dim spki = _key.ExportSubjectPublicKeyInfo()
            _trusted = New Dictionary(Of String, Byte())(StringComparer.Ordinal) From {{AnnouncementSigning.KeyIdOf(spki), spki}}
        End Sub

        Public ReadOnly Property IsConfigured As Boolean Implements IAnnouncementSource.IsConfigured
            Get
                Return _demo.CurrentValue.SimulateAnnouncements
            End Get
        End Property

        Public ReadOnly Property Description As String Implements IAnnouncementSource.Description
            Get
                Return If(IsConfigured, "DEMO (anuncios inventados)", String.Empty)
            End Get
        End Property

        Public Function TrustedKeys() As IReadOnlyDictionary(Of String, Byte()) Implements IAnnouncementSource.TrustedKeys
            Return _trusted
        End Function

        Public Function FetchAsync(cancellationToken As CancellationToken) As Task(Of AnnouncementFetch) Implements IAnnouncementSource.FetchAsync
            Dim today = _clock.Now.Date
            If today <> _signedFor Then
                _signed = AnnouncementSigning.Serialize(AnnouncementSigning.Sign(Build(today), _key))
                _signedFor = today
            End If
            Return Task.FromResult(New AnnouncementFetch With {.Content = _signed, .Source = "demo"})
        End Function

        ''' <summary>A welcome card, a maintenance strip, and one for another plant (not shown: it shows targeting).</summary>
        Public Shared Function Build(today As Date) As AnnouncementFeed
            Dim dayStart As New DateTimeOffset(today)
            Dim saturday = today.AddDays(((CInt(DayOfWeek.Saturday) - CInt(today.DayOfWeek)) + 7) Mod 7)
            Return New AnnouncementFeed With {
                .Version = today.Year * 10000L + today.Month * 100 + today.Day,
                .IssuedAt = dayStart,
                .Announcements = New List(Of Announcement) From {
                    New Announcement With {
                        .Id = "demo-bienvenida-" & today.ToString("yyyyMMdd", Globalization.CultureInfo.InvariantCulture),
                        .Title = "Nueva meta de Embarques desde el lunes",
                        .Message = "A partir del lunes la meta diaria de Embarques sube a $165,000." & Environment.NewLine & Environment.NewLine &
                                   "Gracias a todo el equipo por el esfuerzo de este mes: cerramos septiembre al 104 %.",
                        .TitleEn = "New Shipping goal starting Monday",
                        .MessageEn = "Starting Monday the daily Shipping goal goes up to $165,000." & Environment.NewLine & Environment.NewLine &
                                     "Thanks to the whole team for this month's effort: we closed September at 104%.",
                        .Severity = AnnouncementSeverity.Info, .Display = AnnouncementDisplay.Modal, .Sound = True,
                        .StartsAt = dayStart, .EndsAt = dayStart.AddDays(1)},
                    New Announcement With {
                        .Id = "demo-mantenimiento",
                        .Title = "Mantenimiento de JDE",
                        .Message = $"El sábado {saturday:dd/MM} de 22:00 a 02:00 el AS400 estará en mantenimiento: el tablero mostrará los últimos datos.",
                        .TitleEn = "JDE maintenance",
                        .MessageEn = $"On Saturday {saturday:MM/dd} from 22:00 to 02:00 the AS400 will be under maintenance: the dashboard will show the last data.",
                        .Severity = AnnouncementSeverity.Warning, .Display = AnnouncementDisplay.Banner,
                        .StartsAt = dayStart, .EndsAt = New DateTimeOffset(saturday.AddDays(1).AddHours(2))},
                    New Announcement With {
                        .Id = "demo-otra-planta",
                        .Title = "Solo para la planta 099",
                        .Message = "Este anuncio no debe verse aquí: va dirigido a otra planta.",
                        .Severity = AnnouncementSeverity.Critical, .Display = AnnouncementDisplay.Modal,
                        .Targets = New List(Of String) From {"planta:099"},
                        .StartsAt = dayStart, .EndsAt = dayStart.AddDays(1)}}}
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            _key.Dispose()
        End Sub

    End Class

End Namespace
