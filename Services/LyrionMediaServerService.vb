Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

Namespace Services
    ''' <summary>Schmaler HTTPS-Client für die öffentliche Lyrion-JSON-RPC-Schnittstelle.
    ''' Die Player-Anbindung (SlimProto) bleibt absichtlich getrennt: Bibliothek und Cover dürfen
    ''' auch dann funktionieren, wenn der lokale Player noch nicht verbunden ist.</summary>
    Public NotInheritable Class LyrionMediaServerService
        Private Sub New()
        End Sub
        Public NotInheritable Class Album
            Public Property Id As String = String.Empty
            Public Property Title As String = String.Empty
            Public Property Artist As String = String.Empty
            Public Property Year As String = String.Empty
            Public Property ArtworkTrackId As String = String.Empty
        End Class
        Public NotInheritable Class Song
            Public Property Id As String = String.Empty
            Public Property Title As String = String.Empty
            Public Property Artist As String = String.Empty
            Public Property TrackNumber As String = String.Empty
            Public Property Duration As String = String.Empty
            Public Property Year As String = String.Empty
            ''' <summary>Der Inhaltstyp des Servers, etwa "mp3" oder "flc".</summary>
            Public Property ContentType As String = String.Empty
            ''' <summary>Wie der Server sie schreibt: mal "320", mal "320kbps CBR".</summary>
            Public Property Bitrate As String = String.Empty
            Public Property SampleRate As String = String.Empty
            Public Property FileSize As String = String.Empty
        End Class
        Public NotInheritable Class Player
            Public Property Id As String = String.Empty
            Public Property Name As String = String.Empty
        End Class
        Private Shared ReadOnly Client As New HttpClient With {.Timeout = TimeSpan.FromSeconds(12)}
        ''' <summary>Ein Ausschnitt der Albenliste und die Gesamtzahl dazu. Die Gesamtzahl kommt vom
        ''' Server und nicht aus der Laenge des Ausschnitts: nur mit ihr weiss die Ansicht, ob sich
        ''' weiteres Nachladen noch lohnt.</summary>
        Public NotInheritable Class AlbumPage
            Public Property Albums As New List(Of Album)()
            Public Property Total As Integer
        End Class

        Public Shared Async Function GetAlbumsAsync(search As String, start As Integer, count As Integer, cancellationToken As CancellationToken) As Task(Of AlbumPage)
            Dim baseUrl = AppSettingsService.Current.LyrionServerUrl.Trim().TrimEnd("/"c)
            If String.IsNullOrWhiteSpace(baseUrl) Then Throw New InvalidOperationException(LocalizationService.T("Bitte zuerst die Adresse des Lyrion Media Server eintragen."))
            ' "l" liefert den Albumnamen. Ohne dieses Tag kommen nur Cover und Metadaten an,
            ' die Beschriftung der Album-Kacheln bleibt dann leer.
            Dim command As New List(Of String) From {"albums", Math.Max(0, start).ToString(CultureInfo.InvariantCulture), Math.Max(1, count).ToString(CultureInfo.InvariantCulture), "tags:aljy", "sort:artflow"}
            If Not String.IsNullOrWhiteSpace(search) Then command.Add("search:" & search.Trim())
            Dim body = JsonSerializer.Serialize(New With {.id = 1, .method = "slim.request", .params = New Object() {"", command.ToArray()}})
            Using response = Await Client.PostAsync(baseUrl & "/jsonrpc.js", New StringContent(body, Encoding.UTF8, "application/json"), cancellationToken)
                response.EnsureSuccessStatusCode()
                Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync(cancellationToken))
                    Dim result = document.RootElement.GetProperty("result")
                    Dim page As New AlbumPage()
                    Dim total As Integer
                    If Integer.TryParse(Text(result, "count"), NumberStyles.Integer, CultureInfo.InvariantCulture, total) Then page.Total = total
                    Dim rows As JsonElement
                    If Not result.TryGetProperty("albums_loop", rows) OrElse rows.ValueKind <> JsonValueKind.Array Then Return page
                    For Each row In rows.EnumerateArray()
                        page.Albums.Add(New Album With {.Id = Text(row, "id"), .Title = FirstText(row, "album", "title"), .Artist = FirstText(row, "artist", "albumartist"), .Year = Text(row, "year"), .ArtworkTrackId = Text(row, "artwork_track_id")})
                    Next
                    ' Meldet der Server keine Gesamtzahl, gilt der Ausschnitt als das Ende - sonst
                    ' liefe das Nachladen ins Leere weiter.
                    If page.Total <= 0 Then page.Total = start + page.Albums.Count
                    Return page
                End Using
            End Using
        End Function
        Public Shared Async Function GetAlbumSongsAsync(albumId As String, cancellationToken As CancellationToken) As Task(Of List(Of Song))
            Dim baseUrl = AppSettingsService.Current.LyrionServerUrl.Trim().TrimEnd("/"c)
            If String.IsNullOrWhiteSpace(baseUrl) OrElse String.IsNullOrWhiteSpace(albumId) Then Return New List(Of Song)()
            ' Neben Interpret (a), Nummer (t) und Laufzeit (d) auch die technischen Angaben:
            ' Jahr (y), Inhaltstyp (o), Bitrate (r), Abtastrate (T) und Dateigroesse (f). Ohne
            ' diese Kennzeichen liefert der Server sie schlicht nicht mit.
            Dim command As String() = {"titles", "0", "500", "tags:adforTty", "album_id:" & albumId}
            Dim body = JsonSerializer.Serialize(New With {.id = 2, .method = "slim.request", .params = New Object() {"", command}})
            Using response = Await Client.PostAsync(baseUrl & "/jsonrpc.js", New StringContent(body, Encoding.UTF8, "application/json"), cancellationToken)
                response.EnsureSuccessStatusCode()
                Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync(cancellationToken))
                    Dim result = document.RootElement.GetProperty("result"), rows As JsonElement, songs As New List(Of Song)()
                    ' Wie bei den Alben auch auf die Art pruefen: meldet ein Server das Feld
                    ' anders als eine Liste, wirft EnumerateArray statt eine leere Liste zu geben.
                    If Not result.TryGetProperty("titles_loop", rows) OrElse rows.ValueKind <> JsonValueKind.Array Then Return songs
                    For Each row In rows.EnumerateArray()
                        songs.Add(New Song With {.Id = Text(row, "id"), .Title = Text(row, "title"), .Artist = Text(row, "artist"), .TrackNumber = Text(row, "tracknum"), .Duration = Text(row, "duration"),
                                                 .Year = Text(row, "year"), .ContentType = FirstText(row, "type", "content_type"), .Bitrate = Text(row, "bitrate"), .SampleRate = FirstText(row, "samplerate", "samplingrate"), .FileSize = Text(row, "filesize")})
                    Next
                    Return songs
                End Using
            End Using
        End Function
        Public Shared Async Function FindPlayerAsync(cancellationToken As CancellationToken) As Task(Of Player)
            Dim result = Await RequestAsync("", {"players", "0", "100"}, cancellationToken)
            Dim rows As JsonElement
            If Not result.TryGetProperty("players_loop", rows) Then Return Nothing
            Dim wanted = AppSettingsService.Current.LyrionClientName
            For Each row In rows.EnumerateArray()
                Dim player As New Player With {.Id = Text(row, "playerid"), .Name = Text(row, "name")}
                If String.Equals(player.Name, wanted, StringComparison.CurrentCultureIgnoreCase) Then Return player
            Next
            Return Nothing
        End Function
        Public Shared Async Function PlaySongAsync(songId As String, cancellationToken As CancellationToken) As Task
            Dim player = Await FindPlayerAsync(cancellationToken)
            If player Is Nothing Then Throw New InvalidOperationException(LocalizationService.T("Der konfigurierte Lyrion-Client ist nicht verbunden."))
            Await RequestAsync(player.Id, {"playlistcontrol", "cmd:load", "track_id:" & songId}, cancellationToken)
        End Function
        ''' <summary>Die HTTP-Download-Adresse ist zugleich ein direkt abspielbarer Stream.
        ''' Damit kann FerrumPlay die Bibliothek selbst wiedergeben, ohne als SlimProto-Client
        ''' beim Server angemeldet zu sein.</summary>
        Public Shared Function StreamUrl(songId As String) As String
            Dim baseUrl = AppSettingsService.Current.LyrionServerUrl.Trim().TrimEnd("/"c)
            If String.IsNullOrWhiteSpace(baseUrl) OrElse String.IsNullOrWhiteSpace(songId) Then Return String.Empty
            Return baseUrl & "/music/" & Uri.EscapeDataString(songId) & "/download"
        End Function
        Private Shared Async Function RequestAsync(playerId As String, command As String(), cancellationToken As CancellationToken) As Task(Of JsonElement)
            Dim baseUrl = AppSettingsService.Current.LyrionServerUrl.Trim().TrimEnd("/"c)
            If String.IsNullOrWhiteSpace(baseUrl) Then Throw New InvalidOperationException(LocalizationService.T("Bitte zuerst die Adresse des Lyrion Media Server eintragen."))
            Dim body = JsonSerializer.Serialize(New With {.id = 3, .method = "slim.request", .params = New Object() {playerId, command}})
            Using response = Await Client.PostAsync(baseUrl & "/jsonrpc.js", New StringContent(body, Encoding.UTF8, "application/json"), cancellationToken)
                response.EnsureSuccessStatusCode()
                Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync(cancellationToken))
                    Return document.RootElement.GetProperty("result").Clone()
                End Using
            End Using
        End Function
        Public Shared Function ArtworkUrl(album As Album) As String
            If album Is Nothing OrElse String.IsNullOrWhiteSpace(album.ArtworkTrackId) Then Return String.Empty
            Return AppSettingsService.Current.LyrionServerUrl.Trim().TrimEnd("/"c) & "/music/" & Uri.EscapeDataString(album.ArtworkTrackId) & "/cover.jpg"
        End Function

        ''' <summary>Die Adresse eines vom Server verkleinerten Covers. Fuer eine Kachel von 160
        ''' Punkten ist das Originalcover um Groessenordnungen zu gross; der Server kann selbst
        ''' skalieren, und nur so bleibt das Rollen durch eine grosse Bibliothek fluessig.</summary>
        Public Shared Function ThumbnailUrl(album As Album, size As Integer) As String
            If album Is Nothing OrElse String.IsNullOrWhiteSpace(album.ArtworkTrackId) Then Return String.Empty
            Dim edge = Math.Clamp(size, 32, 1024)
            Return AppSettingsService.Current.LyrionServerUrl.Trim().TrimEnd("/"c) & "/music/" & Uri.EscapeDataString(album.ArtworkTrackId) & $"/cover_{edge}x{edge}_o.jpg"
        End Function

        Public Shared Async Function GetArtworkAsync(album As Album, cancellationToken As CancellationToken) As Task(Of Byte())
            Dim url = ArtworkUrl(album)
            If String.IsNullOrWhiteSpace(url) Then Return Nothing
            Return Await Client.GetByteArrayAsync(url, cancellationToken)
        End Function

        ' Die geladenen Kacheln bleiben als JPEG liegen, nicht als entpacktes Bild: ein paar
        ' Kilobyte je Album statt einiger hundert. Beim Zurueckrollen ist das Cover damit sofort da,
        ' ohne den Server ein zweites Mal zu fragen.
        Private Shared ReadOnly ThumbnailCache As New Dictionary(Of String, Byte())(StringComparer.Ordinal)
        Private Shared ReadOnly ThumbnailOrder As New Queue(Of String)()
        Private Const ThumbnailCacheLimit As Integer = 2000
        ' Mehr als eine Handvoll gleichzeitiger Anfragen macht das Rollen nicht schneller, belastet
        ' aber einen Server mit langsamem Datentraeger spuerbar.
        Private Shared ReadOnly ThumbnailGate As New SemaphoreSlim(4)

        ''' <summary>Das verkleinerte Cover eines Albums, aus dem Zwischenspeicher oder vom Server.
        ''' Gibt es kein Cover, kommt Nothing zurueck - und zwar auch gemerkt, damit eine Kachel
        ''' ohne Bild nicht bei jedem Vorbeirollen erneut angefragt wird.</summary>
        Public Shared Async Function GetThumbnailAsync(album As Album, size As Integer, cancellationToken As CancellationToken) As Task(Of Byte())
            Dim url = ThumbnailUrl(album, size)
            If String.IsNullOrWhiteSpace(url) Then Return Nothing
            SyncLock ThumbnailCache
                Dim cached As Byte() = Nothing
                If ThumbnailCache.TryGetValue(url, cached) Then Return cached
            End SyncLock
            Await ThumbnailGate.WaitAsync(cancellationToken)
            Try
                SyncLock ThumbnailCache
                    Dim cached As Byte() = Nothing
                    If ThumbnailCache.TryGetValue(url, cached) Then Return cached
                End SyncLock
                Dim bytes As Byte() = Nothing
                Try
                    bytes = Await Client.GetByteArrayAsync(url, cancellationToken)
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As HttpRequestException When ex.StatusCode.HasValue AndAlso ex.StatusCode.Value = Net.HttpStatusCode.NotFound
                    ' Ein Album ohne Cover bleibt eines. Das gemerkte Nothing verhindert, dass bei
                    ' jedem Vorbeirollen erneut gefragt wird.
                    bytes = Nothing
                Catch ex As Exception
                    ' Ein Aussetzer des Servers darf sich dagegen NICHT festsetzen: nichts merken,
                    ' dann klappt es beim naechsten Vorbeirollen wieder.
                    DiagnosticLogService.LogException("Lyrion.Thumbnail", ex)
                    Return Nothing
                End Try
                SyncLock ThumbnailCache
                    If Not ThumbnailCache.ContainsKey(url) Then
                        ThumbnailCache(url) = bytes
                        ThumbnailOrder.Enqueue(url)
                        While ThumbnailOrder.Count > ThumbnailCacheLimit
                            ThumbnailCache.Remove(ThumbnailOrder.Dequeue())
                        End While
                    End If
                End SyncLock
                Return bytes
            Finally
                ThumbnailGate.Release()
            End Try
        End Function

        Private Shared Function Text(element As JsonElement, name As String) As String
            Dim value As JsonElement
            Return If(element.TryGetProperty(name, value) AndAlso value.ValueKind <> JsonValueKind.Null, value.ToString(), String.Empty)
        End Function
        Private Shared Function FirstText(element As JsonElement, ParamArray names As String()) As String
            For Each name In names
                Dim value = Text(element, name)
                If Not String.IsNullOrWhiteSpace(value) Then Return value
            Next
            Return String.Empty
        End Function
    End Class
End Namespace
