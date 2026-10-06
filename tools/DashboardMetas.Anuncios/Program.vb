Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports DashboardMetas.Core.Announcements

''' <summary>
''' Publisher tool of the remote announcements: creates the signing key, writes an example, signs the
''' announcements file (with the same rules the app applies) and verifies a signed file or address.
''' Exit code 0 = ok, 1 = invalid data, 2 = wrong usage.
''' </summary>
Module Program

    Private ReadOnly DefaultFolder As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DashboardMetas-Anuncios")
    Private Const PrivateKeyFile As String = "clave-privada-anuncios.pem"
    Private Const PublicKeyFile As String = "clave-publica-anuncios.txt"
    Private Const KeyEnvironmentVariable As String = "DASHBOARDMETAS_CLAVE_ANUNCIOS"

    Function Main(args As String()) As Integer
        Console.OutputEncoding = Encoding.UTF8
        Try
            Dim command = If(args.Length = 0, "ayuda", args(0).ToLowerInvariant())
            Dim rest = args.Skip(1).ToArray()
            Select Case command
                Case "clave-nueva" : Return NewKey(rest)
                Case "ejemplo" : Return Example(rest)
                Case "firmar" : Return SignFile(rest)
                Case "verificar" : Return VerifyFile(rest)
                Case "ayuda", "help", "-h", "--help", "/?" : Help() : Return 0
                Case Else
                    Fail($"Comando desconocido: {args(0)}")
                    Help()
                    Return 2
            End Select
        Catch ex As Exception
            Fail(ex.Message)
            Return 1
        End Try
    End Function

    Private Sub Help()
        Console.WriteLine("
anuncios - anuncios remotos de Dashboard Metas

  anuncios clave-nueva [--carpeta <carpeta>]
      Crea la clave para firmar. La privada queda en la carpeta (por defecto Documentos\DashboardMetas-Anuncios);
      la pública hay que pegarla en src\DashboardMetas.Core\Announcements\AnnouncementKeys.vb y publicar la app.

  anuncios ejemplo [anuncios.json]
      Escribe un archivo de anuncios de ejemplo para editar.

  anuncios firmar <anuncios.json> [--clave <clave.pem>] [--salida control.json]
      Revisa los anuncios y crea control.json firmado (el archivo que se sube a la dirección de anuncios).
      La clave se busca en --clave, en la variable " & KeyEnvironmentVariable & " o en la carpeta por defecto.

  anuncios verificar <control.json | https://…>
      Comprueba la firma con las claves que acepta la app y muestra qué anuncio está activo, programado o vencido.
")
    End Sub

    ' ---- clave-nueva

    Private Function NewKey(args As String()) As Integer
        Dim folder = OptionValue(args, "--carpeta", DefaultFolder)
        Directory.CreateDirectory(folder)
        Dim privatePath = Path.Combine(folder, PrivateKeyFile)
        If File.Exists(privatePath) Then
            Fail($"Ya existe {privatePath}. No se sobrescribe: si de verdad quieres otra clave, usa otra carpeta.")
            Return 1
        End If
        Dim key = AnnouncementSigning.CreateKey()
        File.WriteAllText(privatePath, key.PrivatePem)
        File.WriteAllText(Path.Combine(folder, PublicKeyFile), $"Id de la clave: {key.KeyId}{Environment.NewLine}Clave pública: {key.PublicKey}{Environment.NewLine}")
        Ok($"Clave creada en {folder}")
        Console.WriteLine()
        Console.WriteLine("Pega esta línea en AnnouncementKeys.vb (diccionario Production) y publica la app:")
        Console.WriteLine($"    {{""{key.KeyId}"", ""{key.PublicKey}""}}")
        Console.WriteLine()
        Warn("La clave privada es la que firma: guarda una copia en un lugar seguro y no la subas a git ni la compartas.")
        Return 0
    End Function

    ' ---- ejemplo

    Private Function Example(args As String()) As Integer
        Dim target = If(args.FirstOrDefault(Function(a) Not a.StartsWith("--", StringComparison.Ordinal)), "anuncios.json")
        If File.Exists(target) Then
            Fail($"{target} ya existe; no se sobrescribe.")
            Return 1
        End If
        Dim today = New DateTimeOffset(Date.Today)
        Dim feed As New AnnouncementFeed With {.Announcements = New List(Of Announcement) From {
            New Announcement With {
                .Id = "inventario-" & Date.Today.ToString("yyyy-MM", Globalization.CultureInfo.InvariantCulture),
                .Title = "Inventario físico este sábado",
                .Message = "El sábado no hay producción: se hace el inventario físico. El lunes se trabaja en horario normal.",
                .TitleEn = "Physical inventory this Saturday",
                .MessageEn = "No production on Saturday: physical inventory. Monday is a normal working day.",
                .Severity = AnnouncementSeverity.Info, .Display = AnnouncementDisplay.Modal, .Sound = True,
                .StartsAt = today.AddHours(6), .EndsAt = today.AddDays(2).AddHours(18)},
            New Announcement With {
                .Id = "mantenimiento-jde",
                .Title = "Mantenimiento de JDE",
                .Message = "El domingo de 22:00 a 02:00 el AS400 estará en mantenimiento.",
                .Severity = AnnouncementSeverity.Warning, .Display = AnnouncementDisplay.Banner,
                .StartsAt = today, .EndsAt = today.AddDays(4),
                .Targets = New List(Of String) From {"planta:027"}}}}
        File.WriteAllText(target, JsonSerializer.Serialize(New With {.announcements = feed.Announcements}, AnnouncementJson.Options), New UTF8Encoding(False))
        Ok($"Ejemplo escrito en {Path.GetFullPath(target)}")
        Console.WriteLine("Edítalo y luego: anuncios firmar " & target)
        Return 0
    End Function

    ' ---- firmar

    Private Function SignFile(args As String()) As Integer
        Dim input = args.FirstOrDefault(Function(a) Not a.StartsWith("--", StringComparison.Ordinal))
        If input Is Nothing Then
            Fail("Falta el archivo de anuncios: anuncios firmar anuncios.json")
            Return 2
        End If
        Dim output = OptionValue(args, "--salida", Path.Combine(If(Path.GetDirectoryName(Path.GetFullPath(input)), "."), "control.json"))
        Dim keyPath = OptionValue(args, "--clave", If(Environment.GetEnvironmentVariable(KeyEnvironmentVariable), Path.Combine(DefaultFolder, PrivateKeyFile)))
        If Not File.Exists(keyPath) Then
            Fail($"No se encontró la clave privada en {keyPath}. Créala con «anuncios clave-nueva» o indica --clave.")
            Return 2
        End If

        Dim feed = JsonSerializer.Deserialize(Of AnnouncementFeed)(File.ReadAllText(input), AnnouncementJson.Options)
        If feed Is Nothing Then feed = New AnnouncementFeed()
        If feed.Announcements Is Nothing Then feed.Announcements = New List(Of Announcement)()
        Dim errors = AnnouncementRules.Validate(feed)
        If errors.Count > 0 Then
            Fail("No se firmó: hay que corregir el archivo de anuncios.")
            For Each e In errors
                Console.WriteLine("  • " & e)
            Next
            Return 1
        End If

        ' The version only grows: never lower than the previous control.json (the app would ignore it)
        Dim now = DateTimeOffset.UtcNow
        feed.IssuedAt = now
        feed.Version = Math.Max(now.ToUnixTimeSeconds(), PreviousVersion(output) + 1)

        Dim pem = File.ReadAllText(keyPath)
        Dim signed = AnnouncementSigning.Sign(feed, pem)
        Dim trusted = AnnouncementKeys.Trusted()
        If Not trusted.ContainsKey(signed.KeyId) Then
            Warn($"La clave {signed.KeyId} no está en AnnouncementKeys.vb: la app no aceptará este archivo hasta publicar una versión con esa clave.")
        End If
        File.WriteAllText(output, AnnouncementSigning.Serialize(signed), New UTF8Encoding(False))
        Ok($"Firmado: {Path.GetFullPath(output)} (versión {feed.Version}, clave {signed.KeyId})")
        PrintAnnouncements(feed)
        Console.WriteLine()
        Console.WriteLine("Sube control.json a la dirección de anuncios; las PCs lo toman en su siguiente consulta (1 min por defecto).")
        Return 0
    End Function

    Private Function PreviousVersion(path As String) As Long
        Try
            If Not File.Exists(path) Then Return 0
            Dim signed = JsonSerializer.Deserialize(Of SignedFeed)(File.ReadAllText(path), AnnouncementJson.Options)
            Dim feed = JsonSerializer.Deserialize(Of AnnouncementFeed)(Convert.FromBase64String(signed.Payload), AnnouncementJson.Options)
            Return feed.Version
        Catch
            Return 0
        End Try
    End Function

    ' ---- verificar

    Private Function VerifyFile(args As String()) As Integer
        Dim target = args.FirstOrDefault(Function(a) Not a.StartsWith("--", StringComparison.Ordinal))
        If target Is Nothing Then
            Fail("Falta el archivo o la dirección: anuncios verificar control.json")
            Return 2
        End If
        Dim json As String
        If target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
            Using http As New HttpClient With {.Timeout = TimeSpan.FromSeconds(20)}
                json = http.GetStringAsync(target).GetAwaiter().GetResult()
            End Using
        Else
            json = File.ReadAllText(target)
        End If

        Dim check = AnnouncementSigning.Verify(json, AnnouncementKeys.Trusted())
        If Not check.IsValid Then
            Fail("No válido: " & check.Error)
            Return 1
        End If
        Ok($"Firma válida (clave {check.KeyId}), versión {check.Feed.Version}, publicado {check.Feed.IssuedAt.ToLocalTime():yyyy-MM-dd HH:mm}")
        For Each w In check.Warnings
            Warn("Descartado por la app: " & w)
        Next
        PrintAnnouncements(check.Feed)
        Return 0
    End Function

    ' ---- output

    Private Sub PrintAnnouncements(feed As AnnouncementFeed)
        Console.WriteLine()
        If feed.Announcements.Count = 0 Then
            Console.WriteLine("Sin anuncios (publicarlo así retira todos los anuncios de las pantallas).")
            Return
        End If
        Dim now = DateTimeOffset.Now
        For Each a In feed.Announcements
            Dim state = If(a.StartsAt.HasValue AndAlso now < a.StartsAt.Value, "PROGRAMADO", If(a.EndsAt.HasValue AndAlso now >= a.EndsAt.Value, "VENCIDO", "ACTIVO"))
            Dim color = If(state = "ACTIVO", ConsoleColor.Green, If(state = "PROGRAMADO", ConsoleColor.Cyan, ConsoleColor.DarkGray))
            Write($"  {state,-10} ", color)
            Console.WriteLine($"{a.Id}  [{a.Severity.ToString().ToLowerInvariant()} · {a.Display.ToString().ToLowerInvariant()}]  {a.Title}")
            Dim targets = If(a.Targets Is Nothing OrElse a.Targets.Count = 0, "todas las PCs", String.Join(", ", a.Targets))
            Console.WriteLine($"             {Format(a.StartsAt)} → {Format(a.EndsAt)} · {targets}{If(a.Dismissible, "", " · no se puede cerrar")}{If(a.Sound, " · con sonido", "")}")
        Next
    End Sub

    Private Function Format(value As DateTimeOffset?) As String
        Return If(value.HasValue, value.Value.ToLocalTime().ToString("ddd dd/MM HH:mm", Globalization.CultureInfo.GetCultureInfo("es-ES")), "ya")
    End Function

    Private Function OptionValue(args As String(), name As String, fallback As String) As String
        Dim i = Array.FindIndex(args, Function(a) String.Equals(a, name, StringComparison.OrdinalIgnoreCase))
        Return If(i >= 0 AndAlso i + 1 < args.Length, args(i + 1), fallback)
    End Function

    Private Sub Ok(text As String)
        Write("✓ ", ConsoleColor.Green)
        Console.WriteLine(text)
    End Sub

    Private Sub Warn(text As String)
        Write("! ", ConsoleColor.Yellow)
        Console.WriteLine(text)
    End Sub

    Private Sub Fail(text As String)
        Write("✕ ", ConsoleColor.Red)
        Console.Error.WriteLine(text)
    End Sub

    Private Sub Write(text As String, color As ConsoleColor)
        Dim previous = Console.ForegroundColor
        Console.ForegroundColor = color
        Console.Write(text)
        Console.ForegroundColor = previous
    End Sub

End Module
