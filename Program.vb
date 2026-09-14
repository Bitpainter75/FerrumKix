Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Avalonia
Imports FerrumPlay.Services

Module Program

    ''' <summary>Startparameter, der das Protokoll fuer diesen Lauf einschaltet.</summary>
    Private Const DebugSwitch As String = "--debug"

    <STAThread>
    Function Main(args As String()) As Integer
        ' DER PROTOKOLLSCHALTER ZUERST, noch vor allem anderen. Er ist fuer den Fall gebaut, in dem
        ' die Anwendung ueberhaupt nicht hochkommt: das Protokoll liegt ab Werk aus und liesse sich
        ' sonst nur in einer Anwendung einschalten, die man nicht starten kann.
        If args IsNot Nothing AndAlso args.Any(Function(a) String.Equals(a, DebugSwitch, StringComparison.OrdinalIgnoreCase)) Then
            DiagnosticLogService.ForceEnable()
        End If

        ' Ohne Parameter entscheidet der Schalter aus den Einstellungen. Das Lesen der Datei ist
        ' hier schon ungefaehrlich: LoadCore faengt alles ab und meldet ein Scheitern ueber
        ' LogException, und das haengt an keinem Schalter. Ein mit --debug erzwungenes Protokoll
        ' laesst RefreshEnabled ohnehin stehen.
        DiagnosticLogService.RefreshEnabled(AppSettingsService.Current.EnableDiagnosticLogging)

        _forceX11 = args IsNot Nothing AndAlso args.Any(Function(a) String.Equals(a, X11Switch, StringComparison.OrdinalIgnoreCase))

        ' DIE BEIDEN SICHERHEITSNETZE GEHOEREN HIERHER und nicht in die Anwendungsklasse. Dort
        ' werden sie erst angemeldet, wenn Avalonia schon steht; ein Absturz beim Aufbau des
        ' Toolkits faellt vorher und hinterliesse keine Spur.
        AddHandler AppDomain.CurrentDomain.UnhandledException,
            Sub(sender, e) DiagnosticLogService.LogException("UnhandledException", TryCast(e.ExceptionObject, Exception))
        AddHandler Threading.Tasks.TaskScheduler.UnobservedTaskException,
            Sub(sender, e)
                DiagnosticLogService.LogException("UnobservedTaskException", e.Exception)
                e.SetObserved()
            End Sub

        Dim remaining = SplitStartupPaths(args)

        ' EINE INSTANZ. Laeuft FerrumPlay schon, bekommt die laufende Instanz die Pfade und dieser
        ' Prozess endet hier - noch vor Avalonia, damit kein zweites Fenster aufblitzt. Siehe
        ' SingleInstanceService.
        If SingleInstanceService.ClaimOrHandOver(_startupPaths) Then
            DiagnosticLogService.LogAlways("App.Start", $"pid={Environment.ProcessId}, an die laufende Instanz uebergeben, paths={_startupPaths.Count}")
            Return 0
        End If

        DiagnosticLogService.LogAlways("App.Start", $"pid={Environment.ProcessId}, paths={_startupPaths.Count}, " &
                                                    $"Fenstersystem={If(OperatingSystem.IsLinux() AndAlso Not _forceX11, "Wayland (Rueckfall X11)", "Erkennung")}")
        Try
            Return BuildAvaloniaApp().StartWithClassicDesktopLifetime(ForwardedArguments(remaining))
        Finally
            SingleInstanceService.Shutdown()
        End Try
    End Function

    Private ReadOnly _startupPaths As New List(Of String)()

    ''' <summary>Dateien und Ordner, die beim Aufruf uebergeben wurden: "FerrumPlay a.mp3 ~/Musik/Album".
    ''' Sie kommen in die Wiedergabeliste, und der erste davon wird gespielt. Leer, wenn nichts
    ''' uebergeben wurde.</summary>
    Public ReadOnly Property StartupPaths As IReadOnlyList(Of String)
        Get
            Return _startupPaths
        End Get
    End Property

    ''' <summary>Trennt die Pfade von den uebrigen Angaben. Als Pfad gilt nur, was es auch gibt;
    ''' alles andere bleibt Schalter und geht weiter. Relative Pfade gelten ab dem Ordner, aus dem
    ''' aufgerufen wurde.</summary>
    Private Function SplitStartupPaths(args As String()) As String()
        If args Is Nothing Then Return Array.Empty(Of String)()
        Dim remaining As New List(Of String)()
        For Each arg In args
            Dim path = ToExistingPath(arg)
            If path Is Nothing Then
                remaining.Add(arg)
            Else
                _startupPaths.Add(path)
            End If
        Next
        Return remaining.ToArray()
    End Function

    ''' <summary>Ein Dateimanager uebergibt je nach Eintrag in der .desktop-Datei (%U statt %F) auch
    ''' Adressen der Form file:///... - die werden hier in einen gewoehnlichen Pfad gewandelt.</summary>
    Private Function ToExistingPath(arg As String) As String
        If String.IsNullOrWhiteSpace(arg) Then Return Nothing
        Dim candidate = arg
        Try
            If candidate.StartsWith("file://", StringComparison.OrdinalIgnoreCase) Then candidate = New Uri(candidate).LocalPath
            If candidate.StartsWith("-", StringComparison.Ordinal) Then Return Nothing
            Dim full = IO.Path.GetFullPath(candidate)
            If IO.File.Exists(full) OrElse IO.Directory.Exists(full) Then Return full
        Catch ex As Exception
            DiagnosticLogService.LogException("App.StartupPath", ex)
        End Try
        Return Nothing
    End Function

    ''' <summary>Die eigenen Schalter gehoeren nicht an Avalonia weiter: es kennt sie nicht und
    ''' meldet sie als Fehler.</summary>
    Private Function ForwardedArguments(args As String()) As String()
        If args Is Nothing Then Return Array.Empty(Of String)()
        Return args.Where(Function(a) Not String.Equals(a, DebugSwitch, StringComparison.OrdinalIgnoreCase) AndAlso
                                      Not String.Equals(a, X11Switch, StringComparison.OrdinalIgnoreCase)).ToArray()
    End Function

    ''' <summary>Startparameter, der den alten X11-Weg erzwingt.</summary>
    Private Const X11Switch As String = "--x11"

    ''' <summary>Baut die Anwendung auf dem Fenstersystem auf, das dazu passt.
    '''
    ''' <para>Unter Linux ueber WAYLAND, mit Rueckfall auf X11, wenn keines da ist. Nicht aus
    ''' Vorliebe: Avalonia.Desktop bringt nur das X11-Backend mit, und auf einem Wayland-Schreibtisch
    ''' laeuft die Anwendung dann unter XWayland. Deren Bruecke zwischen den beiden Welten reicht
    ''' beim Ablegen zwar die angebotenen Formen durch, als Daten aber den Inhalt der
    ''' ZWISCHENABLAGE - ein aus Dolphin gezogenes Bild kam nie an. Als eigener Wayland-Client
    ''' gibt es diese Bruecke nicht mehr.</para>
    '''
    ''' <para>Windows und macOS bleiben bei der Erkennung; dort gibt es kein Wayland, und ein
    ''' Rueckfall, der erst scheitern muss, waere nur eine Fehlerquelle mehr.</para></summary>
    Public Function BuildAvaloniaApp() As AppBuilder
        ' Die Erkennung steht ZUERST, auch auf dem Wayland-Weg: UseWaylandWithFallback verlangt
        ' ein bereits eingerichtetes Rueckfall-Backend und wirft sonst beim Aufbau.
        Dim builder = AppBuilder.Configure(Of App)().UsePlatformDetect()
        If OperatingSystem.IsLinux() AndAlso Not _forceX11 Then builder = builder.UseWaylandWithFallback()
        Return builder.LogToTrace()
    End Function

    ''' <summary>Ob die Anwendung auf dem Wayland-Backend laeuft. Ein paar Entscheidungen haengen
    ''' daran, die sich sonst nirgends ablesen lassen - siehe MainWindow.RestoreWindowPlacement.</summary>
    Public ReadOnly Property UsesWayland As Boolean
        Get
            Return OperatingSystem.IsLinux() AndAlso Not _forceX11 AndAlso
                   Not String.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
        End Get
    End Property

    ''' <summary>Ob <c>--x11</c> uebergeben wurde. Das Wayland-Backend ist das juengere von
    ''' beiden; geht dort etwas schief, muss der alte Weg ohne neue Fassung erreichbar sein.</summary>
    Private _forceX11 As Boolean

End Module
