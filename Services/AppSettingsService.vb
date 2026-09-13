Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports System.Text.RegularExpressions

Namespace Services

    ''' <summary>Was sich die Anwendung von einem Start zum naechsten merkt.
    '''
    ''' <para>Eine Datei, ein Objekt, geschrieben nur beim Beenden und bei bewussten Aenderungen.
    ''' Geht das Lesen schief, startet die Anwendung mit den Vorgaben statt gar nicht: eine
    ''' beschaedigte Einstellungsdatei darf niemanden aussperren.</para></summary>
    Public NotInheritable Class AppSettings

        Public Property WindowLeft As Integer = -1
        Public Property WindowTop As Integer = -1
        Public Property WindowWidth As Double = 1180
        Public Property WindowHeight As Double = 780
        Public Property WindowMaximized As Boolean = False

        ''' <summary>Die Lautstaerke in Prozent.</summary>
        Public Property Volume As Double = 80
        Public Property Muted As Boolean = False

        Public Property Shuffle As Boolean = False

        ''' <summary>0 = aus, 1 = ganze Liste, 2 = einzelner Titel.</summary>
        Public Property RepeatMode As Integer = 0

        ''' <summary>Der zuletzt gespielte Titel, damit die Anwendung dort wieder aufsetzt. Der
        ''' Pfad und nicht die Nummer: die Liste kann sich zwischendurch geaendert haben.</summary>
        Public Property LastTrackPath As String = String.Empty

        ''' <summary>Der zuletzt geoeffnete Ordner. Nur damit der Dateidialog dort aufgeht, wo man
        ''' zuletzt war.</summary>
        Public Property LastBrowseFolder As String = String.Empty

        ''' <summary>Wo im letzten Titel gestoppt wurde, in Sekunden. Wird nur gebraucht, wenn
        ''' <see cref="ResumeOnStart"/> gesetzt ist.</summary>
        Public Property LastPositionSeconds As Double = 0

        ''' <summary>Breite der Coverspalte in DIP. Der Anwender kann die Trennlinie ziehen.</summary>
        Public Property SidePanelWidth As Double = 300

        ''' <summary>Ohne Luecke von Titel zu Titel. Bei einem durchgehend gemischten Album hoert
        ''' man den Unterschied, bei einer Sammlung einzelner Titel nicht.</summary>
        Public Property GaplessPlayback As Boolean = True

        ''' <summary>Lautstaerkeangleichung nach ReplayGain: 0 = aus, 1 = nach Titel, 2 = nach Album,
        ''' 3 = automatisch (nach Album, bei zufaelliger Reihenfolge nach Titel). Ab Werk aus: nach
        ''' dem Aktualisieren soll nichts ploetzlich anders klingen.</summary>
        Public Property ReplayGainMode As Integer = 0

        ''' <summary>Die Vorverstaerkung auf die angeglichenen Titel in dB, -15 bis +15.</summary>
        Public Property ReplayGainPreamp As Double = 0

        ''' <summary>Beim Start dort weitermachen, wo zuletzt aufgehoert wurde. Ab Werk aus: eine
        ''' Anwendung, die beim Oeffnen von selbst Musik macht, ist eine Zumutung.</summary>
        Public Property ResumeOnStart As Boolean = False

        ''' <summary>Das unscharfe Titelbild hinter der Oberflaeche. Es kostet Grafikleistung, und
        ''' wer eine ruhige Flaeche will, schaltet es ab.</summary>
        Public Property ShowBlurredBackground As Boolean = True

        ''' <summary>Ganzzahliger Versatz auf alle Schriftgroessen. 0 = Auslieferung. Siehe
        ''' <see cref="FontScaleService"/>.</summary>
        Public Property FontSizeOffset As Integer = 0

        ''' <summary>Die Akzentfarbe als #RRGGBB. Aus ihr leiten sich die Farben der Fussleiste und
        ''' der laufenden Zeile ab, siehe <see cref="AccentColorService"/>.</summary>
        Public Property AccentColor As String = "#F08A1A"

        ''' <summary>"System", "German" oder "English". Siehe <see cref="LocalizationService"/>.</summary>
        Public Property LanguageMode As String = "System"

        ''' <summary>Ein Vergroesserungsfaktor je Bildschirm. Avalonias X11-Weg sieht genau das
        ''' vor: die Variable AVALONIA_SCREEN_SCALE_FACTORS nimmt mehrere Paare, durch Semikolon
        ''' getrennt, und vergleicht den Namen mit dem Anzeigenamen des Bildschirms - demselben,
        ''' den die Einstellungen auflisten.</summary>
        Public Property ApplicationScaleFactors As New List(Of ScreenScaleSetting)()

        ' MP3-Tagging: Die Werte sind Vorgaben fuer den Album-Workflow, bewusst aber nicht
        ' persoenlich fest verdrahtet. Sie werden erst vom Tagging-Bereich verwendet.
        Public Property TagCoverSize As Integer = 800
        Public Property TagCoverJpegQuality As Integer = 95
        Public Property TagFileNamePattern As String = AppSettingsService.DefaultTagFileNamePattern
        Public Property TagAlbumArtistFollowsArtist As Boolean = True
        Public Property TagAlbumSortFollowsYear As Boolean = True
        Public Property TagRemoveOtherFields As Boolean = True
        ''' <summary>Die Tracknummer im erzeugten Dateinamen auf die Stellenzahl des Albums auffuellen.</summary>
        Public Property TagPadTrackNumberToAlbumLength As Boolean = True
        Public Property ConverterDefaultFormat As Integer = 0
        Public Property ConverterDefaultBitrate As Integer = 192
        Public Property ConverterDefaultVbr As Boolean = False
        Public Property ConverterDefaultMode As Integer = 0
        ''' <summary>Eigene Genre-Vorgaben, eine je Zeile im Einstellungsdialog.</summary>
        Public Property TagGenres As New List(Of String)()
        ''' <summary>Vollständige LMS-Basisadresse, etwa https://music.example.lan/.</summary>
        Public Property LyrionServerUrl As String = String.Empty
        Public Property LyrionClientName As String = "FerrumPlay"
        ''' <summary>Wohin eine Audio-CD gerippt wird. LEER heisst: der Musikordner des Nutzers -
        ''' siehe <see cref="AppSettingsService.ResolvedCdRipTarget"/>. Gemerkt wird der leere Wert
        ''' und nicht der aufgeloeste Pfad: zieht der Musikordner um, zieht das Ziel mit.</summary>
        Public Property CdRipTargetPath As String = String.Empty

        ''' <summary>Das Lyrion-Geraet, das ferngesteuert wird. LEER heisst: oertlich abspielen.
        ''' Gemerkt wird die Kennung, denn nur sie ist eindeutig; der Name steht daneben, damit die
        ''' Ansicht beim Start etwas anzuzeigen hat, bevor die Geraeteliste geholt ist.</summary>
        Public Property LyrionRemotePlayerId As String = String.Empty
        Public Property LyrionRemotePlayerName As String = String.Empty

        ''' <summary>Der Ordner, in den der Favoritenabgleich schreibt. Leer heisst: kein Abgleich.
        ''' Er ist die EINZIGE Angabe dafuer - woher die Dateien kommen und wie der Serverpfad
        ''' abzuschneiden ist, erfragt der Abgleich beim Server.</summary>
        Public Property LyrionSyncTargetPath As String = String.Empty

    End Class

    ''' <summary>Ein Bildschirm und der Faktor, mit dem die Anwendung darauf vergroessert wird.</summary>
    Public NotInheritable Class ScreenScaleSetting
        Public Property ScreenName As String = String.Empty
        Public Property Scale As Double = 1.0
    End Class

    Public NotInheritable Class AppSettingsService
        Private Sub New()
        End Sub

        ''' <summary>Das Muster, mit dem getaggte MP3-Dateien umbenannt werden, solange nichts
        ''' anderes eingestellt ist. Die Schreibweise ist die von Puddletag.</summary>
        Public Const DefaultTagFileNamePattern As String = "%artist% - %track% - %title%"

        Private Shared ReadOnly Gate As New Object()
        Private Shared _current As AppSettings

        Private Shared ReadOnly SerializerOptions As New JsonSerializerOptions With {
            .WriteIndented = True,
            .DefaultIgnoreCondition = JsonIgnoreCondition.Never
        }

        Public Shared ReadOnly Property SettingsPath As String
            Get
                Return Path.Combine(DiagnosticLogService.AppDataDirectory, "settings.json")
            End Get
        End Property

        ''' <summary>Die geladenen Einstellungen. Beim ersten Zugriff wird gelesen.</summary>
        Public Shared ReadOnly Property Current As AppSettings
            Get
                SyncLock Gate
                    If _current Is Nothing Then _current = LoadCore()
                    Return _current
                End SyncLock
            End Get
        End Property

        Private Shared Function LoadCore() As AppSettings
            Try
                Dim path = SettingsPath
                If Not File.Exists(path) Then Return New AppSettings()
                Dim json = File.ReadAllText(path)
                If String.IsNullOrWhiteSpace(json) Then Return New AppSettings()
                Dim loaded = JsonSerializer.Deserialize(Of AppSettings)(json, SerializerOptions)
                If loaded Is Nothing Then Return New AppSettings()

                ' Was aus der Datei kommt, wird erst geprueft und dann geglaubt. Ein von Hand
                ' verstellter Faktor von 12 machte die Anwendung sonst unbedienbar.
                loaded.AccentColor = AccentColorService.Normalize(loaded.AccentColor)
                loaded.FontSizeOffset = FontScaleService.Normalize(loaded.FontSizeOffset)
                loaded.LanguageMode = LocalizationService.NormalizeLanguageMode(loaded.LanguageMode)
                loaded.ApplicationScaleFactors = NormalizeScreenScaleFactors(loaded.ApplicationScaleFactors)
                loaded.ReplayGainMode = Math.Clamp(loaded.ReplayGainMode, 0, 3)
                loaded.ReplayGainPreamp = NormalizeReplayGainPreamp(loaded.ReplayGainPreamp)
                loaded.TagCoverSize = Math.Clamp(loaded.TagCoverSize, 64, 3000)
                loaded.TagCoverJpegQuality = Math.Clamp(loaded.TagCoverJpegQuality, 1, 100)
                loaded.TagFileNamePattern = NormalizeTagFileNamePattern(loaded.TagFileNamePattern)
                loaded.ConverterDefaultFormat = Math.Clamp(loaded.ConverterDefaultFormat, 0, 2)
                loaded.ConverterDefaultMode = Math.Clamp(loaded.ConverterDefaultMode, 0, 2)
                loaded.ConverterDefaultBitrate = NormalizeConverterBitrate(loaded.ConverterDefaultBitrate)
                loaded.TagGenres = If(loaded.TagGenres, New List(Of String)()).Select(Function(entry) If(entry, String.Empty).Trim()).Where(Function(entry) entry.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList()
                Return loaded
            Catch ex As Exception
                DiagnosticLogService.LogException("Settings.Load", ex)
                Return New AppSettings()
            End Try
        End Function

        ''' <summary>Der Zielordner fuers Rippen, mit Vorgabe. Ohne eigene Einstellung der
        ''' Musikordner des Nutzers; unter Linux ist das der aus <c>user-dirs.dirs</c>, sonst
        ''' <c>~/Music</c>. Laesst sich auch der nicht bestimmen, bleibt das Heimatverzeichnis -
        ''' ein leerer Vorschlag waere keiner.</summary>
        Public Shared ReadOnly Property ResolvedCdRipTarget As String
            Get
                Dim configured = Current.CdRipTargetPath
                If Not String.IsNullOrWhiteSpace(configured) Then Return configured.Trim()
                Dim music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                If Not String.IsNullOrWhiteSpace(music) Then Return music
                Return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            End Get
        End Property

        Public Shared Sub Save()
            Try
                Dim settings As AppSettings
                SyncLock Gate
                    If _current Is Nothing Then Return
                    settings = _current
                End SyncLock

                Directory.CreateDirectory(DiagnosticLogService.AppDataDirectory)
                Dim json = JsonSerializer.Serialize(settings, SerializerOptions)
                ' Erst daneben schreiben, dann umbenennen. Bricht der Schreibvorgang ab, steht
                ' danach die alte Datei da und nicht eine halbe.
                Dim path = SettingsPath
                Dim temporary = path & ".tmp"
                File.WriteAllText(temporary, json)
                File.Move(temporary, path, overwrite:=True)
            Catch ex As Exception
                DiagnosticLogService.LogException("Settings.Save", ex)
            End Try
        End Sub

        ' Fruehere Fassungen und Puddletag selbst schreiben $num(%track%,2). Die fuehrenden Nullen
        ' kommen inzwischen aus TagPadTrackNumberToAlbumLength, deshalb bleibt vom Aufruf nur der
        ' Platzhalter uebrig - ein von dort uebernommenes Muster laeuft damit weiter.
        Private Shared ReadOnly NumFunction As New Regex("\$num\(\s*([^,()]*?)\s*,\s*\d{1,2}\s*\)", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

        ''' <summary>Ein leeres Muster wuerde zu einem Dateinamen aus nichts fuehren. Dann gilt
        ''' wieder die Vorgabe.</summary>
        ''' <summary>Die Bitraten, unter denen der Konverter waehlen laesst. Eine andere Zahl
        ''' gibt es dort nicht - und eine von Hand eingetragene fand im Konverter kein
        ''' passendes Feld und liess ihn beim Oeffnen abstuerzen.</summary>
        Public Shared ReadOnly Property ConverterBitrates As Integer() = {128, 192, 256, 320}

        ''' <summary>Die naechstgelegene angebotene Bitrate zu einem gemerkten Wert.</summary>
        Public Shared Function NormalizeConverterBitrate(value As Integer) As Integer
            Return ConverterBitrates.OrderBy(Function(rate) Math.Abs(rate - value)).First()
        End Function

        Public Shared Function NormalizeTagFileNamePattern(value As String) As String
            Dim text = NumFunction.Replace(If(value, String.Empty), "$1").Trim()
            Return If(text.Length = 0, DefaultTagFileNamePattern, text)
        End Function

        ''' <summary>Die Vorverstaerkung in halben Dezibel zwischen -15 und +15. Feiner hoert man es
        ''' nicht, und groeber waere der Regler nicht mehr zu treffen.</summary>
        Public Shared Function NormalizeReplayGainPreamp(value As Double) As Double
            If Double.IsNaN(value) OrElse Double.IsInfinity(value) Then Return 0
            Return Math.Clamp(Math.Round(value * 2) / 2, -15.0, 15.0)
        End Function

        ' Die Vergroesserung der ganzen Anwendung

        ''' <summary>Der zulaessige Bereich eines Faktors. Unter 1 wuerde die Anwendung kleiner als
        ''' vorgesehen, ueber 2,5 passt kein Fenster mehr auf einen gewoehnlichen Bildschirm.</summary>
        Public Shared Function NormalizeScale(value As Double) As Double
            If Double.IsNaN(value) OrElse Double.IsInfinity(value) Then Return 1.0
            Return Math.Clamp(Math.Round(value, 2), 1.0, 2.5)
        End Function

        ''' <summary>Ein Bildschirmname darf kein Semikolon und kein Gleichheitszeichen enthalten:
        ''' beides trennt in der Umgebungsvariablen.</summary>
        Public Shared Function NormalizeScreenName(value As String) As String
            Dim text = If(value, String.Empty).Trim()
            Return text.Replace(";"c, "_"c).Replace("="c, "_"c)
        End Function

        ''' <summary>Raeumt die Faktorenliste auf: leere Namen fallen weg, doppelte ebenso, und der
        ''' erste gewinnt.</summary>
        Public Shared Function NormalizeScreenScaleFactors(factors As List(Of ScreenScaleSetting)) As List(Of ScreenScaleSetting)
            If factors Is Nothing Then Return New List(Of ScreenScaleSetting)()
            Return factors.
                Where(Function(f) f IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(f.ScreenName)).
                Select(Function(f) New ScreenScaleSetting With {
                    .ScreenName = NormalizeScreenName(f.ScreenName),
                    .Scale = NormalizeScale(f.Scale)}).
                GroupBy(Function(f) f.ScreenName, StringComparer.Ordinal).
                Select(Function(g) g.First()).
                ToList()
        End Function

        Public Shared Function BuildScreenScaleFactors(factors As List(Of ScreenScaleSetting)) As String
            Dim parts = NormalizeScreenScaleFactors(factors).
                Where(Function(f) f.Scale > 1.0001).
                Select(Function(f) $"{f.ScreenName}={f.Scale.ToString("0.##", CultureInfo.InvariantCulture)}").
                ToList()
            Return String.Join(";", parts)
        End Function

        ''' <summary>Setzt die Umgebungsvariable, an der Avalonia die Vergroesserung abliest. MUSS
        ''' laufen, BEVOR Avalonia hochfaehrt: sie wird beim Aufbau des Fenstersystems einmal
        ''' gelesen und danach nie wieder. Eine Aenderung in den Einstellungen wirkt deshalb erst
        ''' beim naechsten Start, und die Einstellungen sagen das auch.</summary>
        Public Shared Sub ApplyApplicationScaleEnvironment()
            ' Die Variable wirkt nur auf Avalonias X11-Weg. Unter Windows und macOS skaliert
            ' Avalonia von sich aus je Bildschirm, dort waere die Einstellung wirkungslos.
            If Not OperatingSystem.IsLinux() Then Return

            Dim value = BuildScreenScaleFactors(Current.ApplicationScaleFactors)
            ' Kein Eintrag ueber 1,0: die Variable ganz WEGLASSEN statt leer setzen. Eine leere
            ' Variable ist fuer Avalonia nicht dasselbe wie keine - sie verdraengt die eigene
            ' Erkennung und erzwingt ueberall den Faktor 1.
            Environment.SetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS",
                                               If(value.Length > 0, value, Nothing))
        End Sub

    End Class

End Namespace
