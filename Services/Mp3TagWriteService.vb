Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Globalization
Imports System.Linq
Imports SkiaSharp

Namespace Services

    ''' <summary>Schreibt den bewusst kleinen, albumorientierten ID3v2-Bestand von FerrumPlay.
    ''' Diese Klasse fasst ausschliesslich MP3 an; andere Dateiformate werden nie implizit
    ''' konvertiert oder umgetaggt.</summary>
    Public NotInheritable Class Mp3TagWriteService
        Private Sub New()
        End Sub

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

        Public Shared Function Write(filePath As String, values As Values) As String
            If String.IsNullOrWhiteSpace(filePath) OrElse Not String.Equals(Path.GetExtension(filePath), ".mp3", StringComparison.OrdinalIgnoreCase) Then Throw New ArgumentException(LocalizationService.T("Nur MP3-Dateien können getaggt werden."))
            If values Is Nothing Then Throw New ArgumentNullException(NameOf(values))
            Dim genre = SingleGenre(values.Genre)
            Using file = TagLib.File.Create(filePath)
                ' Die konkrete ID3v2-Instanz wird vollstaendig geleert, damit keine Spezialrahmen
                ' (Kommentar, Bewertung, Lyrics, MusicBrainz usw.) still stehen bleiben.
                Dim tag = DirectCast(file.GetTag(TagLib.TagTypes.Id3v2, True), TagLib.Id3v2.Tag)
                ' Ohne neu gezogenes Bild bleibt das vorhandene Frontcover erhalten. Erst ein
                ' neues Bild ersetzt bewusst ALLE bisherigen Bilder durch genau dieses eine.
                Dim retainedCover = tag.Pictures?.FirstOrDefault(Function(picture) picture IsNot Nothing AndAlso picture.Type = TagLib.PictureType.FrontCover)
                If retainedCover Is Nothing Then retainedCover = tag.Pictures?.FirstOrDefault(Function(picture) picture IsNot Nothing)
                If AppSettingsService.Current.TagRemoveOtherFields Then
                    file.RemoveTags(TagLib.TagTypes.Id3v1 Or TagLib.TagTypes.Ape)
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
                ' Der ID3v2-Rahmen TRCK ist Text; dort bewahren wir die gewünschte Darstellung.
                If AppSettingsService.Current.TagPadTrackNumberToAlbumLength AndAlso values.TrackNumber > 0 Then
                    TagLib.Id3v2.TextInformationFrame.Get(tag, "TRCK", True).Text = {FormatTrackNumber(values.TrackNumber, values.TotalTracks)}
                End If
                tag.Disc = CUInt(Math.Max(0, values.DiscNumber))
                tag.AlbumSort = Clean(values.AlbumSortOrder)
                If values.CoverSource IsNot Nothing AndAlso values.CoverSource.Length > 0 Then
                    tag.Pictures = {New TagLib.Picture(New TagLib.ByteVector(ResizeCover(values.CoverSource)))}
                    tag.Pictures(0).Type = TagLib.PictureType.FrontCover
                    tag.Pictures(0).MimeType = "image/jpeg"
                ElseIf retainedCover IsNot Nothing AndAlso AppSettingsService.Current.TagRemoveOtherFields Then
                    tag.Pictures = {retainedCover}
                End If
                file.Save()
            End Using
            Dim target = RenameForTags(filePath, values)
            ' Die Datei hat jetzt ein anderes Bild - und moeglicherweise einen anderen Namen. Beide
            ' Pfade muessen aus dem Zwischenspeicher, sonst zeigt die Anwendung weiter das alte
            ' Cover samt seinem alten Mass.
            CoverArtService.Invalidate(filePath, target)
            Return target
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
            Dim target = Path.Combine(Path.GetDirectoryName(filePath), name & ".mp3")
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
            If values Is Nothing Then Throw New ArgumentNullException(NameOf(values))
            Dim text = AppSettingsService.NormalizeTagFileNamePattern(pattern)
            For Each name In PlaceholderNames
                text = text.Replace(name, TextFor(name, values), StringComparison.OrdinalIgnoreCase)
            Next
            Return Sanitize(text)
        End Function

        ''' <summary>Der Wert eines Platzhalters. Die Tracknummer bekommt ihre fuehrenden Nullen
        ''' aus der Einstellung dazu, alles andere steht so da, wie es im Tag steht. Ein
        ''' unbekannter Name bleibt stehen: wer ihn eintippt, meinte vermutlich diesen Text.</summary>
        Private Shared Function TextFor(name As String, values As Values) As String
            Select Case name.ToLowerInvariant()
                Case "%artist%" : Return Clean(values.Artist)
                Case "%albumartist%" : Return Clean(values.AlbumArtist)
                Case "%album%" : Return Clean(values.Album)
                Case "%title%" : Return Clean(values.Title)
                Case "%genre%" : Return SingleGenre(values.Genre)
                Case "%track%" : Return FormatTrackNumber(values.TrackNumber, values.TotalTracks)
                Case "%totaltracks%" : Return Math.Max(0, values.TotalTracks).ToString(CultureInfo.InvariantCulture)
                Case "%disc%" : Return Math.Max(0, values.DiscNumber).ToString(CultureInfo.InvariantCulture)
                Case "%year%" : Return If(values.Year > 0, values.Year.ToString(CultureInfo.InvariantCulture), String.Empty)
                Case Else : Return name
            End Select
        End Function

        ''' <summary>Die Tracknummer als Text, wie sie im Tag, im Dateinamen und in den
        ''' Eingabefeldern erscheint. Ist das Auffuellen eingeschaltet, richtet sich die
        ''' Stellenzahl nach der Titelzahl des Albums: 01 bei zehn Titeln, 001 bei hundert.
        ''' An einer Stelle entschieden, damit Feld, Tag und Dateiname nie auseinanderlaufen.</summary>
        Public Shared Function FormatTrackNumber(number As Integer, totalTracks As Integer) As String
            Dim value = Math.Max(0, number)
            If Not AppSettingsService.Current.TagPadTrackNumberToAlbumLength Then Return value.ToString(CultureInfo.InvariantCulture)
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
