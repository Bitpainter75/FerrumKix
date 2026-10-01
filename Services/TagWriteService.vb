Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Globalization
Imports System.Linq
Imports SkiaSharp

Namespace Services

    ''' <summary>Schreibt den bewusst kleinen, albumorientierten Tag-Bestand von FerrumKix.
    '''
    ''' <para>Je Format genau EIN Tag, und zwar der, den die Player dieses Formats lesen: ID3v2 in
    ''' MP3, Vorbis Comments in FLAC, Ogg Vorbis und Opus, die iTunes-Atome in M4A. Die Felder
    ''' sind ueberall dieselben; nur wo sie liegen, unterscheidet sich.</para>
    '''
    ''' <para>Was nicht in <see cref="WritableExtensions"/> steht, fasst diese Klasse nicht an.
    ''' WAV und rohes AAC haben keinen Tag, den Player verlaesslich lesen - dort ein Kennzeichen
    ''' hineinzuschreiben saehe nach Erfolg aus und kaeme nirgends an.</para></summary>
    Public NotInheritable Class TagWriteService
        Private Sub New()
        End Sub

        ''' <summary>Die Endungen, deren Tags der Tag-Editor schreiben kann.</summary>
        Public Shared ReadOnly WritableExtensions As String() = {".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".m4b"}

        ''' <summary>Ob sich die Tags dieser Datei schreiben lassen. Entschieden wird allein an der
        ''' Endung: dieselbe Antwort braucht das Kontextmenue, bevor irgendetwas gelesen ist.</summary>
        Public Shared Function CanWrite(filePath As String) As Boolean
            If String.IsNullOrWhiteSpace(filePath) Then Return False
            Dim extension As String
            Try
                extension = Path.GetExtension(filePath)
            Catch
                Return False
            End Try
            Return WritableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
        End Function

        Public NotInheritable Class Values
            Public Property Artist As String = String.Empty
            Public Property AlbumArtist As String = String.Empty
            Public Property Album As String = String.Empty
            Public Property Year As Integer
            Public Property Genre As String = String.Empty
            Public Property AlbumSortOrder As String = String.Empty
            Public Property DiscNumber As Integer
            Public Property Title As String = String.Empty
            Public Property TrackNumber As Integer
            Public Property TotalTracks As Integer
            Public Property CoverSource As Byte()
        End Class

        ''' <summary>Schreibt die Kennzeichen und benennt die Datei nach dem Muster des Taggens
        ''' um. <paramref name="rename"/> schaltet das Umbenennen ab: der Konverter hat seinen
        ''' Namen aus seinem EIGENEN Muster schon vergeben, und das Muster des Taggens duerfte
        ''' ihn sonst gleich wieder ueberschreiben.</summary>
        Public Shared Function Write(filePath As String, values As Values, Optional rename As Boolean = True) As String
            If Not CanWrite(filePath) Then Throw New ArgumentException(LocalizationService.T("Dieses Dateiformat kann nicht getaggt werden."))
            If values Is Nothing Then Throw New ArgumentNullException(NameOf(values))
            Dim genre = SingleGenre(values.Genre)
            Dim isMp3 = String.Equals(Path.GetExtension(filePath), ".mp3", StringComparison.OrdinalIgnoreCase)
            Using file = TagLib.File.Create(filePath)
                ' Die konkrete Instanz des Format-Tags wird vollstaendig geleert, damit keine
                ' Spezialfelder (Kommentar, Bewertung, Lyrics, MusicBrainz usw.) still stehen bleiben.
                Dim tag = PrimaryTag(file)
                ' FLAC fuehrt Bilder in eigenen Bloecken neben den Vorbis Comments; nur die
                ' Sicht der ganzen Datei kommt an sie heran. Ogg und M4A tragen sie im Tag selbst.
                Dim pictureTag = If(TypeOf file Is TagLib.Flac.File, file.Tag, tag)
                ' Ohne neu gezogenes Bild bleibt das vorhandene Frontcover erhalten. Erst ein
                ' neues Bild ersetzt bewusst ALLE bisherigen Bilder durch genau dieses eine.
                Dim retainedCover = pictureTag.Pictures?.FirstOrDefault(Function(picture) picture IsNot Nothing AndAlso picture.Type = TagLib.PictureType.FrontCover)
                If retainedCover Is Nothing Then retainedCover = pictureTag.Pictures?.FirstOrDefault(Function(picture) picture IsNot Nothing)
                If AppSettingsService.Current.TagRemoveOtherFields Then
                    ' ID3 in einer FLAC-Datei ist nicht vorgesehen und wird dort nur von wenigen
                    ' Programmen gelesen - es bleibt nur der eine Tag des Formats stehen.
                    file.RemoveTags(TagLib.TagTypes.Id3v1 Or TagLib.TagTypes.Ape Or If(isMp3, TagLib.TagTypes.None, TagLib.TagTypes.Id3v2))
                    tag.Clear()
                End If
                tag.Title = Clean(values.Title)
                tag.Performers = One(Clean(values.Artist))
                tag.AlbumArtists = One(Clean(values.AlbumArtist))
                tag.Album = Clean(values.Album)
                tag.Year = CUInt(Math.Max(0, values.Year))
                tag.Genres = One(genre)
                tag.Track = CUInt(Math.Max(0, values.TrackNumber))
                ' Die abstrakte Track-Eigenschaft ist eine Zahl und verwirft führende Nullen.
                ' Der ID3v2-Rahmen TRCK und das Vorbis-Feld TRACKNUMBER sind Text; dort bewahren
                ' wir die gewünschte Darstellung. Das M4A-Atom kennt nur die Zahl.
                If AppSettingsService.Current.TagPadTrackNumberToAlbumLength AndAlso values.TrackNumber > 0 Then
                    Dim padded = FormatTrackNumber(values.TrackNumber, values.TotalTracks)
                    Dim id3 = TryCast(tag, TagLib.Id3v2.Tag)
                    If id3 IsNot Nothing Then TagLib.Id3v2.TextInformationFrame.Get(id3, "TRCK", True).Text = {padded}
                    TryCast(tag, TagLib.Ogg.XiphComment)?.SetField("TRACKNUMBER", padded)
                End If
                tag.Disc = CUInt(Math.Max(0, values.DiscNumber))
                tag.AlbumSort = Clean(values.AlbumSortOrder)
                If values.CoverSource IsNot Nothing AndAlso values.CoverSource.Length > 0 Then
                    Dim cover As New TagLib.Picture(New TagLib.ByteVector(ResizeCover(values.CoverSource))) With {
                        .Type = TagLib.PictureType.FrontCover,
                        .MimeType = "image/jpeg"}
                    pictureTag.Pictures = {cover}
                ElseIf retainedCover IsNot Nothing AndAlso AppSettingsService.Current.TagRemoveOtherFields Then
                    pictureTag.Pictures = {retainedCover}
                End If
                file.Save()
            End Using
            Dim target = If(rename, RenameForTags(filePath, values), filePath)
            ' Die Datei hat jetzt ein anderes Bild - und moeglicherweise einen anderen Namen. Beide
            ' Pfade muessen aus dem Zwischenspeicher, sonst zeigt die Anwendung weiter das alte
            ' Cover samt seinem alten Mass.
            CoverArtService.Invalidate(filePath, target)
            Return target
        End Function

        ''' <summary>Der eine Tag, den dieses Format traegt - angelegt, falls er noch fehlt.</summary>
        Private Shared Function PrimaryTag(file As TagLib.File) As TagLib.Tag
            Dim type = If(TypeOf file Is TagLib.Mpeg.AudioFile, TagLib.TagTypes.Id3v2,
                       If(TypeOf file Is TagLib.Mpeg4.File, TagLib.TagTypes.Apple, TagLib.TagTypes.Xiph))
            Dim tag = file.GetTag(type, True)
            If tag Is Nothing Then Throw New InvalidDataException(LocalizationService.T("Dieses Dateiformat kann nicht getaggt werden."))
            Return tag
        End Function

        ''' <summary>Bringt ein Coverbild auf die eingestellte Kantenlaenge. Das Seitenverhaeltnis
        ''' bleibt: ein eingescanntes Booklet ist selten quadratisch und stuende sonst gestaucht im
        ''' Tag. Ein bereits kleineres Bild wird NICHT hochgerechnet - das brachte nur Dateigroesse
        ''' und keinen einzigen Bildpunkt mehr.</summary>
        ''' <summary>Fuer den Konverter: dasselbe Verkleinern wie beim Taggen, damit ein
        ''' eingebettetes Titelbild ueberall dieselbe eingestellte Kantenlaenge hat.</summary>
        Friend Shared Function ScaleCoverToSetting(source As Byte()) As Byte()
            Return ResizeCover(source)
        End Function

        Private Shared Function ResizeCover(source As Byte()) As Byte()
            Dim size = AppSettingsService.Current.TagCoverSize
            Using input = SKBitmap.Decode(source)
                If input Is Nothing Then Throw New InvalidDataException(LocalizationService.T("Das Coverbild konnte nicht gelesen werden."))
                Dim scale = Math.Min(1.0, Math.Min(size / CDbl(Math.Max(1, input.Width)), size / CDbl(Math.Max(1, input.Height))))
                Dim width = Math.Max(1, CInt(Math.Round(input.Width * scale)))
                Dim height = Math.Max(1, CInt(Math.Round(input.Height * scale)))
                If width = input.Width AndAlso height = input.Height Then Return Encode(input)
                Using resized = input.Resize(New SKImageInfo(width, height), New SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None))
                    If resized Is Nothing Then Throw New InvalidDataException(LocalizationService.T("Das Coverbild konnte nicht skaliert werden."))
                    Return Encode(resized)
                End Using
            End Using
        End Function

        Private Shared Function Encode(bitmap As SKBitmap) As Byte()
            Using image = SKImage.FromBitmap(bitmap), data = image.Encode(SKEncodedImageFormat.Jpeg, AppSettingsService.Current.TagCoverJpegQuality)
                Return data.ToArray()
            End Using
        End Function

        Private Shared Function RenameForTags(filePath As String, values As Values) As String
            Dim name = BuildFileName(AppSettingsService.Current.TagFileNamePattern, values)
            ' Ein Muster, das nur aus leeren Platzhaltern besteht, darf keine Datei namens ".mp3"
            ' erzeugen. Dann bleibt der bisherige Name stehen.
            If name.Length = 0 Then Return filePath
            Dim target = Path.Combine(Path.GetDirectoryName(filePath), name & Path.GetExtension(filePath))
            If String.Equals(target, filePath, StringComparison.Ordinal) Then Return filePath
            If File.Exists(target) Then Throw New IOException(LocalizationService.Format("Zieldatei existiert bereits: {0}", Path.GetFileName(target)))
            File.Move(filePath, target)
            Return target
        End Function

        ''' <summary>Die Platzhalter, die ein Benennungsmuster kennt. Die Schreibweise folgt
        ''' Puddletag, damit ein dort erprobtes Muster hier weiterverwendet werden kann.</summary>
        Public Shared ReadOnly Property PlaceholderNames As String() = {
            "%artist%", "%albumartist%", "%album%", "%title%",
            "%track%", "%totaltracks%", "%disc%", "%year%", "%genre%"}

        ''' <summary>Setzt ein Benennungsmuster in einen Dateinamen ohne Endung um. Wird beim
        ''' Speichern und fuer die Vorschau in den Einstellungen verwendet, damit die Vorschau nie
        ''' etwas anderes zeigt als das spaetere Ergebnis.</summary>
        Public Shared Function BuildFileName(pattern As String, values As Values) As String
            Return BuildFileName(pattern, values, AppSettingsService.Current.TagPadTrackNumberToAlbumLength)
        End Function

        ''' <summary>Dasselbe mit ausdruecklich gewaehltem Auffuellen der Tracknummer. Der
        ''' Konverter hat dafuer eine eigene Einstellung und darf nicht die des Taggens lesen.</summary>
        Public Shared Function BuildFileName(pattern As String, values As Values, padTrackNumber As Boolean) As String
            If values Is Nothing Then Throw New ArgumentNullException(NameOf(values))
            Dim text = AppSettingsService.NormalizeTagFileNamePattern(pattern)
            For Each name In PlaceholderNames
                text = text.Replace(name, TextFor(name, values, padTrackNumber), StringComparison.OrdinalIgnoreCase)
            Next
            Return Sanitize(text)
        End Function

        ''' <summary>Der Wert eines Platzhalters. Die Tracknummer bekommt ihre fuehrenden Nullen
        ''' aus der Einstellung dazu, alles andere steht so da, wie es im Tag steht. Ein
        ''' unbekannter Name bleibt stehen: wer ihn eintippt, meinte vermutlich diesen Text.</summary>
        Private Shared Function TextFor(name As String, values As Values, padTrackNumber As Boolean) As String
            Select Case name.ToLowerInvariant()
                Case "%artist%" : Return Clean(values.Artist)
                Case "%albumartist%" : Return Clean(values.AlbumArtist)
                Case "%album%" : Return Clean(values.Album)
                Case "%title%" : Return Clean(values.Title)
                Case "%genre%" : Return SingleGenre(values.Genre)
                ' Eine fehlende Zahl ist keine Null, sondern nichts: sonst hiesse eine Datei
                ' ohne Nummer "00 - Titel" und eine ohne Disc-Angabe traege eine "0" im Namen.
                ' %year% haelt es seit jeher so; die drei Zahlen tun es jetzt ebenso.
                Case "%track%" : Return If(values.TrackNumber > 0, FormatTrackNumber(values.TrackNumber, values.TotalTracks, padTrackNumber), String.Empty)
                Case "%totaltracks%" : Return If(values.TotalTracks > 0, values.TotalTracks.ToString(CultureInfo.InvariantCulture), String.Empty)
                Case "%disc%" : Return If(values.DiscNumber > 0, values.DiscNumber.ToString(CultureInfo.InvariantCulture), String.Empty)
                Case "%year%" : Return If(values.Year > 0, values.Year.ToString(CultureInfo.InvariantCulture), String.Empty)
                Case Else : Return name
            End Select
        End Function

        ''' <summary>Die Tracknummer als Text, wie sie im Tag, im Dateinamen und in den
        ''' Eingabefeldern erscheint. Ist das Auffuellen eingeschaltet, richtet sich die
        ''' Stellenzahl nach der Titelzahl des Albums: 01 bei zehn Titeln, 001 bei hundert.
        ''' An einer Stelle entschieden, damit Feld, Tag und Dateiname nie auseinanderlaufen.</summary>
        Public Shared Function FormatTrackNumber(number As Integer, totalTracks As Integer) As String
            Return FormatTrackNumber(number, totalTracks, AppSettingsService.Current.TagPadTrackNumberToAlbumLength)
        End Function

        ''' <summary>Dieselbe Darstellung mit ausdruecklich gewaehltem Auffuellen, fuer den
        ''' Konverter und seine eigene Einstellung.</summary>
        Public Shared Function FormatTrackNumber(number As Integer, totalTracks As Integer, pad As Boolean) As String
            Dim value = Math.Max(0, number)
            If Not pad Then Return value.ToString(CultureInfo.InvariantCulture)
            Dim digits = Math.Max(1, Math.Max(totalTracks, value)).ToString(CultureInfo.InvariantCulture).Length
            Return value.ToString(New String("0"c, digits), CultureInfo.InvariantCulture)
        End Function

        ''' <summary>Macht aus dem ausgefuellten Muster einen Namen, den das Dateisystem annimmt.</summary>
        Private Shared Function Sanitize(name As String) As String
            Dim text = If(name, String.Empty)
            For Each bad In Path.GetInvalidFileNameChars() : text = text.Replace(bad, " "c) : Next
            ' Ein leer gebliebener Platzhalter hinterlaesst sonst doppelte Leerzeichen im Namen.
            While text.Contains("  ") : text = text.Replace("  ", " ") : End While
            Return text.Trim().Trim("-"c, "_"c, "."c).Trim()
        End Function

        Private Shared Function One(value As String) As String()
            Return If(String.IsNullOrWhiteSpace(value), Array.Empty(Of String)(), {value})
        End Function
        Private Shared Function Clean(value As String) As String
            Return If(value, String.Empty).Replace(ChrW(0), " "c).Trim()
        End Function
        Private Shared Function SingleGenre(value As String) As String
            Return Clean(value).Replace("//", " ").Replace("/", " ").Trim()
        End Function
    End Class
End Namespace
