Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Text.Json.Serialization

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

    End Class

    ''' <summary>Ein Bildschirm und der Faktor, mit dem die Anwendung darauf vergroessert wird.</summary>
    Public NotInheritable Class ScreenScaleSetting
        Public Property ScreenName As String = String.Empty
        Public Property Scale As Double = 1.0
    End Class

    Public NotInheritable Class AppSettingsService
        Private Sub New()
        End Sub

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
                Return loaded
            Catch ex As Exception
                DiagnosticLogService.LogException("Settings.Load", ex)
                Return New AppSettings()
            End Try
        End Function

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
