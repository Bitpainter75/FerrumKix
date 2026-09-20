Imports System
Imports System.IO
Imports FerrumKix.Models

Namespace Services

    ''' <summary>Liest die Kennzeichen einer Tondatei.
    '''
    ''' <para>Ein einziger Lesevorgang je Datei: TagLib oeffnet sie, liest Kennzeichen UND
    ''' Eigenschaften der Tonspur, und danach ist sie wieder zu. Die Anwendung fasst eine Datei im
    ''' Fotobestand nie zum Schreiben an, und hier gilt dasselbe - dieser Dienst oeffnet
    ''' ausschliesslich lesend.</para></summary>
    Public NotInheritable Class TagReadService
        Private Sub New()
        End Sub

        ''' <summary>Die Endungen, die die Anwendung als Tondatei annimmt. Was mpv sonst noch
        ''' abspielen koennte, ist gross; diese Liste ist das, was in einer Musiksammlung
        ''' vorkommt.</summary>
        Public Shared ReadOnly SupportedExtensions As String() = {
            ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".m4b", ".aac",
            ".wav", ".wv", ".ape", ".wma", ".mpc", ".aiff", ".aif", ".alac", ".dsf", ".mp2"
        }

        ''' <summary>Der Parameter heisst NICHT "path". VB unterscheidet keine Gross- und
        ''' Kleinschreibung, und ein so benannter Parameter verdeckt die Klasse
        ''' <see cref="IO.Path"/> im ganzen Rumpf. Das gilt in dieser Datei ueberall.</summary>
        Public Shared Function IsSupportedFile(filePath As String) As Boolean
            If String.IsNullOrWhiteSpace(filePath) Then Return False
            Dim extension As String
            Try
                extension = Path.GetExtension(filePath)
            Catch
                Return False
            End Try
            If String.IsNullOrEmpty(extension) Then Return False
            For Each candidate In SupportedExtensions
                If String.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        End Function

        ''' <summary>Liest eine Datei ein. Gibt auch dann einen Titel zurueck, wenn die Kennzeichen
        ''' fehlen oder beschaedigt sind: der Dateiname allein reicht fuer eine Zeile in der Liste,
        ''' und eine Datei wegzulassen, weil ihr Kennzeichen kaputt ist, waere die schlechtere
        ''' Antwort. Nothing kommt nur zurueck, wenn es die Datei nicht gibt.</summary>
        Public Shared Function Read(filePath As String) As Track
            If String.IsNullOrWhiteSpace(filePath) Then Return Nothing

            Dim info As FileInfo
            Try
                info = New FileInfo(filePath)
                If Not info.Exists Then Return Nothing
            Catch ex As Exception
                DiagnosticLogService.LogException("TagRead.FileInfo", ex)
                Return Nothing
            End Try

            Dim track As New Track With {
                .FilePath = filePath,
                .FileSize = info.Length,
                .TagsReadUtc = Date.UtcNow
            }

            Try
                Using file = TagLib.File.Create(filePath)
                    Dim tag = file.Tag
                    If tag IsNot Nothing Then
                        track.Title = CleanText(tag.Title)
                        track.Artist = CleanText(FirstOrJoined(tag.Performers, tag.AlbumArtists))
                        track.AlbumArtist = CleanText(FirstOrJoined(tag.AlbumArtists, tag.Performers))
                        track.Album = CleanText(tag.Album)
                        track.Genre = CleanText(FirstOrJoined(tag.Genres, Nothing))
                        track.Year = CInt(tag.Year)
                        track.TrackNumber = CInt(tag.Track)
                        track.DiscNumber = CInt(tag.Disc)
                        track.AlbumSortOrder = CleanText(tag.AlbumSort)
                    End If

                    Dim properties = file.Properties
                    If properties IsNot Nothing Then
                        track.DurationSeconds = properties.Duration.TotalSeconds
                        track.SampleRate = properties.AudioSampleRate
                        track.Bitrate = properties.AudioBitrate
                        track.Channels = properties.AudioChannels
                        track.Codec = DescribeCodec(properties, filePath)
                    End If
                End Using
            Catch ex As Exception
                ' Beschaedigte oder unbekannte Kennzeichen sind kein Grund, die Datei fallen zu
                ' lassen: mpv spielt sie trotzdem ab. Was fehlt, bleibt leer.
                DiagnosticLogService.Log("TagRead", $"{filePath}: {ex.Message}")
            End Try

            If String.IsNullOrEmpty(track.Codec) Then track.Codec = ExtensionLabel(filePath)
            Return track
        End Function

        ''' <summary>Der Name des Formats, wie ihn die Liste zeigt. TagLib nennt die Beschreibung
        ''' der Tonspur sehr ausfuehrlich ("MPEG Version 1 Audio, Layer 3"); daraus wird die
        ''' Kurzform, die auch auf eine Zeile passt.</summary>
        Private Shared Function DescribeCodec(properties As TagLib.Properties, filePath As String) As String
            Dim description As String = Nothing
            Try
                For Each codec In properties.Codecs
                    If codec Is Nothing Then Continue For
                    description = codec.Description
                    If Not String.IsNullOrWhiteSpace(description) Then Exit For
                Next
            Catch
            End Try

            If String.IsNullOrWhiteSpace(description) Then Return ExtensionLabel(filePath)

            Dim text = description.ToUpperInvariant()
            If text.Contains("LAYER 3") Then Return "MP3"
            If text.Contains("LAYER 2") Then Return "MP2"
            If text.Contains("FLAC") Then Return "FLAC"
            If text.Contains("VORBIS") Then Return "OGG"
            If text.Contains("OPUS") Then Return "OPUS"
            If text.Contains("AAC") Then Return "AAC"
            If text.Contains("ALAC") Then Return "ALAC"
            If text.Contains("WAVPACK") Then Return "WV"
            If text.Contains("MONKEY") Then Return "APE"
            Return ExtensionLabel(filePath)
        End Function

        Private Shared Function ExtensionLabel(filePath As String) As String
            Try
                Dim extension = Path.GetExtension(filePath)
                If String.IsNullOrEmpty(extension) Then Return String.Empty
                Return extension.TrimStart("."c).ToUpperInvariant()
            Catch
                Return String.Empty
            End Try
        End Function

        ''' <summary>Der erste Eintrag der bevorzugten Liste, sonst der erste der Ersatzliste.
        ''' Mehrere Interpreten werden mit Komma verbunden - abgeschnitten waere die Angabe
        ''' schlicht falsch.</summary>
        Private Shared Function FirstOrJoined(preferred As String(), fallback As String()) As String
            Dim value = JoinNonEmpty(preferred)
            If Not String.IsNullOrWhiteSpace(value) Then Return value
            Return JoinNonEmpty(fallback)
        End Function

        Private Shared Function JoinNonEmpty(values As String()) As String
            If values Is Nothing OrElse values.Length = 0 Then Return String.Empty
            Dim parts As New List(Of String)()
            For Each value In values
                If Not String.IsNullOrWhiteSpace(value) Then parts.Add(value.Trim())
            Next
            Return String.Join(", ", parts)
        End Function

        ''' <summary>Kennzeichen aus dem Netz tragen oft ein Null-Zeichen oder Leerraum am Ende mit
        ''' sich. Beides zerlegt spaeter die Anzeige, und keines davon ist gewollt.</summary>
        Private Shared Function CleanText(value As String) As String
            If String.IsNullOrEmpty(value) Then Return String.Empty
            Return value.Replace(ChrW(0), " "c).Trim()
        End Function

    End Class

End Namespace
