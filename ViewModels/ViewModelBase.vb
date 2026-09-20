Imports System.ComponentModel
Imports System.Runtime.CompilerServices

Namespace ViewModels

    ''' <summary>Die Grundlage aller Bauplaene.
    '''
    ''' <para>Von Hand und nicht ueber eine Bibliothek: FerrumKix hat wenige Bauplaene, und die
    ''' Meldung ueber eine geaenderte Eigenschaft ist die einzige Aufgabe, die sie sich teilen.</para></summary>
    Public MustInherit Class ViewModelBase
        Implements INotifyPropertyChanged

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Protected Sub RaisePropertyChanged(<CallerMemberName> Optional propertyName As String = Nothing)
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
        End Sub

        ''' <summary>Setzt ein Feld und meldet die Aenderung, wenn der Wert wirklich ein anderer
        ''' ist. Gibt True zurueck, wenn gesetzt wurde: der Aufrufer haengt daran oft weitere
        ''' Meldungen.</summary>
        Protected Function SetField(Of T)(ByRef field As T, value As T,
                                          <CallerMemberName> Optional propertyName As String = Nothing) As Boolean
            If EqualityComparer(Of T).Default.Equals(field, value) Then Return False
            field = value
            RaisePropertyChanged(propertyName)
            Return True
        End Function

    End Class

End Namespace
