Imports System
Imports System.Threading

Namespace Services

    ''' <summary>Der EINE laufende Lyrion-Vorgang der Anwendung.
    '''
    ''' <para>Abgleich, Favoriten-Aufraeumen und das Durchsuchen der Serverbibliothek teilen sich
    ''' diesen Platz - es laeuft immer hoechstens einer. Das ist keine Sparsamkeit, sondern
    ''' Notwendigkeit: waehrend der Server seine Bibliothek durchsucht, sind Alben- und Titelliste
    ''' in Bewegung, und ein Abgleich, der mitten hinein liest, holte einen Stand, den es so nie
    ''' gab. Ebenso arbeiten Abgleich und Aufraeumen beide an der Favoritenliste des Servers.</para>
    '''
    ''' <para>Warum hier und nicht in der Ansicht: die Lyrion-Ansicht wird bei JEDEM Oeffnen neu
    ''' gebaut (<c>PlayerView.ShowLyrionPanel</c>). Ein Vorgang, dessen Zustand am Panel haengt,
    ''' ist nach einem Blick auf die Wiedergabeliste unsichtbar - und der naechste Klick startete
    ''' einen zweiten.</para></summary>
    Public NotInheritable Class LyrionTaskState

        Private Sub New()
        End Sub

        ''' <summary>Welcher Vorgang den Platz hat. Der Knopf, dem er gehoert, bricht ihn ab; jeder
        ''' andere Start wird abgelehnt, statt sich dazwischenzudraengen.</summary>
        Public Enum Kind
            None = 0
            Sync = 1
            Cleanup = 2
            Scan = 3
        End Enum

        Public Enum Phase
            Idle = 0
            Running = 1
            ''' <summary>Abbruch angefordert, der Vorgang raeumt noch auf. Ein laufender Download
            ''' oder ein Serverdurchlauf braucht dafuer einen Augenblick - und genau dieser
            ''' Augenblick ist der, in dem eine Ansicht ohne eigenen Zustand ratlos dasteht.</summary>
            Stopping = 2
        End Enum

        Private Shared ReadOnly Gate As New Object()
        Private Shared _phase As Phase = Phase.Idle
        Private Shared _kind As Kind = Kind.None
        Private Shared _cancel As CancellationTokenSource
        Private Shared _status As String = String.Empty

        ''' <summary>Meldet jede Aenderung an Zustand ODER Meldungstext. Wird aus einem
        ''' Hintergrundfaden ausgeloest - wer die Oberflaeche anfasst, muss selbst auf den
        ''' Oberflaechenfaden wechseln.</summary>
        Public Shared Event Changed As EventHandler

        Public Shared ReadOnly Property Current As Phase
            Get
                SyncLock Gate
                    Return _phase
                End SyncLock
            End Get
        End Property

        Public Shared ReadOnly Property Running As Kind
            Get
                SyncLock Gate
                    Return _kind
                End SyncLock
            End Get
        End Property

        Public Shared ReadOnly Property IsBusy As Boolean
            Get
                Return Current <> Phase.Idle
            End Get
        End Property

        ''' <summary>Die letzte Meldung. Sie bleibt nach dem Vorgang stehen: wer die Ansicht
        ''' zwischendurch geschlossen hatte, sieht beim naechsten Oeffnen, was herausgekommen
        ''' ist.</summary>
        Public Shared ReadOnly Property Status As String
            Get
                SyncLock Gate
                    Return _status
                End SyncLock
            End Get
        End Property

        ''' <summary>Beansprucht den Platz. Gibt die Abbruchquelle zurueck, oder Nothing, wenn
        ''' schon etwas laeuft - der Aufrufer startet dann nichts.</summary>
        Public Shared Function TryBegin(what As Kind) As CancellationTokenSource
            Dim source As CancellationTokenSource
            SyncLock Gate
                If _phase <> Phase.Idle Then Return Nothing
                source = New CancellationTokenSource()
                _cancel = source
                _kind = what
                _phase = Phase.Running
            End SyncLock
            Return source
        End Function

        ''' <summary>Fordert den Abbruch an, aber NUR fuer den genannten Vorgang. True heisst: er
        ''' lief und wird jetzt abgebrochen. So bricht der Abgleichknopf keinen Serverdurchlauf ab
        ''' und der Aktualisieren-Knopf keinen Abgleich.</summary>
        Public Shared Function RequestCancel(what As Kind, stoppingText As String) As Boolean
            Dim source As CancellationTokenSource
            SyncLock Gate
                If _kind <> what OrElse _phase <> Phase.Running Then Return _kind = what
                _phase = Phase.Stopping
                source = _cancel
            End SyncLock
            SetStatus(stoppingText)
            source?.Cancel()
            Return True
        End Function

        ''' <summary>Nimmt die stehende Meldung weg. GEHOERT HIERHER und nicht in die Ansicht: die
        ''' Lyrion-Ansicht wird bei jedem Oeffnen neu gebaut, ein dort gemerktes "weggeklickt" waere
        ''' beim naechsten Blick wieder vergessen und die Zeile staende erneut da.</summary>
        Public Shared Sub DismissStatus()
            SetStatus(String.Empty)
        End Sub

        Public Shared Sub SetStatus(text As String)
            SyncLock Gate
                _status = If(text, String.Empty)
            End SyncLock
            RaiseEvent Changed(Nothing, EventArgs.Empty)
        End Sub

        ''' <summary>Gibt den Platz frei. Gehoert in ein Finally - sonst bleibt nach einem Fehler
        ''' alles Weitere fuer immer gesperrt.</summary>
        Public Shared Sub Finish()
            Dim source As CancellationTokenSource
            SyncLock Gate
                source = _cancel
                _cancel = Nothing
                _kind = Kind.None
                _phase = Phase.Idle
            End SyncLock
            source?.Dispose()
            RaiseEvent Changed(Nothing, EventArgs.Empty)
        End Sub

    End Class

End Namespace
