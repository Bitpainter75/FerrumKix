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
Imports FerrumKix.Models
Imports FerrumKix.Services
Imports FerrumKix.ViewModels

Namespace Views
    Public NotInheritable Class ConversionQueueRow
        Inherits ViewModelBase
        Private _status As String
        Private _isEnabled As Boolean = True
        Private _isConverting As Boolean
        Public Sub New(track As Track, number As Integer)
            Me.Track = track
            Me.Title = track.DisplayTitle
            Me.Detail = track.FormatText
            Me.Number = number
            _status = LocalizationService.T("Wartet")
        End Sub
        Public ReadOnly Property Track As Track
        Public ReadOnly Property Title As String
        Public ReadOnly Property Detail As String

        ''' <summary>Die Stelle IN DER GRUPPE, genau wie in der Wiedergabeliste - und nicht die
        ''' Nummer aus den Kennzeichen: eine Liste, deren Zahlen springen, weil ein Album
        ''' unvollstaendig ist, liest sich falsch.</summary>
        Public ReadOnly Property Number As Integer

        Public ReadOnly Property NumberedTitle As String
            Get
                Return $"{Number}. {Title}"
            End Get
        End Property

        ''' <summary>Das Haekchen vor dem Titel. Ohne Haken bleibt die Zeile stehen, wird aber
        ''' nicht umgewandelt - dasselbe Verhalten wie in der Wiedergabeliste, wo ein abgehakter
        ''' Titel uebersprungen wird.</summary>
        Public Property IsEnabled As Boolean
            Get
                Return _isEnabled
            End Get
            Set(value As Boolean)
                SetField(_isEnabled, value)
            End Set
        End Property

        ''' <summary>Dieser Titel ist gerade an der Reihe. Faerbt die Zeile wie der laufende Titel
        ''' in der Wiedergabeliste.</summary>
        Public Property IsConverting As Boolean
            Get
                Return _isConverting
            End Get
            Set(value As Boolean)
                SetField(_isConverting, value)
            End Set
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
        Private _closeWhenFinished As Boolean
        ''' <summary>Die Zeilen des laufenden Auftrags IN DER REIHENFOLGE DES DIENSTES. Seine
        ''' Fortschrittsmeldungen nennen eine Stelle in seiner eigenen, sortierten Liste (siehe
        ''' AudioConversionService.ConvertAsync) - und die ist eine andere als die Reihenfolge in
        ''' der Anzeige, sobald nicht alles abgehakt ist oder die Auswahl anders sortiert war.</summary>
        Private _running As List(Of ConversionQueueRow) = New List(Of ConversionQueueRow)()
        Public Event CloseRequested As EventHandler
        Public ReadOnly Property Queue As New ObservableCollection(Of ConversionQueueRow)()
        Public ReadOnly Property DisplayRows As New ObservableCollection(Of Object)()

        Public Sub New()
            InitializeComponent()
            DataContext = Me
            LocalizationService.ApplyTo(Me)
            ApplyDefaults()
            ' Mit AddressOf statt einer Lambda: nur so laesst sich die Anmeldung beim Verlassen
            ' wieder loesen. Sonst haelt das statische Ereignis jedes je geoeffnete Panel fest.
            AddHandler LocalizationService.LanguageChanged, AddressOf OnLanguageChanged
            AddHandler DetachedFromVisualTree, Sub(sender, e) RemoveHandler LocalizationService.LanguageChanged, AddressOf OnLanguageChanged
        End Sub

        Private Sub OnLanguageChanged(sender As Object, e As EventArgs)
            LocalizationService.ApplyTo(Me)
        End Sub

        Private Sub ApplyDefaults()
            Dim settings = AppSettingsService.Current
            FindControl(Of RadioButton)(If(settings.ConverterDefaultFormat = 1, "FlacRadio", If(settings.ConverterDefaultFormat = 2, "OggRadio", "Mp3Radio"))).IsChecked = True
            FindControl(Of RadioButton)(If(settings.ConverterDefaultVbr, "VbrRadio", "CbrRadio")).IsChecked = True
            FindControl(Of RadioButton)($"B{AppSettingsService.NormalizeConverterBitrate(settings.ConverterDefaultBitrate)}Radio").IsChecked = True
            FindControl(Of ComboBox)("ModeBox").SelectedIndex = Math.Clamp(settings.ConverterDefaultMode, 0, 2)
        End Sub

        Public Sub New(tracks As IEnumerable(Of Track))
            Me.New()
            _tracks = tracks.Where(Function(track) track IsNot Nothing).ToList()
            AddQueueRows()
            UpdateCountText()
            ' Bei Dateien liegt der Ordner der Dateien nahe. Eine Audio-CD hat keinen - dafuer
            ' gibt es die Einstellung, und ohne sie den Musikordner des Nutzers.
            Dim firstFile = _tracks.FirstOrDefault(Function(track) Not track.IsAudioCdTrack)
            Dim root = If(firstFile Is Nothing, AppSettingsService.ResolvedCdRipTarget, Path.GetDirectoryName(firstFile.FilePath))
            Dim cdTrack = _tracks.FirstOrDefault(Function(track) track.IsAudioCdTrack)
            FindControl(Of TextBox)("FolderBox").Text = If(firstFile Is Nothing AndAlso cdTrack IsNot Nothing,
                                                          AudioConversionService.ResolveCdRipFolder(root, cdTrack), root)
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
                Dim number = 0
                For Each track In groupTracks
                    number += 1
                    Dim row As New ConversionQueueRow(track, number)
                    ' Der Kopf zeigt, wie viele Titel abgehakt sind, und ohne einen einzigen Haken
                    ' laesst sich nichts starten. Beides haengt an jedem einzelnen Haekchen.
                    AddHandler row.PropertyChanged, AddressOf OnQueueRowChanged
                    Queue.Add(row)
                    DisplayRows.Add(row)
                Next
                start = [end]
            End While
        End Sub

        Private Sub OnQueueRowChanged(sender As Object, e As ComponentModel.PropertyChangedEventArgs)
            If e.PropertyName <> NameOf(ConversionQueueRow.IsEnabled) Then Return
            ' Ein abgehakter Titel wartet auf nichts mehr. Waehrend eines Laufs bleibt der Stand
            ' dagegen stehen: dort zaehlt, was der Dienst gemeldet hat.
            Dim row = TryCast(sender, ConversionQueueRow)
            If row IsNot Nothing AndAlso _cancel Is Nothing Then row.Status = If(row.IsEnabled, LocalizationService.T("Wartet"), String.Empty)
            UpdateCountText()
        End Sub

        ''' <summary>Die Zeilen mit Haken - und nur die werden umgewandelt.</summary>
        Private Function CheckedRows() As List(Of ConversionQueueRow)
            Return Queue.Where(Function(row) row.IsEnabled).ToList()
        End Function

        ''' <summary>Rechts im Kopf steht, wie viele Titel an der Reihe sind. Sind alle abgehakt,
        ''' bleibt es bei der blossen Anzahl - die zweite Zahl saehe dort nur nach einer Auswahl
        ''' aus, die niemand getroffen hat.</summary>
        Private Sub UpdateCountText()
            ' Ueber Where und nicht ueber Count(Bedingung): die Sammlung hat eine eigene
            ' Eigenschaft Count, und VB liest den Klammerausdruck dann als Zugriff darauf.
            Dim checkedCount = Queue.Where(Function(row) row.IsEnabled).Count()
            FindControl(Of TextBlock)("CountText").Text = If(checkedCount = Queue.Count,
                LocalizationService.Format("{0} Titel", Queue.Count),
                LocalizationService.Format("{0} von {1} Titeln", checkedCount, Queue.Count))
            Dim convertButton = FindControl(Of Button)("ConvertButton")
            If convertButton IsNot Nothing AndAlso _cancel Is Nothing Then convertButton.IsEnabled = checkedCount > 0
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
            ' Ohne Haken keine Umwandlung: die Zeile bleibt stehen, bleibt aber aussen vor.
            Dim chosen = CheckedRows()
            If chosen.Count = 0 Then Status(LocalizationService.T("Keine Titel zum Konvertieren ausgewählt.")) : Return
            Dim chosenTracks = chosen.Select(Function(row) row.Track).ToList()
            Dim folder = FindControl(Of TextBox)("FolderBox").Text
            If String.IsNullOrWhiteSpace(folder) Then Status(LocalizationService.T("Bitte zuerst einen Zielordner auswählen.")) : Return
            Dim format = If(FindControl(Of RadioButton)("FlacRadio").IsChecked.GetValueOrDefault(), AudioConversionService.OutputFormat.Flac, If(FindControl(Of RadioButton)("OggRadio").IsChecked.GetValueOrDefault(), AudioConversionService.OutputFormat.Ogg, AudioConversionService.OutputFormat.Mp3))
            If IsSameFormatInSourceFolder(chosenTracks, folder, format) Then
                Status(LocalizationService.T("Das Zielformat entspricht bereits der Quelldatei im selben Ordner. Bitte einen anderen Zielordner oder ein anderes Format wählen."))
                Return
            End If
            ' DIESELBE SORTIERUNG WIE IM DIENST. Er ordnet die Titel selbst nach Disc, Nummer und
            ' Dateiname und meldet danach seine Stellen; ohne dieselbe Ordnung hier landete der
            ' Stand eines Titels in der Zeile eines anderen.
            _running = chosen.OrderBy(Function(row) row.Track.DiscNumber).
                              ThenBy(Function(row) row.Track.TrackNumber).
                              ThenBy(Function(row) row.Track.FilePath, StringComparer.OrdinalIgnoreCase).ToList()
            Dim request As New AudioConversionService.Request With {
                .Tracks = chosenTracks, .OutputDirectory = folder, .Format = format,
                .OutputDirectoryIncludesCdSubfolder = chosenTracks.Count > 0 AndAlso chosenTracks.All(Function(track) track.IsAudioCdTrack),
                .BitrateKbps = SelectedBitrate(), .VariableBitrate = FindControl(Of RadioButton)("VbrRadio").IsChecked.GetValueOrDefault(),
                .Mode = SelectedMode(), .ItemProgress = AddressOf UpdateQueue}
            Dim existing = AudioConversionService.ExistingSingleOutputPaths(request)
            If existing.Count > 0 Then
                Dim owner = TryCast(TopLevel.GetTopLevel(Me)?.DataContext, MainWindowViewModel)
                Dim confirmed = If(owner Is Nothing, False, Await owner.ShowConfirmAsync(LocalizationService.T("Vorhandene Dateien überschreiben?"),
                    LocalizationService.Format("{0} Zieldatei(en) existieren bereits.", existing.Count), String.Join(Environment.NewLine, existing.Take(8)), LocalizationService.T("Alle überschreiben"), LocalizationService.T("Abbrechen")))
                If Not confirmed Then Return
                request.OverwriteExisting = True
            End If
            _cancel = New CancellationTokenSource()
            SetProcessingControls(True)
            ShowProgress(True)
            For Each row In Queue : row.Status = If(row.IsEnabled, LocalizationService.T("Wartet"), String.Empty) : Next
            Try
                Await AudioConversionService.ConvertAsync(request, New Progress(Of String)(AddressOf Status), _cancel.Token)
                Status(LocalizationService.T("Fertig konvertiert."))
            Catch ex As OperationCanceledException
                Status(LocalizationService.T("Konvertierung abgebrochen."))
            Catch ex As Exception
                DiagnosticLogService.LogException("Conversion", ex)
                Status(ex.Message)
            Finally
                ShowProgress(False)
                For Each row In Queue : row.IsConverting = False : Next
                _running = New List(Of ConversionQueueRow)()
                ' Erst die Abbruchquelle aufloesen, dann die Bedienung zurueckholen: solange sie
                ' steht, gilt der Lauf als laufend, und der Startknopf bliebe gesperrt.
                _cancel?.Dispose() : _cancel = Nothing
                SetProcessingControls(False)
                If _closeWhenFinished Then RaiseEvent CloseRequested(Me, EventArgs.Empty)
            End Try
        End Sub

        Private Shared Function IsSameFormatInSourceFolder(tracks As List(Of Track), folder As String, format As AudioConversionService.OutputFormat) As Boolean
            Dim target = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)
            Dim extension = If(format = AudioConversionService.OutputFormat.Mp3, ".mp3", If(format = AudioConversionService.OutputFormat.Flac, ".flac", ".ogg"))
            Return tracks.Any(Function(track) Not track.IsAudioCdTrack AndAlso
                               String.Equals(Path.GetFullPath(Path.GetDirectoryName(track.FilePath)).TrimEnd(Path.DirectorySeparatorChar), target, StringComparison.OrdinalIgnoreCase) AndAlso
                               String.Equals(Path.GetExtension(track.FilePath), extension, StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>Eine entfernte CD macht jede CDDA-Quelle unlesbar. Der laufende Rip wird
        ''' abgebrochen; erst nach dem Aufraeumen schliesst sich das Panel, damit die lokale
        ''' Wiedergabeliste wieder sichtbar wird.</summary>
        Public Sub CloseForRemovedAudioCd()
            If Not _tracks.Any(Function(track) track IsNot Nothing AndAlso track.IsAudioCdTrack) Then Return
            If _cancel Is Nothing Then
                RaiseEvent CloseRequested(Me, EventArgs.Empty)
                Return
            End If
            _closeWhenFinished = True
            _cancel.Cancel()
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
                                         If index < 0 OrElse index >= _running.Count Then Return
                                         Dim row = _running(index)
                                         row.Status = text
                                         ' Fertig ist nicht mehr an der Reihe. Sonst blieben beim
                                         ' Zusammenfuehren alle Zeilen des Ordners hervorgehoben.
                                         Dim isDone = String.Equals(text, LocalizationService.T("Fertig"), StringComparison.Ordinal)
                                         For Each other In Queue : other.IsConverting = False : Next
                                         row.IsConverting = Not isDone
                                         ' Die Liste rollt dem Titel nach, der gerade an der Reihe
                                         ' ist - auch wenn er weit unten steht.
                                         FindControl(Of ListBox)("QueueBox")?.ScrollIntoView(row)
                                     End Sub)
        End Sub

        Private Sub OnBackClick(sender As Object, e As RoutedEventArgs)
            If _cancel IsNot Nothing Then
                CancelProcessing()
            Else
                RaiseEvent CloseRequested(Me, EventArgs.Empty)
            End If
        End Sub

        ''' <summary>Bricht den laufenden Konvertierungs- oder Rip-Vorgang ab. Diese Methode wird
        ''' auch von der Fortschrittsdecke des Hauptfensters aufgerufen, die das Panel waehrend
        ''' eines Laufs absichtlich verdeckt.</summary>
        Public Sub CancelProcessing()
            If _cancel Is Nothing OrElse _cancel.IsCancellationRequested Then Return
            Status(LocalizationService.T("Konvertierung wird abgebrochen …"))
            FindControl(Of Button)("CancelButton").IsEnabled = False
            _cancel.Cancel()
        End Sub

        Private Sub OnCancelClick(sender As Object, e As RoutedEventArgs)
            CancelProcessing()
        End Sub

        ''' <summary>Die Warteschlange bleibt bewusst aktiv und scrollbar. Gesperrt werden nur
        ''' Eingaben, die den bereits gestarteten Auftrag veraendern koennten - und weil daran
        ''' waehrend eines Laufs ohnehin nichts mehr zu aendern ist, verschwindet der ganze Block
        ''' und gibt seinen Platz der Liste.</summary>
        Private Sub SetProcessingControls(isProcessing As Boolean)
            FindControl(Of Border)("SettingsBox").IsVisible = Not isProcessing
            FindControl(Of Button)("BackButton").IsVisible = Not isProcessing
            Dim cancelButton = FindControl(Of Button)("CancelButton")
            cancelButton.IsVisible = isProcessing
            cancelButton.IsEnabled = isProcessing

            For Each controlName In {"ConvertButton", "ChooseFolderButton"}
                FindControl(Of Button)(controlName).IsEnabled = Not isProcessing
            Next
            FindControl(Of ComboBox)("ModeBox").IsEnabled = Not isProcessing
            For Each controlName In {"Mp3Radio", "FlacRadio", "OggRadio", "CbrRadio", "VbrRadio", "B128Radio", "B192Radio", "B256Radio", "B320Radio"}
                FindControl(Of RadioButton)(controlName).IsEnabled = Not isProcessing
            Next
            If Not isProcessing Then UpdateCountText()
        End Sub

        ''' <summary>Die Statuszeile traegt den Text UND ihre Sichtbarkeit: solange nichts zu
        ''' melden ist, gibt die Leiste ihren Platz an die Liste ab.</summary>
        Private Sub Status(text As String)
            FindControl(Of TextBlock)("StatusText").Text = text
            FindControl(Of Border)("StatusBar").IsVisible = Not String.IsNullOrWhiteSpace(text) OrElse
                                                            FindControl(Of ProgressBar)("Progress").IsVisible
        End Sub

        ''' <summary>Der Laufbalken gehoert in dieselbe Leiste. Er bringt sie mit, auch wenn noch
        ''' keine Meldung da steht - sonst faenge ein Lauf ohne jedes Zeichen an.</summary>
        Private Sub ShowProgress(isVisible As Boolean)
            FindControl(Of ProgressBar)("Progress").IsVisible = isVisible
            If isVisible Then FindControl(Of Border)("StatusBar").IsVisible = True
        End Sub
    End Class
End Namespace
