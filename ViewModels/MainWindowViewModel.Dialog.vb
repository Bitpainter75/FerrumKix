Imports System
Imports System.Threading.Tasks

Namespace ViewModels

    ''' <summary>Die Sicherheitsabfrage des Fensters. Dasselbe Muster wie in FerrumPix
    ''' (DialogOverlayView): KEIN eigenes Fenster, sondern eine Decke ueber dem Inhalt, die an
    ''' diesen Eigenschaften haengt.
    '''
    ''' <para>Ein eigenes Fenster waere hier das Falsche: FerrumPlay traegt seine Fensterleiste
    ''' selbst (<c>WindowDecorations="None"</c>), ein zweites Fenster braechte seine eigene mit und
    ''' saehe fremd aus. Die Decke liegt im selben Rahmen und erbt Farben und Schrift.</para>
    '''
    ''' <para><see cref="ShowConfirmAsync"/> gibt eine Aufgabe zurueck, die erst faellt, wenn
    ''' geantwortet wurde - der Aufrufer liest das Ergebnis also da, wo er gefragt hat, statt es
    ''' ueber einen Rueckruf einzusammeln.</para>
    '''
    ''' <para>Die Texte kommen FERTIG herein. Sie werden hier NICHT noch einmal uebersetzt: sie
    ''' tragen meist schon eingesetzte Zahlen, und ein zweiter Durchgang faende dafuer keinen
    ''' Schluessel mehr.</para></summary>
    Partial Public NotInheritable Class MainWindowViewModel

        Private _dialogCompletion As TaskCompletionSource(Of Boolean)
        Private _dialogTitle As String = String.Empty
        Private _dialogMessage As String = String.Empty
        Private _dialogDetails As String = String.Empty
        Private _dialogConfirmText As String = String.Empty
        Private _dialogCancelText As String = String.Empty

        Public ReadOnly Property IsDialogOpen As Boolean
            Get
                Return _dialogCompletion IsNot Nothing
            End Get
        End Property

        Public Property DialogTitle As String
            Get
                Return _dialogTitle
            End Get
            Private Set(value As String)
                SetField(_dialogTitle, value)
            End Set
        End Property

        Public Property DialogMessage As String
            Get
                Return _dialogMessage
            End Get
            Private Set(value As String)
                SetField(_dialogMessage, value)
            End Set
        End Property

        ''' <summary>Die Auflistung unter der Frage - bei einer Frage nach Eintraegen stehen die
        ''' Eintraege da und nicht nur ihre Anzahl. Wer loeschen soll, will sehen, was.</summary>
        Public Property DialogDetails As String
            Get
                Return _dialogDetails
            End Get
            Private Set(value As String)
                If SetField(_dialogDetails, value) Then RaisePropertyChanged(NameOf(HasDialogDetails))
            End Set
        End Property

        Public ReadOnly Property HasDialogDetails As Boolean
            Get
                Return Not String.IsNullOrWhiteSpace(_dialogDetails)
            End Get
        End Property

        Public Property DialogConfirmText As String
            Get
                Return _dialogConfirmText
            End Get
            Private Set(value As String)
                SetField(_dialogConfirmText, value)
            End Set
        End Property

        Public Property DialogCancelText As String
            Get
                Return _dialogCancelText
            End Get
            Private Set(value As String)
                SetField(_dialogCancelText, value)
            End Set
        End Property

        ''' <summary>Fragt nach und wartet auf die Antwort. True heisst: der Nutzer hat den
        ''' bestaetigenden Knopf gedrueckt.</summary>
        Public Function ShowConfirmAsync(title As String, message As String, details As String,
                                         confirmText As String, cancelText As String) As Task(Of Boolean)
            ' Eine noch offene Frage wird verneint, nicht stehen gelassen: sonst wartete ihr
            ' Aufrufer fuer immer auf eine Antwort, die niemand mehr geben kann.
            _dialogCompletion?.TrySetResult(False)

            DialogTitle = title
            DialogMessage = message
            DialogDetails = details
            DialogConfirmText = confirmText
            DialogCancelText = cancelText
            _dialogCompletion = New TaskCompletionSource(Of Boolean)()
            RaisePropertyChanged(NameOf(IsDialogOpen))
            Return _dialogCompletion.Task
        End Function

        Public Sub ConfirmDialog()
            CompleteDialog(True)
        End Sub

        Public Sub CancelDialog()
            CompleteDialog(False)
        End Sub

        Private Sub CompleteDialog(answer As Boolean)
            Dim completion = _dialogCompletion
            If completion Is Nothing Then Return
            _dialogCompletion = Nothing
            RaisePropertyChanged(NameOf(IsDialogOpen))
            completion.TrySetResult(answer)
        End Sub

    End Class

End Namespace
