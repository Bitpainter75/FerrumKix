Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports FerrumPlay.Services
Imports FerrumPlay.ViewModels
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

        ''' <summary>Der Vergroesserungsregler wurde losgelassen. ERST JETZT wird das Fenster
        ''' neu vermessen: waehrend des Zuges geschah das bei jeder Zwischenstellung, und das ist
        ''' bei jedem Bildpunkt ein kompletter Durchgang durch den Baum. Die Prozentzahl daneben
        ''' laeuft weiterhin mit - sie haengt am Wert, nicht an diesem Ereignis.</summary>
        Private Sub OnScreenScaleCommitted()
            TryCast(DataContext, MainWindowViewModel)?.CommitUiScale()
        End Sub

        Private Sub OnSectionNavClick(sender As Object, e As RoutedEventArgs)
            Dim button = TryCast(sender, Button)
            Dim name = TryCast(button?.Tag, String)
            If String.IsNullOrEmpty(name) Then Return

            Dim section = Me.FindControl(Of Border)(name)
            section?.BringIntoView()
        End Sub

        ''' <summary>Zeigt den Ordner mit Protokollen und Einstellungen im Dateiverwalter. Denselben
        ''' Weg wie ein Link: der Dateiverwalter ist dem System gegenueber nichts anderes als der
        ''' Browser.</summary>
        Public Sub OnOpenLogFolderClick(sender As Object, e As RoutedEventArgs)
            OpenExternalUrl(DiagnosticLogService.AppDataDirectory)
            e.Handled = True
        End Sub

        Public Sub OnLicenseLinkClick(sender As Object, e As RoutedEventArgs)
            OpenExternalUrl(TryCast(TryCast(sender, Control)?.Tag, String))
            e.Handled = True
        End Sub

        ''' <summary>Der Hinweis neben der Versionsangabe fuehrt zur zuletzt veroeffentlichten
        ''' Fassung. Die Adresse steht im Dienst, der auch die Nummer holt - eine zweite Stelle mit
        ''' derselben Adresse liefe irgendwann auseinander.</summary>
        Public Sub OnReleasePageClick(sender As Object, e As RoutedEventArgs)
            OpenExternalUrl(UpdateCheckService.ReleasesAddress)
            e.Handled = True
        End Sub

        ''' <summary>Uebergibt die Adresse dem Browser des Systems. Scheitert das, bleibt es still:
        ''' ein nicht geoeffneter Link ist kein Grund, die Anwendung anzuhalten.</summary>
        Private Shared Sub OpenExternalUrl(url As String)
            If String.IsNullOrWhiteSpace(url) Then Return
            Try
                Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
            Catch ex As Exception
                DiagnosticLogService.LogException("SettingsView.OpenExternalUrl", ex)
            End Try
        End Sub

    End Class

End Namespace
