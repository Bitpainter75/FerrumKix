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
  ''' <summary>Die Alben zur laufenden Suche, VOLLSTAENDIG und in der Reihenfolge, die der
  ''' Dienst geliefert hat. Aus ihr baut ApplyAlbumView die sichtbaren Kacheln.</summary>
  Private _albums As New List(Of LyrionMediaServerService.Album)()
  ''' <summary>Die Favoritenadressen des Servers. Siehe GetFavoriteAlbumUrlsAsync.</summary>
  Private _favoriteUrls As New HashSet(Of String)(StringComparer.Ordinal)
  Private _favoritesLoaded As Boolean
  ''' <summary>Die gebauten Kacheln nach Album-Kennung, damit Umsortieren und Filtern das
  ''' geladene Cover nicht wegwerfen.</summary>
  Private ReadOnly _tilesById As New Dictionary(Of String, LyrionAlbumTile)(StringComparer.Ordinal)
  Private _sort As LyrionMediaServerService.AlbumSort = LyrionMediaServerService.AlbumSort.ArtistYear
  Private _descending As Boolean
  Private _favoritesOnly As Boolean
  ''' <summary>Das Fuellen des Auswahlfeldes loest selbst eine Auswahlaenderung aus. Ohne
  ''' diese Sperre laedt jeder Sprachwechsel die Bibliothek neu.</summary>
  Private _suppressSortChange As Boolean
  Private ReadOnly _searchDebounce As New Avalonia.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(300)}
  ''' <summary>Die Geraete des Servers, in der Reihenfolge des Auswahlfeldes. Die ERSTE Stelle ist
  ''' die oertliche Wiedergabe und hat kein Geraet - deshalb Nothing an Stelle 0.</summary>
  Private _targets As New List(Of LyrionRemoteService.RemotePlayer)()
  ''' <summary>Das Fuellen des Auswahlfeldes loest selbst eine Auswahlaenderung aus. Ohne diese
  ''' Sperre schaltete jeder Aufbau der Liste die Wiedergabe um.</summary>
  Private _suppressTargetChange As Boolean
  Public Sub New()
   Me.New(Nothing, Nothing)
  End Sub
  Public Sub New(existingTracks As IEnumerable(Of Track), currentTrack As Track)
   AvaloniaXamlLoader.Load(Me)
   ' Wie im Tag-Bereich: gebaut wird dieses Panel erst nach dem Uebersetzungsdurchlauf des
   ' Fensters, also laeuft er hier noch einmal und bei jedem Sprachwechsel erneut.
   LocalizationService.ApplyTo(Me)
   AddHandler LocalizationService.LanguageChanged, AddressOf OnLanguageChanged
   ' Der Abgleich laeuft im Dienst und nicht hier: dieses Panel wird bei jedem Oeffnen neu
   ' gebaut, ein laufender Lauf ueberlebt das. Beim Anmelden holt sich die Ansicht sofort den
   ' aktuellen Stand - sonst saehe ein neu geoeffnetes Panel einen laufenden Abgleich nicht.
   AddHandler LyrionTaskState.Changed, AddressOf OnSyncStateChanged
   AddHandler LyrionLibraryScanService.Completed, AddressOf OnLibraryScanCompleted
   AddHandler DetachedFromVisualTree, Sub(sender, e)
                                        RemoveHandler LocalizationService.LanguageChanged, AddressOf OnLanguageChanged
                                        RemoveHandler LyrionTaskState.Changed, AddressOf OnSyncStateChanged
                                        RemoveHandler LyrionLibraryScanService.Completed, AddressOf OnLibraryScanCompleted
                                      End Sub
   FillSortBox()
   RenderSyncState()
   LoadPlaybackTargetsAsync()
   UpdateSortDirection()
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
   ' Das Auswahlfeld wird aus Code gefuellt, der Durchlauf ueber den Baum erreicht es nicht.
   FillSortBox()
   ' Ebenso der Kurzhinweis des Abgleichknopfes: er wechselt zwischen Starten und Abbrechen und
   ' wird deshalb gesetzt, nicht uebersetzt.
   RenderSyncState()
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
  ''' <summary>Die Reihenfolgen, wie sie im Auswahlfeld stehen.</summary>
  Private Shared ReadOnly SortOrder As LyrionMediaServerService.AlbumSort() = {
   LyrionMediaServerService.AlbumSort.Recent, LyrionMediaServerService.AlbumSort.ArtistYear,
   LyrionMediaServerService.AlbumSort.AlbumTitle, LyrionMediaServerService.AlbumSort.YearAlbum}

  Private Shared Function SortLabel(sort As LyrionMediaServerService.AlbumSort) As String
   Select Case sort
    Case LyrionMediaServerService.AlbumSort.Recent : Return LocalizationService.T("Zuletzt hinzugefügt")
    Case LyrionMediaServerService.AlbumSort.AlbumTitle : Return LocalizationService.T("Album")
    Case LyrionMediaServerService.AlbumSort.YearAlbum : Return LocalizationService.T("Jahr/Album")
    Case Else : Return LocalizationService.T("Interpret/Jahr")
   End Select
  End Function

  ''' <summary>Fuellt das Auswahlfeld. Laeuft auch bei jedem Sprachwechsel erneut; das Setzen der
  ''' Liste loest dabei eine Auswahlaenderung aus, die keine Neuladung bedeuten darf.</summary>
  Private Sub FillSortBox()
   Dim box = FindControl(Of ComboBox)("SortBox")
   _suppressSortChange = True
   Try
    box.ItemsSource = SortOrder.Select(AddressOf SortLabel).ToList()
    box.SelectedIndex = Math.Max(0, Array.IndexOf(SortOrder, _sort))
   Finally
    _suppressSortChange = False
   End Try
  End Sub

  Private Sub OnSortChanged(sender As Object, e As SelectionChangedEventArgs)
   If _suppressSortChange Then Return
   Dim index = FindControl(Of ComboBox)("SortBox").SelectedIndex
   If index < 0 OrElse index >= SortOrder.Length OrElse SortOrder(index) = _sort Then Return
   _sort = SortOrder(index)
   LoadAlbumsAsync()
  End Sub

  ''' <summary>Die Richtung kehrt nur die vorhandene Liste um - dafuer muss der Server nicht
  ''' gefragt werden.</summary>
  Private Sub OnSortDirectionClick(sender As Object, e As RoutedEventArgs)
   _descending = Not _descending
   UpdateSortDirection()
   ApplyAlbumView()
  End Sub

  Private Sub UpdateSortDirection()
   Dim icon = FindControl(Of FerrumPlay.Controls.SvgIcon)("SortDirectionIcon")
   If icon Is Nothing Then Return
   icon.Source = If(_descending, "avares://FerrumPlay/Assets/Icons/outline/chevron-up.svg", "avares://FerrumPlay/Assets/Icons/outline/chevron-down.svg")
  End Sub

  Private Sub OnFavoriteFilterClick(sender As Object, e As RoutedEventArgs)
   _favoritesOnly = Not _favoritesOnly
   Dim button = FindControl(Of Button)("FavoriteFilterButton")
   If _favoritesOnly Then button.Classes.Add("active") Else button.Classes.Remove("active")
   ApplyAlbumView()
  End Sub

  ''' <summary>Der Stern auf einer Kachel. Er steckt IN der Albumschaltflaeche, deren Klick das
  ''' Album oeffnet - ohne dieses Handled liefe beides auf einmal.</summary>
  Private Async Sub OnFavoriteBadgeClick(sender As Object, e As RoutedEventArgs)
   e.Handled = True
   Dim tile = TryCast(TryCast(sender, Button)?.Tag, LyrionAlbumTile)
   If tile Is Nothing Then Return
   Await tile.ToggleFavoriteAsync()
   ' Der Filter arbeitet auf dieser Menge: ohne den Nachtrag zeigte er ein gerade abgewaehltes
   ' Album weiter und ein neu gemerktes nicht.
   If tile.IsFavorite Then _favoriteUrls.Add(tile.Album.FavoritesUrl) Else _favoriteUrls.Remove(tile.Album.FavoritesUrl)
   If _favoritesOnly Then ApplyAlbumView()
  End Sub

  ''' <summary>Holt die Albenliste vollstaendig und zeigt sie an. Die Favoriten kommen nur beim
  ''' ersten Mal mit: sie aendern sich nur ueber das Sternchen, und das traegt seine Aenderung
  ''' selbst nach. Das Aktualisieren-Symbol laesst beides neu holen.</summary>
  Private Async Sub LoadAlbumsAsync()
   Dim request = Threading.Interlocked.Increment(_searchRequest)
   Dim scroll = FindControl(Of ScrollViewer)("AlbumScroll")
   scroll.IsVisible = True : FindControl(Of ScrollViewer)("TrackScroll").IsVisible = False
   FindControl(Of TextBlock)("PageTitle").Text = "Lyrion Media Server"
   FindControl(Of TextBlock)("Status").Text = LocalizationService.T("Alben werden geladen …")
   Try
    Dim albums = Await LyrionMediaServerService.GetAlbumsAsync(FindControl(Of TextBox)("SearchBox").Text, _sort, Threading.CancellationToken.None)
    If Not _favoritesLoaded Then
     _favoriteUrls = Await LyrionMediaServerService.GetFavoriteAlbumUrlsAsync(Threading.CancellationToken.None)
     _favoritesLoaded = True
    End If
    If request <> Threading.Volatile.Read(_searchRequest) Then Return
    _albums = albums
    scroll.Offset = New Avalonia.Vector(0, 0)
    ApplyAlbumView()
   Catch ex As Exception
    If request = Threading.Volatile.Read(_searchRequest) Then FindControl(Of TextBlock)("Status").Text = ex.Message
   End Try
  End Sub

  ''' <summary>Baut aus der geholten Liste die sichtbaren Kacheln: erst der Favoritenfilter, dann
  ''' die Richtung. SORTIERT wird hier nicht - die Reihenfolge steht schon fest, sie wird
  ''' hoechstens umgedreht.</summary>
  Private Sub ApplyAlbumView()
   Dim shown As IEnumerable(Of LyrionMediaServerService.Album) = _albums
   If _favoritesOnly Then shown = shown.Where(Function(album) _favoriteUrls.Contains(album.FavoritesUrl))
   Dim ordered = shown.ToList()
   If _descending Then ordered.Reverse()
   ' Die Liste wird als Ganzes gesetzt statt Kachel fuer Kachel angehaengt: bei mehreren tausend
   ' Alben waeren das ebenso viele Meldungen an den Repeater.
   FindControl(Of ItemsRepeater)("Albums").ItemsSource = ordered.Select(AddressOf TileFor).ToList()
   FindControl(Of TextBlock)("Status").Text = StatusText(ordered.Count)
  End Sub

  ''' <summary>Die Kachel zu einem Album, und zwar immer DIESELBE. Beim Umsortieren oder Filtern
  ''' bleibt so das schon geladene Cover erhalten, statt erneut vom Server zu kommen.</summary>
  Private Function TileFor(album As LyrionMediaServerService.Album) As LyrionAlbumTile
   Dim key = If(album.Id, String.Empty)
   Dim tile As LyrionAlbumTile = Nothing
   If key.Length = 0 OrElse Not _tilesById.TryGetValue(key, tile) Then
    tile = New LyrionAlbumTile(album)
    If key.Length > 0 Then _tilesById(key) = tile
   End If
   tile.IsFavorite = _favoriteUrls.Contains(tile.Album.FavoritesUrl)
   Return tile
  End Function

  Private Function StatusText(count As Integer) As String
   If count = 0 Then Return LocalizationService.T("Keine Alben gefunden.")
   If _favoritesOnly Then Return LocalizationService.Format("{0} von {1} Alben", count, _albums.Count)
   If IsSearching() Then
    ' KEINE typografischen Anfuehrungszeichen in diesem Text: VB nimmt " und " als
    ' Zeichenkettengrenze an und beendet die Zeichenkette mittendrin. Siehe
    ' FALLEN_UND_ENTSCHEIDUNGEN.md.
    ' Bei einer Suche sortiert der Dienst selbst - bis auf "zuletzt hinzugefuegt". Wann ein Album
    ' in die Bibliothek kam, sagt die Albenabfrage nicht, also bleibt die Reihenfolge des Servers
    ' stehen. Das gehoert gesagt, statt eine Reihenfolge vorzutaeuschen.
    If _sort = LyrionMediaServerService.AlbumSort.Recent Then Return LocalizationService.Format("{0} Treffer · zuletzt hinzugefügt gilt für eine Suche nicht", count)
    Return LocalizationService.Format("{0} Treffer", count)
   End If
   ' Bei "Zuletzt hinzugefuegt" ist die Zahl NICHT die Bibliothek: der Server gibt davon nur so
   ' viele heraus, wie seine Einstellung browseagelimit erlaubt.
   If _sort = LyrionMediaServerService.AlbumSort.Recent Then Return LocalizationService.Format("{0} zuletzt hinzugefügte Alben", count)
   Return LocalizationService.Format("{0} Alben", count)
  End Function

  ''' <summary>Ob gerade nach einem Begriff gefiltert wird.</summary>
  Private Function IsSearching() As Boolean
   Return Not String.IsNullOrWhiteSpace(FindControl(Of TextBox)("SearchBox")?.Text)
  End Function

  ''' <summary>Der Repeater hat eine Kachel gebaut - erst jetzt lohnt sich ihr Cover. Und erst
  ''' jetzt gibt es die Kachel ueberhaupt: der Uebersetzungsdurchlauf beim Aufbau des Panels
  ''' hat sie nicht gesehen, also bekommt sie ihren hier.</summary>
  Private Sub OnAlbumTilePrepared(sender As Object, e As ItemsRepeaterElementPreparedEventArgs)
   Dim element = TryCast(e.Element, Control)
   If element IsNot Nothing Then LocalizationService.ApplyTo(element)
   TryCast(element?.DataContext, LyrionAlbumTile)?.RequestCover()
  End Sub

  ''' <summary>Der Repeater reicht die Kachel weiter. Ihr Bild wird losgelassen, die Bilddaten
  ''' bleiben im Zwischenspeicher des Dienstes.</summary>
  Private Sub OnAlbumTileClearing(sender As Object, e As ItemsRepeaterElementClearingEventArgs)
   TryCast(TryCast(e.Element, Control)?.DataContext, LyrionAlbumTile)?.ReleaseCover()
  End Sub
  Private Sub OnAlbumClick(sender As Object, e As RoutedEventArgs)
   Dim tile = TryCast(TryCast(sender, Button)?.Tag, LyrionAlbumTile)
   If tile Is Nothing Then Return
   Dim ignored = ShowAlbumAsync(tile.Album)
  End Sub

  Private Async Function ShowAlbumAsync(album As LyrionMediaServerService.Album) As Threading.Tasks.Task
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
  End Function
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

    If LyrionRemoteService.IsRemote Then
     ' Auf dem Geraet wird die ganze Wiedergabeliste des Servers gesetzt und auf den gewaehlten
     ' Titel gesprungen - der Server fuehrt die Reihenfolge dann selbst, wie bei jedem anderen
     ' Steuergeraet auch.
     If String.IsNullOrWhiteSpace(_shownAlbum?.Id) Then Throw New InvalidOperationException(LocalizationService.T("Dieses Album lässt sich nicht an das Gerät übergeben."))
     player.PlayLyrionAlbumRemote(_shownAlbum.Id, _albumTracks.IndexOf(track))
     FindControl(Of TextBlock)("Status").Text = LocalizationService.Format("Wiedergabe auf {0}: {1}", LyrionRemoteService.SelectedPlayerName, track.Title)
     Return Threading.Tasks.Task.CompletedTask
    End If

    player.PlayLyrionAlbum(_albumTracks, track)
    FindControl(Of TextBlock)("Status").Text = LocalizationService.Format("Wiedergabe in FerrumPlay: {0}", track.Title)
   Catch ex As Exception
    FindControl(Of TextBlock)("Status").Text = ex.Message
   End Try
   Return Threading.Tasks.Task.CompletedTask
  End Function

  ''' <summary>Fuellt das Auswahlfeld: die oertliche Wiedergabe und jedes am Server angemeldete
  ''' Geraet. Ein gemerktes Geraet, das der Server gerade nicht kennt, steht trotzdem darin -
  ''' sonst spraenge die Auswahl beim Oeffnen stillschweigend auf "Lokal" zurueck.</summary>
  Private Async Sub LoadPlaybackTargetsAsync()
   Dim players As New List(Of LyrionRemoteService.RemotePlayer)()
   Try
    players = Await LyrionRemoteService.GetPlayersAsync(Threading.CancellationToken.None)
   Catch ex As Exception
    DiagnosticLogService.LogException("Lyrion.Remote", ex)
   End Try

   Dim chosen = LyrionRemoteService.SelectedPlayerId
   If chosen.Length > 0 AndAlso Not players.Any(Function(entry) entry.Id = chosen) Then
    players.Insert(0, New LyrionRemoteService.RemotePlayer With {
                   .Id = chosen, .Name = LyrionRemoteService.SelectedPlayerName, .Connected = False})
   End If

   _targets.Clear()
   _targets.Add(Nothing)
   _targets.AddRange(players)
   FillTargetBox()
  End Sub

  Private Sub FillTargetBox()
   Dim box = FindControl(Of ComboBox)("TargetBox")
   If box Is Nothing Then Return
   _suppressTargetChange = True
   Try
    box.ItemsSource = _targets.Select(AddressOf TargetLabel).ToList()
    Dim chosen = LyrionRemoteService.SelectedPlayerId
    Dim index = _targets.FindIndex(Function(entry) If(entry?.Id, String.Empty) = chosen)
    box.SelectedIndex = Math.Max(0, index)
   Finally
    _suppressTargetChange = False
   End Try
  End Sub

  Private Shared Function TargetLabel(player As LyrionRemoteService.RemotePlayer) As String
   If player Is Nothing Then Return LocalizationService.T("Lokal abspielen")
   If Not player.Connected Then Return player.Name & " " & LocalizationService.T("(nicht verbunden)")
   Return player.Name
  End Function

  ''' <summary>Umschalten zwischen oertlicher Wiedergabe und einem Geraet.</summary>
  Private Sub OnPlaybackTargetChanged(sender As Object, e As SelectionChangedEventArgs)
   If _suppressTargetChange Then Return
   Dim index = FindControl(Of ComboBox)("TargetBox").SelectedIndex
   If index < 0 OrElse index >= _targets.Count Then Return
   Dim player = _targets(index)
   LyrionRemoteService.SelectPlayer(If(player?.Id, String.Empty), If(player?.Name, String.Empty))
  End Sub
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

  ''' <summary>Aktualisieren heisst zweierlei, und die Reihenfolge ist der Punkt: erst laesst der
  ''' SERVER nach neuen und geaenderten Titeln sehen, dann wird seine Antwort geholt. Nur die
  ''' Liste noch einmal zu holen zeigte denselben Stand - eine eben hinzugekommene Platte kennt
  ''' der Server ja noch gar nicht.
  '''
  ''' <para>Laeuft der Durchlauf, kommt die Liste erst, wenn er fertig ist (siehe
  ''' OnLibraryScanCompleted). Konnte er nicht anfangen, weil ein anderer Vorgang laeuft, wird
  ''' wenigstens die Liste geholt - ein Klick darf nicht folgenlos bleiben.</para></summary>
  Private Sub OnReloadClick(sender As Object, e As RoutedEventArgs)
   If _shownAlbum IsNot Nothing Then Dim reloading = ShowAlbumAsync(_shownAlbum) : Return
   Select Case LyrionLibraryScanService.Toggle()
    Case LyrionLibraryScanService.StartResult.Started, LyrionLibraryScanService.StartResult.Cancelling
     RenderSyncState()
     Return
   End Select
   ReloadLibrary()
  End Sub

  ''' <summary>Der Server ist durch - jetzt lohnt die Liste. Von einem Hintergrundfaden.</summary>
  Private Sub OnLibraryScanCompleted(sender As Object, e As EventArgs)
   Avalonia.Threading.Dispatcher.UIThread.Post(AddressOf ReloadLibrary)
  End Sub

  ''' <summary>Alles noch einmal. Auch die Favoriten koennen sich anderswo geaendert haben, und
  ''' ein Cover kann ein anderes geworden sein.</summary>
  Private Sub ReloadLibrary()
   _favoritesLoaded = False
   _tilesById.Clear()
   LoadAlbumsAsync()
  End Sub
  ''' <summary>Ein Knopf fuer beides: er startet den Abgleich, und waehrend er laeuft bricht er
  ''' ihn ab. Die Entscheidung faellt im Dienst, weil dort der Lauf liegt - dieses Panel wird bei
  ''' jedem Oeffnen neu gebaut und wuesste von einem laufenden Abgleich sonst nichts.</summary>
  Private Sub OnSyncClick(sender As Object, e As RoutedEventArgs)
   LyrionFavoriteSyncService.Toggle()
  End Sub

  ''' <summary>Nimmt die Schlussmeldung weg. Einen LAUFENDEN Abgleich beendet dieser Knopf nicht -
  ''' waehrend des Laufs steht er deshalb gar nicht da.</summary>
  Private Sub OnSyncDismissClick(sender As Object, e As RoutedEventArgs)
   If LyrionTaskState.IsBusy Then Return
   ' Weggeklickt wird die MELDUNG im Dienst, nicht die Zeile in diesem Panel. Merkte sich das
   ' Panel es, waere es beim naechsten Oeffnen vergessen - die Ansicht wird jedes Mal neu
   ' gebaut - und die Zeile staende wieder da.
   LyrionTaskState.DismissStatus()
  End Sub

  ''' <summary>Der Dienst meldet sich aus einem Hintergrundfaden - der Wechsel auf den
  ''' Oberflaechenfaden gehoert hierher.</summary>
  Private Sub OnSyncStateChanged(sender As Object, e As EventArgs)
   Avalonia.Threading.Dispatcher.UIThread.Post(AddressOf RenderSyncState)
  End Sub

  ''' <summary>Zeichnet den Stand des Abgleichs: die eigene Meldungszeile, die Farbe des
  ''' Abgleichknopfes und sein Kurzhinweis. Einziger Ort, an dem das geschieht - so steht nach
  ''' jedem Ereignis dasselbe da, egal ob die Ansicht gerade neu gebaut wurde.</summary>
  Private Sub RenderSyncState()
   Dim box = FindControl(Of Border)("SyncStatusBox")
   Dim line = FindControl(Of TextBlock)("SyncStatus")
   Dim button = FindControl(Of Button)("SyncButton")
   Dim dismiss = FindControl(Of Button)("SyncDismissButton")
   If box Is Nothing OrElse line Is Nothing OrElse button Is Nothing OrElse dismiss Is Nothing Then Return

   Dim running = LyrionTaskState.Running
   Dim busy = LyrionTaskState.IsBusy
   Dim text = LyrionTaskState.Status

   ' Die Zeile steht genau dann da, wenn es etwas zu sagen gibt - eine weggeklickte Meldung ist
   ' eine leere. Der Aufraeumknopf faehrt mit ihr: er gehoert zu dieser Meldung, und wer sie
   ' wegklickt, bekommt ihn beim naechsten Lauf wieder.
   Dim cleanup = FindControl(Of Button)("SyncCleanupButton")
   Dim unresolved = LyrionFavoriteSyncService.Unresolved.Count
   Dim show = Not String.IsNullOrEmpty(text)
   If cleanup IsNot Nothing Then cleanup.IsVisible = show AndAlso Not busy AndAlso unresolved > 0

   box.IsVisible = show
   line.Text = text
   ' Die Farbe sagt, ob noch etwas geschieht: das ist der Unterschied, um den es geht.
   SetClass(line, "sync-busy", busy)
   dismiss.IsVisible = Not busy

   ' Hervorgehoben wird der Knopf, dem der laufende Vorgang GEHOERT - und nur der. Sonst saehe
   ' es aus, als liesse sich ein Durchsuchen ueber den Abgleichknopf beenden.
   Dim syncing = running = LyrionTaskState.Kind.Sync OrElse running = LyrionTaskState.Kind.Cleanup
   Dim scanning = running = LyrionTaskState.Kind.Scan
   SetClass(button, "active", syncing)
   ' Das angehaengte Leerzeichen macht es LocalizationService.ApplyTo nach: bei krummer
   ' Skalierung fehlt dem Hinweis sonst beim Anordnen ein Bruchteil, und das letzte Zeichen
   ' verschwindet.
   ToolTip.SetTip(button, If(syncing,
                             LocalizationService.T("Abgleich abbrechen"),
                             LocalizationService.T("Favoriten in den Zielordner abgleichen")) & " ")

   Dim reload = FindControl(Of Button)("ReloadButton")
   If reload IsNot Nothing Then
    SetClass(reload, "active", scanning)
    ToolTip.SetTip(reload, If(scanning,
                              LocalizationService.T("Durchsuchen abbrechen"),
                              LocalizationService.T("Bibliothek aktualisieren")) & " ")
    ' Gesperrt, solange der ANDERE Vorgang laeuft. Ein Knopf, dessen Klick nur eine Absage
    ' einbraechte, sagt das besser, indem er sich gar nicht erst druecken laesst - und die
    ' Absage kann dann auch keine fremde Fortschrittszeile uebermalen.
    reload.IsEnabled = Not syncing
   End If
   button.IsEnabled = Not scanning
  End Sub

  ''' <summary>Nimmt die Favoriteneintraege, zu denen es kein Album mehr gibt, beim Server aus
  ''' den Favoriten - nach Rueckfrage MIT Auflistung. Beim Server geloescht ist geloescht, und die
  ''' blosse Anzahl sagt nicht, was verschwindet.</summary>
  Private Async Sub OnRemoveUnresolvedClick(sender As Object, e As RoutedEventArgs)
   Dim entries = LyrionFavoriteSyncService.Unresolved
   Dim viewModel = TryCast(DataContext, MainWindowViewModel)
   If entries.Count = 0 OrElse viewModel Is Nothing OrElse LyrionTaskState.IsBusy Then Return

   Dim listed = String.Join(Environment.NewLine,
                            entries.Select(Function(entry) "· " & If(String.IsNullOrWhiteSpace(entry.Name),
                                                                     entry.Url, entry.Name)))
   Dim confirmed = Await viewModel.ShowConfirmAsync(
    LocalizationService.T("Favoriten ohne passendes Album entfernen?"),
    LocalizationService.Format("Zu diesen {0} Favoriteneinträgen gibt es kein Album mehr: umbenannt, neu getaggt oder gelöscht. Sollen sie beim Server aus den Favoriten genommen werden? An der Musik ändert das nichts.", entries.Count),
    listed,
    LocalizationService.T("Aus Favoriten entfernen"),
    LocalizationService.T("Abbrechen"))
   If Not confirmed Then Return
   LyrionFavoriteSyncService.StartUnresolvedCleanup()
  End Sub

  ''' <summary>Schlaegt das Album auf, das gerade auf dem Geraet laeuft, und waehlt darin den
  ''' laufenden Titel. Die Album-Kennung kommt aus der Zustandsabfrage; das Album selbst wird aus
  ''' der geholten Liste genommen, und wenn es dort fehlt - etwa weil gerade nach einem Begriff
  ''' gefiltert wird - aus den Angaben des Geraets gebaut.</summary>
  Private Async Function ShowRemoteAlbumAsync() As Threading.Tasks.Task
   Dim status = LyrionRemoteService.Status
   If status Is Nothing OrElse Not status.Reachable OrElse String.IsNullOrWhiteSpace(status.AlbumId) Then
    FindControl(Of TextBlock)("Status").Text = LocalizationService.T("Auf dem Gerät läuft gerade nichts.")
    Return
   End If

   Dim album = _albums.FirstOrDefault(Function(entry) entry.Id = status.AlbumId)
   If album Is Nothing Then
    album = New LyrionMediaServerService.Album With {
     .Id = status.AlbumId, .Title = status.Album, .Artist = status.Artist,
     .ArtworkTrackId = status.ArtworkTrackId}
   End If

   Await ShowAlbumAsync(album)

   ' Den laufenden Titel auswaehlen. Ueber den Namen und nicht ueber die Stelle in der Liste: das
   ' Geraet fuehrt seine eigene Reihenfolge, und bei Zufall stimmt die Stelle nicht mit unserer.
   Dim list = FindControl(Of ListBox)("Tracks")
   Dim item = list.Items.OfType(Of ListBoxItem)().FirstOrDefault(
    Function(entry) String.Equals(TryCast(entry.Tag, Track)?.Title, status.Title, StringComparison.CurrentCultureIgnoreCase))
   If item Is Nothing Then Return
   list.SelectedItem = item
   Avalonia.Threading.Dispatcher.UIThread.Post(Sub() list.ScrollIntoView(item))
  End Function

  ''' <summary>Setzt oder nimmt eine Klasse. RenderSyncState laeuft bei jeder Meldung erneut;
  ''' ein blosses Classes.Add legte die Klasse dann ein ums andere Mal nach.</summary>
  Private Shared Sub SetClass(target As Control, name As String, wanted As Boolean)
   If wanted Then
    If Not target.Classes.Contains(name) Then target.Classes.Add(name)
   Else
    target.Classes.Remove(name)
   End If
  End Sub

  Private Sub OnJumpToCurrentTrackClick(sender As Object, e As RoutedEventArgs)
   ' Ferngesteuert steht der laufende Titel nicht in einer hiesigen Liste, sondern auf dem Geraet.
   ' Sein Album wird deshalb beim Server nachgeschlagen und aufgeschlagen.
   If LyrionRemoteService.IsRemote Then
    Dim jumping = ShowRemoteAlbumAsync()
    Return
   End If
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
