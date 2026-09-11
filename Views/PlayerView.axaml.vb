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

        Public Sub New()
            InitializeComponent()

            Dim seek = Me.FindControl(Of Controls.SeekBar)("Seek")
            If seek IsNot Nothing Then AddHandler seek.Seeked, AddressOf OnSeeked
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
            If _focusViewModel IsNot Nothing Then RemoveHandler _focusViewModel.PlaylistFocusRequested, AddressOf OnPlaylistFocusRequested
            _focusViewModel = ViewModel
            If _focusViewModel IsNot Nothing Then AddHandler _focusViewModel.PlaylistFocusRequested, AddressOf OnPlaylistFocusRequested
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

        Private Sub OnSeeked(seconds As Double)
            ViewModel?.SeekTo(seconds)
        End Sub

        Private Sub OnJumpToCurrentTrackClick(sender As Object, e As RoutedEventArgs)
            Dim viewModel = Me.ViewModel
            Dim track = viewModel?.CurrentTrack
            If track IsNot Nothing Then viewModel.FocusTrackInPlaylist(track)
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
            ShowConverter({row.Track})
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
            Dim vm As MainWindowViewModel = Me.ViewModel
            If group Is Nothing OrElse vm Is Nothing Then Return
            ShowConverter(vm.TracksInGroup(group))
        End Sub

        Private Sub ShowConverter(tracks As IEnumerable(Of Track))
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

        Private Sub OnConverterCloseRequested(sender As Object, e As EventArgs)
            Dim host = Me.FindControl(Of ContentControl)("ConverterHost")
            host.Content = Nothing
            host.IsVisible = False
            Me.FindControl(Of Control)("PlaylistHeader").IsVisible = True
            Me.FindControl(Of ListBox)("PlaylistBox").IsVisible = True
            Me.FindControl(Of Control)("PlaylistEmptyHint").IsVisible = ViewModel IsNot Nothing AndAlso ViewModel.IsPlaylistEmpty
            Me.FindControl(Of Control)("PlaylistSummaryText").IsVisible = True
            Me.FindControl(Of Control)("PlaylistToolbar").IsVisible = True
            _converterPanel = Nothing
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
