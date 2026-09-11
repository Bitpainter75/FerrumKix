Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports Avalonia.Platform.Storage
Imports FerrumPlay.Models
Imports FerrumPlay.Services

Namespace Views
    Public Class ConversionDialog
        Inherits Window
        Private _tracks As IReadOnlyList(Of Track) = New List(Of Track)()
        Private _cancel As CancellationTokenSource

        Public Sub New()
            InitializeComponent()
            LocalizationService.ApplyTo(Me)
            AddHandler LocalizationService.LanguageChanged, Sub(sender, e) LocalizationService.ApplyTo(Me)
        End Sub

        Public Sub New(tracks As IEnumerable(Of Track))
            Me.New()
            _tracks = tracks.Where(Function(track) track IsNot Nothing).ToList()
            FindControl(Of TextBlock)("SelectionText").Text = LocalizationService.Format("{0} Titel konvertieren", _tracks.Count)
            FindControl(Of TextBox)("FolderBox").Text = If(_tracks.Count > 0, Path.GetDirectoryName(_tracks(0).FilePath), String.Empty)
        End Sub

        Private Sub InitializeComponent()
            AvaloniaXamlLoader.Load(Me)
        End Sub

        Private Async Sub OnChooseFolderClick(sender As Object, e As RoutedEventArgs)
            Dim folders = Await StorageProvider.OpenFolderPickerAsync(New FolderPickerOpenOptions With {.Title = LocalizationService.T("Zielordner"), .AllowMultiple = False})
            Dim path = folders.FirstOrDefault()?.TryGetLocalPath()
            If Not String.IsNullOrWhiteSpace(path) Then FindControl(Of TextBox)("FolderBox").Text = path
        End Sub

        Private Async Sub OnConvertClick(sender As Object, e As RoutedEventArgs)
            Dim folder = FindControl(Of TextBox)("FolderBox").Text
            If String.IsNullOrWhiteSpace(folder) Then Status(LocalizationService.T("Bitte zuerst einen Zielordner auswählen.")) : Return
            _cancel = New CancellationTokenSource()
            FindControl(Of Button)("ConvertButton").IsEnabled = False
            FindControl(Of ProgressBar)("Progress").IsVisible = True
            Dim progress = New Progress(Of String)(AddressOf Status)
            Try
                Await AudioConversionService.ConvertAsync(New AudioConversionService.Request With {
                    .Tracks = _tracks, .OutputDirectory = folder,
                    .Format = CType(FindControl(Of ComboBox)("FormatBox").SelectedIndex, AudioConversionService.OutputFormat),
                    .BitrateKbps = New Integer() {128, 192, 256, 320}(FindControl(Of ComboBox)("BitrateBox").SelectedIndex),
                    .VariableBitrate = FindControl(Of CheckBox)("VbrBox").IsChecked.GetValueOrDefault(),
                    .Mode = CType(FindControl(Of ComboBox)("ModeBox").SelectedIndex, AudioConversionService.ConversionMode),
                    .SplitExistingCue = FindControl(Of CheckBox)("CueBox").IsChecked.GetValueOrDefault()
                }, progress, _cancel.Token)
                Status(LocalizationService.T("Fertig konvertiert."))
            Catch ex As OperationCanceledException
                Status(LocalizationService.T("Konvertierung abgebrochen."))
            Catch ex As Exception
                DiagnosticLogService.LogException("Conversion", ex)
                Status(LocalizationService.T(ex.Message))
            Finally
                FindControl(Of ProgressBar)("Progress").IsVisible = False
                FindControl(Of Button)("ConvertButton").IsEnabled = True
                _cancel?.Dispose() : _cancel = Nothing
            End Try
        End Sub

        Private Sub OnCancelClick(sender As Object, e As RoutedEventArgs)
            If _cancel IsNot Nothing Then _cancel.Cancel() Else Close()
        End Sub

        Private Sub Status(value As String)
            FindControl(Of TextBlock)("StatusText").Text = value
        End Sub
    End Class
End Namespace
