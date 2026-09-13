Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports FerrumPlay.Services
Imports System.Linq
Imports Avalonia.Platform.Storage
Imports System.Diagnostics

Namespace Views

    Public Class SettingsView
        Inherits UserControl

        Public Sub New()
            InitializeComponent()
        End Sub

        Private Sub InitializeComponent()
            AvaloniaXamlLoader.Load(Me)
        End Sub

        ''' <summary>Ein Klick in der Bereichsliste rollt zum zugehoerigen Abschnitt. Die Liste
        ''' waehlt also nicht aus, sondern zeigt hin: alle Abschnitte stehen untereinander und
        ''' lassen sich auch durch Rollen erreichen. Der Name des Abschnitts steht im Tag des
        ''' Knopfes.</summary>
        ''' <summary>Der Zielordner des Favoritenabgleichs. Das Feld laesst sich auch tippen; der
        ''' Knopf ist nur der bequeme Weg.</summary>
        Private Async Sub OnChooseLyrionSyncFolderClick(sender As Object, e As RoutedEventArgs)
            Dim storage = TopLevel.GetTopLevel(Me)?.StorageProvider
            If storage Is Nothing Then Return
            Dim folders = Await storage.OpenFolderPickerAsync(New FolderPickerOpenOptions With {
                .Title = LocalizationService.T("Zielordner für den Favoriten-Abgleich"), .AllowMultiple = False})
            Dim chosen = folders.FirstOrDefault()?.TryGetLocalPath()
            If Not String.IsNullOrWhiteSpace(chosen) Then FindControl(Of TextBox)("LyrionSyncFolderBox").Text = chosen
        End Sub

        ''' <summary>Der Zielordner fuers Rippen einer Audio-CD. Leer lassen heisst: Musikordner.</summary>
        Private Async Sub OnChooseCdRipFolderClick(sender As Object, e As RoutedEventArgs)
            Dim storage = TopLevel.GetTopLevel(Me)?.StorageProvider
            If storage Is Nothing Then Return
            Dim folders = Await storage.OpenFolderPickerAsync(New FolderPickerOpenOptions With {
                .Title = LocalizationService.T("Zielordner fürs Rippen"), .AllowMultiple = False})
            Dim chosen = folders.FirstOrDefault()?.TryGetLocalPath()
            If Not String.IsNullOrWhiteSpace(chosen) Then FindControl(Of TextBox)("CdRipFolderBox").Text = chosen
        End Sub

        Private Sub OnSectionNavClick(sender As Object, e As RoutedEventArgs)
            Dim button = TryCast(sender, Button)
            Dim name = TryCast(button?.Tag, String)
            If String.IsNullOrEmpty(name) Then Return

            Dim section = Me.FindControl(Of Border)(name)
            section?.BringIntoView()
        End Sub

        Public Sub OnLicenseLinkClick(sender As Object, e As RoutedEventArgs)
            Dim url = TryCast(TryCast(sender, Control)?.Tag, String)
            If String.IsNullOrWhiteSpace(url) Then Return
            Try
                Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
            Catch
            End Try
            e.Handled = True
        End Sub

    End Class

End Namespace
