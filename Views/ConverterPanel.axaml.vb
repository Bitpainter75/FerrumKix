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
            Me.Track = track
            Me.Title = track.DisplayTitle
            Me.Detail = track.FormatText
            _status = LocalizationService.T("Wartet")
        End Sub
        Public ReadOnly Property Track As Track
        Public ReadOnly Property Title As String
        Public ReadOnly Property Detail As String
        ''' <summary>Die Kennzeichen-Tracknummer bleibt beim Konvertieren sichtbar. Hat die Datei
        ''' keine, zeigt der Strich bewusst an, dass keine Nummer erfunden wurde.</summary>
        Public ReadOnly Property TrackNumberText As String
            Get
                Return If(Track.TrackNumber > 0, Track.TrackNumber.ToString(), "–")
            End Get
        End Property
        Public Property Status As String
            Get
                Return _status
            End Get
            Set(value As String)
                SetField(_status, value)
            End Set
        End Property
    End Class

    ''' <summary>Eine nicht konvertierbare Trennerzeile. Sie bewahrt die Album-/Ordnerstruktur in
    ''' der Warteschlange, ohne die Fortschrittsindizes der eigentlichen Titel zu verschieben.</summary>
    Public NotInheritable Class ConversionQueueGroupRow
        Public Sub New(title As String, trackCount As Integer)
            Me.Title = title
            Me.Summary = LocalizationService.Format("{0} Titel", trackCount)
        End Sub
        Public ReadOnly Property Title As String
        Public ReadOnly Property Summary As String
    End Class

    Public Class ConverterPanel
        Inherits UserControl
        Private _tracks As List(Of Track) = New List(Of Track)()
        Private _cancel As CancellationTokenSource
        Public Event CloseRequested As EventHandler
        Public ReadOnly Property Queue As New ObservableCollection(Of ConversionQueueRow)()
        Public ReadOnly Property DisplayRows As New ObservableCollection(Of Object)()

        Public Sub New()
            InitializeComponent()
            DataContext = Me
            LocalizationService.ApplyTo(Me)
            AddHandler LocalizationService.LanguageChanged, Sub(sender, e) LocalizationService.ApplyTo(Me)
        End Sub

        Public Sub New(tracks As IEnumerable(Of Track))
            Me.New()
            _tracks = tracks.Where(Function(track) track IsNot Nothing).ToList()
            AddQueueRows()
            FindControl(Of TextBlock)("CountText").Text = LocalizationService.Format("{0} Titel", _tracks.Count)
            Dim firstFile = _tracks.FirstOrDefault(Function(track) Not track.IsAudioCdTrack)
            FindControl(Of TextBox)("FolderBox").Text = If(firstFile Is Nothing, AppSettingsService.Current.LastBrowseFolder, Path.GetDirectoryName(firstFile.FilePath))
        End Sub

        Private Sub InitializeComponent()
            AvaloniaXamlLoader.Load(Me)
        End Sub

        Private Sub AddQueueRows()
            ' Die Auswahl behält ihre Reihenfolge; ein Album wird nur dann mit einer neuen
            ' Überschrift begonnen, wenn in der Auswahl tatsächlich ein anderer Ordner folgt.
            Dim start = 0
            While start < _tracks.Count
                Dim folder = _tracks(start).FolderPath
                Dim [end] = start + 1
                While [end] < _tracks.Count AndAlso String.Equals(_tracks([end]).FolderPath, folder, StringComparison.Ordinal)
                    [end] += 1
                End While

                Dim groupTracks = _tracks.GetRange(start, [end] - start)
                DisplayRows.Add(New ConversionQueueGroupRow(DescribeGroup(folder, groupTracks), groupTracks.Count))
                For Each track In groupTracks
                    Dim row As New ConversionQueueRow(track)
                    Queue.Add(row)
                    DisplayRows.Add(row)
                Next
                start = [end]
            End While
        End Sub

        Private Shared Function DescribeGroup(folder As String, tracks As List(Of Track)) As String
            Dim first = tracks.FirstOrDefault()
            If first IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(first.Album) Then
                Dim artist = If(String.IsNullOrWhiteSpace(first.AlbumArtist), first.Artist, first.AlbumArtist)
                Dim title = If(String.IsNullOrWhiteSpace(artist), first.Album, $"{artist} - {first.Album}")
                If first.Year > 0 Then title &= $" [{first.Year}]"
                Return title
            End If
            Try
                Dim name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))
                If Not String.IsNullOrEmpty(name) Then Return name
            Catch
            End Try
            Return folder
        End Function

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
            Select Case FindControl(Of ComboBox)("ModeBox").SelectedIndex
                Case 1 : Return AudioConversionService.ConversionMode.AllSourcesOneResult
                Case 2 : Return AudioConversionService.ConversionMode.OneResultPerFolder
                Case Else : Return AudioConversionService.ConversionMode.OneResultPerSource
            End Select
        End Function

        Private Function SelectedBitrate() As Integer
            If FindControl(Of RadioButton)("B128Radio").IsChecked.GetValueOrDefault() Then Return 128
            If FindControl(Of RadioButton)("B256Radio").IsChecked.GetValueOrDefault() Then Return 256
            If FindControl(Of RadioButton)("B320Radio").IsChecked.GetValueOrDefault() Then Return 320
            Return 192
        End Function

        Private Sub UpdateQueue(index As Integer, text As String)
            Dispatcher.UIThread.Post(Sub()
                                         If index < 0 OrElse index >= Queue.Count Then Return
                                         Queue(index).Status = text
                                         ' Die Warteschlange waechst nach unten. Beim Wechsel zum
                                         ' naechsten Titel bleibt er automatisch im sichtbaren Bereich,
                                         ' auch wenn die vorherigen Ergebnisse die Liste gefuellt haben.
                                         Dim queueBox = FindControl(Of ListBox)("QueueBox")
                                         queueBox?.ScrollIntoView(Queue(index))
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
