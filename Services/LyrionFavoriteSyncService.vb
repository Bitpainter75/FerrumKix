Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Threading
Imports System.Threading.Tasks

Namespace Services

    ''' <summary>Gleicht die als Favorit gemerkten Alben des Lyrion Media Server in einen oertlichen
    ''' Ordner ab.
    '''
    ''' <para>Dieselbe Aufgabe wie das Skript lyrion-sync, nur ohne rsync und ohne die Bibliothek
    ''' einzuhaengen: die Dateien kommen ueber <c>/music/{id}/download</c> vom Server und sind
    ''' bitgenau dieselben. Einzustellen ist deshalb NUR der Zielordner.</para>
    '''
    ''' <para>Uebertragen wird ausschliesslich, was fehlt oder sich geaendert hat. Verglichen wird
    ''' ueber Groesse UND Aenderungszeit; beides liefert der Server zu jedem Titel mit. Die geholte
    ''' Datei bekommt die Aenderungszeit des Servers aufgepraegt - nur deshalb erkennt der naechste
    ''' Lauf sie wieder. Ein neu getaggter Titel wird damit nachgeholt, obwohl er gleich gross
    ''' geblieben ist; genau das uebersieht ein Abgleich, der allein auf die Groesse schaut.</para>
    '''
    ''' <para>Was im Zielordner liegt und nicht mehr zu den Favoriten gehoert, wird geloescht, leer
    ''' gewordene Ordner verschwinden mit. Der Zielordner traegt danach den Stand der Favoriten und
    ''' nichts sonst. Geloescht wird ausschliesslich UNTERHALB des Zielordners, geprueft am
    ''' aufgeloesten Pfad.</para></summary>
    Public NotInheritable Class LyrionFavoriteSyncService

        Private Sub New()
        End Sub

        ' Eigener Client: der Abgleich laedt grosse Dateien und darf nicht am kurzen Zeitlimit der
        ' Bibliotheksabfragen haengen.
        Private Shared ReadOnly Client As New HttpClient With {.Timeout = TimeSpan.FromMinutes(30)}

        ''' <summary>Ein Titel, wie ihn der Abgleich braucht.</summary>
        Public NotInheritable Class SyncTrack
            Public Property Id As String = String.Empty
            ''' <summary>Der Pfad UNTERHALB des Zielordners, aus der Serveradresse gewonnen.</summary>
            Public Property RelativePath As String = String.Empty
            Public Property Size As Long
            ''' <summary>Aenderungszeit des Servers als Unix-Sekunden.</summary>
            Public Property ModifiedUnix As Long
        End Class

        ''' <summary>Was ein Lauf vorhat, ermittelt bevor er etwas anfasst.</summary>
        Public NotInheritable Class SyncPlan
            Public Property TargetRoot As String = String.Empty
            ''' <summary>Was geholt werden muss, weil es fehlt oder sich geaendert hat.</summary>
            Public Property Fetch As New List(Of SyncTrack)()
            ''' <summary>Vollstaendige Pfade im Zielordner, die nicht mehr dazugehoeren.</summary>
            Public Property Remove As New List(Of String)()
            ''' <summary>Titel, die schon richtig im Zielordner liegen.</summary>
            Public Property UpToDate As Integer
            Public Property FavoriteAlbums As Integer
            ''' <summary>Favoriteneintraege ohne passendes Album - meist umbenannt oder neu getaggt.</summary>
            Public Property Unresolved As New List(Of String)()

            Public ReadOnly Property BytesToFetch As Long
                Get
                    Return Fetch.Sum(Function(track) track.Size)
                End Get
            End Property

            Public ReadOnly Property HasWork As Boolean
                Get
                    Return Fetch.Count > 0 OrElse Remove.Count > 0
                End Get
            End Property
        End Class

        Public NotInheritable Class SyncResult
            Public Property Fetched As Integer
            Public Property Removed As Integer
            Public Property Failed As Integer
            Public Property BytesFetched As Long
        End Class

        ''' <summary>Stellt fest, was zu tun ist. Fasst nichts an.</summary>
        Public Shared Async Function BuildPlanAsync(targetRoot As String, report As Action(Of String), cancellationToken As CancellationToken) As Task(Of SyncPlan)
            Dim plan As New SyncPlan With {.TargetRoot = NormalizeRoot(targetRoot)}
            If plan.TargetRoot.Length = 0 Then
                Throw New InvalidOperationException(LocalizationService.T("Bitte zuerst einen Zielordner für den Favoritenabgleich wählen."))
            End If

            report(LocalizationService.T("Alben werden gelesen …"))
            Dim albums = Await LyrionMediaServerService.GetAlbumsAsync(String.Empty, LyrionMediaServerService.AlbumSort.ArtistYear, cancellationToken)
            Dim albumByFavoriteUrl As New Dictionary(Of String, String)(StringComparer.Ordinal)
            For Each album In albums
                If album.FavoritesUrl.Length > 0 AndAlso album.Id.Length > 0 Then albumByFavoriteUrl(album.FavoritesUrl) = album.Id
            Next

            report(LocalizationService.T("Favoriten werden gelesen …"))
            Dim favorites = Await LyrionMediaServerService.GetFavoriteEntriesAsync(cancellationToken)
            Dim wantedAlbums As New HashSet(Of String)(StringComparer.Ordinal)
            For Each entry In favorites
                Dim albumId As String = Nothing
                If albumByFavoriteUrl.TryGetValue(entry.Url, albumId) Then
                    wantedAlbums.Add(albumId)
                Else
                    ' Kommt vor, wenn ein Album seit dem Merken umbenannt oder neu getaggt wurde:
                    ' die Favoritenadresse traegt Titel und Interpret, und beides stimmt dann nicht
                    ' mehr. Der Lauf geht weiter und meldet es.
                    plan.Unresolved.Add(entry.Name)
                End If
            Next
            plan.FavoriteAlbums = wantedAlbums.Count
            ''' Ohne diese Sperre haette eine Stoerung beim Server - Favoritenliste vorruebergehend
            ''' leer, Albenabfrage ohne Ergebnis - zur Folge, dass der Sollstand leer ist und der
            ''' Lauf den ganzen Zielordner als ueberfluessig ansieht und loescht.
            If wantedAlbums.Count = 0 Then
                Throw New InvalidOperationException(LocalizationService.T("Der Server meldet kein einziges Favoritenalbum. Der Abgleich bricht ab, damit der Zielordner nicht geleert wird."))
            End If

            report(LocalizationService.T("Titel werden gelesen …"))
            Dim mediaDirs = Await LyrionMediaServerService.GetMediaDirsAsync(cancellationToken)
            Dim tracks = Await LyrionMediaServerService.GetAllLibraryTracksAsync(cancellationToken)

            ' Der Sollstand: voller Zielpfad je Titel. Auf Linux wird zwischen Gross- und
            ' Kleinschreibung unterschieden, der Vergleich also Ordinal.
            Dim wanted As New Dictionary(Of String, SyncTrack)(StringComparer.Ordinal)
            For Each track In tracks
                If Not wantedAlbums.Contains(track.AlbumId) Then Continue For
                Dim relative = RelativePathOf(track.Url, mediaDirs)
                If relative.Length = 0 Then Continue For
                Dim full As String
                Try
                    full = Path.GetFullPath(Path.Combine(plan.TargetRoot, relative))
                Catch ex As Exception
                    DiagnosticLogService.Log("Lyrion.Sync", $"{track.Url}: {ex.Message}")
                    Continue For
                End Try
                ' Ein ".." in der Serveradresse duerfte nie aus dem Zielordner herausfuehren.
                If IsInside(plan.TargetRoot, full) Then
                    wanted(full) = New SyncTrack With {.Id = track.Id, .RelativePath = relative,
                                                       .Size = track.Size, .ModifiedUnix = track.ModifiedUnix}
                End If
            Next

            report(LocalizationService.T("Zielordner wird verglichen …"))
            For Each pair In wanted
                Dim info As New FileInfo(pair.Key)
                If info.Exists AndAlso info.Length = pair.Value.Size AndAlso ToUnix(info.LastWriteTimeUtc) = pair.Value.ModifiedUnix Then
                    plan.UpToDate += 1
                Else
                    plan.Fetch.Add(pair.Value)
                End If
            Next

            If Directory.Exists(plan.TargetRoot) Then
                For Each file In Directory.EnumerateFiles(plan.TargetRoot, "*", SearchOption.AllDirectories)
                    Dim full = Path.GetFullPath(file)
                    If Not wanted.ContainsKey(full) Then plan.Remove.Add(full)
                Next
            End If
            Return plan
        End Function

        ''' <summary>Fuehrt den Plan aus: erst holen, dann aufraeumen. In dieser Reihenfolge, damit
        ''' ein Abbruch mitten im Lauf nichts loescht, was noch nicht ersetzt wurde.</summary>
        Public Shared Async Function RunAsync(plan As SyncPlan, report As Action(Of String), cancellationToken As CancellationToken) As Task(Of SyncResult)
            Dim result As New SyncResult()
            Directory.CreateDirectory(plan.TargetRoot)

            Dim done = 0
            For Each track In plan.Fetch
                cancellationToken.ThrowIfCancellationRequested()
                done += 1
                report(LocalizationService.Format("Titel {0} von {1} wird geholt: {2}", done, plan.Fetch.Count, Path.GetFileName(track.RelativePath)))
                Try
                    Await FetchAsync(plan.TargetRoot, track, cancellationToken)
                    result.Fetched += 1
                    result.BytesFetched += track.Size
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    ' Ein einzelner Fehlschlag bricht den Lauf nicht ab; die Datei wird beim
                    ' naechsten Mal erneut versucht, weil sie weiterhin fehlt.
                    result.Failed += 1
                    DiagnosticLogService.Log("Lyrion.Sync", $"{track.RelativePath}: {ex.Message}")
                End Try
            Next

            For Each obsolete In plan.Remove
                cancellationToken.ThrowIfCancellationRequested()
                If Not IsInside(plan.TargetRoot, obsolete) Then Continue For
                Try
                    File.Delete(obsolete)
                    result.Removed += 1
                Catch ex As Exception
                    DiagnosticLogService.Log("Lyrion.Sync", $"{obsolete}: {ex.Message}")
                End Try
            Next
            PruneEmptyDirectories(plan.TargetRoot)
            Return result
        End Function

        ''' <summary>Holt einen Titel. Erst neben das Ziel unter ".part", dann an Ort und Stelle -
        ''' ein Abbruch mitten im Laden hinterlaesst so keine halbe Datei, die beim naechsten Lauf
        ''' fuer vollstaendig gehalten wird. Uebrig gebliebene ".part" raeumt der naechste Lauf
        ''' ohnehin weg, sie gehoeren nicht zum Sollstand.</summary>
        Private Shared Async Function FetchAsync(root As String, track As SyncTrack, cancellationToken As CancellationToken) As Task
            Dim target = Path.Combine(root, track.RelativePath)
            Directory.CreateDirectory(Path.GetDirectoryName(target))
            Dim temporary = target & ".part"
            Using response = Await Client.GetAsync(LyrionMediaServerService.DownloadUrl(track.Id), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                response.EnsureSuccessStatusCode()
                Using source = Await response.Content.ReadAsStreamAsync(cancellationToken),
                      destination = New FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync:=True)
                    Await source.CopyToAsync(destination, cancellationToken)
                End Using
            End Using
            File.Move(temporary, target, overwrite:=True)
            ' Ohne dieses Aufpraegen truege die Datei die Zeit des Ladens, und der naechste Lauf
            ' hielte sie fuer veraendert - er lade jedes Mal alles erneut.
            File.SetLastWriteTimeUtc(target, DateTime.UnixEpoch.AddSeconds(track.ModifiedUnix))
        End Function

        ''' <summary>Der Pfad unterhalb des Zielordners, aus der Serveradresse eines Titels. Der
        ''' Server nennt seine Medienordner selbst (<c>pref mediadirs</c>); der laengste passende
        ''' gewinnt, denn lagen zwei ineinander, geriete das Ziel sonst eine Ebene zu tief. Passt
        ''' keiner, wird nur der fuehrende Schraegstrich abgeworfen - dann steht eine Ebene mehr im
        ''' Ziel, aber nichts geht verloren.</summary>
        Private Shared Function RelativePathOf(url As String, mediaDirs As IEnumerable(Of String)) As String
            If String.IsNullOrEmpty(url) OrElse Not url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) Then Return String.Empty
            Dim serverPath = Uri.UnescapeDataString(url.Substring("file://".Length))
            If serverPath.Length = 0 Then Return String.Empty

            Dim best = String.Empty
            For Each folder In If(mediaDirs, Enumerable.Empty(Of String)())
                If String.IsNullOrWhiteSpace(folder) Then Continue For
                Dim prefix = folder.TrimEnd("/"c) & "/"
                If serverPath.StartsWith(prefix, StringComparison.Ordinal) AndAlso prefix.Length > best.Length Then best = prefix
            Next
            Return If(best.Length > 0, serverPath.Substring(best.Length), serverPath.TrimStart("/"c))
        End Function

        Private Shared Function NormalizeRoot(targetRoot As String) As String
            If String.IsNullOrWhiteSpace(targetRoot) Then Return String.Empty
            Try
                Return Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetRoot.Trim()))
            Catch
                Return String.Empty
            End Try
        End Function

        ''' <summary>Ob ein Pfad wirklich unterhalb des Zielordners liegt. Alles, was geloescht
        ''' wird, geht vorher hier durch.
        '''
        ''' <para>Der Parameter heisst NICHT "path": VB unterscheidet keine Gross- und
        ''' Kleinschreibung, ein so benannter Parameter verdeckt die Klasse <see cref="IO.Path"/>
        ''' im ganzen Rumpf. Dieselbe Falle wie in <see cref="CoverArtService"/>.</para></summary>
        Private Shared Function IsInside(root As String, candidate As String) As Boolean
            If String.IsNullOrEmpty(root) OrElse String.IsNullOrEmpty(candidate) Then Return False
            Dim fence = Path.TrimEndingDirectorySeparator(root) & Path.DirectorySeparatorChar
            Return candidate.StartsWith(fence, StringComparison.Ordinal)
        End Function

        ''' <summary>Entfernt leer gewordene Ordner, von innen nach aussen. Der Zielordner selbst
        ''' bleibt stehen, auch wenn nichts mehr darin liegt.</summary>
        Private Shared Sub PruneEmptyDirectories(root As String)
            Try
                For Each folder In Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).
                                            OrderByDescending(Function(entry) entry.Length)
                    Try
                        If Not Directory.EnumerateFileSystemEntries(folder).Any() Then Directory.Delete(folder)
                    Catch ex As Exception
                        DiagnosticLogService.Log("Lyrion.Sync", $"{folder}: {ex.Message}")
                    End Try
                Next
            Catch ex As Exception
                DiagnosticLogService.Log("Lyrion.Sync", ex.Message)
            End Try
        End Sub

        Private Shared Function ToUnix(value As Date) As Long
            Return CLng(Math.Floor((value - Date.UnixEpoch).TotalSeconds))
        End Function

        ''' <summary>Eine Datenmenge als Text, wie sie in der Statuszeile steht.</summary>
        Public Shared Function FormatBytes(bytes As Long) As String
            If bytes >= 1024L * 1024L * 1024L Then Return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.0", Globalization.CultureInfo.CurrentCulture) & " GiB"
            If bytes >= 1024L * 1024L Then Return (bytes / (1024.0 * 1024.0)).ToString("0", Globalization.CultureInfo.CurrentCulture) & " MiB"
            Return (bytes / 1024.0).ToString("0", Globalization.CultureInfo.CurrentCulture) & " KiB"
        End Function

    End Class

End Namespace
