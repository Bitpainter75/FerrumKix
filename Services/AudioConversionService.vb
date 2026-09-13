Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports FerrumPlay.Models

Namespace Services

    ''' <summary>Konvertiert lokale Titel mit den vom System bereitgestellten FFmpeg-Werkzeugen.
    ''' FFmpeg bleibt absichtlich ausserhalb des AppImage: Distributionen pflegen damit ihre
    ''' Codec-Auswahl und Sicherheitsupdates selbst, genau wie bei libmpv.</summary>
    Public NotInheritable Class AudioConversionService
        Private Sub New()
        End Sub

        Public Enum OutputFormat
            Mp3
            Flac
            Ogg
        End Enum

        ''' <summary>Entspricht den vertrauten Batch-Konvertierungsmodi: Quelle, gesamte Auswahl
        ''' oder jeweils ein Ordner. Ein CUE beschreibt dabei das neu erzeugte Gesamtwerk.</summary>
        Public Enum ConversionMode
            OneResultPerSource
            AllSourcesOneResult
            AllSourcesOneResultWithCue
            OneResultPerFolder
            OneResultPerFolderWithCue
        End Enum

        Public NotInheritable Class Request
            Public Property Tracks As IReadOnlyList(Of Track)
            Public Property OutputDirectory As String = String.Empty
            Public Property Format As OutputFormat = OutputFormat.Mp3
            Public Property BitrateKbps As Integer = 192
            Public Property VariableBitrate As Boolean
            Public Property Mode As ConversionMode = ConversionMode.OneResultPerSource
            Public Property SplitExistingCue As Boolean
            ''' <summary>Status je Ursprungszeile fuer die Warteschlange der Oberfläche. -1 steht
            ''' fuer einen Sammellauf, dessen Ergebnis mehrere Zeilen umfasst.</summary>
            Public Property ItemProgress As Action(Of Integer, String)
        End Class

        Public Shared Function IsFfmpegAvailable() As Boolean
            Return CanStart("ffmpeg", "-version")
        End Function

        Public Shared Function IsCdParanoiaAvailable() As Boolean
            Return CanStart("cdparanoia", "--version")
        End Function

        Public Shared Async Function ConvertAsync(request As Request, progress As IProgress(Of String), cancellationToken As CancellationToken) As Task
            If request Is Nothing OrElse request.Tracks Is Nothing OrElse request.Tracks.Count = 0 Then Throw New ArgumentException(LocalizationService.T("Keine Titel zum Konvertieren ausgewählt."))
            If String.IsNullOrWhiteSpace(request.OutputDirectory) Then Throw New ArgumentException(LocalizationService.T("Kein Zielordner gewählt."))
            If Not IsFfmpegAvailable() Then Throw New InvalidOperationException(LocalizationService.T("FFmpeg wurde nicht gefunden. Bitte installiere das Paket 'ffmpeg'."))
            Directory.CreateDirectory(request.OutputDirectory)

            Dim tracks = request.Tracks.Where(Function(track) track IsNot Nothing).OrderBy(Function(track) track.DiscNumber).ThenBy(Function(track) track.TrackNumber).ThenBy(Function(track) track.FilePath, StringComparer.OrdinalIgnoreCase).ToList()
            Select Case request.Mode
                Case ConversionMode.AllSourcesOneResult, ConversionMode.AllSourcesOneResultWithCue
                    progress?.Report(LocalizationService.T("Titel werden zusammengeführt …"))
                    request.ItemProgress?.Invoke(0, LocalizationService.T("Wird zusammengeführt"))
                    Await ConvertMergedAsync(tracks, request, progress, cancellationToken, request.Mode = ConversionMode.AllSourcesOneResultWithCue)
                    For index = 0 To tracks.Count - 1 : request.ItemProgress?.Invoke(index, LocalizationService.T("Fertig")) : Next
                    Return
                Case ConversionMode.OneResultPerFolder, ConversionMode.OneResultPerFolderWithCue
                    Dim folders = tracks.GroupBy(Function(track) track.FolderPath, StringComparer.OrdinalIgnoreCase).ToList()
                    For index = 0 To folders.Count - 1
                        progress?.Report(LocalizationService.Format("Konvertiere Ordner {0} von {1} …", index + 1, folders.Count))
                        Dim firstIndex = tracks.IndexOf(folders(index).First())
                        request.ItemProgress?.Invoke(firstIndex, LocalizationService.T("Wird zusammengeführt"))
                        Await ConvertMergedAsync(folders(index).ToList(), request, progress, cancellationToken, request.Mode = ConversionMode.OneResultPerFolderWithCue)
                        For Each track In folders(index) : request.ItemProgress?.Invoke(tracks.IndexOf(track), LocalizationService.T("Fertig")) : Next
                    Next
                    Return
            End Select

            If request.SplitExistingCue AndAlso tracks.Count = 1 AndAlso Not tracks(0).IsAudioCdTrack Then
                Dim cue = Path.ChangeExtension(tracks(0).FilePath, ".cue")
                If File.Exists(cue) Then
                    Await ConvertCueAsync(tracks(0), cue, request, progress, cancellationToken)
                    Return
                End If
            End If

            For index = 0 To tracks.Count - 1
                cancellationToken.ThrowIfCancellationRequested()
                Dim track = tracks(index)
                progress?.Report(LocalizationService.Format("Konvertiere {0} von {1}: {2}", index + 1, tracks.Count, track.ShortTitle))
                request.ItemProgress?.Invoke(index, LocalizationService.T("Konvertiert"))
                Dim input = Await GetInputAsync(track, request.OutputDirectory, cancellationToken)
                Try
                    Dim target = UniquePath(request.OutputDirectory, SafeFileName($"{TrackPrefix(track)}{track.DisplayTitle}") & ExtensionFor(request.Format))
                    ' Bei einer Audio-CD traegt die Zwischendatei keine Kennzeichen; sie kommen aus
                    ' dem Titel, den die Erkennung gefuellt hat.
                    Await RunFfmpegAsync(input, target, request, Nothing, Nothing, cancellationToken, tags:=track)
                    request.ItemProgress?.Invoke(index, LocalizationService.T("Fertig"))
                Finally
                    DeleteTemporaryCdWav(input)
                End Try
            Next
        End Function

        ''' <summary>Schreibt Titel, Interpret, Album und Nummer in die Zieldatei.
        '''
        ''' <para>Nur, was wirklich dasteht: ein leeres <c>-metadata</c> loeschte den Wert, den die
        ''' Quelle vielleicht schon mitbringt. Und "Titel 07" oder "Audio-CD" sind Platzhalter und
        ''' keine Angaben - sie werden ausgelassen, damit eine nicht erkannte CD keine falschen
        ''' Kennzeichen bekommt.</para></summary>
        Private Shared Sub AddMetadata(psi As ProcessStartInfo, track As Track)
            If track Is Nothing Then Return
            Add(psi, "title", track.Title, track.IsAudioCdTrack)
            Add(psi, "artist", track.Artist, False)
            Add(psi, "album", track.Album, track.IsAudioCdTrack)
            Add(psi, "album_artist", track.AlbumArtist, False)
            If track.TrackNumber > 0 Then
                psi.ArgumentList.Add("-metadata") : psi.ArgumentList.Add("track=" & track.TrackNumber.ToString(CultureInfo.InvariantCulture))
            End If
            If track.Year > 0 Then
                psi.ArgumentList.Add("-metadata") : psi.ArgumentList.Add("date=" & track.Year.ToString(CultureInfo.InvariantCulture))
            End If
            If Not String.IsNullOrWhiteSpace(track.Genre) Then
                psi.ArgumentList.Add("-metadata") : psi.ArgumentList.Add("genre=" & track.Genre.Trim())
            End If
        End Sub

        Private Shared Sub Add(psi As ProcessStartInfo, name As String, value As String, guardPlaceholder As Boolean)
            If String.IsNullOrWhiteSpace(value) Then Return
            If guardPlaceholder AndAlso IsCdPlaceholder(value) Then Return
            psi.ArgumentList.Add("-metadata") : psi.ArgumentList.Add(name & "=" & value.Trim())
        End Sub

        ''' <summary>Die Ersatztexte, die AudioCdService vergibt, solange nichts erkannt ist:
        ''' "Titel 07" und "Audio-CD". Sie sind bewusst NICHT uebersetzt - sie sind eine Marke und
        ''' kein Text fuer den Nutzer, und nur deshalb laesst sich hier verlaesslich erkennen, dass
        ''' nichts dasteht, was in die Kennzeichen gehoert.</summary>
        Private Shared Function IsCdPlaceholder(value As String) As Boolean
            Dim trimmed = value.Trim()
            If String.Equals(trimmed, "Audio-CD", StringComparison.Ordinal) Then Return True
            If Not trimmed.StartsWith("Titel ", StringComparison.Ordinal) Then Return False
            Dim rest = trimmed.Substring("Titel ".Length)
            Return rest.Length > 0 AndAlso rest.All(AddressOf Char.IsDigit)
        End Function

        Private Shared Sub DeleteTemporaryCdWav(path As String)
            If String.IsNullOrWhiteSpace(path) OrElse Not path.StartsWith(IO.Path.GetTempPath(), StringComparison.Ordinal) Then Return
            Try
                If File.Exists(path) Then File.Delete(path)
            Catch
            End Try
        End Sub

        Private Shared Async Function ConvertMergedAsync(tracks As List(Of Track), request As Request, progress As IProgress(Of String), cancellationToken As CancellationToken, writeCueFile As Boolean) As Task
            If tracks.Count = 0 Then Return
            If tracks.Any(Function(track) track.IsAudioCdTrack) Then Throw New InvalidOperationException(LocalizationService.T("Audio-CD-Titel werden einzeln gerippt. Bitte den Modus 'Eine Quelle – ein Ergebnis' wählen."))
            Dim listPath = Path.Combine(Path.GetTempPath(), $"ferrumplay-concat-{Guid.NewGuid():N}.txt")
            Try
                Dim lines = tracks.Select(Function(track) "file '" & track.FilePath.Replace("'", "'\\''") & "'")
                ' Der concat-Demuxer erwartet sein erstes Schlüsselwort bytegenau als "file".
                ' Ein UTF-8-BOM würde davor unsichtbar U+FEFF schreiben und FFmpeg lehnt die
                ' Liste dann mit "unknown keyword" ab.
                Await File.WriteAllLinesAsync(listPath, lines, New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False), cancellationToken)
                Dim stem = If(Not String.IsNullOrWhiteSpace(tracks(0).Album), tracks(0).Album, If(Not String.IsNullOrWhiteSpace(tracks(0).FolderPath), Path.GetFileName(tracks(0).FolderPath), "Zusammengeführt"))
                Dim target = UniquePath(request.OutputDirectory, SafeFileName(stem) & ExtensionFor(request.Format))
                Await RunFfmpegAsync(listPath, target, request, Nothing, Nothing, cancellationToken, isConcatList:=True)
                If writeCueFile Then WriteCue(target, tracks)
            Finally
                Try
                    If File.Exists(listPath) Then File.Delete(listPath)
                Catch
                End Try
            End Try
        End Function

        Private Shared Sub WriteCue(audioPath As String, tracks As List(Of Track))
            Dim cuePath = Path.ChangeExtension(audioPath, ".cue"), elapsed As Double = 0
            Using writer As New StreamWriter(cuePath, append:=False, Encoding.UTF8)
                writer.WriteLine($"FILE ""{Path.GetFileName(audioPath)}"" {If(Path.GetExtension(audioPath).Equals(".flac", StringComparison.OrdinalIgnoreCase), "WAVE", "MP3")}")
                For index = 0 To tracks.Count - 1
                    Dim track = tracks(index)
                    Dim frameTotal = CInt(Math.Max(0, Math.Floor(elapsed * 75)))
                    writer.WriteLine($"  TRACK {index + 1:00} AUDIO")
                    writer.WriteLine($"    TITLE ""{track.ShortTitle.Replace("""", "'")}""")
                    If Not String.IsNullOrWhiteSpace(track.Artist) Then writer.WriteLine($"    PERFORMER ""{track.Artist.Replace("""", "'")}""")
                    writer.WriteLine($"    INDEX 01 {frameTotal \ 75 \ 60:00}:{(frameTotal \ 75) Mod 60:00}:{frameTotal Mod 75:00}")
                    elapsed += Math.Max(0, track.DurationSeconds)
                Next
            End Using
        End Sub

        ' Eine vorhandene CUE neben einer grossen FLAC/APE-Datei bedeutet das Gegenteil von
        ' "mit CUE": sie wird in physische Einzeldateien aufgeteilt.
        Private Shared Async Function ConvertCueAsync(source As Track, cuePath As String, request As Request, progress As IProgress(Of String), cancellationToken As CancellationToken) As Task
            Dim entries = ParseCue(cuePath)
            If entries.Count = 0 Then Throw New InvalidOperationException(LocalizationService.T("Die CUE-Datei enthält keine verwertbaren INDEX-01-Einträge."))
            Dim total = entries.Count
            For index = 0 To total - 1
                cancellationToken.ThrowIfCancellationRequested()
                Dim entry = entries(index)
                Dim endSeconds As Double? = If(index + 1 < total, entries(index + 1).StartSeconds, Nothing)
                progress?.Report(LocalizationService.Format("Teile CUE {0} von {1}: {2}", index + 1, total, entry.Title))
                Dim target = UniquePath(request.OutputDirectory, SafeFileName($"{entry.Number:00} - {entry.Title}") & ExtensionFor(request.Format))
                Await RunFfmpegAsync(source.FilePath, target, request, entry.StartSeconds, endSeconds, cancellationToken)
            Next
        End Function

        ' Der alte Pfad blieb hier bewusst nicht stehen: die Entscheidung ob zusammengeführt
        ' wird, liegt jetzt ausschliesslich im ConversionMode und ist fuer die Oberfläche lesbar.
        Private Shared Async Function UnusedLegacyPath() As Task
            Await Task.CompletedTask
        End Function

        Private Shared Async Function GetInputAsync(track As Track, outputDirectory As String, cancellationToken As CancellationToken) As Task(Of String)
            If Not track.IsAudioCdTrack Then Return track.FilePath
            If Not IsCdParanoiaAvailable() Then Throw New InvalidOperationException(LocalizationService.T("Zum Rippen einer Audio-CD fehlt 'cdparanoia'."))
            Dim device As String = Nothing, number As Integer, last As Integer
            If Not Track.TryGetAudioCdSource(track.FilePath, device, number, last) Then Throw New InvalidOperationException(LocalizationService.T("Die Audio-CD-Quelle ist ungültig."))
            Dim temporary = Path.Combine(Path.GetTempPath(), $"ferrumplay-cd-{Guid.NewGuid():N}.wav")
            Dim psi As New ProcessStartInfo("cdparanoia") With {.RedirectStandardError = True, .RedirectStandardOutput = True, .UseShellExecute = False, .CreateNoWindow = True}
            psi.ArgumentList.Add("-d") : psi.ArgumentList.Add(device) : psi.ArgumentList.Add(number.ToString(CultureInfo.InvariantCulture)) : psi.ArgumentList.Add(temporary)
            Await RunProcessAsync(psi, cancellationToken)
            Return temporary
        End Function

        ''' <param name="tags">Die Angaben, die in die Zieldatei sollen. Nothing heisst: nehmen,
        ''' was in der Quelle steht. Bei einer Audio-CD steht dort NICHTS - CDDA kennt keine
        ''' Kennzeichen -, und ohne diesen Weg kaeme die gerippte Datei ohne Titel heraus, obwohl
        ''' die Erkennung ihn laengst ermittelt hat.</param>
        Private Shared Async Function RunFfmpegAsync(input As String, target As String, request As Request, startSeconds As Double?, endSeconds As Double?, cancellationToken As CancellationToken, Optional isConcatList As Boolean = False, Optional tags As Track = Nothing) As Task
            Dim psi As New ProcessStartInfo("ffmpeg") With {.RedirectStandardError = True, .RedirectStandardOutput = True, .UseShellExecute = False, .CreateNoWindow = True}
            psi.ArgumentList.Add("-hide_banner") : psi.ArgumentList.Add("-y")
            If isConcatList Then
                psi.ArgumentList.Add("-f") : psi.ArgumentList.Add("concat") : psi.ArgumentList.Add("-safe") : psi.ArgumentList.Add("0")
            End If
            If startSeconds.HasValue Then
                psi.ArgumentList.Add("-ss") : psi.ArgumentList.Add(startSeconds.Value.ToString("0.000", CultureInfo.InvariantCulture))
            End If
            psi.ArgumentList.Add("-i") : psi.ArgumentList.Add(input)
            If endSeconds.HasValue AndAlso startSeconds.HasValue Then
                psi.ArgumentList.Add("-t") : psi.ArgumentList.Add((endSeconds.Value - startSeconds.Value).ToString("0.000", CultureInfo.InvariantCulture))
            End If
            psi.ArgumentList.Add("-map_metadata") : psi.ArgumentList.Add("0") : psi.ArgumentList.Add("-map") : psi.ArgumentList.Add("0:a:0")
            AddMetadata(psi, tags)
            Select Case request.Format
                Case OutputFormat.Mp3
                    psi.ArgumentList.Add("-c:a") : psi.ArgumentList.Add("libmp3lame")
                    If request.VariableBitrate Then
                        psi.ArgumentList.Add("-q:a") : psi.ArgumentList.Add(If(request.BitrateKbps >= 256, "0", If(request.BitrateKbps >= 192, "2", "4")))
                    Else
                        psi.ArgumentList.Add("-b:a") : psi.ArgumentList.Add(request.BitrateKbps.ToString(CultureInfo.InvariantCulture) & "k")
                    End If
                Case OutputFormat.Flac
                    psi.ArgumentList.Add("-c:a") : psi.ArgumentList.Add("flac") : psi.ArgumentList.Add("-compression_level") : psi.ArgumentList.Add("8")
                Case OutputFormat.Ogg
                    psi.ArgumentList.Add("-c:a") : psi.ArgumentList.Add("libvorbis") : psi.ArgumentList.Add("-q:a") : psi.ArgumentList.Add(If(request.BitrateKbps >= 256, "7", If(request.BitrateKbps >= 192, "5", "3")))
            End Select
            psi.ArgumentList.Add(target)
            Await RunProcessAsync(psi, cancellationToken)
        End Function

        Private Shared Async Function RunProcessAsync(psi As ProcessStartInfo, cancellationToken As CancellationToken) As Task
            Using process As New Process With {.StartInfo = psi}
                process.Start()
                Dim errorTask = process.StandardError.ReadToEndAsync()
                Await process.WaitForExitAsync(cancellationToken)
                Dim errors = Await errorTask
                If process.ExitCode <> 0 Then Throw New InvalidOperationException(If(String.IsNullOrWhiteSpace(errors), $"{psi.FileName} wurde mit Fehlercode {process.ExitCode} beendet.", errors.Trim()))
            End Using
        End Function

        Private Shared Function CanStart(command As String, argument As String) As Boolean
            Try
                Using startedProcess As Process = System.Diagnostics.Process.Start(New ProcessStartInfo(command, argument) With {.RedirectStandardOutput = True, .RedirectStandardError = True, .UseShellExecute = False, .CreateNoWindow = True})
                    Return startedProcess IsNot Nothing
                End Using
            Catch : Return False : End Try
        End Function

        Private Shared Function ExtensionFor(format As OutputFormat) As String
            Return If(format = OutputFormat.Mp3, ".mp3", If(format = OutputFormat.Flac, ".flac", ".ogg"))
        End Function

        Private Shared Function TrackPrefix(track As Track) As String
            Return If(track.TrackNumber > 0, track.TrackNumber.ToString("00", CultureInfo.InvariantCulture) & " - ", String.Empty)
        End Function

        Private Shared Function SafeFileName(value As String) As String
            Dim result = value
            For Each invalid In Path.GetInvalidFileNameChars() : result = result.Replace(invalid, "_"c) : Next
            Return If(String.IsNullOrWhiteSpace(result), "Titel", result.Trim())
        End Function

        Private Shared Function UniquePath(folder As String, fileName As String) As String
            Dim result = Path.Combine(folder, fileName), index = 2
            While File.Exists(result)
                result = Path.Combine(folder, Path.GetFileNameWithoutExtension(fileName) & $" ({index})" & Path.GetExtension(fileName)) : index += 1
            End While
            Return result
        End Function

        Private NotInheritable Class CueEntry
            Public Property Number As Integer
            Public Property Title As String = "Titel"
            Public Property StartSeconds As Double
        End Class

        Private Shared Function ParseCue(path As String) As List(Of CueEntry)
            Dim result As New List(Of CueEntry)(), current As CueEntry = Nothing
            For Each line In File.ReadLines(path)
                Dim value = line.Trim()
                If value.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase) Then
                    Dim parts = value.Split(" "c, StringSplitOptions.RemoveEmptyEntries)
                    Dim number As Integer = result.Count + 1
                    If parts.Length > 1 Then Integer.TryParse(parts(1), number)
                    current = New CueEntry With {.Number = number}
                ElseIf current IsNot Nothing AndAlso value.StartsWith("TITLE ", StringComparison.OrdinalIgnoreCase) Then
                    current.Title = value.Substring(6).Trim().Trim(""""c)
                ElseIf current IsNot Nothing AndAlso value.StartsWith("INDEX 01 ", StringComparison.OrdinalIgnoreCase) Then
                    Dim time = value.Substring(9).Trim().Split(" "c)(0).Split(":"c)
                    If time.Length = 3 Then
                        Dim minutes As Integer, seconds As Integer, frames As Integer
                        If Integer.TryParse(time(0), minutes) AndAlso Integer.TryParse(time(1), seconds) AndAlso Integer.TryParse(time(2), frames) Then
                            current.StartSeconds = minutes * 60 + seconds + frames / 75.0 : result.Add(current)
                        End If
                    End If
                End If
            Next
            Return result
        End Function
    End Class
End Namespace
