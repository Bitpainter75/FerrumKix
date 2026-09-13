Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
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
            ''' <summary>Unter dieser Adresse fuehrt der Server das Album in den Favoriten. Sie
            ''' kommt aus der Albenabfrage und ist der einzige Bezug zwischen beiden Listen: die
            ''' Favoritenliste kennt keine Album-Kennung.</summary>
            Public Property FavoritesUrl As String = String.Empty
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
        ''' <summary>Fuer die GROSSEN Abfragen des Favoritenabgleichs. Die Titelliste der ganzen
        ''' Bibliothek sind gut 20 MB; ueber eine langsame Leitung ist sie mit dem kurzen Zeitlimit
        ''' der Bibliotheksabfragen nicht zu holen. Und ein ueberschrittenes Zeitlimit meldet
        ''' HttpClient als ABGEBROCHENEN Vorgang - der Abgleich stuende danach als "abgebrochen"
        ''' da, obwohl niemand ihn abgebrochen hat.</summary>
        Private Shared ReadOnly BulkClient As New HttpClient With {.Timeout = TimeSpan.FromMinutes(5)}
        ''' <summary>Wonach die Albenuebersicht sortiert. Bis auf <see cref="AlbumSort.AlbumTitle"/>
        ''' sortiert der Server selbst: er kennt die Sortiernamen der Bibliothek und stellt "The
        ''' Beatles" unter B. Nach dem Albumtitel kann er nicht sortieren, das uebernimmt die
        ''' Ansicht.</summary>
        Public Enum AlbumSort
            ''' <summary>Zuletzt hinzugefuegt. Davon gibt der Server nur so viele heraus, wie seine
            ''' Einstellung "browseagelimit" erlaubt - voreingestellt 200.</summary>
            Recent = 0
            ArtistYear = 1
            AlbumTitle = 2
            YearAlbum = 3
        End Enum

        ''' <summary>Die VOLLSTAENDIGE Albenliste zu einer Suche, in der gewuenschten Reihenfolge.
        '''
        ''' <para>Absichtlich nicht seitenweise: der Server gibt 6500 Alben in einem Zug in
        ''' Sekundenbruchteilen heraus, und nur mit der ganzen Liste laesst sich absteigend
        ''' sortieren oder auf die Favoriten filtern, ohne bei jedem Handgriff neu zu fragen.</para>
        '''
        ''' <para>Erst wird nur gezaehlt: die Anzahl steht im Kopf der Antwort, und ohne sie muesste
        ''' die Abfrage eine Obergrenze raten.</para></summary>
        Public Shared Async Function GetAlbumsAsync(search As String, sort As AlbumSort, cancellationToken As CancellationToken, Optional bulk As Boolean = False) As Task(Of List(Of Album))
            Dim albums As New List(Of Album)()
            Dim probe = Await RequestAsync("", AlbumCommand(0, sort, search), cancellationToken, bulk)
            Dim total As Integer
            If Not Integer.TryParse(Text(probe, "count"), NumberStyles.Integer, CultureInfo.InvariantCulture, total) OrElse total <= 0 Then Return albums

            Dim result = Await RequestAsync("", AlbumCommand(total, sort, search), cancellationToken, bulk)
            Dim rows As JsonElement
            If Not result.TryGetProperty("albums_loop", rows) OrElse rows.ValueKind <> JsonValueKind.Array Then Return albums
            For Each row In rows.EnumerateArray()
                albums.Add(New Album With {.Id = Text(row, "id"), .Title = FirstText(row, "album", "title"), .Artist = FirstText(row, "artist", "albumartist"),
                                           .Year = Text(row, "year"), .ArtworkTrackId = Text(row, "artwork_track_id"), .FavoritesUrl = Text(row, "favorites_url")})
            Next
            If sort = AlbumSort.AlbumTitle Then
                albums = albums.OrderBy(Function(album) album.Title, StringComparer.CurrentCultureIgnoreCase).
                                ThenBy(Function(album) album.Artist, StringComparer.CurrentCultureIgnoreCase).ToList()
            End If
            Return albums
        End Function

        ''' <summary>"l" liefert den Albumnamen. Ohne dieses Tag kommen nur Cover und Metadaten an,
        ''' die Beschriftung der Album-Kacheln bliebe leer. Mit <paramref name="count"/> = 0 zaehlt
        ''' der Server nur.</summary>
        Private Shared Function AlbumCommand(count As Integer, sort As AlbumSort, search As String) As String()
            Dim command As New List(Of String) From {"albums", "0", Math.Max(0, count).ToString(CultureInfo.InvariantCulture), "tags:aljy", "sort:" & ServerSort(sort)}
            If Not String.IsNullOrWhiteSpace(search) Then command.Add("search:" & search.Trim())
            Return command.ToArray()
        End Function

        ''' <summary>Nach dem Albumtitel kennt der Server keine Reihenfolge - er nimmt "sort:album"
        ''' entgegen und liefert trotzdem die Voreinstellung. Dafuer wird nach Interpret geholt und
        ''' die Liste danach umsortiert.</summary>
        Private Shared Function ServerSort(sort As AlbumSort) As String
            Select Case sort
                Case AlbumSort.Recent : Return "new"
                Case AlbumSort.YearAlbum : Return "yearalbum"
                Case Else : Return "artflow"
            End Select
        End Function

        ''' <summary>Woran ein Favoriteneintrag als ALBUM zu erkennen ist. Einzelne Titel und
        ''' Radiosender stehen in derselben Liste und tragen andere Adressen.</summary>
        Private Const AlbumFavoritePrefix As String = "db:album."

        ''' <summary>Die Adressen der als Favorit gemerkten Alben, fuer den Filter in der
        ''' Uebersicht. Wer auch die Namen braucht, nimmt <see cref="GetFavoriteEntriesAsync"/>.</summary>
        Public Shared Async Function GetFavoriteAlbumUrlsAsync(cancellationToken As CancellationToken, Optional bulk As Boolean = False) As Task(Of HashSet(Of String))
            Dim entries = Await GetFavoriteEntriesAsync(cancellationToken, bulk)
            Return New HashSet(Of String)(entries.Select(Function(entry) entry.Url), StringComparer.Ordinal)
        End Function

        ''' <summary>Ein Favoriteneintrag, so wie der Server ihn fuehrt.</summary>
        Public NotInheritable Class FavoriteEntry
            Public Property Url As String = String.Empty
            Public Property Name As String = String.Empty
        End Class

        ''' <summary>Ein Titel der Bibliothek mit dem, was der Abgleich braucht.</summary>
        Public NotInheritable Class LibraryTrack
            Public Property Id As String = String.Empty
            Public Property AlbumId As String = String.Empty
            ''' <summary>Die Adresse, unter der die Datei auf dem SERVER liegt, etwa
            ''' file:///music/D/Deep%20Purple/...</summary>
            Public Property Url As String = String.Empty
            Public Property Size As Long
            ''' <summary>Aenderungszeit der Datei auf dem Server, als Unix-Sekunden.</summary>
            Public Property ModifiedUnix As Long
        End Class

        ''' <summary>Die Favoriteneintraege, die ALBEN sind. Einzelne Titel und Radiosender stehen
        ''' in derselben Liste und tragen andere Adressen.</summary>
        Public Shared Async Function GetFavoriteEntriesAsync(cancellationToken As CancellationToken, Optional bulk As Boolean = False) As Task(Of List(Of FavoriteEntry))
            Dim entries As New List(Of FavoriteEntry)()
            Dim probe = Await RequestAsync("", {"favorites", "items", "0", "0"}, cancellationToken, bulk)
            Dim total As Integer
            If Not Integer.TryParse(Text(probe, "count"), NumberStyles.Integer, CultureInfo.InvariantCulture, total) OrElse total <= 0 Then Return entries

            Dim result = Await RequestAsync("", {"favorites", "items", "0", total.ToString(CultureInfo.InvariantCulture), "want_url:1"}, cancellationToken, bulk)
            Dim rows As JsonElement
            If Not result.TryGetProperty("loop_loop", rows) OrElse rows.ValueKind <> JsonValueKind.Array Then Return entries
            For Each row In rows.EnumerateArray()
                Dim url = Text(row, "url")
                If url.StartsWith(AlbumFavoritePrefix, StringComparison.Ordinal) Then
                    entries.Add(New FavoriteEntry With {.Url = url, .Name = Text(row, "name")})
                End If
            Next
            Return entries
        End Function

        ''' <summary>Die Ordner, in denen der Server seine Medien liegen hat. Damit laesst sich der
        ''' serverseitige Teil eines Titelpfades abschneiden, ohne ihn irgendwo einzutragen.</summary>
        Public Shared Async Function GetMediaDirsAsync(cancellationToken As CancellationToken) As Task(Of List(Of String))
            Dim folders As New List(Of String)()
            Dim result = Await RequestAsync("", {"pref", "mediadirs", "?"}, cancellationToken, bulk:=True)
            Dim value As JsonElement
            If Not result.TryGetProperty("_p2", value) Then Return folders
            If value.ValueKind = JsonValueKind.Array Then
                For Each entry In value.EnumerateArray()
                    If entry.ValueKind = JsonValueKind.String Then folders.Add(entry.GetString())
                Next
            ElseIf value.ValueKind = JsonValueKind.String Then
                folders.Add(value.GetString())
            End If
            Return folders
        End Function

        ''' <summary>ALLE Titel der Bibliothek mit Adresse, Aenderungszeit, Groesse und Album.
        '''
        ''' <para>Bewusst in einem Zug statt je Album: bei 800 Favoritenalben waeren das 800
        ''' Abfragen und rund vierzig Sekunden, waehrend die ganze Bibliothek in gut zwei Sekunden
        ''' herueberkommt. Die Antwort ist gross (Groessenordnung 20 MB bei 78000 Titeln), wird
        ''' aber nur fuer den Abgleich gebraucht und danach wieder freigegeben.</para></summary>
        Public Shared Async Function GetAllLibraryTracksAsync(cancellationToken As CancellationToken) As Task(Of List(Of LibraryTrack))
            Dim tracks As New List(Of LibraryTrack)()
            Dim probe = Await RequestAsync("", {"tracks", "0", "0", "tags:u"}, cancellationToken, bulk:=True)
            Dim total As Integer
            If Not Integer.TryParse(Text(probe, "count"), NumberStyles.Integer, CultureInfo.InvariantCulture, total) OrElse total <= 0 Then Return tracks

            Dim result = Await RequestAsync("", {"tracks", "0", total.ToString(CultureInfo.InvariantCulture), "tags:unfe"}, cancellationToken, bulk:=True)
            Dim rows As JsonElement
            If Not result.TryGetProperty("titles_loop", rows) OrElse rows.ValueKind <> JsonValueKind.Array Then Return tracks
            For Each row In rows.EnumerateArray()
                Dim size As Long
                Long.TryParse(Text(row, "filesize"), NumberStyles.Integer, CultureInfo.InvariantCulture, size)
                Dim modified As Long
                Long.TryParse(Text(row, "modificationTime"), NumberStyles.Integer, CultureInfo.InvariantCulture, modified)
                tracks.Add(New LibraryTrack With {.Id = Text(row, "id"), .AlbumId = Text(row, "album_id"),
                                                  .Url = Text(row, "url"), .Size = size, .ModifiedUnix = modified})
            Next
            Return tracks
        End Function

        ''' <summary>Die Adresse, unter der der Server eine Titeldatei unveraendert herausgibt. Sie
        ''' liefert dieselben Bytes wie die Datei in der Ablage.</summary>
        Public Shared Function DownloadUrl(trackId As String) As String
            Dim baseUrl = AppSettingsService.Current.LyrionServerUrl.Trim().TrimEnd("/"c)
            If String.IsNullOrWhiteSpace(baseUrl) OrElse String.IsNullOrWhiteSpace(trackId) Then Return String.Empty
            Return baseUrl & "/music/" & Uri.EscapeDataString(trackId) & "/download"
        End Function

        ''' <summary>Setzt oder loescht den Favoritenstatus eines Albums und liefert den Stand
        ''' danach. Die Nummer zum Loeschen wird JEDES MAL frisch geholt: sie ist die Stelle in der
        ''' Favoritenliste und verschiebt sich, sobald davor ein Eintrag wegfaellt.</summary>
        Public Shared Async Function SetAlbumFavoriteAsync(album As Album, favorite As Boolean, cancellationToken As CancellationToken) As Task(Of Boolean)
            If album Is Nothing OrElse String.IsNullOrWhiteSpace(album.FavoritesUrl) Then Return False
            If favorite Then
                Await RequestAsync("", {"favorites", "add", "url:" & album.FavoritesUrl, "title:" & album.Title}, cancellationToken)
                Return True
            End If
            Await DeleteFavoriteAsync(album.FavoritesUrl, cancellationToken)
            Return False
        End Function

        ''' <summary>Loescht einen Favoriteneintrag ueber seine Adresse. Gibt True zurueck, wenn
        ''' wirklich einer weg ist, und False, wenn der Server unter dieser Adresse keinen fuehrt.
        '''
        ''' <para>Die Nummer wird JEDES MAL frisch geholt: sie ist die STELLE in der
        ''' Favoritenliste, nicht die Kennung des Eintrags. Sie verschiebt sich, sobald davor ein
        ''' Eintrag wegfaellt - wer eine Reihe von Eintraegen nach einer einmal geholten
        ''' Nummernliste loescht, trifft ab dem zweiten den falschen.</para></summary>
        Public Shared Async Function DeleteFavoriteAsync(favoritesUrl As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            If String.IsNullOrWhiteSpace(favoritesUrl) Then Return False
            Dim existing = Await RequestAsync("", {"favorites", "exists", favoritesUrl}, cancellationToken)
            If Text(existing, "exists") <> "1" Then Return False
            Dim index = Text(existing, "index")
            If String.IsNullOrWhiteSpace(index) Then Return False
            Await RequestAsync("", {"favorites", "delete", "item_id:" & index}, cancellationToken)
            Return True
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
        ''' <summary>Eine Abfrage an den Server. <paramref name="bulk"/> nimmt den Client mit dem
        ''' langen Zeitlimit - fuer die Abfragen des Abgleichs, die Megabyte am Stueck holen.</summary>
        Friend Shared Async Function RequestAsync(playerId As String, command As String(), cancellationToken As CancellationToken, Optional bulk As Boolean = False) As Task(Of JsonElement)
            Dim baseUrl = AppSettingsService.Current.LyrionServerUrl.Trim().TrimEnd("/"c)
            If String.IsNullOrWhiteSpace(baseUrl) Then Throw New InvalidOperationException(LocalizationService.T("Bitte zuerst die Adresse des Lyrion Media Server eintragen."))
            Dim body = JsonSerializer.Serialize(New With {.id = 3, .method = "slim.request", .params = New Object() {playerId, command}})
            Using response = Await If(bulk, BulkClient, Client).PostAsync(baseUrl & "/jsonrpc.js", New StringContent(body, Encoding.UTF8, "application/json"), cancellationToken)
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
