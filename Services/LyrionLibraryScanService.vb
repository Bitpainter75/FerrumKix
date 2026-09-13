Imports System
Imports System.Threading
Imports System.Threading.Tasks

Namespace Services

    ''' <summary>Laesst den Lyrion-Server nach neuen und geaenderten Titeln sehen und verfolgt,
    ''' wie weit er ist.
    '''
    ''' <para>Der Knopf "Bibliothek aktualisieren" holte bisher nur die Albenliste noch einmal.
    ''' Das zeigt aber denselben Stand, solange der SERVER nichts Neues gelesen hat - eine eben
    ''' hinzugekommene Platte taucht damit nicht auf. Erst durchsuchen, dann holen.</para>
    '''
    ''' <para>Gebaut wie der Favoritenabgleich: der Vorgang gehoert der Anwendung
    ''' (<see cref="LyrionTaskState"/>), laeuft auf einem Hintergrundfaden, meldet seinen
    ''' Fortschritt in dieselbe Zeile und laesst sich abbrechen.</para></summary>
    Public NotInheritable Class LyrionLibraryScanService

        Private Sub New()
        End Sub

        Public Enum StartResult
            ''' <summary>Der Durchlauf hat begonnen. Die Liste lohnt sich erst, wenn er fertig ist.</summary>
            Started = 0
            ''' <summary>Es lief schon einer und wird jetzt abgebrochen.</summary>
            Cancelling = 1
            ''' <summary>Ein ANDERER Lyrion-Vorgang laeuft. Es wurde nichts angestossen.</summary>
            Busy = 2
        End Enum

        ''' <summary>Der Server ist durch. Erst jetzt lohnt es, die Albenliste neu zu holen.</summary>
        Public Shared Event Completed As EventHandler

        ''' <summary>Stoesst den Durchlauf an, oder bricht den laufenden ab.</summary>
        Public Shared Function Toggle() As StartResult
            If LyrionTaskState.RequestCancel(LyrionTaskState.Kind.Scan,
                                             LocalizationService.T("Durchsuchen wird abgebrochen …")) Then
                Return StartResult.Cancelling
            End If

            Dim source = LyrionTaskState.TryBegin(LyrionTaskState.Kind.Scan)
            If source Is Nothing Then
                ' Nichts anstossen und es auch sagen. Der Aufrufer holt dann wenigstens die Liste
                ' noch einmal, statt dass der Klick folgenlos bleibt.
                LyrionTaskState.SetStatus(LocalizationService.T("Es läuft gerade ein anderer Lyrion-Vorgang."))
                Return StartResult.Busy
            End If

            LyrionTaskState.SetStatus(LocalizationService.T("Serverbibliothek wird durchsucht …"))
            Task.Run(Function() RunAsync(source.Token))
            Return StartResult.Started
        End Function

        Private Shared Async Function RunAsync(token As CancellationToken) As Task
            Dim finished = False
            Dim cancelled = False
            Try
                ' Der innere Block faengt alles ab. Das Absagen beim Server steht DANACH und nicht
                ' im Catch: VB laesst in einem Catch kein Await zu.
                Try
                    Await LyrionMediaServerService.StartRescanAsync(token)

                    ' Der Server nimmt den Befehl entgegen und faengt erst danach an: unmittelbar
                    ' hinterher steht "rescan" noch auf 0. Ohne diese Geduld gaelte der Durchlauf
                    ' sofort als beendet. Bleibt es bei 0, war wirklich nichts zu tun.
                    Dim progress As LyrionMediaServerService.ScanProgress = Nothing
                    Dim waited = 0
                    Do
                        progress = Await LyrionMediaServerService.GetScanProgressAsync(token)
                        If progress.Running OrElse waited >= 10000 Then Exit Do
                        Await Task.Delay(500, token)
                        waited += 500
                    Loop

                    Dim elapsed = progress.TotalTime
                    Do While progress.Running
                        LyrionTaskState.SetStatus(Describe(progress))
                        If progress.TotalTime.Length > 0 Then elapsed = progress.TotalTime
                        Await Task.Delay(1000, token)
                        progress = Await LyrionMediaServerService.GetScanProgressAsync(token)
                    Loop

                    finished = True
                    LyrionTaskState.SetStatus(If(elapsed.Length > 0,
                                                 LocalizationService.Format("Serverbibliothek durchsucht ({0}).", elapsed),
                                                 LocalizationService.T("Serverbibliothek durchsucht.")))
                Catch ex As OperationCanceledException When token.IsCancellationRequested
                    cancelled = True
                Catch ex As OperationCanceledException
                    ' Kein Abbruch, sondern das Zeitlimit des Clients - dieselbe Falle wie beim
                    ' Abgleich: HttpClient meldet sein Zeitlimit als abgebrochenen Vorgang.
                    LyrionTaskState.SetStatus(LocalizationService.T("Der Server hat nicht rechtzeitig geantwortet. Der Abgleich wurde nicht ausgeführt."))
                    DiagnosticLogService.LogException("Lyrion.Scan", ex)
                Catch ex As Exception
                    LyrionTaskState.SetStatus(ex.Message)
                    DiagnosticLogService.LogException("Lyrion.Scan", ex)
                End Try

                If cancelled Then
                    ' Der Server muss es auch erfahren. Bloss wegzusehen hiesse, "abgebrochen" zu
                    ' melden, waehrend er weiterliest - dieselbe Unehrlichkeit, die der Abgleich
                    ' schon hinter sich hat. Mit eigenem Token, das hiesige ist ja abgebrochen.
                    Try
                        Await LyrionMediaServerService.AbortScanAsync(CancellationToken.None)
                    Catch abortFailure As Exception
                        DiagnosticLogService.LogAlways("Lyrion.Scan", abortFailure.Message)
                    End Try
                    LyrionTaskState.SetStatus(LocalizationService.T("Durchsuchen abgebrochen."))
                End If
            Finally
                LyrionTaskState.Finish()
            End Try

            ' NACH Finish: die Ansicht holt daraufhin die Liste, und dabei soll kein Vorgang mehr
            ' als laufend gelten.
            If finished Then RaiseEvent Completed(Nothing, EventArgs.Empty)
        End Function

        ''' <summary>Die Fortschrittszeile. Der Text des Schrittes kommt vom Server und damit in
        ''' dessen Sprache; uebersetzt ist der Rahmen darum.</summary>
        Private Shared Function Describe(progress As LyrionMediaServerService.ScanProgress) As String
            Dim detail = progress.Description
            If progress.Percent >= 0 Then
                detail = If(detail.Length > 0, detail & " · ", String.Empty) &
                         progress.Percent.ToString(Globalization.CultureInfo.CurrentCulture) & " %"
            End If
            If detail.Length = 0 Then Return LocalizationService.T("Serverbibliothek wird durchsucht …")
            Return LocalizationService.Format("Serverbibliothek wird durchsucht: {0}", detail)
        End Function

    End Class

End Namespace
