Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
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
