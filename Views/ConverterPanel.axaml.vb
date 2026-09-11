Imports System
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports Avalonia.Platform.Storage
Imports Avalonia.Threading
Imports FerrumPlay.Models
Imports FerrumPlay.Services
Imports FerrumPlay.ViewModels

Namespace Views
    Public NotInheritable Class ConversionQueueRow
        Inherits ViewModelBase
        Private _status As String
        Public Sub New(track As Track)
            Me.Title = track.DisplayTitle
            Me.Detail = track.FormatText
            _status = LocalizationService.T("Wartet")
        End Sub
        Public ReadOnly Property Title As String
        Public ReadOnly Property Detail As String
        Public Property Status As String
            Get
                Return _status
            End Get
            Set(value As String)
                SetField(_status, value)
            End Set
        End Property
    End Class

    Public Class ConverterPanel
        Inherits UserControl
        Private _tracks As List(Of Track) = New List(Of Track)()
        Private _cancel As CancellationTokenSource
        Public Event CloseRequested As EventHandler
        Public ReadOnly Property Queue As New ObservableCollection(Of ConversionQueueRow)()

        Public Sub New()
            InitializeComponent()
            DataContext = Me
            LocalizationService.ApplyTo(Me)
            AddHandler LocalizationService.LanguageChanged, Sub(sender, e) LocalizationService.ApplyTo(Me)
        End Sub

        Public Sub New(tracks As IEnumerable(Of Track))
            Me.New()
            _tracks = tracks.Where(Function(track) track IsNot Nothing).ToList()
            For Each track In _tracks : Queue.Add(New ConversionQueueRow(track)) : Next
            FindControl(Of TextBlock)("CountText").Text = LocalizationService.Format("{0} Titel", _tracks.Count)
            Dim firstFile = _tracks.FirstOrDefault(Function(track) Not track.IsAudioCdTrack)
            FindControl(Of TextBox)("FolderBox").Text = If(firstFile Is Nothing, AppSettingsService.Current.LastBrowseFolder, Path.GetDirectoryName(firstFile.FilePath))
        End Sub

        Private Sub InitializeComponent()
            AvaloniaXamlLoader.Load(Me)
        End Sub

        Private Async Sub OnChooseFolderClick(sender As Object, e As RoutedEventArgs)
            Dim folders = Await TopLevel.GetTopLevel(Me).StorageProvider.OpenFolderPickerAsync(New FolderPickerOpenOptions With {.Title = LocalizationService.T("Zielordner"), .AllowMultiple = False})
            Dim path = folders.FirstOrDefault()?.TryGetLocalPath()
            If Not String.IsNullOrWhiteSpace(path) Then FindControl(Of TextBox)("FolderBox").Text = path
        End Sub

        Private Async Sub OnConvertClick(sender As Object, e As RoutedEventArgs)
            Dim folder = FindControl(Of TextBox)("FolderBox").Text
            If String.IsNullOrWhiteSpace(folder) Then Status(LocalizationService.T("Bitte zuerst einen Zielordner auswählen.")) : Return
            _cancel = New CancellationTokenSource()
            FindControl(Of Button)("ConvertButton").IsEnabled = False
            FindControl(Of ProgressBar)("Progress").IsVisible = True
            For Each row In Queue : row.Status = LocalizationService.T("Wartet") : Next
            Try
                Await AudioConversionService.ConvertAsync(New AudioConversionService.Request With {
                    .Tracks = _tracks, .OutputDirectory = folder,
                    .Format = If(FindControl(Of RadioButton)("FlacRadio").IsChecked.GetValueOrDefault(), AudioConversionService.OutputFormat.Flac, If(FindControl(Of RadioButton)("OggRadio").IsChecked.GetValueOrDefault(), AudioConversionService.OutputFormat.Ogg, AudioConversionService.OutputFormat.Mp3)),
                    .BitrateKbps = SelectedBitrate(),
                    .VariableBitrate = FindControl(Of RadioButton)("VbrRadio").IsChecked.GetValueOrDefault(),
                    .Mode = SelectedMode(),
                    .SplitExistingCue = FindControl(Of CheckBox)("SplitCueBox").IsChecked.GetValueOrDefault(),
                    .ItemProgress = AddressOf UpdateQueue
                }, New Progress(Of String)(AddressOf Status), _cancel.Token)
                Status(LocalizationService.T("Fertig konvertiert."))
            Catch ex As OperationCanceledException
                Status(LocalizationService.T("Konvertierung abgebrochen."))
            Catch ex As Exception
                DiagnosticLogService.LogException("Conversion", ex)
                Status(ex.Message)
            Finally
                FindControl(Of ProgressBar)("Progress").IsVisible = False
                FindControl(Of Button)("ConvertButton").IsEnabled = True
                _cancel?.Dispose() : _cancel = Nothing
            End Try
        End Sub

        Private Function SelectedMode() As AudioConversionService.ConversionMode
            If FindControl(Of RadioButton)("AllRadio").IsChecked.GetValueOrDefault() Then Return AudioConversionService.ConversionMode.AllSourcesOneResult
            If FindControl(Of RadioButton)("AllCueRadio").IsChecked.GetValueOrDefault() Then Return AudioConversionService.ConversionMode.AllSourcesOneResultWithCue
            If FindControl(Of RadioButton)("FolderRadio").IsChecked.GetValueOrDefault() Then Return AudioConversionService.ConversionMode.OneResultPerFolder
            If FindControl(Of RadioButton)("FolderCueRadio").IsChecked.GetValueOrDefault() Then Return AudioConversionService.ConversionMode.OneResultPerFolderWithCue
            Return AudioConversionService.ConversionMode.OneResultPerSource
        End Function

        Private Function SelectedBitrate() As Integer
            If FindControl(Of RadioButton)("B128Radio").IsChecked.GetValueOrDefault() Then Return 128
            If FindControl(Of RadioButton)("B256Radio").IsChecked.GetValueOrDefault() Then Return 256
            If FindControl(Of RadioButton)("B320Radio").IsChecked.GetValueOrDefault() Then Return 320
            Return 192
        End Function

        Private Sub UpdateQueue(index As Integer, text As String)
            Dispatcher.UIThread.Post(Sub()
                                         If index >= 0 AndAlso index < Queue.Count Then Queue(index).Status = text
                                     End Sub)
        End Sub

        Private Sub OnBackClick(sender As Object, e As RoutedEventArgs)
            If _cancel IsNot Nothing Then _cancel.Cancel() Else RaiseEvent CloseRequested(Me, EventArgs.Empty)
        End Sub

        Private Sub Status(text As String)
            FindControl(Of TextBlock)("StatusText").Text = text
        End Sub
    End Class
End Namespace
