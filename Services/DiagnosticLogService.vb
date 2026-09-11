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
        Private Shared _enabled As Boolean = False

        Public Shared Sub ForceEnable()
            _enabled = True
        End Sub

        Public Shared ReadOnly Property IsEnabled As Boolean
            Get
                Return _enabled
            End Get
        End Property

        ''' <summary>Der Ordner fuer Protokolle und Einstellungen. Folgt der XDG-Vorgabe, damit die
        ''' Anwendung nicht im Heimatverzeichnis herumliegt.</summary>
        Public Shared ReadOnly Property AppDataDirectory As String
            Get
                Dim baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                If String.IsNullOrEmpty(baseDir) Then baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                Return Path.Combine(baseDir, "FerrumPlay")
            End Get
        End Property

        Public Shared Sub Log(area As String, message As String)
            If Not _enabled Then Return
            Write("ferrumplay.log", area, message)
        End Sub

        Public Shared Sub LogAlways(area As String, message As String)
            Write("ferrumplay.log", area, message)
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
