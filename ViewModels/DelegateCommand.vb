Imports System
Imports System.Windows.Input

Namespace ViewModels

    ''' <summary>Ein Kommando, das einfach einen uebergebenen Aufruf ausfuehrt.</summary>
    Public NotInheritable Class DelegateCommand
        Implements ICommand

        Private ReadOnly _execute As Action(Of Object)
        Private ReadOnly _canExecute As Func(Of Boolean)

        Public Sub New(execute As Action)
            Me.New(Sub(ignored) execute?.Invoke())
        End Sub

        Public Sub New(execute As Action(Of Object), Optional canExecute As Func(Of Boolean) = Nothing)
            _execute = execute
            _canExecute = canExecute
        End Sub

        Public Event CanExecuteChanged As EventHandler Implements ICommand.CanExecuteChanged

        Public Function CanExecute(parameter As Object) As Boolean Implements ICommand.CanExecute
            Return _execute IsNot Nothing AndAlso (_canExecute Is Nothing OrElse _canExecute())
        End Function

        Public Sub Execute(parameter As Object) Implements ICommand.Execute
            _execute?.Invoke(parameter)
        End Sub

        ''' <summary>Meldet, dass sich die Ausfuehrbarkeit geaendert haben koennte.</summary>
        Public Sub RaiseCanExecuteChanged()
            RaiseEvent CanExecuteChanged(Me, EventArgs.Empty)
        End Sub

    End Class

End Namespace
