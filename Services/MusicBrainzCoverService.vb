Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

Namespace Services

    ''' <summary>Sucht das Cover eines Albums bei MusicBrainz, ausgehend von Artist und Albumname.
    '''
    ''' <para>Das Gegenstueck zur CD-Erkennung (<see cref="MusicBrainzDiscService"/>) fuer Dateien,
    ''' die schon gerippt sind: dort fehlt das Inhaltsverzeichnis und damit die Disc-Kennung, also
    ''' wird nach dem Namen gesucht.</para>
    '''
    ''' <para>Gesucht wird nach dem ALBUM ("release-group") und nicht nach einer Ausgabe. Ein
    ''' bekanntes Album hat Dutzende Pressungen, die Auswahl bestuende sonst aus lauter
    ''' gleichlautenden Zeilen. Die Cover Art Archive waehlt fuer ein Album selbst ein
    ''' Titelbild aus einer seiner Ausgaben.</para>
    '''
    ''' <para>Die Bilder dort sind oft kleiner und schlechter als ein selbst gesuchtes. Die Suche
    ''' ist deshalb ein Angebot neben dem Ablegen und ersetzt es nicht; das Mass des gefundenen
    ''' Bildes steht in der Coverspalte, bevor es geschrieben wird.</para></summary>
    Public NotInheritable Class MusicBrainzCoverService

        Private Sub New()
        End Sub

        ''' <summary>Ein Album, das zur Suche passt.</summary>
        Public NotInheritable Class AlbumMatch
            Public Property Id As String = String.Empty
            Public Property Title As String = String.Empty
            Public Property Artist As String = String.Empty
            ''' <summary>Die Art, wie MusicBrainz sie fuehrt: "Album", "Single", "EP" …</summary>
            Public Property PrimaryType As String = String.Empty
            Public Property FirstReleaseDate As String = String.Empty

            ''' <summary>Wie das Album in der Auswahl steht.</summary>
            Public ReadOnly Property Label As String
                Get
                    Dim parts As New List(Of String) From {Artist & " – " & Title}
                    If FirstReleaseDate.Length >= 4 Then parts.Add(FirstReleaseDate.Substring(0, 4))
                    If PrimaryType.Length > 0 Then parts.Add(PrimaryType)
                    Return String.Join("  ·  ", parts)
                End Get
            End Property
        End Class

        ''' <summary>So viele Treffer kommen in die Auswahl. Was danach folgt, passt erfahrungsgemaess
        ''' nur noch dem Wortlaut nach.</summary>
        Private Const MaxMatches As Integer = 10

        ''' <summary>Die Alben, die zu Artist und Albumname passen, der beste Treffer zuerst. Ohne
        ''' Albumname wird nicht gesucht: nach dem Artist allein kaeme seine ganze
        ''' Diskografie.</summary>
        Public Shared Async Function SearchAlbumsAsync(artist As String, album As String, cancellationToken As CancellationToken) As Task(Of List(Of AlbumMatch))
            Dim matches As New List(Of AlbumMatch)()
            If String.IsNullOrWhiteSpace(album) Then Return matches

            ' Beides als Phrase: die Woerter in dieser Reihenfolge, Gross- und Kleinschreibung
            ' und Satzzeichen egal. Ohne Anfuehrungszeichen faende "The Wall" jedes Album, in dem
            ' "the" vorkommt.
            Dim query = "releasegroup:" & Phrase(album)
            If Not String.IsNullOrWhiteSpace(artist) Then query &= " AND artist:" & Phrase(artist)
            Dim url = "https://musicbrainz.org/ws/2/release-group/?fmt=json&limit=" &
                      MaxMatches.ToString(CultureInfo.InvariantCulture) & "&query=" & Uri.EscapeDataString(query)

            Using response = Await MusicBrainzDiscService.GetAsync(url, cancellationToken)
                response.EnsureSuccessStatusCode()
                Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync(cancellationToken))
                    Dim rows As JsonElement
                    If Not document.RootElement.TryGetProperty("release-groups", rows) OrElse rows.ValueKind <> JsonValueKind.Array Then Return matches
                    For Each row In rows.EnumerateArray()
                        Dim match As New AlbumMatch With {
                            .Id = MusicBrainzDiscService.Text(row, "id"),
                            .Title = MusicBrainzDiscService.Text(row, "title"),
                            .Artist = MusicBrainzDiscService.ArtistCredit(row),
                            .PrimaryType = MusicBrainzDiscService.Text(row, "primary-type"),
                            .FirstReleaseDate = MusicBrainzDiscService.Text(row, "first-release-date")}
                        If match.Id.Length > 0 Then matches.Add(match)
                    Next
                End Using
            End Using
            ' Die Phrase passt auch auf jeden Titel, in dem sie nur VORKOMMT: zu "Nevermind" kaemen
            ' "Nevermind Sessions" und ein Dutzend Bootlegs mit. Traegt ein Treffer genau den
            ' gesuchten Namen, bleiben nur solche stehen.
            Dim exact = matches.Where(Function(match) String.Equals(match.Title.Trim(), album.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList()
            Return If(exact.Count > 0, exact, matches)
        End Function

        ''' <summary>Das Titelbild eines Albums in der Groesse, die dem Wunschmass am naechsten
        ''' kommt, ohne darunter zu liegen (siehe <see cref="MusicBrainzDiscService.CoverUrlFor"/>).
        ''' Nothing, wenn es zu diesem Album keines gibt - das ist kein Fehler.</summary>
        Public Shared Async Function DownloadCoverAsync(albumId As String, wantedSize As Integer, cancellationToken As CancellationToken) As Task(Of Byte())
            Dim url = MusicBrainzDiscService.CoverUrlFor("release-group", albumId, wantedSize)
            If url.Length = 0 Then Return Nothing
            Using response = Await MusicBrainzDiscService.GetAsync(url, cancellationToken)
                If response.StatusCode = Net.HttpStatusCode.NotFound Then Return Nothing
                response.EnsureSuccessStatusCode()
                Return Await response.Content.ReadAsByteArrayAsync(cancellationToken)
            End Using
        End Function

        ''' <summary>Ein Suchbegriff als Phrase der Lucene-Syntax, die MusicBrainz versteht. In
        ''' Anfuehrungszeichen muessen nur noch sie selbst und der Rueckstrich maskiert werden.</summary>
        Private Shared Function Phrase(value As String) As String
            Return """" & value.Trim().Replace("\", "\\").Replace("""", "\""") & """"
        End Function

    End Class

End Namespace
