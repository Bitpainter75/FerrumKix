Imports System
Imports System.IO
Imports System.Text

Namespace Services

    ''' <summary>Das Protokoll fuer die Fehlersuche.
    '''
    ''' <para>Ab Werk aus: eine Anwendung, die im Normalbetrieb schreibt, verliert entweder Zeit
    ''' oder Platten. Der Startparameter <c>--debug</c> schaltet es ein. Ausnahmen landen immer in
    ''' <c>errors.log</c>, auch bei ausgeschaltetem Protokoll: was schiefgeht, will man
    ''' nachtraeglich sehen koennen, und dafuer laesst es sich nicht vorher einschalten.</para></summary>
    Public NotInheritable Class DiagnosticLogService
        Private Sub New()
        End Sub

        Private Shared ReadOnly Gate As New Object()

        ''' <summary>Ob ausfuehrlich protokolliert wird. Der Wert wird GEMERKT und nicht bei jeder
        ''' Zeile aus der Einstellungsdatei gelesen: <see cref="Log"/> laeuft im Zweifel hundertfach
        ''' je Bedienschritt durch, und zwar auch dann, wenn gar nichts geschrieben wird. Gelesen
        ''' wird er von mehreren Faeden - dem Anzeigefaden, dem Befehlsfaden des Abspielers, dem
        ''' Ereignisfaden von libmpv -, deshalb ueber Volatile.</summary>
        Private Shared _enabled As Integer = 0

        ''' <summary>Der Startparameter hat das Protokoll erzwungen. Dann darf die EINSTELLUNG es
        ''' nicht wieder ausschalten: sie wird kurz nach dem Start angewandt, und wer mit
        ''' <c>--debug</c> startet, will das Protokoll fuer DIESEN Lauf - unabhaengig davon, was
        ''' in der Datei steht. Genau der Fall, den der Parameter aufklaeren soll, ist ja der, in
        ''' dem noch niemand etwas einstellen konnte.</summary>
        Private Shared _forcedOn As Boolean

        ''' <summary>Das Protokoll fuer diesen Lauf einschalten und eingeschaltet lassen.</summary>
        Public Shared Sub ForceEnable()
            _forcedOn = True
            Threading.Volatile.Write(_enabled, 1)
        End Sub

        ''' <summary>Nach dem Umlegen des Schalters in den Einstellungen aufrufen - und einmal
        ''' beim Start, damit der gemerkte Stand dem der Datei entspricht.</summary>
        Public Shared Sub RefreshEnabled(value As Boolean)
            If _forcedOn AndAlso Not value Then Return
            Threading.Volatile.Write(_enabled, If(value, 1, 0))
        End Sub

        Public Shared ReadOnly Property IsEnabled As Boolean
            Get
                Return Threading.Volatile.Read(_enabled) = 1
            End Get
        End Property

        ''' <summary>Der Ordner fuer Protokolle und Einstellungen. Folgt der XDG-Vorgabe, damit die
        ''' Anwendung nicht im Heimatverzeichnis herumliegt.</summary>
        Public Shared ReadOnly Property AppDataDirectory As String
            Get
                Return AppDataDirectoryFor("FerrumKix")
            End Get
        End Property

        ''' <summary>Derselbe Ort, aber fuer einen frei gewaehlten Anwendungsnamen. Der Umzug von
        ''' FerrumPlay braucht den Pfad des alten Namens, und zwar nach genau dieser Regel; eine
        ''' zweite Fassung davon waere die naechste, die auseinanderlaeuft.
        ''' Siehe <see cref="LegacyNameMigration"/>.</summary>
        Friend Shared Function AppDataDirectoryFor(applicationName As String) As String
            Dim baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            If String.IsNullOrEmpty(baseDir) Then baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            Return Path.Combine(baseDir, applicationName)
        End Function

        Public Shared Sub Log(area As String, message As String)
            If Not IsEnabled Then Return
            Write("ferrumkix.log", area, message)
        End Sub

        Public Shared Sub LogAlways(area As String, message As String)
            Write("ferrumkix.log", area, message)
        End Sub

        Public Shared Sub LogException(area As String, ex As Exception)
            Write("errors.log", area, If(ex Is Nothing, "(keine Ausnahme)", ex.ToString()))
        End Sub

        Private Shared Sub Write(fileName As String, area As String, message As String)
            Try
                Dim directory = AppDataDirectory
                IO.Directory.CreateDirectory(directory)
                Dim line = $"{Date.Now:yyyy-MM-dd HH:mm:ss.fff} [{area}] {message}{Environment.NewLine}"
                SyncLock Gate
                    File.AppendAllText(Path.Combine(directory, fileName), line, Encoding.UTF8)
                End SyncLock
            Catch
                ' Ein Protokoll, das selbst scheitert, darf die Anwendung nicht mitnehmen.
            End Try
        End Sub

    End Class

End Namespace
