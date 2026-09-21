Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports FerrumKix.ViewModels

Namespace Views

    ''' <summary>Der Sichtteil der Sicherheitsabfrage. Er haelt keinen eigenen Zustand - gefragt
    ''' und geantwortet wird ueber <see cref="MainWindowViewModel.ShowConfirmAsync"/>.</summary>
    Public Class ConfirmDialogOverlay
        Inherits UserControl

        Public Sub New()
            AvaloniaXamlLoader.Load(Me)
        End Sub

        Private Sub OnConfirmClick(sender As Object, e As RoutedEventArgs)
            TryCast(DataContext, MainWindowViewModel)?.ConfirmDialog()
        End Sub

        Private Sub OnCancelClick(sender As Object, e As RoutedEventArgs)
            TryCast(DataContext, MainWindowViewModel)?.CancelDialog()
        End Sub

        ' Die Bindung aktualisiert den Wert normalerweise bereits. Der Handler stellt aber auch
        ' bei Plattform-Backends sicher, dass ein Klick in die Liste die gewaehlte Ausgabe liefert.
        Private Sub OnChoiceSelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            Dim list = TryCast(sender, ListBox)
            Dim viewModel = TryCast(DataContext, MainWindowViewModel)
            If list IsNot Nothing AndAlso viewModel IsNot Nothing AndAlso list.SelectedIndex >= 0 Then
                viewModel.DialogSelectedChoice = list.SelectedIndex
            End If
        End Sub

    End Class

End Namespace
