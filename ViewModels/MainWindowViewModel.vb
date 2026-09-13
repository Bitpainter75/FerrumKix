Imports System
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Avalonia.Media.Imaging
Imports Avalonia.Threading
Imports FerrumPlay.Models
Imports FerrumPlay.Services

Namespace ViewModels

    ''' <summary>Was die Anwendung gerade zeigt.</summary>
    Public Enum AppMode
        Player = 0
        Settings = 1
    End Enum

    ''' <summary>Wie es nach dem letzten Titel weitergeht.</summary>
    Public Enum RepeatMode
        [Off] = 0
        All = 1
        [Single] = 2
    End Enum

    ''' <summary>Die Dateiliste und eine eingelegte Audio-CD bleiben getrennte Wiedergabelisten.</summary>
    Public Enum PlaylistKind
        Files = 0
        AudioCd = 1
    End Enum

    Public NotInheritable Class MainWindowViewModel
        Inherits ViewModelBase
        Implements IDisposable

        Private ReadOnly _player As New AudioPlayer()

        ''' <summary>Die vollstaendige Liste in der Reihenfolge, in der sie hinzugefuegt wurde.
        ''' Sie ist die Quelle fuer alles andere: die angezeigten Zeilen, die Abspielreihenfolge
        ''' und die gespeicherte Datei.</summary>
        Private ReadOnly _tracks As New List(Of Track)()
        Private ReadOnly _audioCdTracks As New List(Of Track)()
        ' Die vom Lyrion-Album gestartete, flüchtige Reihenfolge wird nie gespeichert und tritt
        ' nur an die Stelle der lokalen Liste, solange einer ihrer Titel läuft.
        Private ReadOnly _lyrionTracks As New List(Of Track)()
        Private _lyrionPlayOrder As New List(Of Track)()
        Public Event LyrionPlayOrderChanged(order As IReadOnlyList(Of Track))
        Public Event LyrionCurrentTrackChanged(track As Track)
        Public ReadOnly Property IsPlayingLyrion As Boolean
            Get
                Return _currentTrack IsNot Nothing AndAlso _lyrionTracks.Contains(_currentTrack)
            End Get
        End Property
        Public ReadOnly Property LyrionPlayOrder As IReadOnlyList(Of Track)
            Get
                Return _lyrionPlayOrder.AsReadOnly()
            End Get
        End Property
        ' mpv liefert bei manchen CD-Laufwerken eine absolute Disc-Zeit statt der Zeit des
        ' angewählten Tracks. Der erste gemeldete Wert ist dann unser Bezugspunkt.
        Private _audioCdTimeOffset As Double?
        Private _audioCdPlaybackLoaded As Boolean

        ''' <summary>Die Zeile zu einem Titel. Sie ueberlebt das Neuaufbauen der Anzeige, damit das
        ''' Haekchen vor einem Titel nicht bei jedem Tastendruck im Suchfeld zurueckspringt.</summary>
        Private ReadOnly _rowsByTrack As New Dictionary(Of Track, PlaylistTrackRow)()

        ''' <summary>Welche Gruppen zugeklappt sind. Am Ordnerpfad und nicht an der Zeile: die
        ''' Zeilen entstehen beim Aufbauen neu, der Ordner bleibt.</summary>
        Private ReadOnly _collapsedFolders As New HashSet(Of String)(StringComparer.Ordinal)

        ''' <summary>Die Reihenfolge, in der gespielt wird. Bei Zufall eine gemischte Fassung von
        ''' <c>_tracks</c>, sonst dieselbe Reihenfolge.</summary>
        Private _playOrder As New List(Of Track)()

        Private ReadOnly _shuffleRandom As New Random()

        Private _currentTrack As Track
        Private _currentCover As Bitmap
        Private _mode As AppMode = AppMode.Player
        Private _searchText As String = String.Empty
        Private _statusText As String = String.Empty

        Private _positionSeconds As Double
        Private _durationSeconds As Double
        Private _isPlaying As Boolean
        Private _volume As Double = 80
        Private _isMuted As Boolean
        Private _isShuffle As Boolean
        Private _repeat As RepeatMode = RepeatMode.Off
        Private _sidePanelWidth As Double = 300
        Private _selectedPlaylist As PlaylistKind = PlaylistKind.Files

        ''' <summary>Laeuft gerade ein Ordner-Einlesen. Zwei gleichzeitig waeren erlaubt, aber der
        ''' Balken kann nur eines zeigen, und die Reihenfolge in der Liste wuerde sich mischen.</summary>
        Private _isScanning As Boolean

        ''' <summary>Ein Vergroesserungsfaktor wurde in dieser Sitzung verstellt.</summary>
        Private _restartNeeded As Boolean

        ''' <summary>Der Abbruch fuer das Einlesen. Beim Beenden wird gezogen, damit ein Lauf ueber
        ''' ein Netzlaufwerk die Anwendung nicht festhaelt.</summary>
        Private ReadOnly _shutdown As New CancellationTokenSource()
        Private _audioCdMonitor As Timer
        Private _audioCdCheckRunning As Integer

        ''' <summary>Nur das JUENGSTE Titelbild darf ankommen. Wer schnell durch die Liste geht,
        ''' startet mehrere Ladevorgaenge; ohne diese Nummer gewinnt der, der zufaellig zuletzt
        ''' fertig wird, und im Fenster steht das Bild eines Titels, der laengst vorbei ist.</summary>
        Private _coverRequest As Integer = 0

        ''' <summary>Nichts geladen: vor dem ersten Abspielen, nach Stop, nach einem Titel, der sich
        ''' nicht oeffnen liess. Unterscheidet fuer MPRIS "angehalten" von "pausiert".</summary>
        Private _isStopped As Boolean = True

        ''' <summary>Wohin es geht, wenn sich der gerade gestartete Titel nicht oeffnen laesst: +1
        ''' weiter, -1 zurueck, 0 gar nicht (der Titel war gezielt gewaehlt).</summary>
        Private _skipDirection As Integer

        ''' <summary>Wie viele Titel hintereinander sich nicht oeffnen liessen. Zurueckgesetzt,
        ''' sobald einer wirklich laeuft.</summary>
        Private _consecutiveFailures As Integer
        Private Const MaxConsecutiveFailures As Integer = 20

        Private _missingCheckRunning As Boolean
        Private _lastMissingCheckUtc As Date = Date.MinValue
        Private Shared ReadOnly MissingCheckInterval As TimeSpan = TimeSpan.FromSeconds(15)

        ''' <summary>Was hinzugefuegt werden soll, waehrend schon eingelesen wird - ein zweiter
        ''' Aufruf aus dem Dateimanager, ein weiteres Ablegen. Es kommt danach dran, statt
        ''' verworfen zu werden.</summary>
        Private ReadOnly _queuedAdds As New Queue(Of (Paths As List(Of String), PlayFirst As Boolean))()

        ''' <param name="startupPaths">Dateien und Ordner aus dem Aufruf. Kommen welche, werden sie
        ''' angehaengt und der erste davon gespielt; das Fortsetzen des zuletzt gespielten Titels
        ''' entfaellt dann - sonst liefe er kurz an, bevor der gewuenschte uebernimmt.</param>
        Public Sub New(Optional startupPaths As IEnumerable(Of String) = Nothing)
            Rows = New ObservableCollection(Of PlaylistRow)()

            HookLyrionRemote()
            PlayPauseCommand = New DelegateCommand(AddressOf TogglePlayPause)
            StopCommand = New DelegateCommand(AddressOf StopPlayback)
            NextCommand = New DelegateCommand(Sub() PlayNext(userRequested:=True))
            PreviousCommand = New DelegateCommand(AddressOf PlayPrevious)
            ToggleShuffleCommand = New DelegateCommand(Sub() IsShuffle = Not IsShuffle)
            CycleRepeatCommand = New DelegateCommand(AddressOf CycleRepeat)
            ToggleMuteCommand = New DelegateCommand(Sub() IsMuted = Not IsMuted)
            ClearPlaylistCommand = New DelegateCommand(AddressOf ClearPlaylist)
            ShowFilesPlaylistCommand = New DelegateCommand(Sub() SelectedPlaylist = PlaylistKind.Files)
            ShowAudioCdPlaylistCommand = New DelegateCommand(Sub() SelectedPlaylist = PlaylistKind.AudioCd)
            ClearSearchCommand = New DelegateCommand(Sub() SearchText = String.Empty)
            RemoveMissingTracksCommand = New DelegateCommand(AddressOf RemoveMissingTracks)
            OpenSettingsCommand = New DelegateCommand(Sub() Mode = AppMode.Settings)
            ClosePanelCommand = New DelegateCommand(Sub() Mode = AppMode.Player)
            ' Der Parameter kommt aus dem AXAML und ist dort eine Zeichenkette, auch wenn eine Zahl
            ' darin steht. Ausdruecklich gewandelt statt CInt auf ein Object: das waere eine spaete
            ' Wandlung, die erst zur Laufzeit auffaellt, wenn jemand etwas anderes hineinschreibt.
            SetFontSizeCommand = New DelegateCommand(
                Sub(parameter)
                    Dim offset As Integer
                    If Integer.TryParse(Convert.ToString(parameter, Globalization.CultureInfo.InvariantCulture),
                                        offset) Then SetFontSizeOffset(offset)
                End Sub)
            SetAccentColorCommand = New DelegateCommand(Sub(parameter) SetAccentColor(TryCast(parameter, String)))

            For Each language In LocalizationService.Languages
                LanguageChoices.Add(New LanguageChoice(language.Key, language.Name))
            Next
            BuildAppearanceChoices()
            BuildReplayGainChoices()
            LoadSettings()
            WirePlayer()
            _player.Start()
            _audioCdMonitor = New Timer(Sub() CheckAudioCd(), Nothing, TimeSpan.Zero, TimeSpan.FromSeconds(5))
            ' VOR dem Wiederherstellen: dann bekommt schon der zuletzt gespielte Titel sein Bild
            ' fuer MPRIS.
            ConnectToSession()

            Dim openAtStart = If(startupPaths?.ToList(), New List(Of String)())
            RestorePlaylist(allowResume:=openAtStart.Count = 0)

            ' Welche Dateien fehlen, klaert sich im Hintergrund. Die Liste steht bis dahin schon da.
            Dispatcher.UIThread.Post(Sub() CheckMissingFiles(reportCount:=True))

            ' Ueber den Dispatcher und nicht direkt: das Einlesen wartet auf Hintergrundarbeit und
            ' soll danach auf dem Oberflaechenfaden weitermachen, und der laeuft erst, wenn das
            ' Fenster steht.
            If openAtStart.Count > 0 Then
                Dispatcher.UIThread.Post(Async Sub() Await AddPathsAsync(openAtStart, playFirst:=True))
            End If
        End Sub

        ' Die Wiedergabeliste

        Public ReadOnly Property Rows As ObservableCollection(Of PlaylistRow)

        Public ReadOnly Property PlayPauseCommand As DelegateCommand
        Public ReadOnly Property StopCommand As DelegateCommand
        Public ReadOnly Property NextCommand As DelegateCommand
        Public ReadOnly Property PreviousCommand As DelegateCommand
        Public ReadOnly Property ToggleShuffleCommand As DelegateCommand
        Public ReadOnly Property CycleRepeatCommand As DelegateCommand
        Public ReadOnly Property ToggleMuteCommand As DelegateCommand
        Public ReadOnly Property ClearPlaylistCommand As DelegateCommand
        Public ReadOnly Property ShowFilesPlaylistCommand As DelegateCommand
        Public ReadOnly Property ShowAudioCdPlaylistCommand As DelegateCommand
        Public ReadOnly Property ClearSearchCommand As DelegateCommand
        Public ReadOnly Property OpenSettingsCommand As DelegateCommand
        Public ReadOnly Property ClosePanelCommand As DelegateCommand
        Public ReadOnly Property RemoveMissingTracksCommand As DelegateCommand

        Public Property Mode As AppMode
            Get
                Return _mode
            End Get
            Set(value As AppMode)
                If SetField(_mode, value) Then
                    RaisePropertyChanged(NameOf(IsPlayerVisible))
                    RaisePropertyChanged(NameOf(IsSettingsVisible))
                End If
            End Set
        End Property

        Public ReadOnly Property IsPlayerVisible As Boolean
            Get
                Return _mode = AppMode.Player
            End Get
        End Property

        Public ReadOnly Property IsSettingsVisible As Boolean
            Get
                Return _mode = AppMode.Settings
            End Get
        End Property

        ''' <summary>Der Filter ueber der Liste. Jeder Tastendruck baut die Zeilen neu auf; bei
        ''' einigen tausend Titeln ist das eine Sache von Millisekunden, weil nur Zeichenketten
        ''' verglichen werden.</summary>
        Public Property SearchText As String
            Get
                Return _searchText
            End Get
            Set(value As String)
                If SetField(_searchText, If(value, String.Empty)) Then
                    RaisePropertyChanged(NameOf(HasSearchText))
                    RebuildRows()
                End If
            End Set
        End Property

        Public ReadOnly Property HasSearchText As Boolean
            Get
                Return Not String.IsNullOrEmpty(_searchText)
            End Get
        End Property

        ''' <summary>Die Zeile unter der Liste: "8 / 00:32:21 / 74,51 MB".</summary>
        Public ReadOnly Property PlaylistSummary As String
            Get
                Dim tracks = ActiveTracks()
                If tracks.Count = 0 Then Return String.Empty
                Dim totalSeconds = tracks.Sum(Function(t) t.DurationSeconds)
                Dim totalBytes = tracks.Sum(Function(t) t.FileSize)
                Dim sizeText = Track.FormatFileSize(totalBytes)
                Return If(String.IsNullOrEmpty(sizeText), $"{tracks.Count} / {FormatLongDuration(totalSeconds)}",
                          $"{tracks.Count} / {FormatLongDuration(totalSeconds)} / {sizeText}")
            End Get
        End Property

        Public ReadOnly Property IsPlaylistEmpty As Boolean
            Get
                Return ActiveTracks().Count = 0
            End Get
        End Property

        Public Property SelectedPlaylist As PlaylistKind
            Get
                Return _selectedPlaylist
            End Get
            Set(value As PlaylistKind)
                If Not SetField(_selectedPlaylist, value) Then Return
                RaisePropertyChanged(NameOf(IsFilesPlaylistSelected))
                RaisePropertyChanged(NameOf(IsAudioCdPlaylistSelected))
                RebuildRows()
            End Set
        End Property

        Public ReadOnly Property IsFilesPlaylistSelected As Boolean
            Get
                Return _selectedPlaylist = PlaylistKind.Files
            End Get
        End Property

        Public ReadOnly Property IsAudioCdPlaylistSelected As Boolean
            Get
                Return _selectedPlaylist = PlaylistKind.AudioCd
            End Get
        End Property

        Public ReadOnly Property HasAudioCd As Boolean
            Get
                Return _audioCdTracks.Count > 0
            End Get
        End Property

        Private Function ActiveTracks() As List(Of Track)
            Return If(_selectedPlaylist = PlaylistKind.AudioCd, _audioCdTracks, _tracks)
        End Function

        ' Einstellungen. Sie stehen hier und nicht in einem eigenen Bauplan, weil es (noch) wenige
        ' sind und jede von ihnen unmittelbar auf den Spieler oder die Ansicht wirkt. Kommen mehr
        ' dazu, gehoeren sie in einen SettingsViewModel neben diesen hier.

        Public Property IsGaplessEnabled As Boolean
            Get
                Return AppSettingsService.Current.GaplessPlayback
            End Get
            Set(value As Boolean)
                If AppSettingsService.Current.GaplessPlayback = value Then Return
                AppSettingsService.Current.GaplessPlayback = value
                _player.SetGapless(value)
                RaisePropertyChanged()
            End Set
        End Property

        Public Property TagAlbumArtistFollowsArtist As Boolean
            Get
                Return AppSettingsService.Current.TagAlbumArtistFollowsArtist
            End Get
            Set(value As Boolean)
                AppSettingsService.Current.TagAlbumArtistFollowsArtist = value
                AppSettingsService.Save()
            End Set
        End Property
        Public Property TagAlbumSortFollowsYear As Boolean
            Get
                Return AppSettingsService.Current.TagAlbumSortFollowsYear
            End Get
            Set(value As Boolean)
                AppSettingsService.Current.TagAlbumSortFollowsYear = value
                AppSettingsService.Save()
            End Set
        End Property

        Public Property TagRemoveOtherFields As Boolean
            Get
                Return AppSettingsService.Current.TagRemoveOtherFields
            End Get
            Set(value As Boolean)
                AppSettingsService.Current.TagRemoveOtherFields = value
                AppSettingsService.Save()
            End Set
        End Property

        Public Property TagPadTrackNumberToAlbumLength As Boolean
            Get
                Return AppSettingsService.Current.TagPadTrackNumberToAlbumLength
            End Get
            Set(value As Boolean)
                AppSettingsService.Current.TagPadTrackNumberToAlbumLength = value
                AppSettingsService.Save()
                RaisePropertyChanged(NameOf(TagFileNamePatternPreview))
            End Set
        End Property

        ''' <summary>Das Benennungsmuster fuer getaggte Dateien, in der Schreibweise von
        ''' Puddletag. Siehe <see cref="Mp3TagWriteService.BuildFileName"/>.</summary>
        Public Property TagFileNamePattern As String
            Get
                Return AppSettingsService.Current.TagFileNamePattern
            End Get
            Set(value As String)
                AppSettingsService.Current.TagFileNamePattern = AppSettingsService.NormalizeTagFileNamePattern(value)
                AppSettingsService.Save()
                RaisePropertyChanged()
                RaisePropertyChanged(NameOf(TagFileNamePatternPreview))
            End Set
        End Property

        ''' <summary>Ein erfundener Titel, am eingestellten Muster vorgefuehrt. Ein Muster
        ''' liest sich schlecht; ein Dateiname liest sich sofort.</summary>
        Public ReadOnly Property TagFileNamePatternPreview As String
            Get
                Try
                    Return Mp3TagWriteService.BuildFileName(TagFileNamePattern, SampleTagValues) & ".mp3"
                Catch ex As Exception
                    Return String.Empty
                End Try
            End Get
        End Property

        Private Shared ReadOnly Property SampleTagValues As Mp3TagWriteService.Values
            Get
                Return New Mp3TagWriteService.Values With {
                    .Artist = "Pink Floyd", .AlbumArtist = "Pink Floyd", .Album = "The Dark Side of the Moon",
                    .Title = "Money", .Genre = "Rock", .Year = 1973,
                    .TrackNumber = 6, .TotalTracks = 10, .DiscNumber = 1}
            End Get
        End Property
        Public Property ConverterDefaultFormat As Integer
            Get
                Return AppSettingsService.Current.ConverterDefaultFormat
            End Get
            Set(value As Integer)
                AppSettingsService.Current.ConverterDefaultFormat = Math.Clamp(value, 0, 2)
                AppSettingsService.Save()
            End Set
        End Property
        Public Property ConverterDefaultBitrateIndex As Integer
            Get
                ' Ueber die gepruefte Bitrate, damit ein fremder Wert in der Einstellungsdatei die
                ' Auswahlliste nicht leer laesst (Array.IndexOf gaebe -1).
                Return Array.IndexOf(AppSettingsService.ConverterBitrates, AppSettingsService.NormalizeConverterBitrate(AppSettingsService.Current.ConverterDefaultBitrate))
            End Get
            Set(value As Integer)
                Dim rates = AppSettingsService.ConverterBitrates
                AppSettingsService.Current.ConverterDefaultBitrate = rates(Math.Clamp(value, 0, rates.Length - 1))
                AppSettingsService.Save()
            End Set
        End Property
        Public Property ConverterDefaultVbr As Boolean
            Get
                Return AppSettingsService.Current.ConverterDefaultVbr
            End Get
            Set(value As Boolean)
                AppSettingsService.Current.ConverterDefaultVbr = value
                AppSettingsService.Save()
            End Set
        End Property
        Public Property TagGenresText As String
            Get
                Return String.Join(Environment.NewLine, AppSettingsService.Current.TagGenres)
            End Get
            Set(value As String)
                AppSettingsService.Current.TagGenres = If(value, String.Empty).Split({Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries).
                    Select(Function(entry) entry.Trim()).Where(Function(entry) entry.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList()
                AppSettingsService.Save()
            End Set
        End Property
        Public Property LyrionServerUrl As String
            Get
                Return AppSettingsService.Current.LyrionServerUrl
            End Get
            Set(value As String)
                AppSettingsService.Current.LyrionServerUrl = If(value, String.Empty).Trim()
                AppSettingsService.Save()
                RaisePropertyChanged()
                RaisePropertyChanged(NameOf(HasLyrionServer))
            End Set
        End Property
        ''' <summary>Die Lyrion-Bibliothek wird erst angeboten, wenn eine Serveradresse
        ''' hinterlegt ist. So bleibt die Wiedergabeliste ohne unkonfigurierten Leereintrag.</summary>
        Public ReadOnly Property HasLyrionServer As Boolean
            Get
                Return Not String.IsNullOrWhiteSpace(AppSettingsService.Current.LyrionServerUrl)
            End Get
        End Property
        Public Property LyrionClientName As String
            Get
                Return AppSettingsService.Current.LyrionClientName
            End Get
            Set(value As String)
                AppSettingsService.Current.LyrionClientName = If(value, String.Empty).Trim()
                AppSettingsService.Save()
            End Set
        End Property

        ''' <summary>Der Zielordner des Favoritenabgleichs. Siehe
        ''' <see cref="LyrionFavoriteSyncService"/>.</summary>
        Public Property LyrionSyncTargetPath As String
            Get
                Return AppSettingsService.Current.LyrionSyncTargetPath
            End Get
            Set(value As String)
                AppSettingsService.Current.LyrionSyncTargetPath = If(value, String.Empty).Trim()
                AppSettingsService.Save()
                RaisePropertyChanged()
            End Set
        End Property

        Public Property IsResumeOnStart As Boolean
            Get
                Return AppSettingsService.Current.ResumeOnStart
            End Get
            Set(value As Boolean)
                If AppSettingsService.Current.ResumeOnStart = value Then Return
                AppSettingsService.Current.ResumeOnStart = value
                RaisePropertyChanged()
            End Set
        End Property

        ' Lautstaerkeangleichung (ReplayGain). In den Einstellungen steht die Stufe als Zahl:
        ' 0 = aus, 1 = nach Titel, 2 = nach Album, 3 = automatisch.

        Public ReadOnly Property ReplayGainChoices As New ObservableCollection(Of ReplayGainChoice)()

        Public Property SelectedReplayGain As ReplayGainChoice
            Get
                Return ReplayGainChoices.FirstOrDefault(Function(c) c.Mode = AppSettingsService.Current.ReplayGainMode)
            End Get
            Set(value As ReplayGainChoice)
                If value Is Nothing OrElse value.Mode = AppSettingsService.Current.ReplayGainMode Then Return
                AppSettingsService.Current.ReplayGainMode = value.Mode
                ApplyReplayGain()
                RaisePropertyChanged()
                RaisePropertyChanged(NameOf(IsReplayGainEnabled))
            End Set
        End Property

        Public ReadOnly Property IsReplayGainEnabled As Boolean
            Get
                Return AppSettingsService.Current.ReplayGainMode <> 0
            End Get
        End Property

        Public Property ReplayGainPreamp As Double
            Get
                Return AppSettingsService.Current.ReplayGainPreamp
            End Get
            Set(value As Double)
                Dim normalized = AppSettingsService.NormalizeReplayGainPreamp(value)
                If AppSettingsService.Current.ReplayGainPreamp = normalized Then Return
                AppSettingsService.Current.ReplayGainPreamp = normalized
                ApplyReplayGain()
                RaisePropertyChanged()
                RaisePropertyChanged(NameOf(ReplayGainPreampText))
            End Set
        End Property

        ''' <summary>"+2,5 dB" - mit Vorzeichen, weil der Regler um die Null herum steht.</summary>
        Public ReadOnly Property ReplayGainPreampText As String
            Get
                Return AppSettingsService.Current.ReplayGainPreamp.ToString("+0.0;-0.0;0", Globalization.CultureInfo.CurrentCulture) & " dB"
            End Get
        End Property

        Private Sub BuildReplayGainChoices()
            ReplayGainChoices.Add(New ReplayGainChoice(0, "Aus"))
            ReplayGainChoices.Add(New ReplayGainChoice(1, "Nach Titel"))
            ReplayGainChoices.Add(New ReplayGainChoice(2, "Nach Album"))
            ReplayGainChoices.Add(New ReplayGainChoice(3, "Automatisch"))
        End Sub

        Private Sub ApplyReplayGain()
            _player.SetReplayGain(ResolveReplayGainMode(), AppSettingsService.Current.ReplayGainPreamp)
        End Sub

        ''' <summary>"Automatisch" gleicht nach Album an, bei zufaelliger Reihenfolge nach Titel: dort
        ''' folgen Titel verschiedener Alben aufeinander, und nur die Titelangabe bringt sie auf
        ''' eine Hoehe. Ein Album am Stueck behaelt mit der Albumangabe seine leisen und lauten
        ''' Stellen.</summary>
        Private Function ResolveReplayGainMode() As String
            Select Case AppSettingsService.Current.ReplayGainMode
                Case 1 : Return "track"
                Case 2 : Return "album"
                Case 3 : Return If(_isShuffle, "track", "album")
                Case Else : Return "no"
            End Select
        End Function

        Public Property IsBlurredBackgroundVisible As Boolean
            Get
                Return AppSettingsService.Current.ShowBlurredBackground
            End Get
            Set(value As Boolean)
                If AppSettingsService.Current.ShowBlurredBackground = value Then Return
                AppSettingsService.Current.ShowBlurredBackground = value
                RaisePropertyChanged()
                RaisePropertyChanged(NameOf(IsCoverBackdropVisible))
            End Set
        End Property

        ''' <summary>Der unscharfe Hintergrund wird nur gezeichnet, wenn er eingeschaltet ist UND
        ''' es ein Bild dazu gibt. Zwei Bedingungen an einer Stelle, damit die Ansicht sie nicht
        ''' verknuepfen muss.</summary>
        Public ReadOnly Property IsCoverBackdropVisible As Boolean
            Get
                Return AppSettingsService.Current.ShowBlurredBackground AndAlso _currentCover IsNot Nothing
            End Get
        End Property

        ' Schriftgroesse, Akzentfarbe, Vergroesserung

        Public ReadOnly Property FontSizeChoices As New ObservableCollection(Of FontSizeChoice)()
        Public ReadOnly Property AccentChoices As New ObservableCollection(Of AccentChoice)()

        ''' <summary>Die angeschlossenen Bildschirme. Bleibt leer, bis das Fenster sie meldet -
        ''' vorher weiss die Anwendung nichts von ihnen.</summary>
        Public ReadOnly Property ScreenScales As New ObservableCollection(Of ScreenScaleRow)()

        Public ReadOnly Property LanguageChoices As New ObservableCollection(Of LanguageChoice)()

        ''' <summary>Die gewaehlte Sprache. Wirkt sofort: das AXAML zieht die Anwendung ueber
        ''' LocalizationService.LanguageChanged nach, die Texte aus dem Code hier.</summary>
        Public Property SelectedLanguage As LanguageChoice
            Get
                Return LanguageChoices.FirstOrDefault(Function(c) c.Key = LocalizationService.LanguageMode)
            End Get
            Set(value As LanguageChoice)
                If value Is Nothing OrElse value.Key = LocalizationService.LanguageMode Then Return
                LocalizationService.LanguageMode = value.Key
                AppSettingsService.Current.LanguageMode = value.Key
                AppSettingsService.Save()
                RaisePropertyChanged()
                RefreshLocalizedText()
            End Set
        End Property

        ''' <summary>Meldet die Texte neu, die im Code gebaut werden. Eine Meldung in der Fussleiste
        ''' wird geleert statt uebersetzt: sie gehoert zu einem Vorgang, der vorbei ist.</summary>
        Private Sub RefreshLocalizedText()
            For Each choice In LanguageChoices
                choice.RefreshText()
            Next
            For Each choice In FontSizeChoices
                choice.RefreshText()
            Next
            For Each row In ScreenScales
                row.RefreshText()
            Next
            For Each choice In ReplayGainChoices
                choice.RefreshText()
            Next
            RaisePropertyChanged(NameOf(ReplayGainPreampText))
            RaisePropertyChanged(NameOf(AudioBackendText))
            RaisePropertyChanged(NameOf(FfmpegText))
            RaisePropertyChanged(NameOf(CdParanoiaText))
            RaisePropertyChanged(NameOf(CurrentDetail))
            StatusText = String.Empty
        End Sub

        Public ReadOnly Property SetFontSizeCommand As DelegateCommand
        Public ReadOnly Property SetAccentColorCommand As DelegateCommand

        ''' <summary>True, sobald ein Faktor verstellt wurde. Die Einstellungen zeigen daraufhin den
        ''' Hinweis, dass es erst beim naechsten Start wirkt - dauerhaft dort stehen soll er nicht,
        ''' er betrifft ja nur den, der gerade etwas geaendert hat.</summary>
        Public ReadOnly Property IsRestartNeeded As Boolean
            Get
                Return _restartNeeded
            End Get
        End Property

        Private Sub SetFontSizeOffset(offset As Integer)
            Dim normalized = FontScaleService.Normalize(offset)
            AppSettingsService.Current.FontSizeOffset = normalized
            FontScaleService.Apply(normalized)
            For Each choice In FontSizeChoices
                choice.IsActive = choice.Offset = normalized
            Next
        End Sub

        Private Sub SetAccentColor(hexColor As String)
            Dim normalized = AccentColorService.Normalize(hexColor)
            AppSettingsService.Current.AccentColor = normalized
            AccentColorService.Apply(normalized)
            For Each choice In AccentChoices
                choice.IsActive = String.Equals(choice.HexColor, normalized, StringComparison.OrdinalIgnoreCase)
            Next
        End Sub

        Private Sub BuildAppearanceChoices()
            For offset = -2 To 6
                FontSizeChoices.Add(New FontSizeChoice(offset))
            Next
            For Each hexColor In AccentColorService.Palette
                AccentChoices.Add(New AccentChoice(hexColor))
            Next

            Dim settings = AppSettingsService.Current
            For Each choice In FontSizeChoices
                choice.IsActive = choice.Offset = settings.FontSizeOffset
            Next
            For Each choice In AccentChoices
                choice.IsActive = String.Equals(choice.HexColor, settings.AccentColor, StringComparison.OrdinalIgnoreCase)
            Next
        End Sub

        ''' <summary>Meldet die angeschlossenen Bildschirme. Wird vom Fenster gerufen, sobald es
        ''' steht: Avalonia kennt sie erst dann. Vorhandene Faktoren aus den Einstellungen werden
        ''' uebernommen, unbekannte Bildschirme fangen bei 1,0 an.</summary>
        Public Sub SetScreens(screens As IEnumerable(Of (Name As String, Description As String)))
            ScreenScales.Clear()
            If screens Is Nothing Then Return

            Dim stored = AppSettingsService.Current.ApplicationScaleFactors
            For Each screen In screens
                Dim name = AppSettingsService.NormalizeScreenName(screen.Name)
                If String.IsNullOrEmpty(name) Then Continue For
                Dim known = stored.FirstOrDefault(Function(f) String.Equals(f.ScreenName, name, StringComparison.Ordinal))
                Dim row = New ScreenScaleRow(name, screen.Description, If(known Is Nothing, 1.0, known.Scale))
                AddHandler row.PropertyChanged, AddressOf OnScreenScaleChanged
                ScreenScales.Add(row)
            Next

            RaisePropertyChanged(NameOf(AreScreenScalesVisible))
        End Sub

        ''' <summary>Die Vergroesserung steht nur unter Linux in den Einstellungen. Unter Windows
        ''' und macOS skaliert Avalonia von sich aus je Bildschirm; ein Regler, der dort nichts
        ''' bewirkt, ist schlimmer als keiner.</summary>
        Public ReadOnly Property AreScreenScalesVisible As Boolean
            Get
                Return OperatingSystem.IsLinux() AndAlso ScreenScales.Count > 0
            End Get
        End Property

        Private Sub OnScreenScaleChanged(sender As Object, e As ComponentModel.PropertyChangedEventArgs)
            If Not String.Equals(e.PropertyName, NameOf(ScreenScaleRow.Scale), StringComparison.Ordinal) Then Return

            AppSettingsService.Current.ApplicationScaleFactors =
                ScreenScales.Select(Function(r) New ScreenScaleSetting With {
                    .ScreenName = r.ScreenName,
                    .Scale = r.Scale}).ToList()

            If _restartNeeded Then Return
            _restartNeeded = True
            RaisePropertyChanged(NameOf(IsRestartNeeded))
        End Sub

        Public ReadOnly Property DisplayVersion As String
            Get
                Dim version = Reflection.Assembly.GetExecutingAssembly().GetName().Version
                Return If(version Is Nothing, "0.0.0", $"{version.Major}.{version.Minor}.{version.Build}")
            End Get
        End Property

        Public ReadOnly Property SettingsFilePath As String
            Get
                Return AppSettingsService.SettingsPath
            End Get
        End Property

        Public ReadOnly Property PlaylistFilePath As String
            Get
                Return PlaylistStore.PlaylistPath
            End Get
        End Property

        ''' <summary>Ob libmpv gefunden wurde. Ohne sie spielt die Anwendung nichts ab, und das
        ''' will man in den Einstellungen ablesen koennen statt an einem stummen Knopf.</summary>
        Public ReadOnly Property AudioBackendText As String
            Get
                Return LocalizationService.T(If(MpvInterop.IsAvailable(), "libmpv gefunden", "libmpv fehlt"))
            End Get
        End Property

        ''' <summary>Zeigt in den Einstellungen, ob FFmpeg für die Konvertierung verfügbar ist.</summary>
        Public ReadOnly Property FfmpegText As String
            Get
                Return LocalizationService.T(If(AudioConversionService.IsFfmpegAvailable(), "FFmpeg gefunden", "FFmpeg fehlt"))
            End Get
        End Property

        ''' <summary>Zeigt in den Einstellungen, ob cdparanoia zum Auslesen von Audio-CDs verfügbar ist.</summary>
        Public ReadOnly Property CdParanoiaText As String
            Get
                Return LocalizationService.T(If(AudioConversionService.IsCdParanoiaAvailable(), "cdparanoia gefunden", "cdparanoia fehlt"))
            End Get
        End Property

        Public Property StatusText As String
            Get
                Return _statusText
            End Get
            Set(value As String)
                SetField(_statusText, If(value, String.Empty))
            End Set
        End Property

        Public Property SidePanelWidth As Double
            Get
                Return _sidePanelWidth
            End Get
            Set(value As Double)
                If SetField(_sidePanelWidth, Math.Clamp(value, 220, 520)) Then RaisePropertyChanged(NameOf(CoverSize))
            End Set
        End Property

        ''' <summary>Die Kantenlaenge des Titelbildes. Ausgerechnet und nicht aus dem Layout
        ''' abgelesen: ein Element, das seine Hoehe aus der eigenen Breite bezieht, misst sich im
        ''' Layout-Durchlauf selbst und stoesst ihn dabei erneut an. Abgezogen wird der Rand der
        ''' Spalte, zweimal 22 Punkte.</summary>
        Public ReadOnly Property CoverSize As Double
            Get
                Return Math.Max(0, _sidePanelWidth - 44)
            End Get
        End Property

        ' Der laufende Titel

        Public ReadOnly Property CurrentTrack As Track
            Get
                Return _currentTrack
            End Get
        End Property

        Public ReadOnly Property CurrentCover As Bitmap
            Get
                Return _currentCover
            End Get
        End Property

        Public ReadOnly Property HasCover As Boolean
            Get
                Return _currentCover IsNot Nothing
            End Get
        End Property

        Public ReadOnly Property CurrentTitle As String
            Get
                Return If(_currentTrack Is Nothing, String.Empty, _currentTrack.ShortTitle)
            End Get
        End Property

        Public ReadOnly Property CurrentArtist As String
            Get
                Return If(_currentTrack Is Nothing, String.Empty, _currentTrack.Artist)
            End Get
        End Property

        Public ReadOnly Property CurrentAlbum As String
            Get
                Return If(_currentTrack Is Nothing, String.Empty, _currentTrack.Album)
            End Get
        End Property

        Public ReadOnly Property CurrentDetail As String
            Get
                Return If(_currentTrack Is Nothing, String.Empty, _currentTrack.DetailText)
            End Get
        End Property

        ''' <summary>Das Jahr unter dem Titelbild. Ohne Jahresangabe bleibt die Zeile leer und
        ''' verschwindet, statt eine Null zu zeigen.</summary>
        Public ReadOnly Property CurrentYearText As String
            Get
                If _currentTrack Is Nothing OrElse _currentTrack.Year <= 0 Then Return String.Empty
                Return _currentTrack.Year.ToString(CultureInfo.InvariantCulture)
            End Get
        End Property

        Public ReadOnly Property HasCurrentYear As Boolean
            Get
                Return CurrentYearText.Length > 0
            End Get
        End Property

        Public ReadOnly Property HasCurrentTrack As Boolean
            Get
                Return _currentTrack IsNot Nothing
            End Get
        End Property

        ' Transport

        Public Property PositionSeconds As Double
            Get
                Return _positionSeconds
            End Get
            Set(value As Double)
                _mpris?.UpdatePosition(value)
                If SetField(_positionSeconds, value) Then RaisePropertyChanged(NameOf(PositionText))
            End Set
        End Property

        Public Property DurationSeconds As Double
            Get
                Return _durationSeconds
            End Get
            Set(value As Double)
                If SetField(_durationSeconds, value) Then RaisePropertyChanged(NameOf(DurationText))
            End Set
        End Property

        Public ReadOnly Property PositionText As String
            Get
                Return Track.FormatDuration(_positionSeconds)
            End Get
        End Property

        Public ReadOnly Property DurationText As String
            Get
                Return Track.FormatDuration(_durationSeconds)
            End Get
        End Property

        Public Property IsPlaying As Boolean
            Get
                Return _isPlaying
            End Get
            Private Set(value As Boolean)
                If SetField(_isPlaying, value) Then RaisePropertyChanged(NameOf(PlayPauseIconSource))
            End Set
        End Property

        Public ReadOnly Property PlayPauseIconSource As String
            Get
                Return If(_isPlaying,
                          "avares://FerrumPlay/Assets/Icons/outline/player-pause.svg",
                          "avares://FerrumPlay/Assets/Icons/outline/player-play.svg")
            End Get
        End Property

        Public Property Volume As Double
            Get
                Return _volume
            End Get
            Set(value As Double)
                Dim clamped = Math.Clamp(value, 0, 100)
                If Not SetField(_volume, clamped) Then Return
                RaisePropertyChanged(NameOf(VolumeIconSource))
                ' Ferngesteuert geht die Lautstaerke ans Geraet und wird NICHT gemerkt: gemerkt
                ' gehoert die der oertlichen Wiedergabe, sonst truege sie beim naechsten Start den
                ' Stand eines fremden Geraets.
                If RemoteSetVolume(clamped) Then Return
                _player.SetVolume(clamped)
                AppSettingsService.Current.Volume = clamped
            End Set
        End Property

        Public Property IsMuted As Boolean
            Get
                Return _isMuted
            End Get
            Set(value As Boolean)
                If Not SetField(_isMuted, value) Then Return
                RaisePropertyChanged(NameOf(VolumeIconSource))
                If RemoteSetMuted(value) Then Return
                _player.SetMuted(value)
                AppSettingsService.Current.Muted = value
            End Set
        End Property

        ''' <summary>Das Lautsprechersymbol folgt der Lautstaerke. Stumm und "auf null gedreht"
        ''' klingen gleich, sehen aber unterschiedlich aus, und das ist gewollt: nur eines von
        ''' beidem laesst sich mit einem Klick rueckgaengig machen.</summary>
        Public ReadOnly Property VolumeIconSource As String
            Get
                If _isMuted Then Return "avares://FerrumPlay/Assets/Icons/outline/volume-3.svg"
                If _volume < 1 Then Return "avares://FerrumPlay/Assets/Icons/outline/volume-3.svg"
                If _volume < 50 Then Return "avares://FerrumPlay/Assets/Icons/outline/volume-2.svg"
                Return "avares://FerrumPlay/Assets/Icons/outline/volume.svg"
            End Get
        End Property

        Public Property IsShuffle As Boolean
            Get
                Return _isShuffle
            End Get
            Set(value As Boolean)
                If Not SetField(_isShuffle, value) Then Return
                AppSettingsService.Current.Shuffle = value
                If RemoteSetShuffle(value) Then Return
                RebuildPlayOrder()
                RebuildLyrionPlayOrder()
                RebuildRows()
                ' Die automatische Angleichung haengt an der Reihenfolge.
                ApplyReplayGain()
            End Set
        End Property

        Public Property Repeat As RepeatMode
            Get
                Return _repeat
            End Get
            Set(value As RepeatMode)
                If Not SetField(_repeat, value) Then Return
                AppSettingsService.Current.RepeatMode = CInt(value)
                RaisePropertyChanged(NameOf(RepeatIconSource))
                RaisePropertyChanged(NameOf(IsRepeatActive))
                RemoteSetRepeat(value)
            End Set
        End Property

        Public ReadOnly Property IsRepeatActive As Boolean
            Get
                Return _repeat <> RepeatMode.Off
            End Get
        End Property

        Public ReadOnly Property RepeatIconSource As String
            Get
                Return If(_repeat = RepeatMode.Single,
                          "avares://FerrumPlay/Assets/Icons/outline/repeat-once.svg",
                          "avares://FerrumPlay/Assets/Icons/outline/repeat.svg")
            End Get
        End Property

        Private Sub CycleRepeat()
            Select Case _repeat
                Case RepeatMode.Off : Repeat = RepeatMode.All
                Case RepeatMode.All : Repeat = RepeatMode.Single
                Case Else : Repeat = RepeatMode.Off
            End Select
        End Sub

        ' Wiedergabe

        ''' <summary>Spielt einen gezielt gewaehlten Titel: Doppelklick, Aufruf, "Oeffnen mit". Laesst
        ''' er sich nicht oeffnen, bleibt es dabei - die Wiedergabe springt dann nicht ungefragt auf
        ''' einen anderen.</summary>
        Public Sub Play(track As Track)
            If Not _lyrionTracks.Contains(track) Then
                _lyrionTracks.Clear()
                ' Mit den Titeln faellt auch ihre Reihenfolge. Sonst gibt LyrionPlayOrder weiter
                ' eine Warteschlange aus, die niemand mehr spielt, und der Lyrion-Bereich springt
                ' beim naechsten Blick darauf in ein Album, das gar nicht mehr laeuft.
                If _lyrionPlayOrder.Count > 0 Then
                    _lyrionPlayOrder = New List(Of Track)()
                    RaiseEvent LyrionPlayOrderChanged(_lyrionPlayOrder.AsReadOnly())
                End If
            End If
            _consecutiveFailures = 0
            PlayCore(track, skipDirection:=0)
        End Sub

        Public Sub PlayLyrionAlbum(tracks As IEnumerable(Of Track), selected As Track)
            Dim queue = If(tracks, Enumerable.Empty(Of Track)()).Where(Function(track) track IsNot Nothing).ToList()
            If selected Is Nothing OrElse Not queue.Contains(selected) Then Return
            _lyrionTracks.Clear()
            _lyrionTracks.AddRange(queue)
            ' Erst spielen, dann die Reihenfolge bilden: bei zufaelliger Reihenfolge zieht sie den
            ' LAUFENDEN Titel nach vorn, und der ist bis zum Play noch der vorige.
            Play(selected)
            RebuildLyrionPlayOrder()
        End Sub

        ''' <summary>Blendet den Titel in der Liste ein und laesst die Ansicht darauf springen.
        ''' Das Ereignis bleibt bei der Ansicht, weil nur sie weiss, welches ListBox-Steuerelement
        ''' gerade den sichtbaren Bereich besitzt.</summary>
        Public Sub FocusTrackInPlaylist(track As Track)
            If track Is Nothing OrElse (Not _tracks.Contains(track) AndAlso Not _audioCdTracks.Contains(track)) Then Return
            SelectedPlaylist = If(track.IsAudioCdTrack, PlaylistKind.AudioCd, PlaylistKind.Files)
            If _collapsedFolders.Remove(track.FolderPath) Then RebuildRows()
            RaiseEvent PlaylistFocusRequested(track)
        End Sub

        ''' <param name="skipDirection">Wohin es geht, wenn sich der Titel nicht oeffnen laesst:
        ''' +1 weiter, -1 zurueck, 0 gar nicht. Siehe <see cref="HandleUnplayableTrack"/>.</param>
        Private Sub PlayCore(track As Track, skipDirection As Integer)
            If track Is Nothing Then Return

            ' Derselbe Titel von vorn (Wiederholen eines Titels) aendert an den Angaben nichts;
            ' MPRIS erfaehrt davon nur ueber Seeked.
            Dim restarting = Object.ReferenceEquals(track, _currentTrack) AndAlso Not _isStopped

            _currentTrack = track
            RaisePropertyChanged(NameOf(IsPlayingLyrion))
            If _lyrionTracks.Contains(track) Then RaiseEvent LyrionCurrentTrackChanged(track)
            _skipDirection = skipDirection
            _isStopped = False
            RaiseCurrentTrackChanged()
            UpdatePlayingRow()
            FocusTrackInPlaylist(track)

            ' Laufzeit und Stelle gehoeren zum alten Titel. Sie stehen zu lassen, bis mpv das erste
            ' Mal meldet, zeigte den Balken des vorigen Titels ueber dem neuen.
            PositionSeconds = 0
            DurationSeconds = track.DurationSeconds
            _audioCdTimeOffset = Nothing
            _audioCdPlaybackLoaded = Not track.IsAudioCdTrack

            _player.Load(track.FilePath, force:=True)
            _player.Play()
            IsPlaying = True

            ' Die Adresse eines Lyrion-Streams steht in keiner gespeicherten Liste und liesse
            ' sich beim naechsten Start zu keiner Zeile machen. Dann bleibt der zuletzt gespielte
            ' oertliche Titel stehen, und das Fortsetzen landet wieder dort.
            If Not _lyrionTracks.Contains(track) Then AppSettingsService.Current.LastTrackPath = track.FilePath
            LoadCoverAsync(track)
            If restarting Then _mpris?.NotifySeeked(0)
            PublishMpris()
        End Sub

        Private Sub TogglePlayPause()
            ' Ferngesteuert? Dann gehoert der Knopf dem Geraet. Diese Pruefung steht in JEDER
            ' Einsprungstelle der Transportsteuerung ganz vorn.
            If RemoteTogglePlayPause() Then Return
            If _currentTrack Is Nothing Then
                Dim first = FirstPlayableTrack()
                If first Is Nothing Then Return
                Play(first)
                Return
            End If

            ' Nach einem Stop ist bei mpv nichts mehr geladen. Ein Pausenwechsel liefe dann ins
            ' Leere, und der Knopf saehe aus, als haenge er.
            If _player.LoadedPath Is Nothing Then
                Play(_currentTrack)
                Return
            End If

            _player.TogglePause()
        End Sub

        Private Sub StopPlayback()
            If RemoteStop() Then Return
            _player.Stop()
            IsPlaying = False
            _isStopped = True
            PositionSeconds = 0
            PublishMpris()
        End Sub

        ''' <summary>Der naechste Titel. <paramref name="userRequested"/> unterscheidet den Klick
        ''' vom Ende eines Titels: nur beim Ende gilt "einzelnen Titel wiederholen", sonst kaeme
        ''' man mit dem Knopf nie weiter.</summary>
        Private Sub PlayNext(userRequested As Boolean)
            If RemoteNext() Then Return
            If _repeat = RepeatMode.Single AndAlso Not userRequested AndAlso _currentTrack IsNot Nothing Then
                PlayCore(_currentTrack, skipDirection:=0)
                Return
            End If

            ' Am Ende angekommen: ohne Wiederholung endet die Wiedergabe hier, statt wieder von
            ' vorn zu beginnen - von selbst neu anzufangen hat niemand verlangt. Der Knopf darf
            ' dagegen immer herum.
            Dim [next] = FindNeighbour(1, wrap:=userRequested OrElse _repeat <> RepeatMode.Off)
            If [next] Is Nothing Then
                If Not userRequested Then StopPlayback()
                Return
            End If
            PlayCore([next], skipDirection:=1)
        End Sub

        ''' <summary>Der vorige Titel. Laeuft der aktuelle schon laenger als drei Sekunden, springt
        ''' der Knopf zunaechst an dessen Anfang. So wie an jeder Anlage.</summary>
        Private Sub PlayPrevious()
            If RemotePrevious() Then Return
            If _currentTrack IsNot Nothing AndAlso _positionSeconds > 3 Then
                SeekTo(0)
                Return
            End If

            Dim previous = FindNeighbour(-1, wrap:=True)
            If previous Is Nothing Then Return
            PlayCore(previous, skipDirection:=-1)
        End Sub

        Public Sub SeekTo(seconds As Double)
            If RemoteSeek(seconds) Then Return
            If _currentTrack Is Nothing Then Return
            _player.Seek(seconds)
            PositionSeconds = seconds
            _mpris?.NotifySeeked(seconds)
        End Sub

        ''' <summary>Die Abspielreihenfolge ohne die Titel, die nicht dran kommen.</summary>
        Private Function PlayableOrder() As List(Of Track)
            Return CurrentPlayOrder().Where(AddressOf IsPlayable).ToList()
        End Function

        ''' <summary>Dran kommt ein Titel, der angehakt ist und dessen Datei nicht als fehlend gilt.</summary>
        Private Function IsPlayable(track As Track) As Boolean
            Dim row As PlaylistTrackRow = Nothing
            If Not _rowsByTrack.TryGetValue(track, row) Then Return True
            Return row.IsEnabled AndAlso Not row.IsMissing
        End Function

        Private Function FirstPlayableTrack() As Track
            Dim order = PlayableOrder()
            Return If(order.Count = 0, Nothing, order(0))
        End Function

        ''' <summary>Der naechste Titel, der dran kommt, in <paramref name="direction"/> (+1 oder -1)
        ''' vom laufenden aus. Gezaehlt wird in der VOLLEN Abspielreihenfolge und nicht in einer
        ''' gefilterten: ist der laufende Titel selbst nicht mehr abspielbar - abgehakt, Datei weg -,
        ''' fehlt er in der gefilterten, und die Suche finge wieder vorn an.</summary>
        ''' <param name="wrap">Ob es ueber das Ende hinaus am anderen Ende weitergehen darf.</param>
        Private Function FindNeighbour(direction As Integer, wrap As Boolean) As Track
            Dim order = CurrentPlayOrder()
            Dim count = order.Count
            If count = 0 Then Return Nothing

            Dim start = If(_currentTrack Is Nothing, -1, order.IndexOf(_currentTrack))
            ' Rueckwaerts ohne laufenden Titel beginnt am Ende.
            If start < 0 AndAlso direction < 0 Then start = 0

            For stepCount = 1 To count
                Dim index = start + direction * stepCount
                If index < 0 OrElse index >= count Then
                    If Not wrap Then Return Nothing
                    index = ((index Mod count) + count) Mod count
                End If
                Dim candidate = order(index)
                If IsPlayable(candidate) Then Return candidate
            Next
            Return Nothing
        End Function

        ''' <summary>Der gerade gestartete Titel liess sich nicht oeffnen: die Datei fehlt
        ''' (<paramref name="missing"/>), oder mpv kann sie nicht lesen. Kam der Titel ueber
        ''' Weiter, Zurueck oder das Ende des vorigen, geht es in derselben Richtung weiter; war er
        ''' gezielt gewaehlt, bleibt die Wiedergabe stehen. Hoechstens
        ''' <see cref="MaxConsecutiveFailures"/> Mal hintereinander - eine Liste aus lauter
        ''' kaputten Dateien liefe sonst mit Wiederholen endlos im Kreis.</summary>
        Private Sub HandleUnplayableTrack(filePath As String, missing As Boolean)
            Dim track = _currentTrack
            If track Is Nothing OrElse _isStopped OrElse
               Not String.Equals(track.FilePath, filePath, StringComparison.Ordinal) Then Return

            If missing Then SetMissing(track, True)
            StatusText = LocalizationService.Format(
                If(missing, "Datei nicht gefunden: {0}", "Titel lässt sich nicht abspielen: {0}"), track.DisplayTitle)

            _consecutiveFailures += 1
            Dim direction = _skipDirection
            If direction <> 0 AndAlso _consecutiveFailures < MaxConsecutiveFailures Then
                Dim neighbour = FindNeighbour(direction, wrap:=direction < 0 OrElse _repeat <> RepeatMode.Off)
                If neighbour IsNot Nothing AndAlso Not Object.ReferenceEquals(neighbour, track) Then
                    PlayCore(neighbour, direction)
                    Return
                End If
            End If

            StopPlayback()
        End Sub

        Private Sub RebuildPlayOrder()
            If Not _isShuffle Then
                _playOrder = New List(Of Track)(_tracks)
                Return
            End If

            ' Fisher-Yates. Der laufende Titel bleibt vorn, damit das Einschalten von Zufall nicht
            ' mitten im Stueck den naechsten Titel unter dem Finger wegzieht.
            Dim shuffled = New List(Of Track)(_tracks)
            For index = shuffled.Count - 1 To 1 Step -1
                Dim swap = _shuffleRandom.Next(index + 1)
                Dim temporary = shuffled(index)
                shuffled(index) = shuffled(swap)
                shuffled(swap) = temporary
            Next

            If _currentTrack IsNot Nothing Then
                Dim position = shuffled.IndexOf(_currentTrack)
                If position > 0 Then
                    shuffled.RemoveAt(position)
                    shuffled.Insert(0, _currentTrack)
                End If
            End If

            _playOrder = shuffled
        End Sub

        Private Function CurrentPlayOrder() As List(Of Track)
            If _currentTrack IsNot Nothing AndAlso _lyrionTracks.Contains(_currentTrack) Then Return _lyrionPlayOrder
            If _currentTrack IsNot Nothing AndAlso _currentTrack.IsAudioCdTrack Then Return _audioCdTracks
            Return If(_selectedPlaylist = PlaylistKind.AudioCd, _audioCdTracks, _playOrder)
        End Function

        Private Sub RebuildLyrionPlayOrder()
            _lyrionPlayOrder = New List(Of Track)(_lyrionTracks)
            If _isShuffle Then
                For index = _lyrionPlayOrder.Count - 1 To 1 Step -1
                    Dim swap = _shuffleRandom.Next(index + 1)
                    Dim temporary = _lyrionPlayOrder(index)
                    _lyrionPlayOrder(index) = _lyrionPlayOrder(swap)
                    _lyrionPlayOrder(swap) = temporary
                Next
                If _currentTrack IsNot Nothing Then
                    Dim position = _lyrionPlayOrder.IndexOf(_currentTrack)
                    If position > 0 Then
                        _lyrionPlayOrder.RemoveAt(position)
                        _lyrionPlayOrder.Insert(0, _currentTrack)
                    End If
                End If
            End If
            RaiseEvent LyrionPlayOrderChanged(_lyrionPlayOrder.AsReadOnly())
        End Sub

        ' Das Titelbild

        Private Sub LoadCoverAsync(track As Track)
            If track.IsAudioCdTrack Then
                _currentCover = Nothing
                RaisePropertyChanged(NameOf(CurrentCover))
                RaisePropertyChanged(NameOf(HasCover))
                RaisePropertyChanged(NameOf(IsCoverBackdropVisible))
                Return
            End If
            Dim request = Interlocked.Increment(_coverRequest)
            Dim path = track.FilePath
            ' MPRIS braucht das Bild als Datei. Ohne MPRIS spart man sich das Auslagern.
            Dim exportArt = _mpris IsNot Nothing

            Task.Run(Sub()
                         Dim bitmap As Bitmap
                         Dim artFile As String
                         If String.IsNullOrWhiteSpace(track.RemoteCoverUrl) Then
                             bitmap = CoverArtService.Load(path)
                             artFile = If(exportArt, CoverArtService.ExportArtFile(path), String.Empty)
                         Else
                             ' Fuer einen Stream gibt es keine Tondatei zum Auslagern: ExportArtFile
                             ' liefe ueber TagLib und Verzeichnispruefungen ins Leere. Das Bild kommt
                             ' ueber das Netz und wird beim Laden gleich mit abgelegt, sonst ginge
                             ' MPRIS ohne Bild hinaus.
                             Dim remote = CoverArtService.LoadRemote(track.RemoteCoverUrl, exportArt)
                             bitmap = remote.Cover
                             artFile = remote.ArtFile
                         End If
                         Dispatcher.UIThread.Post(
                             Sub()
                                 If Volatile.Read(_coverRequest) <> request Then Return
                                 _currentCover = bitmap
                                 RaisePropertyChanged(NameOf(CurrentCover))
                                 RaisePropertyChanged(NameOf(HasCover))
                                 RaisePropertyChanged(NameOf(IsCoverBackdropVisible))
                                 If exportArt Then SetMprisArt(track, artFile)
                             End Sub)
                     End Sub)
        End Sub

        ''' <summary>Liest Anzeige und Titelbild des laufenden Titels neu. Nach dem Taggen
        ''' stimmt beides nicht mehr: die Datei traegt jetzt andere Kennzeichen, ein anderes Bild
        ''' und womoeglich einen anderen Namen.</summary>
        Public Sub RefreshAfterTagging()
            RebuildRows()
            If _currentTrack Is Nothing Then Return
            RaiseCurrentTrackChanged()
            LoadCoverAsync(_currentTrack)
        End Sub

        Private Sub RaiseCurrentTrackChanged()
            RaisePropertyChanged(NameOf(CurrentTrack))
            RaisePropertyChanged(NameOf(CurrentTitle))
            RaisePropertyChanged(NameOf(CurrentArtist))
            RaisePropertyChanged(NameOf(CurrentAlbum))
            RaisePropertyChanged(NameOf(CurrentYearText))
            RaisePropertyChanged(NameOf(HasCurrentYear))
            RaisePropertyChanged(NameOf(CurrentDetail))
            RaisePropertyChanged(NameOf(HasCurrentTrack))
        End Sub

        Private Sub UpdatePlayingRow()
            For Each row In _rowsByTrack.Values
                row.IsPlaying = Object.ReferenceEquals(row.Track, _currentTrack)
            Next
        End Sub

        ' Die Liste fuellen

        ''' <summary>Nimmt Dateien und Ordner an, wie sie aus einem Dialog oder von einem
        ''' Ablegen kommen. Das Einlesen laeuft im Hintergrund; die Liste waechst dabei in
        ''' Haeppchen, damit man den ersten Titel schon starten kann, waehrend der Rest noch
        ''' kommt.</summary>
        ''' <param name="playFirst">Den ersten eingelesenen Titel sofort spielen, sobald das
        ''' erste Haeppchen da ist - nicht erst, wenn ein grosser Ordner ganz durch ist. Steht er
        ''' schon in der Liste, wird der vorhandene Eintrag gespielt.</param>
        Public Async Function AddPathsAsync(paths As IEnumerable(Of String), Optional playFirst As Boolean = False) As Task
            If paths Is Nothing Then Return
            Dim requested = paths.Where(Function(p) Not String.IsNullOrWhiteSpace(p)).ToList()
            If requested.Count = 0 Then Return

            If _isScanning Then
                _queuedAdds.Enqueue((requested, playFirst))
                StatusText = LocalizationService.T("Es wird noch eingelesen.")
                Return
            End If

            _isScanning = True
            StatusText = LocalizationService.T("Wird eingelesen.")
            Try
                Dim files = Await Task.Run(Function() CollectFiles(requested, _shutdown.Token))
                Dim added = 0

                For Each portion In SplitIntoBatches(files, 200)
                    Dim batch = Await Task.Run(Function() ReadTracks(portion, _shutdown.Token))
                    If _shutdown.IsCancellationRequested Then Exit For
                    AppendTracks(batch)
                    added += batch.Count

                    Dim first = batch.FirstOrDefault()
                    If playFirst AndAlso first IsNot Nothing Then
                        playFirst = False
                        Play(_tracks.FirstOrDefault(Function(t) String.Equals(t.FilePath, first.FilePath, StringComparison.Ordinal)))
                    End If
                    StatusText = LocalizationService.Format("{0} Titel eingelesen.", added)
                Next

                StatusText = If(added = 0, LocalizationService.T("Keine abspielbaren Dateien gefunden."), String.Empty)
                SavePlaylist()
            Catch ex As Exception
                DiagnosticLogService.LogException("Playlist.Add", ex)
                StatusText = LocalizationService.T("Das Einlesen ist fehlgeschlagen.")
            Finally
                _isScanning = False
            End Try

            ' Was waehrenddessen kam, ist jetzt dran - eines nach dem anderen, in der Reihenfolge,
            ' in der es kam.
            If _queuedAdds.Count > 0 AndAlso Not _shutdown.IsCancellationRequested Then
                Dim queued = _queuedAdds.Dequeue()
                Await AddPathsAsync(queued.Paths, queued.PlayFirst)
            End If
        End Function

        ''' <summary>Liest die eingelegte Audio-CD ein. CDDA-Titel sind keine Dateien und werden
        ''' deshalb getrennt vom Dateiscanner behandelt. Sie leben nur in dieser Sitzung: nach
        ''' einem Neustart kann dasselbe Laufwerk eine andere CD enthalten.</summary>
        Public Async Function AddAudioCdAsync() As Task
            StatusText = LocalizationService.T("Audio-CD wird gelesen.")
            Try
                Dim tracks = Await Task.Run(AddressOf AudioCdService.ReadFirstDisc)
                If _shutdown.IsCancellationRequested Then Return
                If tracks.Count = 0 Then
                    StatusText = LocalizationService.T("Keine Audio-CD gefunden oder das Laufwerk ist nicht lesbar.")
                    Return
                End If
                SetAudioCdTracks(tracks, selectPlaylist:=True)
                StatusText = LocalizationService.Format("{0} Titel von Audio-CD verfügbar.", tracks.Count)
            Catch ex As Exception
                DiagnosticLogService.LogException("AudioCd.Add", ex)
                StatusText = LocalizationService.T("Die Audio-CD konnte nicht gelesen werden.")
            End Try
        End Function

        ''' <summary>Der Monitor fragt in kleinen Abstaenden die TOC ab. Nur ein Lauf darf zugleich
        ''' lesen; optische Laufwerke reagieren auf parallele TOC-Anfragen teilweise traege.</summary>
        Private Async Sub CheckAudioCd()
            If _shutdown.IsCancellationRequested OrElse Interlocked.Exchange(_audioCdCheckRunning, 1) <> 0 Then Return
            Try
                Dim tracks = Await Task.Run(AddressOf AudioCdService.ReadFirstDisc)
                Dispatcher.UIThread.Post(Sub() SetAudioCdTracks(tracks, selectPlaylist:=False))
            Catch ex As Exception
                DiagnosticLogService.LogException("AudioCd.Monitor", ex)
            Finally
                Interlocked.Exchange(_audioCdCheckRunning, 0)
            End Try
        End Sub

        Private Sub SetAudioCdTracks(tracks As List(Of Track), selectPlaylist As Boolean)
            If _shutdown.IsCancellationRequested Then Return
            tracks = If(tracks, New List(Of Track)())
            Dim unchanged = tracks.Count = _audioCdTracks.Count AndAlso
                            tracks.Select(Function(track) track.FilePath).SequenceEqual(_audioCdTracks.Select(Function(track) track.FilePath), StringComparer.Ordinal)
            If unchanged Then Return

            For Each track In _audioCdTracks
                _rowsByTrack.Remove(track)
            Next
            _audioCdTracks.Clear()
            _audioCdTracks.AddRange(tracks)
            For Each track In _audioCdTracks
                _rowsByTrack(track) = New PlaylistTrackRow(track)
            Next
            RaisePropertyChanged(NameOf(HasAudioCd))

            If _selectedPlaylist = PlaylistKind.AudioCd OrElse selectPlaylist Then
                SelectedPlaylist = PlaylistKind.AudioCd
                ' SetField ruft bei bereits ausgewaehlter CD nicht neu auf.
                RebuildRows()
            End If
        End Sub

        ''' <summary>Sammelt die abspielbaren Dateien unter den angegebenen Pfaden. Ordner werden
        ''' durchgegangen, Dateien direkt genommen. Sortiert wird je Ordner nach Dateiname, weil
        ''' das Betriebssystem keine Reihenfolge zusichert und ein Album sonst gemischt ankaeme.</summary>
        Private Shared Function CollectFiles(paths As List(Of String), cancellation As CancellationToken) As List(Of String)
            Dim files As New List(Of String)()

            For Each path In paths
                If cancellation.IsCancellationRequested Then Exit For
                Try
                    If Directory.Exists(path) Then
                        CollectFolder(path, files, cancellation)
                    ElseIf File.Exists(path) AndAlso TagReadService.IsSupportedFile(path) Then
                        files.Add(path)
                    End If
                Catch ex As Exception
                    DiagnosticLogService.LogException("Playlist.Collect", ex)
                End Try
            Next

            Return files
        End Function

        Private Shared Sub CollectFolder(folder As String, files As List(Of String), cancellation As CancellationToken)
            If cancellation.IsCancellationRequested Then Return

            Dim own As New List(Of String)()
            Try
                For Each file In Directory.EnumerateFiles(folder)
                    If TagReadService.IsSupportedFile(file) Then own.Add(file)
                Next
            Catch ex As Exception
                ' Ein Ordner ohne Leserecht ist kein Grund, den ganzen Lauf abzubrechen.
                DiagnosticLogService.Log("Playlist.Collect", $"{folder}: {ex.Message}")
                Return
            End Try

            own.Sort(StringComparer.CurrentCultureIgnoreCase)
            files.AddRange(own)

            Try
                Dim subFolders = Directory.EnumerateDirectories(folder).ToList()
                subFolders.Sort(StringComparer.CurrentCultureIgnoreCase)
                For Each subFolder In subFolders
                    CollectFolder(subFolder, files, cancellation)
                Next
            Catch ex As Exception
                DiagnosticLogService.Log("Playlist.Collect", $"{folder}: {ex.Message}")
            End Try
        End Sub

        Private Shared Function ReadTracks(paths As List(Of String), cancellation As CancellationToken) As List(Of Track)
            Dim tracks As New List(Of Track)()
            For Each path In paths
                If cancellation.IsCancellationRequested Then Exit For
                Dim track = TagReadService.Read(path)
                If track IsNot Nothing Then tracks.Add(track)
            Next
            Return tracks
        End Function

        ''' <summary>Zerlegt die Liste in Haeppchen. Heisst NICHT "Chunk": so hiesse auch die
        ''' Erweiterungsmethode von LINQ, und der Aufruf traefe dann die falsche.</summary>
        Private Shared Iterator Function SplitIntoBatches(Of T)(items As List(Of T), size As Integer) As IEnumerable(Of List(Of T))
            Dim index = 0
            Do While index < items.Count
                Yield items.GetRange(index, Math.Min(size, items.Count - index))
                index += size
            Loop
        End Function

        Private Sub AppendTracks(tracks As IEnumerable(Of Track))
            Dim known As New Dictionary(Of String, Track)(StringComparer.Ordinal)
            For Each entry In _tracks
                known.TryAdd(entry.FilePath, entry)
            Next
            Dim changed = False

            For Each track In tracks
                ' Denselben Pfad zweimal in der Liste zu haben ist fast immer ein Versehen; wer ihn
                ' wirklich zweimal will, kann ihn zweimal abspielen. Eingelesen wurde er trotzdem
                ' gerade, die Datei ist also da - galt sie als fehlend, gilt sie es jetzt nicht mehr.
                Dim existing As Track = Nothing
                If known.TryGetValue(track.FilePath, existing) Then
                    SetMissing(existing, False)
                    Continue For
                End If
                known.Add(track.FilePath, track)
                _tracks.Add(track)
                _rowsByTrack(track) = New PlaylistTrackRow(track)
                changed = True
            Next

            If Not changed Then Return
            RebuildPlayOrder()
            RebuildRows()
        End Sub

        Public Sub ClearPlaylist()
            StopPlayback()
            _tracks.Clear()
            _rowsByTrack.Clear()
            _collapsedFolders.Clear()
            _playOrder.Clear()
            _currentTrack = Nothing
            _currentCover = Nothing
            RaiseCurrentTrackChanged()
            RaisePropertyChanged(NameOf(CurrentCover))
            RaisePropertyChanged(NameOf(HasCover))
            RebuildRows()
            SavePlaylist()
        End Sub

        Public Sub RemoveTracks(tracks As IEnumerable(Of Track))
            If tracks Is Nothing Then Return
            Dim doomed = New HashSet(Of Track)(tracks)
            If doomed.Count = 0 Then Return

            _tracks.RemoveAll(Function(t) doomed.Contains(t))
            For Each track In doomed
                _rowsByTrack.Remove(track)
            Next

            If _currentTrack IsNot Nothing AndAlso doomed.Contains(_currentTrack) Then
                StopPlayback()
                _currentTrack = Nothing
                RaiseCurrentTrackChanged()
            End If

            RebuildPlayOrder()
            RebuildRows()
            SavePlaylist()
        End Sub

        ''' <summary>Verschiebt einen Titel vor einen anderen. Die gespeicherte Grundreihenfolge
        ''' wird nur ausserhalb des Zufallsmodus bearbeitet; dort zeigt die Liste ohnehin die
        ''' voruebergehende Abspielreihenfolge.</summary>
        Public Sub MoveTrackBefore(source As Track, target As Track)
            If _isShuffle OrElse source Is Nothing OrElse target Is Nothing OrElse Object.ReferenceEquals(source, target) Then Return
            Dim sourceIndex = _tracks.IndexOf(source)
            Dim targetIndex = _tracks.IndexOf(target)
            If sourceIndex < 0 OrElse targetIndex < 0 Then Return
            _tracks.RemoveAt(sourceIndex)
            If sourceIndex < targetIndex Then targetIndex -= 1
            _tracks.Insert(targetIndex, source)
            RebuildPlayOrder()
            RebuildRows()
            SavePlaylist()
        End Sub

        Public Function TracksInGroup(group As PlaylistGroupRow) As List(Of Track)
            If group Is Nothing Then Return New List(Of Track)()
            Return ActiveTracks().Where(Function(track) String.Equals(track.FolderPath, group.FolderPath, StringComparison.Ordinal)).ToList()
        End Function

        Public Sub PlayGroup(group As PlaylistGroupRow)
            Dim first = TracksInGroup(group).FirstOrDefault(Function(track) IsPlayable(track))
            If first IsNot Nothing Then Play(first)
        End Sub

        ' Fehlende Dateien

        ''' <summary>Mindestens ein Titel der Liste hat keine Datei mehr. Blendet den Knopf ein, der
        ''' sie alle auf einmal entfernt.</summary>
        Public ReadOnly Property HasMissingTracks As Boolean
            Get
                Return _rowsByTrack.Values.Any(Function(r) r.IsMissing)
            End Get
        End Property

        Private Sub SetMissing(track As Track, value As Boolean)
            Dim row As PlaylistTrackRow = Nothing
            If Not _rowsByTrack.TryGetValue(track, row) OrElse row.IsMissing = value Then Return
            row.IsMissing = value
            RaisePropertyChanged(NameOf(HasMissingTracks))
        End Sub

        ''' <summary>Prueft erneut, sobald das Fenster wieder in den Vordergrund kommt - ein
        ''' eingestecktes Laufwerk soll seine Titel zurueckbekommen, ohne dass man etwas tun muss.
        ''' Hoechstens alle 15 Sekunden.</summary>
        Public Sub RecheckMissingFiles()
            If Date.UtcNow - _lastMissingCheckUtc < MissingCheckInterval Then Return
            CheckMissingFiles(reportCount:=False)
        End Sub

        ''' <summary>Stellt fest, welche Titel keine Datei mehr haben, und markiert sie. Die
        ''' Pruefung laeuft im Hintergrund: bei tausend Titeln auf einem Netzlaufwerk dauert sie,
        ''' und die Liste soll so lange schon bedienbar sein.</summary>
        ''' <param name="reportCount">Die Anzahl in der Fussleiste melden. Beim Start ja, beim
        ''' stillen Nachpruefen nicht.</param>
        Private Async Sub CheckMissingFiles(reportCount As Boolean)
            If _missingCheckRunning OrElse _tracks.Count = 0 Then Return
            _missingCheckRunning = True
            _lastMissingCheckUtc = Date.UtcNow
            Try
                Dim snapshot = _tracks.ToList()
                Dim cancellation = _shutdown.Token
                Dim missing = Await Task.Run(Function() FindMissing(snapshot, cancellation))
                If cancellation.IsCancellationRequested Then Return

                ' Was inzwischen aus der Liste genommen wurde, hat keine Zeile mehr und bleibt aussen vor.
                For Each track In snapshot
                    Dim row As PlaylistTrackRow = Nothing
                    If _rowsByTrack.TryGetValue(track, row) Then row.IsMissing = missing.Contains(track)
                Next
                RaisePropertyChanged(NameOf(HasMissingTracks))

                If reportCount AndAlso missing.Count > 0 AndAlso String.IsNullOrEmpty(StatusText) Then
                    StatusText = LocalizationService.Format("{0} Titel nicht gefunden.", missing.Count)
                End If
            Catch ex As Exception
                DiagnosticLogService.LogException("Playlist.CheckMissing", ex)
            Finally
                _missingCheckRunning = False
            End Try
        End Sub

        Private Shared Function FindMissing(tracks As List(Of Track), cancellation As CancellationToken) As HashSet(Of Track)
            Dim missing As New HashSet(Of Track)()
            For Each track In tracks
                If cancellation.IsCancellationRequested Then Exit For
                Try
                    ' Ein optisches Laufwerk existiert auch bei ausgeworfenem Medium. Die
                    ' Verfuegbarkeit klaert mpv beim Start; eine CD deshalb nie als "Datei fehlt"
                    ' markieren, sonst bliebe sie nach dem Wiedereinlegen ausgegraut.
                    If track.IsAudioCdTrack Then Continue For
                    If Not File.Exists(track.FilePath) Then missing.Add(track)
                Catch ex As Exception
                    DiagnosticLogService.Log("Playlist.CheckMissing", $"{track.FilePath}: {ex.Message}")
                End Try
            Next
            Return missing
        End Function

        Private Sub RemoveMissingTracks()
            Dim missing = _tracks.Where(Function(t)
                                            Dim row As PlaylistTrackRow = Nothing
                                            Return _rowsByTrack.TryGetValue(t, row) AndAlso row.IsMissing
                                        End Function).ToList()
            If missing.Count = 0 Then Return
            RemoveTracks(missing)
            StatusText = LocalizationService.Format("{0} fehlende Titel entfernt.", missing.Count)
        End Sub

        Public Sub ToggleGroup(group As PlaylistGroupRow)
            If group Is Nothing Then Return
            If group.IsExpanded Then
                _collapsedFolders.Add(group.FolderPath)
            Else
                _collapsedFolders.Remove(group.FolderPath)
            End If
            RebuildRows()
        End Sub

        ''' <summary>Baut die angezeigten Zeilen neu auf: gefiltert, nach Ordner gruppiert,
        ''' zugeklappte Gruppen ohne ihre Titel.</summary>
        Private Sub RebuildRows()
            Rows.Clear()

            Dim filtered = ActiveTracks().Where(AddressOf MatchesSearch).ToList()

            ' Zufall ist nicht nur ein Abspielmodus: die sichtbare Liste zeigt dieselbe Reihenfolge.
            ' Albumüberschriften würden einen gemischten Durchlauf wieder künstlich zusammenziehen.
            If _selectedPlaylist = PlaylistKind.Files AndAlso _isShuffle Then
                Dim number = 1
                For Each track In _playOrder.Where(Function(entry) filtered.Contains(entry))
                    Dim row As PlaylistTrackRow = Nothing
                    If Not _rowsByTrack.TryGetValue(track, row) Then
                        row = New PlaylistTrackRow(track)
                        _rowsByTrack(track) = row
                    End If
                    row.Number = number
                    row.IsPlaying = Object.ReferenceEquals(track, _currentTrack)
                    Rows.Add(row)
                    number += 1
                Next
                RaisePropertyChanged(NameOf(PlaylistSummary))
                RaisePropertyChanged(NameOf(IsPlaylistEmpty))
                RaisePropertyChanged(NameOf(HasMissingTracks))
                Return
            End If

            Dim groups = New List(Of String)()
            Dim byFolder = New Dictionary(Of String, List(Of Track))(StringComparer.Ordinal)

            For Each track In filtered
                Dim folder = track.FolderPath
                Dim bucket As List(Of Track) = Nothing
                If Not byFolder.TryGetValue(folder, bucket) Then
                    bucket = New List(Of Track)()
                    byFolder(folder) = bucket
                    groups.Add(folder)
                End If
                bucket.Add(track)
            Next

            For Each folder In groups
                Dim bucket = byFolder(folder)
                Dim header As New PlaylistGroupRow(folder, DescribeGroup(folder, bucket),
                                                   bucket.Count, bucket.Sum(Function(t) t.DurationSeconds))
                header.IsExpanded = Not _collapsedFolders.Contains(folder)
                Rows.Add(header)

                If Not header.IsExpanded Then Continue For

                Dim number = 1
                For Each track In bucket
                    Dim row As PlaylistTrackRow = Nothing
                    If Not _rowsByTrack.TryGetValue(track, row) Then
                        row = New PlaylistTrackRow(track)
                        _rowsByTrack(track) = row
                    End If
                    row.Number = number
                    row.IsPlaying = Object.ReferenceEquals(track, _currentTrack)
                    Rows.Add(row)
                    number += 1
                Next
            Next

            RaisePropertyChanged(NameOf(PlaylistSummary))
            RaisePropertyChanged(NameOf(IsPlaylistEmpty))
            RaisePropertyChanged(NameOf(HasMissingTracks))
        End Sub

        ''' <summary>Die Ueberschrift einer Gruppe: "Interpret - Album [Jahr]". Fehlen die
        ''' Kennzeichen, steht der Ordnername da - der ist bei einer sortierten Sammlung meistens
        ''' genau diese Angabe.</summary>
        Private Shared Function DescribeGroup(folder As String, tracks As List(Of Track)) As String
            Dim first = tracks.FirstOrDefault()
            If first IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(first.Album) Then
                Dim artist = If(String.IsNullOrWhiteSpace(first.AlbumArtist), first.Artist, first.AlbumArtist)
                Dim text = If(String.IsNullOrWhiteSpace(artist), first.Album, $"{artist} - {first.Album}")
                If first.Year > 0 Then text &= $" [{first.Year}]"
                Return text
            End If

            Try
                Dim name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))
                If Not String.IsNullOrEmpty(name) Then Return name
            Catch
            End Try
            Return folder
        End Function

        Private Function MatchesSearch(track As Track) As Boolean
            If String.IsNullOrEmpty(_searchText) Then Return True
            Dim needle = _searchText
            Return Contains(track.Title, needle) OrElse
                   Contains(track.Artist, needle) OrElse
                   Contains(track.Album, needle) OrElse
                   Contains(track.FilePath, needle)
        End Function

        Private Shared Function Contains(haystack As String, needle As String) As Boolean
            If String.IsNullOrEmpty(haystack) Then Return False
            Return haystack.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0
        End Function

        ''' <summary>Die Gesamtlaufzeit unter der Liste, immer mit Stunden: "00:32:21". Anders als
        ''' bei einem einzelnen Titel steht hier eine Summe, und die soll man vergleichen koennen.</summary>
        Private Shared Function FormatLongDuration(seconds As Double) As String
            If Double.IsNaN(seconds) OrElse Double.IsInfinity(seconds) OrElse seconds < 0 Then seconds = 0
            Dim span = TimeSpan.FromSeconds(Math.Floor(seconds))
            Return $"{CInt(Math.Floor(span.TotalHours)):00}:{span.Minutes:00}:{span.Seconds:00}"
        End Function

        ' Der Spieler

        Private Sub WirePlayer()
            AddHandler _player.TimeChanged, Sub(seconds) Dispatcher.UIThread.Post(Sub() OnTimeReported(seconds))
            AddHandler _player.FileLoaded,
                Sub(path) Dispatcher.UIThread.Post(
                    Sub()
                        ' Erst nach diesem Ereignis gehört eine Zeitmeldung sicher zum gerade
                        ' gewählten CD-Titel und nicht mehr zum vorherigen mpv-Input.
                        If _currentTrack IsNot Nothing AndAlso _currentTrack.IsAudioCdTrack AndAlso
                           String.Equals(_currentTrack.FilePath, path, StringComparison.Ordinal) Then
                            _audioCdTimeOffset = Nothing
                            _audioCdPlaybackLoaded = True
                        End If
                    End Sub)
            AddHandler _player.LoadFailed, Sub(filePath) Dispatcher.UIThread.Post(Sub() HandleUnplayableTrack(filePath, missing:=True))
            AddHandler _player.DurationChanged,
                Sub(seconds) Dispatcher.UIThread.Post(
                    Sub()
                        ' Die TOC liefert die exakte Dauer des einzelnen CD-Titels. mpv meldet
                        ' hier dagegen je nach Laufwerk die Dauer der kompletten Disc.
                        If _currentTrack Is Nothing OrElse Not _currentTrack.IsAudioCdTrack Then DurationSeconds = seconds
                    End Sub)
            AddHandler _player.PauseChanged, Sub(paused) Dispatcher.UIThread.Post(Sub() IsPlaying = Not paused)
            AddHandler _player.MuteChanged, Sub(muted) Dispatcher.UIThread.Post(Sub() SetField(_isMuted, muted, NameOf(IsMuted)))
            AddHandler _player.EndReached, AddressOf OnEndReached
            AddHandler _player.InitializationFailed,
                Sub(ex) Dispatcher.UIThread.Post(
                    Sub() StatusText = LocalizationService.T("libmpv lässt sich nicht laden. Ohne sie spielt FerrumPlay nichts ab."))
            AddHandler _player.PlaybackTerminated,
                Sub() Dispatcher.UIThread.Post(
                    Sub()
                        IsPlaying = False
                        StatusText = LocalizationService.T("Die Wiedergabe wurde vom System beendet.")
                    End Sub)
        End Sub

        ''' <summary>Ein Titel ist zu Ende. NUR bei einem echten Dateiende geht es weiter: mpv
        ''' meldet dasselbe Ereignis auch, wenn WIR gestoppt oder einen anderen Titel geladen
        ''' haben, und dann waere ein Weiterschalten ein Titelsprung, den niemand ausgeloest
        ''' hat.</summary>
        Private Sub OnEndReached(reason As Integer, [error] As Integer)
            If reason = CInt(MpvInterop.MpvEndFileReason.Error) Then
                ' mpv konnte die Datei nicht oeffnen oder nicht zu Ende lesen. Welcher Titel es war,
                ' sagt das Ereignis nicht; gemeint ist der, der gerade dran ist. Fehlende Dateien
                ' kommen hier nicht an, die faengt AudioPlayer.LoadFailed vorher ab.
                Dispatcher.UIThread.Post(
                    Sub()
                        If _currentTrack IsNot Nothing Then HandleUnplayableTrack(_currentTrack.FilePath, missing:=False)
                    End Sub)
                Return
            End If
            If reason <> CInt(MpvInterop.MpvEndFileReason.Eof) Then Return
            Dispatcher.UIThread.Post(Sub() PlayNext(userRequested:=False))
        End Sub

        ''' <summary>mpv meldet die Stelle. Ein Titel, der wirklich laeuft, hat eine Datei - auch
        ''' wenn er vorhin als fehlend galt -, und die Zaehlung der Fehlschlaege beginnt neu.</summary>
        Private Sub OnTimeReported(seconds As Double)
            If _currentTrack IsNot Nothing AndAlso _currentTrack.IsAudioCdTrack Then
                If Not _audioCdPlaybackLoaded Then Return
                Dim titleDuration = Math.Max(0, _currentTrack.DurationSeconds)
                If Not _audioCdTimeOffset.HasValue Then
                    _audioCdTimeOffset = If(titleDuration > 0 AndAlso seconds > titleDuration, seconds, 0)
                End If
                ' Manche Laufwerke melden zuerst 0 (relativ) und wechseln erst beim Anlaufen auf
                ' die absolute Disc-Zeit. Den Wechsel nicht auf das Ende des Titels klemmen,
                ' sondern genau dort den Bezugspunkt setzen.
                If titleDuration > 0 AndAlso _audioCdTimeOffset.Value = 0 AndAlso seconds > titleDuration Then
                    _audioCdTimeOffset = seconds
                End If
                seconds -= _audioCdTimeOffset.Value
                If titleDuration > 0 Then seconds = Math.Clamp(seconds, 0, titleDuration)
            End If
            PositionSeconds = seconds
            If seconds <= 0 Then Return
            _consecutiveFailures = 0
            If _currentTrack IsNot Nothing Then SetMissing(_currentTrack, False)
        End Sub

        ' Merken und wiederherstellen

        Private Sub LoadSettings()
            Dim settings = AppSettingsService.Current
            _volume = Math.Clamp(settings.Volume, 0, 100)
            _isMuted = settings.Muted
            _isShuffle = settings.Shuffle
            _repeat = CType(Math.Clamp(settings.RepeatMode, 0, 2), RepeatMode)
            _sidePanelWidth = Math.Clamp(settings.SidePanelWidth, 220, 520)

            _player.SetVolume(_volume)
            _player.SetMuted(_isMuted)
            _player.SetGapless(settings.GaplessPlayback)
            ApplyReplayGain()
        End Sub

        ''' <param name="allowResume">False, wenn beim Aufruf Dateien uebergeben wurden: dann
        ''' wird der zuletzt gespielte Titel nur ausgewaehlt, auch wenn das Fortsetzen an ist.</param>
        Private Sub RestorePlaylist(allowResume As Boolean)
            Dim stored = PlaylistStore.Load()
            If stored.Count = 0 Then Return

            For Each track In stored
                If String.IsNullOrWhiteSpace(track.FilePath) Then Continue For
                _tracks.Add(track)
                _rowsByTrack(track) = New PlaylistTrackRow(track)
            Next

            RebuildPlayOrder()
            RebuildRows()

            ' Der zuletzt gespielte Titel wird ausgewaehlt, aber NICHT gestartet - es sei denn, der
            ' Anwender hat das Fortsetzen ausdruecklich eingeschaltet. Eine Anwendung, die beim
            ' Oeffnen ungefragt Musik macht, ist eine Zumutung.
            Dim settings = AppSettingsService.Current
            Dim lastPath = settings.LastTrackPath
            If String.IsNullOrWhiteSpace(lastPath) Then Return
            Dim last = _tracks.FirstOrDefault(Function(t) String.Equals(t.FilePath, lastPath, StringComparison.Ordinal))
            If last Is Nothing Then Return

            _currentTrack = last
            RaiseCurrentTrackChanged()
            UpdatePlayingRow()
            DurationSeconds = last.DurationSeconds
            LoadCoverAsync(last)
            FocusTrackInPlaylist(last)

            If Not allowResume OrElse Not settings.ResumeOnStart Then Return

            ' FORTSETZEN. Geladen wird sofort, gesprungen erst danach - beides geht ueber dieselbe
            ' Warteschlange und laeuft deshalb in dieser Reihenfolge ab. Ein Sprung vor dem Laden
            ' liefe ins Leere, weil mpv dann noch nichts offen hat.
            _player.Load(last.FilePath, force:=True)
            _player.Play()
            _isStopped = False
            IsPlaying = True
            Dim resumeAt = Math.Clamp(settings.LastPositionSeconds, 0, Math.Max(0, last.DurationSeconds - 1))
            If resumeAt > 0 Then
                _player.Seek(resumeAt)
                PositionSeconds = resumeAt
            End If
        End Sub

        Public Sub SavePlaylist()
            PlaylistStore.Save(_tracks.Where(Function(track) Not track.IsAudioCdTrack))
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            DisconnectFromSession()
            _shutdown.Cancel()
            _audioCdMonitor?.Dispose()
            ' Die Stelle gehoert zum laufenden Titel. Bei einem Lyrion-Stream wurde sein Pfad
            ' bewusst nicht gemerkt - dann darf auch seine Stelle nicht auf den zuletzt gemerkten
            ' oertlichen Titel uebergehen.
            If Not IsPlayingLyrion Then AppSettingsService.Current.LastPositionSeconds = _positionSeconds
            SavePlaylist()
            AppSettingsService.Save()
            _player.Dispose()
            _shutdown.Dispose()
        End Sub

    End Class

End Namespace
