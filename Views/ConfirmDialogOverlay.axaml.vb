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

    End Class

End Namespace
