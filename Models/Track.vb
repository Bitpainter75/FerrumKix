Imports System
Imports System.Globalization
Imports System.IO
Imports System.Text.Json.Serialization

Namespace Models

    ''' <summary>Ein Titel in der Wiedergabeliste.
    '''
    ''' <para>Die Felder stehen so, wie sie aus der Datei gelesen wurden; die Anzeigetexte
    ''' entstehen daraus. Ein Titel ist damit auch ohne Oberflaeche vollstaendig beschrieben und
    ''' laesst sich unveraendert in die gespeicherte Liste schreiben.</para></summary>
    Public NotInheritable Class Track

        Public Property FilePath As String = String.Empty
        Public Property Title As String = String.Empty
        Public Property Artist As String = String.Empty
        Public Property Album As String = String.Empty
        Public Property AlbumArtist As String = String.Empty
        Public Property Genre As String = String.Empty
        Public Property Year As Integer
        Public Property TrackNumber As Integer
        Public Property DiscNumber As Integer

        ''' <summary>Die Laufzeit in Sekunden. 0 heisst: noch nicht gelesen.</summary>
        Public Property DurationSeconds As Double

        ''' <summary>Die Kurzform des Formats, wie sie in der Liste steht: MP3, FLAC, OGG.</summary>
        Public Property Codec As String = String.Empty
        Public Property SampleRate As Integer
        Public Property Bitrate As Integer
        Public Property Channels As Integer
        Public Property FileSize As Long

        ''' <summary>Wann die Kennzeichen gelesen wurden. Aendert sich die Datei danach, gilt der
        ''' Eintrag in der gespeicherten Liste als veraltet und wird neu gelesen.</summary>
        Public Property TagsReadUtc As Date

        ''' <summary>Ob dieser Eintrag einen physischen CDDA-Titel beschreibt. Der interne URI
        ''' ist absichtlich kein mpv-URI: er bleibt eine eindeutige, speicherbare Kennung pro
        ''' Titel; <see cref="Services.AudioPlayer"/> wandelt ihn erst beim Laden in
        ''' <c>cdda://</c> um.</summary>
        <JsonIgnore>
        Public ReadOnly Property IsAudioCdTrack As Boolean
            Get
                Dim devicePath As String = Nothing
                Dim trackNumber As Integer
                Dim lastTrack As Integer
                Return TryGetAudioCdSource(FilePath, devicePath, trackNumber, lastTrack)
            End Get
        End Property

        Public Shared Function CreateAudioCdPath(devicePath As String, trackNumber As Integer, lastTrack As Integer) As String
            Dim escaped = Uri.EscapeDataString(If(devicePath, String.Empty).TrimStart("/"c))
            Return $"cdda-track:///{escaped}?track={trackNumber}&last={lastTrack}"
        End Function

        Public Shared Function TryGetAudioCdSource(value As String, ByRef devicePath As String,
                                                   ByRef trackNumber As Integer, ByRef lastTrack As Integer) As Boolean
            devicePath = Nothing
            trackNumber = 0
            lastTrack = 0
            If String.IsNullOrWhiteSpace(value) Then Return False
            Try
                Dim uri As New Uri(value, UriKind.Absolute)
                If Not String.Equals(uri.Scheme, "cdda-track", StringComparison.OrdinalIgnoreCase) Then Return False
                devicePath = Uri.UnescapeDataString(uri.AbsolutePath)
                Dim query = uri.Query.TrimStart("?"c).Split("&"c)
                For Each part In query
                    Dim pair = part.Split("="c, 2)
                    If pair.Length <> 2 Then Continue For
                    If String.Equals(pair(0), "track", StringComparison.OrdinalIgnoreCase) Then Integer.TryParse(pair(1), trackNumber)
                    If String.Equals(pair(0), "last", StringComparison.OrdinalIgnoreCase) Then Integer.TryParse(pair(1), lastTrack)
                Next
                Return Not String.IsNullOrWhiteSpace(devicePath) AndAlso trackNumber > 0 AndAlso lastTrack >= trackNumber
            Catch
                devicePath = Nothing
                trackNumber = 0
                lastTrack = 0
                Return False
            End Try
        End Function

        ''' <summary>Der Ordner, in dem die Datei liegt. Die Gruppen der Liste haengen daran und
        ''' nicht am Albumnamen: ein Album ohne Kennzeichen hat keinen, und zwei verschiedene Alben
        ''' koennen denselben tragen.</summary>
        <JsonIgnore>
        Public ReadOnly Property FolderPath As String
            Get
                Dim devicePath As String = Nothing
                Dim trackNumber As Integer
                Dim lastTrack As Integer
                If TryGetAudioCdSource(FilePath, devicePath, trackNumber, lastTrack) Then Return $"Audio-CD ({devicePath})"
                Try
                    Return If(Path.GetDirectoryName(FilePath), String.Empty)
                Catch
                    Return String.Empty
                End Try
            End Get
        End Property

        ''' <summary>Der Titel, wie ihn die Liste zeigt: "Interpret - Titel". Fehlen die
        ''' Kennzeichen, steht der Dateiname da. Der ist immer da und sagt fast immer dasselbe.</summary>
        <JsonIgnore>
        Public ReadOnly Property DisplayTitle As String
            Get
                Dim hasTitle = Not String.IsNullOrWhiteSpace(Title)
                Dim hasArtist = Not String.IsNullOrWhiteSpace(Artist)
                If hasTitle AndAlso hasArtist Then Return $"{Artist} - {Title}"
                If hasTitle Then Return Title
                Try
                    Return Path.GetFileNameWithoutExtension(FilePath)
                Catch
                    Return FilePath
                End Try
            End Get
        End Property

        ''' <summary>Der Name ohne den Interpreten davor. Fuer die Coverspalte, wo der Interpret
        ''' schon in einer eigenen Zeile steht.</summary>
        <JsonIgnore>
        Public ReadOnly Property ShortTitle As String
            Get
                If Not String.IsNullOrWhiteSpace(Title) Then Return Title
                Try
                    Return Path.GetFileNameWithoutExtension(FilePath)
                Catch
                    Return FilePath
                End Try
            End Get
        End Property

        <JsonIgnore>
        Public ReadOnly Property DurationText As String
            Get
                Return FormatDuration(DurationSeconds)
            End Get
        End Property

        ''' <summary>Die zweite Zeile einer Listenzeile: "MP3 :: 44 kHz, 320 kbps, 13,32 MB".
        ''' Leere Angaben fallen weg, statt als Null dazustehen.</summary>
        <JsonIgnore>
        Public ReadOnly Property FormatText As String
            Get
                Dim parts As New List(Of String)()
                If SampleRate > 0 Then parts.Add($"{CInt(Math.Round(SampleRate / 1000.0))} kHz")
                If Bitrate > 0 Then parts.Add($"{Bitrate} kbps")
                If FileSize > 0 Then parts.Add(FormatFileSize(FileSize))

                Dim codecText = If(String.IsNullOrWhiteSpace(Codec), FileExtensionLabel(), Codec)
                If parts.Count = 0 Then Return codecText
                Return codecText & " :: " & String.Join(", ", parts)
            End Get
        End Property

        ''' <summary>Die ausfuehrliche Zeile unter dem Titelbild: "MP3, 44 kHz, 320 kbps, Stereo".</summary>
        <JsonIgnore>
        Public ReadOnly Property DetailText As String
            Get
                Dim parts As New List(Of String)()
                parts.Add(If(String.IsNullOrWhiteSpace(Codec), FileExtensionLabel(), Codec))
                If SampleRate > 0 Then parts.Add($"{CInt(Math.Round(SampleRate / 1000.0))} kHz")
                If Bitrate > 0 Then parts.Add($"{Bitrate} kbps")
                Select Case Channels
                    Case 1 : parts.Add("Mono")
                    Case 2 : parts.Add("Stereo")
                    Case > 2 : parts.Add(Services.LocalizationService.Format("{0} Kanäle", Channels))
                End Select
                Return String.Join(", ", parts)
            End Get
        End Property

        Private Function FileExtensionLabel() As String
            Try
                Dim extension = Path.GetExtension(FilePath)
                If String.IsNullOrEmpty(extension) Then Return String.Empty
                Return extension.TrimStart("."c).ToUpperInvariant()
            Catch
                Return String.Empty
            End Try
        End Function

        ''' <summary>Sekunden als m:ss, ab einer Stunde als h:mm:ss. Negative Werte und
        ''' Unendlichkeiten kommen aus mpv, bevor die Laufzeit feststeht.</summary>
        Public Shared Function FormatDuration(seconds As Double) As String
            If Double.IsNaN(seconds) OrElse Double.IsInfinity(seconds) OrElse seconds < 0 Then seconds = 0
            Dim span = TimeSpan.FromSeconds(Math.Floor(seconds))
            If span.TotalHours >= 1 Then
                Return $"{CInt(Math.Floor(span.TotalHours))}:{span.Minutes:00}:{span.Seconds:00}"
            End If
            Return $"{span.Minutes}:{span.Seconds:00}"
        End Function

        ''' <summary>Die Gesamtlaufzeit einer Gruppe. Anders als bei einem Titel steht hier immer
        ''' die zweistellige Minute, weil die Zahlen untereinander stehen.</summary>
        Public Shared Function FormatTotalDuration(seconds As Double) As String
            If Double.IsNaN(seconds) OrElse Double.IsInfinity(seconds) OrElse seconds < 0 Then seconds = 0
            Dim span = TimeSpan.FromSeconds(Math.Floor(seconds))
            If span.TotalHours >= 1 Then
                Return $"{CInt(Math.Floor(span.TotalHours))}:{span.Minutes:00}:{span.Seconds:00}"
            End If
            Return $"{span.Minutes:00}:{span.Seconds:00}"
        End Function

        Public Shared Function FormatFileSize(bytes As Long) As String
            If bytes <= 0 Then Return String.Empty
            Dim megabytes = bytes / 1048576.0
            If megabytes >= 1024 Then
                Return (megabytes / 1024.0).ToString("0.00", CultureInfo.CurrentCulture) & " GB"
            End If
            Return megabytes.ToString("0.00", CultureInfo.CurrentCulture) & " MB"
        End Function

    End Class

End Namespace
