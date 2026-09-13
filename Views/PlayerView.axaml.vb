Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Avalonia.Controls
' Avalonia.Controls.Primitives wird NICHT eingebunden: dort liegt eine Klasse "Track" (der
' Balken eines Schiebereglers), und die waere in dieser Datei nicht mehr von unserem Titel zu
' unterscheiden. Was von dort gebraucht wird, steht ausgeschrieben da.
Imports Avalonia.Input
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports Avalonia.Platform.Storage
Imports Avalonia.Threading
Imports FerrumPlay.Models
Imports FerrumPlay.Services
Imports FerrumPlay.ViewModels

Namespace Views

    Public Class PlayerView
        Inherits UserControl

        Private _focusViewModel As MainWindowViewModel
        Private _draggedTrack As Track
        Private _converterPanel As ConverterPanel
        Private _tagEditorPanel As TagEditorPanel
        Private _lyrionPanel As LyrionBrowserPanel

        Public Sub New()
            InitializeComponent()

            Dim seek = Me.FindControl(Of Controls.SeekBar)("Seek")
            If seek IsNot Nothing Then AddHandler seek.Seeked, AddressOf OnSeeked
            ' Das Ablegen eines Covers wird HIER angenommen und nicht in der Tag-Coverspalte
            ' selbst: auf dem Weg zum Fenster kommt das geroutete Ereignis hier verlaesslich
            ' vorbei, waehrend es innerhalb der Spalte davon abhinge, welches ihrer Elemente
            ' unter dem Zeiger gerade getroffen wird. Wohin der Zeiger zeigt, entscheidet
            ' stattdessen die Lage.
            Me.AddHandler(DragDrop.DragOverEvent, New EventHandler(Of DragEventArgs)(AddressOf OnTagCoverDragOver))
            Me.AddHandler(DragDrop.DropEvent, New EventHandler(Of DragEventArgs)(AddressOf OnTagCoverDrop))
            Me.AddHandler(DragDrop.DragLeaveEvent, New EventHandler(Of DragEventArgs)(AddressOf OnTagCoverDragLeave))
            AddHandler DataContextChanged, AddressOf OnViewModelDataContextChanged
        End Sub

        Private Sub InitializeComponent()
            AvaloniaXamlLoader.Load(Me)
        End Sub

        Private ReadOnly Property ViewModel As MainWindowViewModel
            Get
                Return TryCast(DataContext, MainWindowViewModel)
            End Get
        End Property

        Private Sub OnViewModelDataContextChanged(sender As Object, e As EventArgs)
            If _focusViewModel IsNot Nothing Then
                RemoveHandler _focusViewModel.PlaylistFocusRequested, AddressOf OnPlaylistFocusRequested
                RemoveHandler _focusViewModel.AudioCdRemoved, AddressOf OnAudioCdRemoved
            End If
            _focusViewModel = ViewModel
            If _focusViewModel IsNot Nothing Then
                AddHandler _focusViewModel.PlaylistFocusRequested, AddressOf OnPlaylistFocusRequested
                AddHandler _focusViewModel.AudioCdRemoved, AddressOf OnAudioCdRemoved
            End If
            ' RestorePlaylist laeuft vor dem Anhaengen der Ansicht. Erst danach ist die ListBox
            ' vorhanden und kann den zuletzt gewaehlten Titel wirklich sichtbar machen.
            Dispatcher.UIThread.Post(Sub()
                                         Dim track = _focusViewModel?.CurrentTrack
                                         If track IsNot Nothing Then OnPlaylistFocusRequested(track)
                                     End Sub)
        End Sub

        Private Sub OnPlaylistFocusRequested(track As Track)
            Dim list = Me.FindControl(Of ListBox)("PlaylistBox")
            If list Is Nothing OrElse track Is Nothing Then Return
            Dim row = list.Items.OfType(Of PlaylistTrackRow)().FirstOrDefault(Function(entry) Object.ReferenceEquals(entry.Track, track))
            If row Is Nothing Then Return
            list.SelectedItem = row
            list.ScrollIntoView(row)
        End Sub

        Private Sub OnAudioCdRemoved(sender As Object, e As EventArgs)
            _converterPanel?.CloseForRemovedAudioCd()
        End Sub

        Private Sub OnSeeked(seconds As Double)
            ViewModel?.SeekTo(seconds)
        End Sub

        Private Sub OnJumpToCurrentTrackClick(sender As Object, e As RoutedEventArgs)
            Dim viewModel = Me.ViewModel
            Dim track = viewModel?.CurrentTrack
            If track Is Nothing Then Return
            If viewModel.IsPlayingLyrion Then
                ShowLyrionPanel(viewModel.LyrionPlayOrder, track)
            Else
                viewModel.FocusTrackInPlaylist(track)
            End If
        End Sub

        Private Sub OnSidePanelDrag(sender As Object, e As VectorEventArgs)
            Dim viewModel = Me.ViewModel
            If viewModel Is Nothing Then Return
            viewModel.SidePanelWidth += e.Vector.X
        End Sub

        ''' <summary>Doppelklick in die Liste. Auf einem Titel startet er ihn, auf einer
        ''' Ueberschrift klappt er die Gruppe um - dieselbe Geste wie in einem Dateibrowser.</summary>
        Private Sub OnPlaylistDoubleTapped(sender As Object, e As TappedEventArgs)
            Dim viewModel = Me.ViewModel
            If viewModel Is Nothing Then Return

            Dim row = FindRow(TryCast(e.Source, Control))
            If row Is Nothing Then Return

            Dim trackRow = TryCast(row, PlaylistTrackRow)
            If trackRow IsNot Nothing Then
                viewModel.Play(trackRow.Track)
                e.Handled = True
                Return
            End If

            Dim groupRow = TryCast(row, PlaylistGroupRow)
            If groupRow IsNot Nothing Then
                viewModel.ToggleGroup(groupRow)
                e.Handled = True
            End If
        End Sub

        Private Sub OnGroupToggleClick(sender As Object, e As RoutedEventArgs)
            Dim button = TryCast(sender, Button)
            Dim groupRow = TryCast(button?.DataContext, PlaylistGroupRow)
            If groupRow Is Nothing Then Return
            ViewModel?.ToggleGroup(groupRow)
            e.Handled = True
        End Sub

        Private Sub OnTrackPlayClick(sender As Object, e As RoutedEventArgs)
            Dim row = TryCast(TryCast(sender, MenuItem)?.Tag, PlaylistTrackRow)
            If row IsNot Nothing Then ViewModel?.Play(row.Track)
        End Sub

        Private Sub OnTrackConvertClick(sender As Object, e As RoutedEventArgs)
            Dim row = TryCast(TryCast(sender, MenuItem)?.Tag, PlaylistTrackRow)
            If row Is Nothing Then Return
            ShowConverter(ConversionTracksForContext(row))
        End Sub
        Private Sub OnTrackTagClick(sender As Object, e As RoutedEventArgs)
            ShowTagEditor(ConversionTracksForContext(TryCast(TryCast(sender, MenuItem)?.Tag, PlaylistTrackRow)))
        End Sub

        Private Sub OnTrackRemoveClick(sender As Object, e As RoutedEventArgs)
            Dim row = TryCast(TryCast(sender, MenuItem)?.Tag, PlaylistTrackRow)
            If row IsNot Nothing Then ViewModel?.RemoveTracks({row.Track})
        End Sub

        Private Sub OnGroupPlayClick(sender As Object, e As RoutedEventArgs)
            ViewModel?.PlayGroup(TryCast(TryCast(sender, MenuItem)?.Tag, PlaylistGroupRow))
        End Sub

        Private Sub OnGroupConvertClick(sender As Object, e As RoutedEventArgs)
            Dim group = TryCast(TryCast(sender, MenuItem)?.Tag, PlaylistGroupRow)
            If group Is Nothing Then Return
            ' Wie im Dateimanager gilt das Kontextmenue fuer die ganze Auswahl, sofern die
            ' angeklickte Albumzeile dazugehört. So lassen sich mehrere Alben auf einmal senden.
            ShowConverter(ConversionTracksForContext(group))
        End Sub
        Private Sub OnGroupTagClick(sender As Object, e As RoutedEventArgs)
            ShowTagEditor(ConversionTracksForContext(TryCast(TryCast(sender, MenuItem)?.Tag, PlaylistGroupRow)))
        End Sub

        ''' <summary>Ein Bereichswechsel nimmt die Statuszeile mit. Sie gehoert zu dem, was eben
        ''' getan wurde - stehen gelassen sieht sie aus, als gehoere sie zur neuen Ansicht.</summary>
        Private Sub ClearStatus()
            ViewModel?.ClearStatus()
        End Sub

        Private Sub ShowTagEditor(tracks As IEnumerable(Of Track))
            ClearStatus()
            Dim selected = tracks?.Where(Function(track) track IsNot Nothing AndAlso String.Equals(IO.Path.GetExtension(track.FilePath), ".mp3", StringComparison.OrdinalIgnoreCase)).ToList()
            If selected Is Nothing OrElse selected.Count = 0 Then Return
            _tagEditorPanel = New TagEditorPanel(selected) : AddHandler _tagEditorPanel.CloseRequested, AddressOf OnTagEditorCloseRequested
            AddHandler _tagEditorPanel.Saved, AddressOf OnTagEditorSaved
            ' Die Coverspalte gehoert jetzt den Dateien, die getaggt werden, und nicht mehr dem,
            ' was gerade laeuft. Erst beim Verlassen kommt die Wiedergabe dort zurueck.
            Dim tagCover = Me.FindControl(Of TagCoverPanel)("TagCoverPanel")
            AddHandler tagCover.CoverChosen, AddressOf OnTagCoverChosen
            tagCover.Show(selected)
            tagCover.IsVisible = True
            Me.FindControl(Of Border)("NowPlayingPanel").IsVisible = False
            Dim host = Me.FindControl(Of ContentControl)("ConverterHost") : host.Content = _tagEditorPanel : host.IsVisible = True
            Me.FindControl(Of Control)("PlaylistHeader").IsVisible = False : Me.FindControl(Of ListBox)("PlaylistBox").IsVisible = False
            Me.FindControl(Of Control)("PlaylistEmptyHint").IsVisible = False : Me.FindControl(Of Control)("PlaylistSummaryText").IsVisible = False : Me.FindControl(Of Control)("PlaylistToolbar").IsVisible = False
        End Sub
        Private Sub OnLyrionClick(sender As Object, e As RoutedEventArgs)
            ShowLyrionPanel()
        End Sub
        Private Sub ShowLyrionPanel(Optional tracks As IEnumerable(Of Track) = Nothing, Optional currentTrack As Track = Nothing)
            ClearStatus()
            _lyrionPanel = New LyrionBrowserPanel(tracks, currentTrack) : AddHandler _lyrionPanel.CloseRequested, AddressOf OnLyrionCloseRequested
            AddHandler _lyrionPanel.JumpToCurrentRequested, AddressOf OnLyrionJumpToCurrentRequested
            Dim host = Me.FindControl(Of ContentControl)("ConverterHost") : host.Content = _lyrionPanel : host.IsVisible = True
            Me.FindControl(Of Control)("PlaylistHeader").IsVisible = False : Me.FindControl(Of ListBox)("PlaylistBox").IsVisible = False
            Me.FindControl(Of Control)("PlaylistEmptyHint").IsVisible = False : Me.FindControl(Of Control)("PlaylistSummaryText").IsVisible = False : Me.FindControl(Of Control)("PlaylistToolbar").IsVisible = False
        End Sub
        Private Sub OnLyrionCloseRequested(sender As Object, e As EventArgs)
            OnConverterCloseRequested(sender, e) : _lyrionPanel = Nothing
        End Sub
        Private Sub OnLyrionJumpToCurrentRequested(track As Track)
            OnLyrionCloseRequested(_lyrionPanel, EventArgs.Empty)
            Dispatcher.UIThread.Post(Sub() ViewModel?.FocusTrackInPlaylist(track))
        End Sub
        Private Sub OnTagEditorCloseRequested(sender As Object, e As EventArgs)
            OnConverterCloseRequested(sender, e) : _tagEditorPanel = Nothing
        End Sub
        ''' <summary>Nach dem Schreiben zeigt die Coverspalte das Bild, das jetzt wirklich in den
        ''' Dateien steht - mit dem Mass, auf das es beim Schreiben gebracht wurde.</summary>
        Private Sub OnTagEditorSaved(tracks As IReadOnlyList(Of Track))
            Me.FindControl(Of TagCoverPanel)("TagCoverPanel")?.Show(tracks)
            ' Auch die Wiedergabeliste und der laufende Titel zeigen sonst weiter die alten
            ' Angaben - der Titel eines Track-Objekts meldet seine Aenderung nicht von selbst.
            ViewModel?.RefreshAfterTagging()
        End Sub

        ''' <summary>Ein Bild wurde auf die Tag-Coverspalte gelegt. Geprueft und gelesen hat sie
        ''' es schon; hier wird es nur an den Tag-Bereich weitergereicht.</summary>
        Private Sub OnTagCoverChosen(bytes As Byte(), fileName As String)
            _tagEditorPanel?.SetCover(bytes, fileName)
        End Sub

        ''' <summary>Die Tag-Coverspalte, wenn sie sichtbar ist UND der Zeiger ueber ihr steht -
        ''' sonst Nothing. Damit landet ein Bild nur dort, wo es hingehoert, und ein Titel, der
        ''' daneben in die Liste gezogen wird, geht weiterhin ans Fenster.</summary>
        Private Function TagCoverTargetAt(e As DragEventArgs) As TagCoverPanel
            If _tagEditorPanel Is Nothing Then Return Nothing
            Dim panel = Me.FindControl(Of TagCoverPanel)("TagCoverPanel")
            If panel Is Nothing OrElse Not panel.IsVisible Then Return Nothing
            Dim point = e.GetPosition(panel)
            If point.X < 0 OrElse point.Y < 0 OrElse point.X > panel.Bounds.Width OrElse point.Y > panel.Bounds.Height Then Return Nothing
            Return panel
        End Function

        Private Sub OnTagCoverDragOver(sender As Object, e As DragEventArgs)
            Dim panel = TagCoverTargetAt(e)
            ' Auch das Abschalten laeuft hier: zieht der Zeiger von der Spalte herunter, ohne das
            ' Fenster zu verlassen, gibt es kein DragLeave.
            Me.FindControl(Of TagCoverPanel)("TagCoverPanel")?.SetDropActive(panel IsNot Nothing)
            If panel Is Nothing Then Return
            e.DragEffects = If(e.DataTransfer.Contains(DataFormat.File), DragDropEffects.Copy, DragDropEffects.None)
            e.Handled = True
        End Sub

        Private Sub OnTagCoverDragLeave(sender As Object, e As DragEventArgs)
            Me.FindControl(Of TagCoverPanel)("TagCoverPanel")?.SetDropActive(False)
        End Sub

        Private Async Sub OnTagCoverDrop(sender As Object, e As DragEventArgs)
            Dim panel = TagCoverTargetAt(e)
            If panel Is Nothing Then Return
            Dim entry = e.DataTransfer.TryGetFiles()?.FirstOrDefault()
            Dim localPath = entry?.TryGetLocalPath()
            If String.IsNullOrWhiteSpace(localPath) Then Return
            ' VOR dem ersten Await: danach ist das Ereignis laengst zum Fenster weitergestiegen,
            ' und dessen Ablegen-Behandlung haengt das Bild zusaetzlich in die Wiedergabeliste.
            e.Handled = True
            panel.SetDropActive(False)
            Try
                Await panel.ApplyCoverAsync(localPath)
            Catch ex As Exception
                DiagnosticLogService.LogException("TagEditor.CoverDrop", ex)
            End Try
        End Sub

        Private Sub ShowConverter(tracks As IEnumerable(Of Track))
            ClearStatus()
            Dim selected = tracks?.Where(Function(track) track IsNot Nothing).ToList()
            If selected Is Nothing OrElse selected.Count = 0 Then Return
            _converterPanel = New ConverterPanel(selected)
            AddHandler _converterPanel.CloseRequested, AddressOf OnConverterCloseRequested
            Dim host = Me.FindControl(Of ContentControl)("ConverterHost")
            host.Content = _converterPanel
            host.IsVisible = True
            Me.FindControl(Of Control)("PlaylistHeader").IsVisible = False
            Me.FindControl(Of ListBox)("PlaylistBox").IsVisible = False
            Me.FindControl(Of Control)("PlaylistEmptyHint").IsVisible = False
            Me.FindControl(Of Control)("PlaylistSummaryText").IsVisible = False
            Me.FindControl(Of Control)("PlaylistToolbar").IsVisible = False
        End Sub

        ''' <summary>Ein Kontextmenü gehört zu seiner angeklickten Zeile. Ist diese Teil einer
        ''' Mehrfachauswahl, gilt seine Aktion aber für die ganze Auswahl – genauso wie im
        ''' Dateimanager. Ein Rechtsklick auf einen nicht markierten Titel bleibt dagegen eine
        ''' Aktion nur für diesen Titel.</summary>
        ''' <summary>Liefert die Titel einer Auswahl. Gruppen stehen fuer alle ihre Titel, damit
        ''' Ordner/Alben und einzelne Titel beliebig zusammen markiert werden koennen.</summary>
        Private Function ConversionTracksForContext(contextRow As PlaylistRow) As IEnumerable(Of Track)
            Dim list = Me.FindControl(Of ListBox)("PlaylistBox")
            If list Is Nothing Then
                Dim loneTrack = TryCast(contextRow, PlaylistTrackRow)
                If loneTrack IsNot Nothing Then Return {loneTrack.Track}
                Dim loneGroup = TryCast(contextRow, PlaylistGroupRow)
                Return If(loneGroup Is Nothing OrElse ViewModel Is Nothing,
                          New List(Of Track)(), ViewModel.TracksInGroup(loneGroup))
            End If

            Dim selectedRows = list.SelectedItems.OfType(Of PlaylistRow)().ToList()
            If Not selectedRows.Any(Function(row) Object.ReferenceEquals(row, contextRow)) Then
                selectedRows = New List(Of PlaylistRow) From {contextRow}
            End If

            Dim selectedTracks As New List(Of Track)()
            Dim seen As New HashSet(Of Track)()
            Dim vm = ViewModel
            For Each row In selectedRows
                Dim trackRow = TryCast(row, PlaylistTrackRow)
                If trackRow IsNot Nothing Then
                    If trackRow.Track IsNot Nothing AndAlso seen.Add(trackRow.Track) Then selectedTracks.Add(trackRow.Track)
                    Continue For
                End If

                Dim groupRow = TryCast(row, PlaylistGroupRow)
                If groupRow Is Nothing OrElse vm Is Nothing Then Continue For
                For Each track In vm.TracksInGroup(groupRow)
                    If track IsNot Nothing AndAlso seen.Add(track) Then selectedTracks.Add(track)
                Next
            Next
            Return selectedTracks
        End Function

        ''' <summary>Mehrfachauswahl ist auch vollstaendig per Tastatur erreichbar. Avalonia
        ''' bringt fuer ListBox nicht auf allen Plattformen eine einheitliche Strg+A-Belegung mit,
        ''' deshalb wird sie hier explizit umgesetzt.</summary>
        Private Sub OnPlaylistKeyDown(sender As Object, e As KeyEventArgs)
            If e.Key <> Key.A OrElse Not e.KeyModifiers.HasFlag(KeyModifiers.Control) Then Return
            Dim list = TryCast(sender, ListBox)
            If list Is Nothing Then Return

            list.SelectedItems.Clear()
            For Each row In list.Items.OfType(Of PlaylistTrackRow)()
                list.SelectedItems.Add(row)
            Next
            e.Handled = True
        End Sub

        Private Sub OnConverterCloseRequested(sender As Object, e As EventArgs)
            ClearStatus()
            Dim panel = TryCast(sender, ConverterPanel)
            Dim host = Me.FindControl(Of ContentControl)("ConverterHost")
            host.Content = Nothing
            host.IsVisible = False
            Me.FindControl(Of Control)("PlaylistHeader").IsVisible = True
            Me.FindControl(Of ListBox)("PlaylistBox").IsVisible = True
            Me.FindControl(Of Control)("PlaylistEmptyHint").IsVisible = ViewModel IsNot Nothing AndAlso ViewModel.IsPlaylistEmpty
            Me.FindControl(Of Control)("PlaylistSummaryText").IsVisible = True
            Me.FindControl(Of Control)("PlaylistToolbar").IsVisible = True
            ' Die Coverspalte kommt hier zurueck und nicht erst im Tag-Bereich: JEDER Weg aus einem
            ' eingeblendeten Bereich laeuft hier durch, auch ein ungewoehnlicher.
            RestoreNowPlayingColumn()
            _converterPanel = Nothing
        End Sub

        ''' <summary>Gibt die Coverspalte wieder der laufenden Wiedergabe.</summary>
        Private Sub RestoreNowPlayingColumn()
            Dim tagCover = Me.FindControl(Of TagCoverPanel)("TagCoverPanel")
            If tagCover Is Nothing OrElse Not tagCover.IsVisible Then Return
            RemoveHandler tagCover.CoverChosen, AddressOf OnTagCoverChosen
            tagCover.IsVisible = False
            Me.FindControl(Of Border)("NowPlayingPanel").IsVisible = True
        End Sub

        Private Sub OnGroupToggleMenuClick(sender As Object, e As RoutedEventArgs)
            ViewModel?.ToggleGroup(TryCast(TryCast(sender, MenuItem)?.Tag, PlaylistGroupRow))
        End Sub

        Private Sub OnGroupRemoveClick(sender As Object, e As RoutedEventArgs)
            Dim group = TryCast(TryCast(sender, MenuItem)?.Tag, PlaylistGroupRow)
            Dim vm As MainWindowViewModel = Me.ViewModel
            If group IsNot Nothing AndAlso vm IsNot Nothing Then vm.RemoveTracks(vm.TracksInGroup(group))
        End Sub

        ' Die Liste wird ohne System-Dateipayload umsortiert: der Zug bleibt innerhalb der
        ' ListBox, und nur die gespeicherte Reihenfolge aendert sich. Im Zufallsmodus ist die
        ' Ansicht absichtlich die temporaere Abspielreihenfolge und daher nicht verschiebbar.
        Private Sub OnTrackPointerPressed(sender As Object, e As PointerPressedEventArgs)
            Dim vm As MainWindowViewModel = Me.ViewModel
            If vm Is Nothing OrElse vm.IsShuffle OrElse Not e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then Return
            _draggedTrack = TryCast(TryCast(sender, Border)?.DataContext, PlaylistTrackRow)?.Track
        End Sub

        Private Sub OnTrackPointerMoved(sender As Object, e As PointerEventArgs)
            Dim vm As MainWindowViewModel = Me.ViewModel
            If _draggedTrack Is Nothing OrElse vm Is Nothing OrElse vm.IsShuffle OrElse Not e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then Return
            Dim target = TryCast(FindRow(TryCast(e.Source, Control)), PlaylistTrackRow)?.Track
            If target Is Nothing OrElse Object.ReferenceEquals(target, _draggedTrack) Then Return
            vm.MoveTrackBefore(_draggedTrack, target)
        End Sub

        Private Sub OnTrackPointerReleased(sender As Object, e As PointerReleasedEventArgs)
            _draggedTrack = Nothing
        End Sub

        ''' <summary>Die Zeile unter dem angeklickten Element. Der Klick landet auf einem Textblock
        ''' oder einem Rahmen tief in der Vorlage; die Zeile steht als Datenzusammenhang daran, und
        ''' der Weg nach oben findet sie sicherer als ein Griff in die Innereien der Liste.</summary>
        Private Shared Function FindRow(source As Control) As PlaylistRow
            Dim control = source
            While control IsNot Nothing
                Dim row = TryCast(control.DataContext, PlaylistRow)
                If row IsNot Nothing Then Return row
                control = TryCast(control.Parent, Control)
            End While
            Return Nothing
        End Function

        ' Dateien und Ordner hinzufuegen

        Private Async Sub OnAddFilesClick(sender As Object, e As RoutedEventArgs)
            Dim viewModel = Me.ViewModel
            Dim storage = TopLevel.GetTopLevel(Me)?.StorageProvider
            If viewModel Is Nothing OrElse storage Is Nothing Then Return

            Try
                Dim files = Await storage.OpenFilePickerAsync(New FilePickerOpenOptions With {
                    .Title = LocalizationService.T("Titel hinzufügen"),
                    .AllowMultiple = True,
                    .SuggestedStartLocation = Await StartLocation(storage),
                    .FileTypeFilter = New List(Of FilePickerFileType) From {
                        New FilePickerFileType(LocalizationService.T("Tondateien")) With {
                            .Patterns = TagReadService.SupportedExtensions.Select(Function(x) "*" & x).ToList()
                        },
                        FilePickerFileTypes.All
                    }
                })

                Dim paths = LocalPaths(files)
                If paths.Count = 0 Then Return
                RememberBrowseFolder(paths(0), isFolder:=False)
                Await viewModel.AddPathsAsync(paths)
            Catch ex As Exception
                DiagnosticLogService.LogException("Player.AddFiles", ex)
            End Try
        End Sub

        Private Async Sub OnAddFolderClick(sender As Object, e As RoutedEventArgs)
            Dim viewModel = Me.ViewModel
            Dim storage = TopLevel.GetTopLevel(Me)?.StorageProvider
            If viewModel Is Nothing OrElse storage Is Nothing Then Return

            Try
                Dim folders = Await storage.OpenFolderPickerAsync(New FolderPickerOpenOptions With {
                    .Title = LocalizationService.T("Ordner hinzufügen"),
                    .AllowMultiple = True,
                    .SuggestedStartLocation = Await StartLocation(storage)
                })

                Dim paths = LocalPaths(folders)
                If paths.Count = 0 Then Return
                RememberBrowseFolder(paths(0), isFolder:=True)
                Await viewModel.AddPathsAsync(paths)
            Catch ex As Exception
                DiagnosticLogService.LogException("Player.AddFolder", ex)
            End Try
        End Sub

        Private Async Sub OnAddAudioCdClick(sender As Object, e As RoutedEventArgs)
            Dim viewModel = Me.ViewModel
            If viewModel Is Nothing Then Return
            Await viewModel.AddAudioCdAsync()
        End Sub

        Private Sub OnRemoveSelectedClick(sender As Object, e As RoutedEventArgs)
            Dim viewModel = Me.ViewModel
            Dim list = Me.FindControl(Of ListBox)("PlaylistBox")
            If viewModel Is Nothing OrElse list Is Nothing Then Return

            ' Ist eine Ueberschrift mit ausgewaehlt, gehen ihre Titel mit: sie stellt die Gruppe
            ' dar, und eine Gruppe ohne Titel gibt es nicht.
            ' Die Schleifenvariable heisst NICHT "item": "Item" ist die Standardeigenschaft von
            ' AvaloniaObject, von dem jedes Steuerelement abstammt, und VB haelt den Namen dann
            ' fuer diese Eigenschaft und verlangt ein Argument.
            Dim doomed As New List(Of Track)()
            For Each entry In list.SelectedItems
                Dim trackRow = TryCast(entry, PlaylistTrackRow)
                If trackRow IsNot Nothing Then
                    doomed.Add(trackRow.Track)
                    Continue For
                End If

                Dim groupRow = TryCast(entry, PlaylistGroupRow)
                If groupRow Is Nothing Then Continue For
                For Each row In list.Items.OfType(Of PlaylistTrackRow)()
                    If String.Equals(row.Track.FolderPath, groupRow.FolderPath, StringComparison.Ordinal) Then doomed.Add(row.Track)
                Next
            Next

            If doomed.Count = 0 Then Return
            viewModel.RemoveTracks(doomed)
        End Sub

        Private Shared Function LocalPaths(items As IEnumerable(Of IStorageItem)) As List(Of String)
            Dim paths As New List(Of String)()
            If items Is Nothing Then Return paths
            For Each entry In items
                Dim localPath = entry.TryGetLocalPath()
                If Not String.IsNullOrWhiteSpace(localPath) Then paths.Add(localPath)
            Next
            Return paths
        End Function

        ''' <summary>Der Ordner, in dem der Dialog aufgeht. Beim ersten Mal der Musikordner des
        ''' Benutzers, danach der zuletzt besuchte.</summary>
        Private Shared Async Function StartLocation(storage As IStorageProvider) As Threading.Tasks.Task(Of IStorageFolder)
            Try
                Dim remembered = AppSettingsService.Current.LastBrowseFolder
                If Not String.IsNullOrWhiteSpace(remembered) AndAlso IO.Directory.Exists(remembered) Then
                    Return Await storage.TryGetFolderFromPathAsync(remembered)
                End If
                Return Await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Music)
            Catch ex As Exception
                DiagnosticLogService.LogException("Player.StartLocation", ex)
                Return Nothing
            End Try
        End Function

        Private Shared Sub RememberBrowseFolder(path As String, isFolder As Boolean)
            Try
                Dim folder = If(isFolder, path, IO.Path.GetDirectoryName(path))
                If Not String.IsNullOrWhiteSpace(folder) Then AppSettingsService.Current.LastBrowseFolder = folder
            Catch ex As Exception
                DiagnosticLogService.LogException("Player.RememberFolder", ex)
            End Try
        End Sub

    End Class

End Namespace
