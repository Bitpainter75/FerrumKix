Imports System
Imports System.Collections.ObjectModel
Imports System.IO
Imports System.Globalization
Imports System.Linq
Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Layout
Imports Avalonia.Markup.Xaml
Imports FerrumPlay.Models
Imports FerrumPlay.Services
Imports FerrumPlay.ViewModels

Namespace Views
 Public Class LyrionBrowserPanel
  Inherits UserControl
  Public Event CloseRequested As EventHandler
  Public Event JumpToCurrentRequested(track As Track)
  Private _shownAlbum As LyrionMediaServerService.Album
  Private _albumTracks As New List(Of Track)()
  Private _viewModel As MainWindowViewModel
  Private _searchRequest As Integer
  ''' <summary>Die Kacheln der Albenuebersicht. Der Repeater haengt daran und baut daraus nur, was
  ''' gerade sichtbar ist.</summary>
  Private ReadOnly _albumTiles As New ObservableCollection(Of LyrionAlbumTile)()
  ''' <summary>Wie viele Alben der Server insgesamt zur aktuellen Suche hat.</summary>
  Private _albumsTotal As Integer
  Private _albumsLoading As Boolean
  Private ReadOnly _searchDebounce As New Avalonia.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(300)}
  Public Sub New()
   Me.New(Nothing, Nothing)
  End Sub
  Public Sub New(existingTracks As IEnumerable(Of Track), currentTrack As Track)
   AvaloniaXamlLoader.Load(Me)
   ' Wie im Tag-Bereich: gebaut wird dieses Panel erst nach dem Uebersetzungsdurchlauf des
   ' Fensters, also laeuft er hier noch einmal und bei jedem Sprachwechsel erneut.
   LocalizationService.ApplyTo(Me)
   AddHandler LocalizationService.LanguageChanged, AddressOf OnLanguageChanged
   AddHandler DetachedFromVisualTree, Sub(sender, e) RemoveHandler LocalizationService.LanguageChanged, AddressOf OnLanguageChanged
   FindControl(Of ItemsRepeater)("Albums").ItemsSource = _albumTiles
   AddHandler _searchDebounce.Tick, AddressOf OnSearchDebounceTick
   AddHandler DataContextChanged, AddressOf OnPanelDataContextChanged
   Dim restored = If(existingTracks, Enumerable.Empty(Of Track)()).ToList()
   If restored.Count = 0 Then
    LoadAlbumsAsync()
   Else
    ShowExistingTracks(restored, currentTrack)
   End If
  End Sub
  Private Sub OnLanguageChanged(sender As Object, e As EventArgs)
   LocalizationService.ApplyTo(Me)
  End Sub

  Private Sub ShowExistingTracks(tracks As List(Of Track), currentTrack As Track)
   ' Eine noch laufende Albumabfrage darf diese wiederhergestellte Trackliste nicht danach
   ' wieder durch das Albengitter ersetzen.
   Threading.Interlocked.Increment(_searchRequest)
   _albumTracks = tracks
   Dim first = tracks(0)
   _shownAlbum = New LyrionMediaServerService.Album With {.Title = first.Album, .Artist = first.AlbumArtist}
   FindControl(Of ScrollViewer)("AlbumScroll").IsVisible = False
   FindControl(Of ScrollViewer)("TrackScroll").IsVisible = True
   FindControl(Of TextBlock)("PageTitle").Text = If(String.IsNullOrWhiteSpace(first.Album), "Lyrion Media Server", first.Album)
   FindControl(Of TextBlock)("Status").Text = first.AlbumArtist & "  ·  " & LocalizationService.Format("{0} Titel", tracks.Count)
   RenderTracks(tracks)
   Dim list = FindControl(Of ListBox)("Tracks")
   Dim selected = list.Items.OfType(Of ListBoxItem)().FirstOrDefault(Function(item) Object.ReferenceEquals(item.Tag, currentTrack))
   If selected IsNot Nothing Then list.SelectedItem = selected : list.ScrollIntoView(selected)
  End Sub
  ''' <summary>Wie viele Alben eine Abfrage holt. Gross genug, dass ein Bildschirm voll wird, klein
  ''' genug, dass die erste Kachel schnell da ist.</summary>
  Private Const AlbumPageSize As Integer = 120

  ''' <summary>Ab diesem Abstand zum Ende des Rollbereichs wird die naechste Seite geholt. Etwa
  ''' zwei Kachelzeilen: der Nachschub ist da, bevor der Anwender das Ende sieht.</summary>
  Private Const AlbumPreloadDistance As Double = 500

  Private Async Sub LoadAlbumsAsync()
   Dim request = Threading.Interlocked.Increment(_searchRequest)
   _albumTiles.Clear()
   _albumsTotal = 0
   _albumsLoading = False
   Dim scroll = FindControl(Of ScrollViewer)("AlbumScroll")
   scroll.IsVisible = True : FindControl(Of ScrollViewer)("TrackScroll").IsVisible = False
   scroll.Offset = New Avalonia.Vector(0, 0)
   FindControl(Of TextBlock)("PageTitle").Text = "Lyrion Media Server"
   FindControl(Of TextBlock)("Status").Text = LocalizationService.T("Alben werden geladen …")
   Await LoadNextAlbumPageAsync(request)
  End Sub

  ''' <summary>Holt den naechsten Ausschnitt und haengt ihn an. Der Ausschnitt wird an der Anfrage
  ''' festgemacht, mit der er begonnen hat: eine inzwischen getippte Suche verwirft ihn.</summary>
  Private Async Function LoadNextAlbumPageAsync(request As Integer) As Threading.Tasks.Task
   If _albumsLoading Then Return
   If _albumTiles.Count > 0 AndAlso _albumTiles.Count >= _albumsTotal Then Return
   _albumsLoading = True
   Try
    Dim offset = _albumTiles.Count
    Dim page = Await LyrionMediaServerService.GetAlbumsAsync(FindControl(Of TextBox)("SearchBox").Text, offset, AlbumPageSize, Threading.CancellationToken.None)
    ' Verworfen wird, was nicht mehr zur laufenden Suche gehoert - und ebenso, was an eine
    ' inzwischen anders gefuellte Liste nicht mehr lueckenlos anschliesst.
    If request <> Threading.Volatile.Read(_searchRequest) OrElse offset <> _albumTiles.Count Then Return
    _albumsTotal = page.Total
    For Each album In page.Albums
     _albumTiles.Add(New LyrionAlbumTile(album))
    Next
    FindControl(Of TextBlock)("Status").Text = If(_albumTiles.Count = 0, LocalizationService.T("Keine Alben gefunden."), LocalizationService.Format("{0} von {1} Alben", _albumTiles.Count, Math.Max(_albumsTotal, _albumTiles.Count)))
    ' Ein hohes Fenster zeigt mehr als eine Seite. Dann muss gleich weitergeladen werden, sonst
    ' gibt es nichts zu rollen und das Nachladen kaeme nie wieder in Gang.
    If page.Albums.Count > 0 Then Avalonia.Threading.Dispatcher.UIThread.Post(Sub() FillAlbumViewport(request), Avalonia.Threading.DispatcherPriority.Background)
   Catch ex As Exception
    If request = Threading.Volatile.Read(_searchRequest) Then FindControl(Of TextBlock)("Status").Text = ex.Message
   Finally
    ' Nur die laufende Suche gibt die Sperre wieder frei. Sonst oeffnete ein spaet
    ' eintreffender Rest der vorigen Suche den Weg fuer eine zweite Abfrage derselben Seite.
    If request = Threading.Volatile.Read(_searchRequest) Then _albumsLoading = False
   End Try
  End Function

  Private Async Sub FillAlbumViewport(request As Integer)
   If request <> Threading.Volatile.Read(_searchRequest) Then Return
   Dim scroll = FindControl(Of ScrollViewer)("AlbumScroll")
   If scroll Is Nothing OrElse Not scroll.IsVisible Then Return
   If scroll.Extent.Height > scroll.Viewport.Height + AlbumPreloadDistance Then Return
   Await LoadNextAlbumPageAsync(request)
  End Sub

  Private Async Sub OnAlbumScrollChanged(sender As Object, e As ScrollChangedEventArgs)
   Dim scroll = TryCast(sender, ScrollViewer)
   If scroll Is Nothing OrElse Not scroll.IsVisible Then Return
   If scroll.Offset.Y + scroll.Viewport.Height < scroll.Extent.Height - AlbumPreloadDistance Then Return
   Await LoadNextAlbumPageAsync(Threading.Volatile.Read(_searchRequest))
  End Sub

  ''' <summary>Der Repeater hat eine Kachel gebaut - erst jetzt lohnt sich ihr Cover.</summary>
  Private Sub OnAlbumTilePrepared(sender As Object, e As ItemsRepeaterElementPreparedEventArgs)
   TryCast(TryCast(e.Element, Control)?.DataContext, LyrionAlbumTile)?.RequestCover()
  End Sub

  ''' <summary>Der Repeater reicht die Kachel weiter. Ihr Bild wird losgelassen, die Bilddaten
  ''' bleiben im Zwischenspeicher des Dienstes.</summary>
  Private Sub OnAlbumTileClearing(sender As Object, e As ItemsRepeaterElementClearingEventArgs)
   TryCast(TryCast(e.Element, Control)?.DataContext, LyrionAlbumTile)?.ReleaseCover()
  End Sub
  Private Sub OnAlbumClick(sender As Object, e As RoutedEventArgs)
   Dim tile = TryCast(TryCast(sender, Button)?.Tag, LyrionAlbumTile)
   If tile Is Nothing Then Return
   ShowAlbumAsync(tile.Album)
  End Sub

  Private Async Sub ShowAlbumAsync(album As LyrionMediaServerService.Album)
   _shownAlbum = album
   If _shownAlbum Is Nothing Then Return
   ' Eine wiederhergestellte Titelliste bringt ein Album OHNE Kennung mit (siehe
   ' ShowExistingTracks). Eine Abfrage dazu liefert planmaessig nichts, und die Liste stuende
   ' danach leer da - also bleibt sie einfach so, wie sie ist.
   If String.IsNullOrWhiteSpace(_shownAlbum.Id) Then
    FindControl(Of ScrollViewer)("AlbumScroll").IsVisible = False
    FindControl(Of ScrollViewer)("TrackScroll").IsVisible = True
    RenderTracks(_albumTracks)
    Return
   End If
   FindControl(Of ScrollViewer)("AlbumScroll").IsVisible = False
   FindControl(Of ScrollViewer)("TrackScroll").IsVisible = True
   Dim tracks = FindControl(Of ListBox)("Tracks") : tracks.Items.Clear()
   Try
    Dim songs = Await LyrionMediaServerService.GetAlbumSongsAsync(_shownAlbum.Id, Threading.CancellationToken.None)
    FindControl(Of TextBlock)("PageTitle").Text = _shownAlbum.Title
    FindControl(Of TextBlock)("Status").Text = _shownAlbum.Artist & If(String.IsNullOrWhiteSpace(_shownAlbum.Year), "", " · " & _shownAlbum.Year) & "  ·  " & LocalizationService.Format("{0} Titel", songs.Count)
    _albumTracks = songs.OrderBy(Function(entry) TrackNumber(entry.TrackNumber)).ThenBy(Function(entry) entry.Title).Select(AddressOf CreateTrack).ToList()
    RenderTracks(_albumTracks)
   Catch ex As Exception
    FindControl(Of TextBlock)("Status").Text = ex.Message
   End Try
  End Sub
  Private Sub RenderTracks(tracksToShow As IEnumerable(Of Track))
   Dim tracks = FindControl(Of ListBox)("Tracks")
   tracks.Items.Clear()
   For Each track In tracksToShow
     Dim row As New Grid With {.ColumnDefinitions = New ColumnDefinitions("Auto,*,Auto"), .ColumnSpacing = 10}
     row.Children.Add(New CheckBox With {.IsChecked = True, .IsHitTestVisible = False, .VerticalAlignment = VerticalAlignment.Center})
     Dim titleText = If(track.TrackNumber = 0, track.Title, track.TrackNumber & ". " & track.Title)
     Dim title As New StackPanel With {.Spacing = 1} : title.Children.Add(New TextBlock With {.Text = titleText, .FontSize = 13, .TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis})
     ' Interpret und technische Angaben in EINER Zeile: die Liste eines Albums bleibt damit so
     ' hoch wie die Wiedergabeliste daneben.
     Dim info = String.Join(" · ", {track.Artist, track.FormatText}.Where(Function(part) Not String.IsNullOrWhiteSpace(part)))
     If info.Length > 0 Then Dim details As New TextBlock With {.Text = info, .TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis} : details.Classes.Add("muted") : title.Children.Add(details)
     Grid.SetColumn(title, 1) : row.Children.Add(title)
     Dim duration As New TextBlock With {.Text = track.DurationText, .VerticalAlignment = VerticalAlignment.Center} : duration.Classes.Add("secondary") : Grid.SetColumn(duration, 2) : row.Children.Add(duration)
     Dim item As New ListBoxItem With {.Content = New Border With {.Child = row, .Padding = New Avalonia.Thickness(12, 5, 16, 5)}, .Tag = track}
     item.Classes.Add("lyrion-track")
     If Object.ReferenceEquals(track, TryCast(DataContext, MainWindowViewModel)?.CurrentTrack) Then item.Classes.Add("playing")
     tracks.Items.Add(item)
    Next
  End Sub
  Private Sub OnPanelDataContextChanged(sender As Object, e As EventArgs)
   If _viewModel IsNot Nothing Then
    RemoveHandler _viewModel.LyrionPlayOrderChanged, AddressOf OnLyrionPlayOrderChanged
    RemoveHandler _viewModel.LyrionCurrentTrackChanged, AddressOf OnLyrionCurrentTrackChanged
   End If
   _viewModel = TryCast(DataContext, MainWindowViewModel)
   If _viewModel IsNot Nothing Then
    AddHandler _viewModel.LyrionPlayOrderChanged, AddressOf OnLyrionPlayOrderChanged
    AddHandler _viewModel.LyrionCurrentTrackChanged, AddressOf OnLyrionCurrentTrackChanged
   End If
  End Sub
  ''' <summary>Die Wiedergabereihenfolge hat sich geaendert, etwa durch den Zufallsschalter. Die
  ''' offene Liste wird nur dann umsortiert, wenn es DIESELBEN Titel sind: laeuft ein anderes Album
  ''' als das gerade durchgesehene, ginge sonst die Liste auf dem Bildschirm verloren - und ein
  ''' Doppelklick spielte aus der falschen.</summary>
  Private Sub OnLyrionPlayOrderChanged(order As IReadOnlyList(Of Track))
   If _shownAlbum Is Nothing OrElse order Is Nothing OrElse order.Count = 0 Then Return
   If order.Count <> _albumTracks.Count OrElse Not order.All(Function(entry) _albumTracks.Contains(entry)) Then Return
   _albumTracks = order.ToList()
   RenderTracks(_albumTracks)
  End Sub
  Private Sub OnLyrionCurrentTrackChanged(track As Track)
   If _shownAlbum Is Nothing OrElse track Is Nothing Then Return
   RenderTracks(_albumTracks)
  End Sub
  Private Async Sub OnSongClick(sender As Object, e As RoutedEventArgs)
   Dim track = TryCast(TryCast(sender, Button)?.Tag, Track)
   If track Is Nothing Then Return
   Await PlaySongAsync(track)
  End Sub
  Private Async Sub OnTracksDoubleTapped(sender As Object, e As RoutedEventArgs)
   Dim list = TryCast(sender, ListBox)
   Dim track = TryCast(TryCast(list?.SelectedItem, ListBoxItem)?.Tag, Track)
   If track Is Nothing Then Return
   Await PlaySongAsync(track)
  End Sub
  Private Function PlaySongAsync(track As Track) As Threading.Tasks.Task
   Try
    FindControl(Of TextBlock)("Status").Text = LocalizationService.T("Wird an Lyrion übergeben …")
    Dim player = TryCast(DataContext, MainWindowViewModel)
    If player Is Nothing Then Throw New InvalidOperationException(LocalizationService.T("Die lokale Wiedergabe steht noch nicht bereit."))
    player.PlayLyrionAlbum(_albumTracks, track)
    FindControl(Of TextBlock)("Status").Text = LocalizationService.Format("Wiedergabe in FerrumPlay: {0}", track.Title)
   Catch ex As Exception
    FindControl(Of TextBlock)("Status").Text = ex.Message
   End Try
   Return Threading.Tasks.Task.CompletedTask
  End Function
  ''' <summary>Getippt wird schneller als der Server antwortet: ohne diese kurze Ruhe schickt ein
  ''' zwoelf Zeichen langer Kuenstlername zwoelf Abfragen los, von denen elf schon beim Eintreffen
  ''' veraltet sind.</summary>
  Private Sub OnSearchTextChanged(sender As Object, e As TextChangedEventArgs)
   _shownAlbum = Nothing
   _searchDebounce.Stop()
   _searchDebounce.Start()
  End Sub
  Private Sub OnSearchDebounceTick(sender As Object, e As EventArgs)
   _searchDebounce.Stop()
   LoadAlbumsAsync()
  End Sub

  Private Sub OnReloadClick(sender As Object, e As RoutedEventArgs)
   If _shownAlbum Is Nothing Then LoadAlbumsAsync() Else ShowAlbumAsync(_shownAlbum)
  End Sub
  Private Sub OnJumpToCurrentTrackClick(sender As Object, e As RoutedEventArgs)
   Dim viewModel = TryCast(DataContext, MainWindowViewModel)
   Dim current = viewModel?.CurrentTrack
   If current Is Nothing Then Return
   Dim activeLyrionOrder = If(viewModel Is Nothing, Array.Empty(Of Track)(), viewModel.LyrionPlayOrder)
   If activeLyrionOrder.Contains(current) Then
    ShowExistingTracks(activeLyrionOrder.ToList(), current)
    Return
   End If
   If Not viewModel.IsPlayingLyrion Then
    RaiseEvent JumpToCurrentRequested(current)
    Return
   End If
   Dim list = FindControl(Of ListBox)("Tracks")
   Dim item = list.Items.OfType(Of ListBoxItem)().FirstOrDefault(Function(entry) Object.ReferenceEquals(entry.Tag, current))
   If item Is Nothing AndAlso viewModel.IsPlayingLyrion Then
    ShowExistingTracks(viewModel.LyrionPlayOrder.ToList(), current)
    list = FindControl(Of ListBox)("Tracks")
    item = list.Items.OfType(Of ListBoxItem)().FirstOrDefault(Function(entry) Object.ReferenceEquals(entry.Tag, current))
   End If
   If item Is Nothing Then Return
   list.SelectedItem = item
   Avalonia.Threading.Dispatcher.UIThread.Post(Sub() list.ScrollIntoView(item))
  End Sub
  Private Sub OnBackClick(sender As Object, e As RoutedEventArgs)
   If _shownAlbum IsNot Nothing Then _shownAlbum = Nothing : LoadAlbumsAsync() : Return
   RaiseEvent CloseRequested(Me, EventArgs.Empty)
  End Sub
  Private Shared Function TrackNumber(value As String) As Integer
   Dim digits = New String(If(value, String.Empty).TakeWhile(AddressOf Char.IsDigit).ToArray())
   Dim number As Integer
   Return If(Integer.TryParse(digits, number), number, Integer.MaxValue)
  End Function
  Private Shared Function FormatDuration(value As String) As String
   Dim seconds As Double
   If Not Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, seconds) AndAlso Not Double.TryParse(value, seconds) Then Return value
   Dim duration = TimeSpan.FromSeconds(Math.Max(0, seconds))
   Return If(duration.TotalHours >= 1, $"{CInt(duration.TotalHours)}:{duration.Minutes:D2}:{duration.Seconds:D2}", $"{CInt(duration.TotalMinutes)}:{duration.Seconds:D2}")
  End Function
  Private Shared Function DurationSeconds(value As String) As Double
   Dim seconds As Double
   If Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, seconds) OrElse Double.TryParse(value, seconds) Then Return Math.Max(0, seconds)
   Return 0
  End Function
  Private Function CreateTrack(song As LyrionMediaServerService.Song) As Track
   Return New Track With {
    .FilePath = LyrionMediaServerService.StreamUrl(song.Id), .Title = song.Title, .Artist = song.Artist,
    .Album = If(_shownAlbum?.Title, String.Empty), .AlbumArtist = If(_shownAlbum?.Artist, String.Empty),
    .RemoteCoverUrl = LyrionMediaServerService.ArtworkUrl(_shownAlbum), .TrackNumber = TrackNumber(song.TrackNumber),
    .DurationSeconds = DurationSeconds(song.Duration),
    .Year = Number(If(song.Year, String.Empty), If(_shownAlbum?.Year, String.Empty)),
    .Codec = CodecLabel(song.ContentType), .SampleRate = Number(song.SampleRate),
    .Bitrate = Kilobits(song.Bitrate), .FileSize = Number(song.FileSize)}
  End Function

  ''' <summary>Die erste Zahl, die in einem der Werte steckt. Der Server schreibt dieselbe Angabe
  ''' je nach Version als Zahl oder als Text, und ein fehlender Wert bleibt 0.</summary>
  Private Shared Function Number(ParamArray values As String()) As Integer
   For Each value In values
    Dim digits = New String(If(value, String.Empty).SkipWhile(Function(c) Not Char.IsDigit(c)).TakeWhile(AddressOf Char.IsDigit).ToArray())
    Dim parsed As Integer
    If digits.Length > 0 AndAlso Integer.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, parsed) Then Return parsed
   Next
   Return 0
  End Function

  ''' <summary>Die Bitrate in kbit/s. Der Server meldet sie mal als "320kbps CBR", mal als reine
  ''' Bit-je-Sekunde-Zahl; ueber 10000 kann nur Letzteres gemeint sein.</summary>
  Private Shared Function Kilobits(value As String) As Integer
   Dim parsed = Number(value)
   Return If(parsed > 10000, CInt(Math.Round(parsed / 1000.0)), parsed)
  End Function

  ''' <summary>Aus dem Inhaltstyp des Servers die Kurzform, die auch bei lokalen Dateien in der
  ''' Liste steht.</summary>
  Private Shared Function CodecLabel(contentType As String) As String
   Select Case If(contentType, String.Empty).Trim().ToLowerInvariant()
    Case "" : Return String.Empty
    Case "mp3" : Return "MP3"
    Case "flc", "flac" : Return "FLAC"
    Case "ogg", "ogf", "ogv" : Return "OGG"
    Case "ops", "opus" : Return "OPUS"
    Case "aac", "mp4", "m4a" : Return "AAC"
    Case "alc", "alac" : Return "ALAC"
    Case "wav", "aif" : Return "WAV"
    Case "wvp" : Return "WV"
    Case "ape" : Return "APE"
    Case Else : Return contentType.Trim().ToUpperInvariant()
   End Select
  End Function
 End Class
End Namespace
