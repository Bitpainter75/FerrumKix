Imports System
Imports System.IO

Namespace Services

    ''' <summary>Der Umzug der Benutzerdaten von FerrumPlay nach FerrumKix.
    '''
    ''' <para>Bis 0.9.5 hiess die Anwendung FerrumPlay, und die Einstellungen, die Wiedergabeliste
    ''' und die Titelbilder liegen unter diesem Namen. Sie werden nicht kopiert, sondern das
    ''' Verzeichnis wird UMBENANNT: alt und neu sind Nachbarn im selben Elternverzeichnis, ein
    ''' Move ist dort ein reiner Namenstausch und kostet auch bei einem Zwischenspeicher von
    ''' einigen hundert Megabyte nichts.</para>
    '''
    ''' <para>VORHANDENES WIRD NIE ANGETASTET. Gibt es den neuen Ordner schon, bleibt der alte
    ''' liegen, wie er ist. Wer beide Staende hat, hat FerrumKix bereits benutzt, und dessen
    ''' Einstellungen sind die juengeren; sie durch den alten Stand zu ersetzen waere genau der
    ''' Datenverlust, den dieser Umzug verhindern soll.</para></summary>
    Friend NotInheritable Class LegacyNameMigration
        Private Sub New()
        End Sub

        Private Const LegacyName As String = "FerrumPlay"
        Private Const CurrentName As String = "FerrumKix"
        Private Const LegacyLogFile As String = "ferrumplay.log"
        Private Const CurrentLogFile As String = "ferrumkix.log"

        ''' <summary>Laeuft als ERSTES in Main. Siehe die Begruendung dort.</summary>
        Public Shared Sub Run()
            Dim settingsMoved = MoveDirectory(DiagnosticLogService.AppDataDirectoryFor(LegacyName),
                                              DiagnosticLogService.AppDataDirectoryFor(CurrentName))
            Dim cacheMoved = MoveDirectory(CoverArtService.CacheDirectoryFor(LegacyName),
                                           CoverArtService.CacheDirectoryFor(CurrentName))
            If settingsMoved Then MoveLogFile()

            ' Erst hier protokollieren: jede Zeile vorher haette das Zielverzeichnis angelegt und
            ' damit den Umzug verhindert, den sie melden soll.
            If settingsMoved OrElse cacheMoved Then
                DiagnosticLogService.LogAlways("Migration",
                    $"Von {LegacyName} uebernommen: Einstellungen={settingsMoved}, Zwischenspeicher={cacheMoved}")
            End If
        End Sub

        Private Shared Function MoveDirectory(legacyPath As String, currentPath As String) As Boolean
            Try
                If Not Directory.Exists(legacyPath) Then Return False
                If Directory.Exists(currentPath) Then Return False
                Directory.Move(legacyPath, currentPath)
                Return True
            Catch
                ' Ein gescheiterter Umzug darf den Start nicht mitnehmen. Die Anwendung faengt dann
                ' mit Werkseinstellungen an, und der alte Ordner liegt unversehrt da - von Hand
                ' umbenannt holt ihn der naechste Start.
                Return False
            End Try
        End Function

        ''' <summary>Die Protokolldatei traegt den Anwendungsnamen und zieht deshalb mit um. Ohne
        ''' das laege im neuen Ordner eine ferrumplay.log, die nie wieder beschrieben wird.</summary>
        Private Shared Sub MoveLogFile()
            Try
                Dim folder = DiagnosticLogService.AppDataDirectoryFor(CurrentName)
                Dim legacyLog = Path.Combine(folder, LegacyLogFile)
                Dim currentLog = Path.Combine(folder, CurrentLogFile)
                If File.Exists(legacyLog) AndAlso Not File.Exists(currentLog) Then File.Move(legacyLog, currentLog)
            Catch
                ' Das Protokoll ist Beiwerk; sein Name darf den Start nicht gefaehrden.
            End Try
        End Sub

    End Class

End Namespace
