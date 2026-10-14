Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports FerrumKix.Models

Namespace Services

    ''' <summary>Liest ein CUE-Sheet und zerlegt eine grosse Albumdatei damit in ihre Titel.
    '''
    ''' <para>Das Sheet kommt entweder als Datei neben dem Album (<c>Album.cue</c> oder
    ''' <c>Album.flac.cue</c>, notfalls eine andere CUE im Ordner, die genau diese Datei nennt) oder
    ''' eingebettet im Kennzeichen CUESHEET, wie es EAC und foobar2000 in FLAC und APE schreiben.
    ''' Die Datei bleibt dabei unangetastet: jeder Titel ist nur ein Bereich darin, siehe
    ''' <see cref="Track.StartSeconds"/>.</para>
    '''
    ''' <para>Ein Sheet mit nur EINEM Titel zerlegt nichts und wird verworfen. Das betrifft vor
    ''' allem die CUE eines Albums aus Einzeldateien, die jeden Titel als eigenen FILE-Block fuehrt:
    ''' dort steht zu jeder Datei genau ein Titel, und die Datei ist schon der Titel.</para></summary>
    Public NotInheritable Class CueSheetService
        Private Sub New()
        End Sub

        Public NotInheritable Class Sheet
            Public Property Title As String = String.Empty
            Public Property Performer As String = String.Empty
            Public Property Genre As String = String.Empty
            Public Property Year As Integer
            Public ReadOnly Property Entries As New List(Of Entry)()
        End Class

        Public NotInheritable Class Entry
            Public Property Number As Integer
            Public Property Title As String = String.Empty
            Public Property Performer As String = String.Empty
            Public Property StartSeconds As Double
        End Class

        Shared Sub New()
            ' Fuer Windows-1252: aeltere CUE-Dateien aus EAC sind fast immer so kodiert.
            Try
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
            Catch
            End Try
        End Sub

        ''' <summary>Ob die Datei ein CUE-Sheet ist - nach der Endung.</summary>
        Public Shared Function IsCueFile(filePath As String) As Boolean
            Try
                Return String.Equals(Path.GetExtension(filePath), ".cue", StringComparison.OrdinalIgnoreCase)
            Catch
                Return False
            End Try
        End Function

        ''' <summary>Das Sheet zu einer Albumdatei: erst eine CUE daneben, dann das eingebettete.
        ''' Nothing, wenn es keines gibt oder es weniger als zwei Titel beschreibt.</summary>
        Public Shared Function Find(audioPath As String, embeddedCue As String) As Sheet
            Try
                For Each candidate In ExternalCandidates(audioPath)
                    Dim sheet = Parse(ReadText(candidate), audioPath, requireFileMatch:=Not IsNamedAfter(candidate, audioPath))
                    If sheet IsNot Nothing Then Return sheet
                Next
            Catch ex As Exception
                DiagnosticLogService.Log("Cue.Find", $"{audioPath}: {ex.Message}")
            End Try
            If String.IsNullOrWhiteSpace(embeddedCue) Then Return Nothing
            Return Parse(embeddedCue, audioPath, requireFileMatch:=False)
        End Function

        ''' <summary>Die Albumdatei, die eine CUE beschreibt. Fuer eine CUE, die direkt in die Liste
        ''' gezogen wird. Nothing, wenn sie keine oder mehrere Dateien nennt oder die genannte fehlt.
        ''' Nennt die CUE eine WAV, die laengst zu FLAC oder APE geworden ist, wird die Datei mit
        ''' demselben Namen und einer anderen bekannten Endung genommen.</summary>
        Public Shared Function ResolveAudioFile(cuePath As String) As String
            Try
                Dim names = FileNames(ReadText(cuePath))
                If names.Count <> 1 Then Return Nothing
                Dim folder = If(Path.GetDirectoryName(cuePath), String.Empty)
                Dim named = Path.Combine(folder, names(0))
                If File.Exists(named) AndAlso TagReadService.IsSupportedFile(named) Then Return named
                Dim stem = Path.GetFileNameWithoutExtension(names(0))
                For Each extension In TagReadService.SupportedExtensions
                    Dim candidate = Path.Combine(folder, stem & extension)
                    If File.Exists(candidate) Then Return candidate
                Next
            Catch ex As Exception
                DiagnosticLogService.Log("Cue.Resolve", $"{cuePath}: {ex.Message}")
            End Try
            Return Nothing
        End Function

        ''' <summary>Macht aus der gelesenen Albumdatei die Titel des Sheets. Was das Sheet nicht
        ''' sagt, kommt aus den Kennzeichen der Datei. Das Ende eines Titels ist der Anfang des
        ''' naechsten; der letzte laeuft bis zum Dateiende.</summary>
        Public Shared Function Split(whole As Track, sheet As Sheet) As List(Of Track)
            Dim result As New List(Of Track)()
            Dim entries = sheet.Entries
            Dim fileDuration = Math.Max(0, whole.DurationSeconds)
            For index = 0 To entries.Count - 1
                Dim entry = entries(index)
                Dim nextStart = If(index + 1 < entries.Count, entries(index + 1).StartSeconds, 0)
                Dim endSeconds = If(index + 1 < entries.Count, nextStart, fileDuration)
                Dim duration = Math.Max(0, endSeconds - entry.StartSeconds)
                ' Die Groesse anteilig: die Liste zaehlt sie fuer ein Album zusammen, und die
                ' ganze Datei bei jedem Titel ergaebe das Vielfache.
                Dim size = If(fileDuration > 0, CLng(whole.FileSize * duration / fileDuration), 0L)
                Dim albumArtist = FirstFilled(sheet.Performer, whole.AlbumArtist, whole.Artist)
                result.Add(New Track With {
                    .FilePath = whole.FilePath,
                    .CueTrackNumber = entry.Number,
                    .StartSeconds = entry.StartSeconds,
                    .EndSeconds = If(index + 1 < entries.Count, nextStart, 0),
                    .Title = FirstFilled(entry.Title, $"Track {entry.Number:00}"),
                    .Artist = FirstFilled(entry.Performer, albumArtist),
                    .AlbumArtist = albumArtist,
                    .Album = FirstFilled(sheet.Title, whole.Album),
                    .Genre = FirstFilled(sheet.Genre, whole.Genre),
                    .Year = If(sheet.Year > 0, sheet.Year, whole.Year),
                    .TrackNumber = entry.Number,
                    .DiscNumber = whole.DiscNumber,
                    .AlbumSortOrder = whole.AlbumSortOrder,
                    .DurationSeconds = duration,
                    .Codec = whole.Codec,
                    .SampleRate = whole.SampleRate,
                    .Bitrate = whole.Bitrate,
                    .Channels = whole.Channels,
                    .FileSize = size,
                    .TagsReadUtc = whole.TagsReadUtc})
            Next
            Return result
        End Function

        ''' <summary>Zerlegt den Text eines Sheets. Nimmt nur die Titel des FILE-Blocks, der die
        ''' Albumdatei nennt; steht im Sheet nur ein einziger FILE-Block, gilt er auch dann, wenn
        ''' der Name nicht passt (<paramref name="requireFileMatch"/> = False) - EAC schreibt dort
        ''' gern noch die WAV hinein, aus der die FLAC einmal entstand.</summary>
        Friend Shared Function Parse(text As String, audioPath As String, requireFileMatch As Boolean) As Sheet
            If String.IsNullOrWhiteSpace(text) Then Return Nothing
            Dim lines = text.Split({ControlChars.CrLf, ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.None)
            Dim files = FileNames(text)
            Dim audioStem = Path.GetFileNameWithoutExtension(audioPath)
            Dim matching = files.Where(Function(name) String.Equals(Path.GetFileNameWithoutExtension(name), audioStem, StringComparison.OrdinalIgnoreCase)).ToList()
            ' Mehrere FILE-Bloecke: nur der passende zaehlt. Ein einzelner oder gar keiner (ein
            ' eingebettetes Sheet nennt mitunter keine Datei) gilt, wenn kein Abgleich verlangt ist.
            Dim acceptAll = files.Count <= 1 AndAlso (Not requireFileMatch OrElse matching.Count = 1)
            If Not acceptAll AndAlso matching.Count = 0 Then Return Nothing

            Dim sheet As New Sheet()
            Dim current As Entry = Nothing
            Dim inMatchingFile = acceptAll
            For Each raw In lines
                Dim line = raw.Trim()
                If line.Length = 0 Then Continue For
                Dim keyword = FirstWord(line)
                Dim rest = line.Substring(keyword.Length).Trim()
                Select Case keyword.ToUpperInvariant()
                    Case "FILE"
                        current = Nothing
                        If Not acceptAll Then
                            Dim name = Unquote(StripFileType(rest))
                            inMatchingFile = String.Equals(Path.GetFileNameWithoutExtension(name), audioStem, StringComparison.OrdinalIgnoreCase)
                        End If
                    Case "TRACK"
                        current = Nothing
                        If Not inMatchingFile Then Continue For
                        Dim number As Integer
                        Integer.TryParse(FirstWord(rest), NumberStyles.Integer, CultureInfo.InvariantCulture, number)
                        current = New Entry With {.Number = If(number > 0, number, sheet.Entries.Count + 1)}
                    Case "TITLE"
                        If current IsNot Nothing Then current.Title = Unquote(rest) Else If sheet.Entries.Count = 0 Then sheet.Title = Unquote(rest)
                    Case "PERFORMER"
                        If current IsNot Nothing Then current.Performer = Unquote(rest) Else If sheet.Entries.Count = 0 Then sheet.Performer = Unquote(rest)
                    Case "REM"
                        If current IsNot Nothing Then Continue For
                        Dim field = FirstWord(rest)
                        Dim value = Unquote(rest.Substring(field.Length).Trim())
                        Select Case field.ToUpperInvariant()
                            Case "GENRE" : sheet.Genre = value
                            Case "DATE"
                                Dim year As Integer
                                If value.Length >= 4 AndAlso Integer.TryParse(value.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, year) Then sheet.Year = year
                        End Select
                    Case "INDEX"
                        If current Is Nothing Then Continue For
                        Dim parts = rest.Split(" "c, StringSplitOptions.RemoveEmptyEntries)
                        If parts.Length < 2 OrElse parts(0) <> "01" Then Continue For
                        Dim start As Double
                        If Not TryParseTime(parts(1), start) Then Continue For
                        current.StartSeconds = start
                        sheet.Entries.Add(current)
                        ' INDEX 01 zaehlt nur einmal je Titel.
                        current = Nothing
                End Select
            Next
            ' Ein Sheet, das rueckwaerts laeuft, ist kaputt - lieber die ganze Datei als falsche
            ' Schnitte.
            For index = 1 To sheet.Entries.Count - 1
                If sheet.Entries(index).StartSeconds <= sheet.Entries(index - 1).StartSeconds Then Return Nothing
            Next
            Return If(sheet.Entries.Count >= 2, sheet, Nothing)
        End Function

        ''' <summary>Die Dateinamen aller FILE-Zeilen, in ihrer Reihenfolge.</summary>
        Private Shared Function FileNames(text As String) As List(Of String)
            Dim result As New List(Of String)()
            If String.IsNullOrEmpty(text) Then Return result
            For Each raw In text.Split({ControlChars.CrLf, ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.None)
                Dim line = raw.Trim()
                If Not String.Equals(FirstWord(line), "FILE", StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim name = Unquote(StripFileType(line.Substring(4).Trim()))
                If name.Length > 0 Then result.Add(name)
            Next
            Return result
        End Function

        ''' <summary>Die CUE-Dateien, die zu einer Albumdatei gehoeren koennen. Zuerst die nach ihr
        ''' benannten, danach jede andere im Ordner - die muss dann aber diese Datei nennen.</summary>
        Private Shared Iterator Function ExternalCandidates(audioPath As String) As IEnumerable(Of String)
            Dim folder = Path.GetDirectoryName(audioPath)
            If String.IsNullOrEmpty(folder) OrElse Not Directory.Exists(folder) Then Return
            Dim named = {Path.ChangeExtension(audioPath, ".cue"), audioPath & ".cue"}
            For Each candidate In named
                If File.Exists(candidate) Then Yield candidate
            Next
            Dim others As List(Of String)
            Try
                others = Directory.EnumerateFiles(folder, "*.*").Where(AddressOf IsCueFile).ToList()
            Catch
                Return
            End Try
            For Each candidate In others
                If named.Contains(candidate, StringComparer.Ordinal) Then Continue For
                Yield candidate
            Next
        End Function

        Private Shared Function IsNamedAfter(cuePath As String, audioPath As String) As Boolean
            Return String.Equals(cuePath, Path.ChangeExtension(audioPath, ".cue"), StringComparison.Ordinal) OrElse
                   String.Equals(cuePath, audioPath & ".cue", StringComparison.Ordinal)
        End Function

        ''' <summary>Liest eine CUE-Datei. Mit BOM nach dem BOM, sonst UTF-8, wenn es gueltiges UTF-8
        ''' ist, und andernfalls Windows-1252 - so schreiben aeltere Ripper.</summary>
        Friend Shared Function ReadText(cuePath As String) As String
            Dim info As New FileInfo(cuePath)
            ' Ein Sheet ist ein paar Kilobyte. Eine "CUE" in Megabytes ist keine.
            If Not info.Exists OrElse info.Length > 1024 * 1024 Then Return String.Empty
            Dim bytes = File.ReadAllBytes(cuePath)
            Using reader As New StreamReader(New MemoryStream(bytes), New UTF8Encoding(False, True), detectEncodingFromByteOrderMarks:=True)
                Try
                    Return reader.ReadToEnd()
                Catch ex As DecoderFallbackException
                End Try
            End Using
            Try
                Return Encoding.GetEncoding(1252).GetString(bytes)
            Catch
                Return Encoding.Latin1.GetString(bytes)
            End Try
        End Function

        ''' <summary>"mm:ss:ff" mit 75 Rahmen je Sekunde. Die Minuten duerfen ueber 99 gehen.</summary>
        Private Shared Function TryParseTime(value As String, ByRef seconds As Double) As Boolean
            seconds = 0
            Dim parts = value.Split(":"c)
            If parts.Length <> 3 Then Return False
            Dim minutes, secs, frames As Integer
            If Not Integer.TryParse(parts(0), NumberStyles.Integer, CultureInfo.InvariantCulture, minutes) OrElse
               Not Integer.TryParse(parts(1), NumberStyles.Integer, CultureInfo.InvariantCulture, secs) OrElse
               Not Integer.TryParse(parts(2), NumberStyles.Integer, CultureInfo.InvariantCulture, frames) Then Return False
            If minutes < 0 OrElse secs < 0 OrElse secs > 59 OrElse frames < 0 OrElse frames > 74 Then Return False
            seconds = minutes * 60 + secs + frames / 75.0
            Return True
        End Function

        Private Shared Function FirstWord(value As String) As String
            Dim index = value.IndexOfAny({" "c, ControlChars.Tab})
            Return If(index < 0, value, value.Substring(0, index))
        End Function

        ''' <summary>Nimmt die Dateiart hinter dem Namen weg: FILE "x.wav" WAVE.</summary>
        Private Shared Function StripFileType(value As String) As String
            If value.StartsWith(""""c) Then
                Dim closing = value.IndexOf(""""c, 1)
                Return If(closing > 0, value.Substring(0, closing + 1), value)
            End If
            Dim index = value.LastIndexOf(" "c)
            Return If(index > 0, value.Substring(0, index), value)
        End Function

        Private Shared Function Unquote(value As String) As String
            Dim trimmed = If(value, String.Empty).Trim()
            If trimmed.Length >= 2 AndAlso trimmed.StartsWith(""""c) AndAlso trimmed.EndsWith(""""c) Then trimmed = trimmed.Substring(1, trimmed.Length - 2)
            Return trimmed.Replace(ChrW(0), " "c).Trim()
        End Function

        Private Shared Function FirstFilled(ParamArray values As String()) As String
            For Each value In values
                If Not String.IsNullOrWhiteSpace(value) Then Return value.Trim()
            Next
            Return String.Empty
        End Function

    End Class

End Namespace
