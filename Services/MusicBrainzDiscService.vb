Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

Namespace Services

    ''' <summary>Erkennt eine eingelegte Audio-CD bei MusicBrainz.
    '''
    ''' <para>freedb ist 2020 abgeschaltet. MusicBrainz ist der lebende Dienst derselben Aufgabe,
    ''' kostenlos und ohne Schluessel; gefragt wird ueber die DISC-KENNUNG, die sich allein aus dem
    ''' Inhaltsverzeichnis der CD berechnen laesst. Die CD selbst wird dafuer nicht gelesen und
    ''' nichts von ihr uebertragen - nur die Laengen ihrer Spuren.</para>
    '''
    ''' <para>Die Kennung ist ein Fingerabdruck des Inhaltsverzeichnisses, nicht der Pressung: zwei
    ''' Ausgaben mit denselben Spurlaengen teilen sie sich. Deshalb kann die Antwort MEHRERE
    ''' Ausgaben enthalten, und deshalb fragt die Anwendung dann nach, statt zu raten.</para></summary>
    Public NotInheritable Class MusicBrainzDiscService

        Private Sub New()
        End Sub

        ''' <summary>MusicBrainz verlangt eine aussagekraeftige Kennung des Aufrufers und sperrt
        ''' anonyme Aufrufe aus. Die Adresse gehoert ausdruecklich dazu.</summary>
        Private Shared ReadOnly Client As New HttpClient With {.Timeout = TimeSpan.FromSeconds(20)}

        Shared Sub New()
            Client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "FerrumPlay/" & VersionText & " ( https://github.com/Bitpainter75/FerrumPlay )")
        End Sub

        Private Shared ReadOnly Property VersionText As String
            Get
                Dim version = Reflection.Assembly.GetExecutingAssembly().GetName().Version
                Return If(version Is Nothing, "0.0", $"{version.Major}.{version.Minor}.{version.Build}")
            End Get
        End Property

        ''' <summary>Ein Titel einer erkannten Ausgabe.</summary>
        Public NotInheritable Class DiscTrack
            Public Property Number As Integer
            Public Property Title As String = String.Empty
            Public Property Artist As String = String.Empty
        End Class

        ''' <summary>Eine Ausgabe, die zu dieser Disc-Kennung passt.</summary>
        Public NotInheritable Class DiscRelease
            Public Property Id As String = String.Empty
            Public Property Title As String = String.Empty
            Public Property Artist As String = String.Empty
            ''' <summary>Erscheinungsdatum, wie MusicBrainz es fuehrt: "1972" oder "1972-03-25".</summary>
            Public Property [Date] As String = String.Empty
            Public Property Country As String = String.Empty
            ''' <summary>Der Zusatz, mit dem MusicBrainz gleichnamige Ausgaben auseinanderhaelt,
            ''' etwa "remastered" oder "japanese edition".</summary>
            Public Property Disambiguation As String = String.Empty
            Public Property Tracks As New List(Of DiscTrack)()

            Public ReadOnly Property Year As Integer
                Get
                    Dim parsed As Integer
                    If [Date].Length >= 4 AndAlso Integer.TryParse([Date].Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, parsed) Then Return parsed
                    Return 0
                End Get
            End Property

            ''' <summary>Wie die Ausgabe in der Auswahl steht: so viel, dass sich zwei Pressungen
            ''' desselben Albums unterscheiden lassen, und nicht mehr.</summary>
            Public ReadOnly Property Label As String
                Get
                    Dim parts As New List(Of String) From {Artist & " – " & Title}
                    If Year > 0 Then parts.Add(Year.ToString(CultureInfo.InvariantCulture))
                    If Country.Length > 0 Then parts.Add(Country)
                    If Disambiguation.Length > 0 Then parts.Add("[" & Disambiguation & "]")
                    parts.Add(LocalizationService.Format("{0} Titel", Tracks.Count))
                    Return String.Join("  ·  ", parts)
                End Get
            End Property
        End Class

        ''' <summary>Die Disc-Kennung aus dem Inhaltsverzeichnis, nach der Vorschrift von
        ''' MusicBrainz (dieselbe wie in libdiscid).
        '''
        ''' <para>Gebildet wird eine Zeichenkette aus erster Spur, letzter Spur und 100 Adressen zu
        ''' je acht Hexstellen: an Stelle 0 das Lead-out, danach die Spuren 1 bis 99, fehlende als
        ''' Null. Auf jede Adresse kommen 150 Bloecke - der Vorlauf, den die CD-Norm vor der ersten
        ''' Spur vorsieht. Darueber SHA-1, und das Ergebnis in Base64 mit drei ausgetauschten
        ''' Zeichen, damit es in eine Adresse passt.</para>
        '''
        ''' <para>Jede Abweichung davon ergibt eine Kennung, die zu nichts passt - deshalb steht
        ''' die Vorschrift hier so ausfuehrlich.</para></summary>
        Public Shared Function DiscIdOf(toc As AudioCdService.DiscToc) As String
            If toc Is Nothing OrElse toc.FirstTrack <= 0 OrElse toc.LastTrack < toc.FirstTrack Then Return String.Empty

            Dim builder As New StringBuilder()
            builder.Append(toc.FirstTrack.ToString("X2", CultureInfo.InvariantCulture))
            builder.Append(toc.LastTrack.ToString("X2", CultureInfo.InvariantCulture))
            builder.Append((toc.LeadOutLba + 150).ToString("X8", CultureInfo.InvariantCulture))
            For number = 1 To 99
                Dim lba As Integer
                If toc.StartLba.TryGetValue(number, lba) Then
                    builder.Append((lba + 150).ToString("X8", CultureInfo.InvariantCulture))
                Else
                    builder.Append("00000000")
                End If
            Next

            Using sha = SHA1.Create()
                Dim hash = sha.ComputeHash(Encoding.ASCII.GetBytes(builder.ToString()))
                Return Convert.ToBase64String(hash).Replace("+"c, "."c).Replace("/"c, "_"c).Replace("="c, "-"c)
            End Using
        End Function

        ''' <summary>Fragt die Ausgaben zu einer Disc-Kennung ab. Eine leere Liste heisst: die CD
        ''' ist dort nicht verzeichnet - kein Fehler, das ist bei seltenen Pressungen normal.</summary>
        Public Shared Async Function LookupAsync(discId As String, cancellationToken As CancellationToken) As Task(Of List(Of DiscRelease))
            Dim releases As New List(Of DiscRelease)()
            If String.IsNullOrWhiteSpace(discId) Then Return releases

            Dim url = "https://musicbrainz.org/ws/2/discid/" & Uri.EscapeDataString(discId) &
                      "?fmt=json&inc=artist-credits+recordings"
            Using response = Await Client.GetAsync(url, cancellationToken)
                ' 404 heisst schlicht "nicht verzeichnet", 400 "so eine Kennung gibt es nicht".
                ' Beides ist kein Fehler, ueber den jemand unterrichtet werden muesste - bei einer
                ' seltenen Pressung ist das erste der Normalfall.
                If response.StatusCode = Net.HttpStatusCode.NotFound OrElse
                   response.StatusCode = Net.HttpStatusCode.BadRequest Then Return releases
                response.EnsureSuccessStatusCode()
                Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync(cancellationToken))
                    Dim rows As JsonElement
                    If Not document.RootElement.TryGetProperty("releases", rows) OrElse rows.ValueKind <> JsonValueKind.Array Then Return releases
                    For Each row In rows.EnumerateArray()
                        Dim release = ReadRelease(row, discId)
                        If release IsNot Nothing AndAlso release.Tracks.Count > 0 Then releases.Add(release)
                    Next
                End Using
            End Using
            Return releases
        End Function

        Private Shared Function ReadRelease(row As JsonElement, discId As String) As DiscRelease
            Dim release As New DiscRelease With {
                .Id = Text(row, "id"),
                .Title = Text(row, "title"),
                .Date = Text(row, "date"),
                .Country = Text(row, "country"),
                .Disambiguation = Text(row, "disambiguation"),
                .Artist = ArtistCredit(row)}

            ' Eine Ausgabe kann mehrere Tontraeger haben (Doppelalbum). Gesucht ist DER, auf dem
            ' diese Disc-Kennung steht - sonst kaemen die Titel der falschen Scheibe heraus.
            Dim media As JsonElement
            If Not row.TryGetProperty("media", media) OrElse media.ValueKind <> JsonValueKind.Array Then Return Nothing
            Dim chosen As JsonElement = Nothing
            Dim found = False
            For Each medium In media.EnumerateArray()
                If Not MediumHasDisc(medium, discId) Then Continue For
                chosen = medium
                found = True
                Exit For
            Next
            ' Kennt die Antwort keine Zuordnung, bleibt der erste Tontraeger - besser als nichts,
            ' und bei einer einzelnen CD ist es ohnehin der richtige.
            If Not found Then
                For Each medium In media.EnumerateArray()
                    chosen = medium
                    found = True
                    Exit For
                Next
            End If
            If Not found Then Return Nothing

            Dim tracks As JsonElement
            If Not chosen.TryGetProperty("tracks", tracks) OrElse tracks.ValueKind <> JsonValueKind.Array Then Return Nothing
            For Each track In tracks.EnumerateArray()
                Dim number As Integer
                Integer.TryParse(Text(track, "position"), NumberStyles.Integer, CultureInfo.InvariantCulture, number)
                Dim title = Text(track, "title")
                Dim artist = ArtistCredit(track)
                ' Bei einem Sampler steht der Interpret am Titel, sonst nur an der Ausgabe.
                If artist.Length = 0 Then artist = release.Artist
                release.Tracks.Add(New DiscTrack With {.Number = number, .Title = title, .Artist = artist})
            Next
            Return release
        End Function

        Private Shared Function MediumHasDisc(medium As JsonElement, discId As String) As Boolean
            Dim discs As JsonElement
            If Not medium.TryGetProperty("discs", discs) OrElse discs.ValueKind <> JsonValueKind.Array Then Return False
            For Each disc In discs.EnumerateArray()
                If String.Equals(Text(disc, "id"), discId, StringComparison.Ordinal) Then Return True
            Next
            Return False
        End Function

        ''' <summary>Der Interpret aus "artist-credit". Die Liste kann mehrere Eintraege haben, die
        ''' mit "joinphrase" verbunden werden - "Simon &amp; Garfunkel" ist EIN Name aus zwei
        ''' Eintraegen.</summary>
        Private Shared Function ArtistCredit(row As JsonElement) As String
            Dim credits As JsonElement
            If Not row.TryGetProperty("artist-credit", credits) OrElse credits.ValueKind <> JsonValueKind.Array Then Return String.Empty
            Dim builder As New StringBuilder()
            For Each credit In credits.EnumerateArray()
                Dim name = Text(credit, "name")
                If name.Length = 0 Then
                    Dim artist As JsonElement
                    If credit.TryGetProperty("artist", artist) Then name = Text(artist, "name")
                End If
                builder.Append(name)
                builder.Append(Text(credit, "joinphrase"))
            Next
            Return builder.ToString().Trim()
        End Function

        Private Shared Function Text(element As JsonElement, name As String) As String
            Dim value As JsonElement
            If Not element.TryGetProperty(name, value) OrElse value.ValueKind = JsonValueKind.Null Then Return String.Empty
            Return If(value.ValueKind = JsonValueKind.String, value.GetString(), value.ToString())
        End Function

    End Class

End Namespace
