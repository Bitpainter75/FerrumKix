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
Imports FerrumPlay.Models
Imports FerrumPlay.Services
Imports FerrumPlay.ViewModels

Namespace Views

    Public Class PlayerView
        Inherits UserControl

        Public Sub New()
            InitializeComponent()

            Dim seek = Me.FindControl(Of Controls.SeekBar)("Seek")
            If seek IsNot Nothing Then AddHandler seek.Seeked, AddressOf OnSeeked
        End Sub

        Private Sub InitializeComponent()
            AvaloniaXamlLoader.Load(Me)
        End Sub

        Private ReadOnly Property ViewModel As MainWindowViewModel
            Get
                Return TryCast(DataContext, MainWindowViewModel)
            End Get
        End Property

        Private Sub OnSeeked(seconds As Double)
            ViewModel?.SeekTo(seconds)
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
