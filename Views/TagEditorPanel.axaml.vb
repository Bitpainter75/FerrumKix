Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports Avalonia.Media.Imaging
Imports Avalonia.Platform.Storage
Imports FerrumPlay.Models
Imports FerrumPlay.Services
Imports FerrumPlay.ViewModels

Namespace Views
 Public Class TagEditorPanel
  Inherits UserControl
  Public Event CloseRequested As EventHandler
  ''' <summary>Es wurde geschrieben. Die Coverspalte holt sich daraufhin das Bild neu: im Tag steht
  ''' jetzt ein anderes, und sein Mass ist das der geschriebenen Fassung.</summary>
  Public Event Saved(tracks As IReadOnlyList(Of Track))
  Private ReadOnly _tracks As List(Of Track)
  Private ReadOnly _rows As New List(Of (Track As Track, Title As TextBox, Number As TextBox))
  Private _cover As Byte()
  Public Sub New()
   Me.New(Array.Empty(Of Track)())
  End Sub
  Public Sub New(tracks As IEnumerable(Of Track))
   _tracks = tracks.Where(Function(t) t IsNot Nothing AndAlso String.Equals(Path.GetExtension(t.FilePath), ".mp3", StringComparison.OrdinalIgnoreCase)).ToList()
   AvaloniaXamlLoader.Load(Me)
   ' Dieses Panel entsteht ERST nach dem Uebersetzungsdurchlauf des Fensters und bliebe sonst in
   ' jeder Sprache deutsch. Der Durchlauf laeuft deshalb hier noch einmal - und bei jedem
   ' Sprachwechsel erneut, solange das Panel im Baum haengt.
   LocalizationService.ApplyTo(Me)
   AddHandler LocalizationService.LanguageChanged, AddressOf OnLanguageChanged
   AddHandler DetachedFromVisualTree, Sub(sender, e) RemoveHandler LocalizationService.LanguageChanged, AddressOf OnLanguageChanged
   Fill()
  End Sub
  Private Sub OnLanguageChanged(sender As Object, e As EventArgs)
   LocalizationService.ApplyTo(Me)
  End Sub

  Private Sub Fill()
   ' Tragen die Dateien ueberhaupt keine Kennzeichen, stuende das Formular sonst leer da und
   ' jede Zeile muesste von Hand getippt werden. Stattdessen steht der Vorschlag aus Dateiname
   ' und Ordner darin - siehe HasNoTags. Er wird wie jede Eingabe erst beim Speichern
   ' geschrieben und laesst sich vorher ueberall aendern.
   Dim suggest = HasNoTags()
   ' Erst sortieren, dann durchzaehlen: die fortlaufende Nummer ergibt sich aus der Reihenfolge
   ' der Dateinamen und nicht daraus, wie die Titel gerade in der Liste angeklickt wurden.
   If suggest Then _tracks.Sort(Function(left, right) StringComparer.CurrentCultureIgnoreCase.Compare(Path.GetFileName(left.FilePath), Path.GetFileName(right.FilePath)))
   Dim first = _tracks.FirstOrDefault() : If first Is Nothing Then Return
   Dim folder = If(suggest, FolderName(first), String.Empty)
   FindControl(Of TextBox)("ArtistBox").Text = If(suggest, folder, first.Artist) : FindControl(Of TextBox)("AlbumArtistBox").Text = first.AlbumArtist : FindControl(Of TextBox)("AlbumBox").Text = If(suggest, folder, first.Album)
   FindControl(Of TextBox)("YearBox").Text = If(first.Year = 0, "", first.Year.ToString()) : FindControl(Of ComboBox)("GenreBox").ItemsSource = AppSettingsService.Current.TagGenres : FindControl(Of ComboBox)("GenreBox").Text = first.Genre : FindControl(Of TextBox)("SortBox").Text = first.AlbumSortOrder : FindControl(Of TextBox)("DiscBox").Text = String.Empty
   ' Die Discnummer startet LEER, auch wenn in den Dateien eine steht: sie gehoert nur zu
   ' mehrteiligen Alben und soll bewusst gesetzt werden. Bleibt das Feld leer, wird auch keine
   ' geschrieben.
   ' Die beiden Werte bleiben sichtbar, auch wenn die gleichnamigen Automatik-Schalter
   ' aktiv sind. So bleibt das Formular ruhig und vollstaendig; beim Speichern greift
   ' weiterhin die gewählte Automatik aus den Einstellungen.
   FindControl(Of StackPanel)("AlbumArtistField").IsVisible = True
   FindControl(Of StackPanel)("AlbumSortField").IsVisible = True
   ' Ist eine Automatik eingeschaltet, zieht das abhaengige Feld sofort mit. Was im Formular
   ' steht, ist damit auch hier genau das, was spaeter geschrieben wird.
   AddHandler FindControl(Of TextBox)("ArtistBox").TextChanged, AddressOf OnArtistChanged
   AddHandler FindControl(Of TextBox)("YearBox").TextChanged, AddressOf OnYearChanged
   ApplyFollowUps()
   Dim position = 0
   For Each track In _tracks
    position += 1
    Dim title As New TextBox With {.Text = If(suggest, Path.GetFileNameWithoutExtension(track.FilePath), track.Title)} : Dim number As New TextBox With {.Text = NumberText(If(suggest, position, track.TrackNumber)), .Width = 90}
    AddHandler number.LostFocus, AddressOf OnNumberLostFocus
    Dim row As New Grid With {.ColumnDefinitions = New ColumnDefinitions("90,*") , .ColumnSpacing = 10}
    row.Children.Add(number) : Grid.SetColumn(title, 1) : row.Children.Add(title) : FindControl(Of StackPanel)("TrackRows").Children.Add(row) : _rows.Add((track, title, number))
   Next
   ShowSummaryHint()
  End Sub

  ''' <summary>Das Mass des Bildes, das die Coverspalte gerade zeigt - leer, wenn keines drin
  ''' steht. Hier stand vorher die eingestellte Kantenlaenge samt JPEG-Guete; die beschreibt
  ''' aber nur, worauf ein Bild beim Speichern gebracht WUERDE, und sagte ueber das
  ''' vorhandene nichts aus. Beim Taggen zaehlt genau das vorhandene: aus ihm ergibt sich,
  ''' ob es fuer die eingestellte Kantenlaenge ueberhaupt genug hergibt.</summary>
  Public Sub ShowCoverSize(text As String)
   _coverSize = If(text, String.Empty).Trim()
   ' Eine Meldung ueber ein gewaehltes Cover oder ueber das Schreiben ist die juengere
   ' Nachricht. Ein Mass, das erst danach eintrifft - die Spalte liest das Bild nebenher -,
   ' darf sie nicht verdraengen.
   If _hintShowsSummary Then ShowSummaryHint()
  End Sub

  Private Sub ShowSummaryHint()
   _hintShowsSummary = True
   FindControl(Of TextBlock)("Hint").Text = If(_coverSize.Length = 0,
                                               LocalizationService.Format("{0} MP3-Datei(en) ausgewählt. Kein Cover vorhanden.", _tracks.Count),
                                               LocalizationService.Format("{0} MP3-Datei(en) ausgewählt. Cover: {1}", _tracks.Count, _coverSize))
  End Sub

  ''' <summary>Was in der Hinweiszeile steht, wenn sie die Auswahl beschreibt, und ob sie das
  ''' gerade tut. Siehe <see cref="ShowCoverSize"/>.</summary>
  Private _coverSize As String = String.Empty
  Private _hintShowsSummary As Boolean
  ''' <summary>Ob in den ausgewaehlten Dateien ueberhaupt kein Kennzeichen steht. Nur dann wird
  ''' vorbelegt: sobald auch nur eine Datei etwas mitbringt, ist das der genauere Stand, und ein
  ''' Vorschlag wuerde ihn ueberschreiben.</summary>
  Private Function HasNoTags() As Boolean
   Return Not _tracks.Any(Function(track) Not String.IsNullOrWhiteSpace(track.Title) OrElse Not String.IsNullOrWhiteSpace(track.Artist) OrElse
                                          Not String.IsNullOrWhiteSpace(track.Album) OrElse Not String.IsNullOrWhiteSpace(track.AlbumArtist) OrElse
                                          track.TrackNumber > 0)
  End Function

  ''' <summary>Der Name des Ordners, in dem die Dateien liegen. In einer aufgeraeumten Sammlung
  ''' heisst er wie das Album, und damit ist er der beste Vorschlag, den es ohne Kennzeichen
  ''' gibt.</summary>
  Private Shared Function FolderName(track As Track) As String
   Try
    Return If(Path.GetFileName(Path.GetDirectoryName(track.FilePath)), String.Empty)
   Catch
    Return String.Empty
   End Try
  End Function

  Private Sub OnArtistChanged(sender As Object, e As TextChangedEventArgs)
   ApplyFollowUps()
  End Sub

  Private Sub OnYearChanged(sender As Object, e As TextChangedEventArgs)
   ApplyFollowUps()
  End Sub

  ''' <summary>Uebertraegt die Werte, die laut Einstellungen einem anderen Feld folgen. Dieselbe
  ''' Zuordnung greift beim Speichern noch einmal, siehe <see cref="OnSaveClick"/>.</summary>
  Private Sub ApplyFollowUps()
   If AppSettingsService.Current.TagAlbumArtistFollowsArtist Then FindControl(Of TextBox)("AlbumArtistBox").Text = FindControl(Of TextBox)("ArtistBox").Text
   If AppSettingsService.Current.TagAlbumSortFollowsYear Then
    Dim year As Integer
    FindControl(Of TextBox)("SortBox").Text = If(Integer.TryParse(FindControl(Of TextBox)("YearBox").Text, year) AndAlso year > 0, year.ToString(), String.Empty)
   End If
  End Sub

  ''' <summary>Die Tracknummer so, wie sie spaeter im Tag steht - mit den fuehrenden Nullen aus
  ''' den Einstellungen. Was im Feld steht, ist damit genau das, was geschrieben wird.</summary>
  Private Function NumberText(number As Integer) As String
   Return If(number = 0, String.Empty, Mp3TagWriteService.FormatTrackNumber(number, _tracks.Count))
  End Function

  ''' <summary>Eine von Hand eingetippte Nummer bekommt ihre fuehrenden Nullen, sobald das Feld
  ''' verlassen wird.</summary>
  Private Sub OnNumberLostFocus(sender As Object, e As RoutedEventArgs)
   Dim box = TryCast(sender, TextBox) : If box Is Nothing Then Return
   Dim number As Integer
   box.Text = If(Integer.TryParse(box.Text, number), NumberText(number), box.Text)
  End Sub

  ''' <summary>Uebernimmt ein Cover, das die Tag-Coverspalte entgegengenommen und bereits als Bild
  ''' geprueft hat. Geschrieben wird es erst beim Speichern.</summary>
  Public Sub SetCover(bytes As Byte(), fileName As String)
   If bytes Is Nothing OrElse bytes.Length = 0 Then Return
   _cover = bytes
   _hintShowsSummary = False
   FindControl(Of TextBlock)("Hint").Text = LocalizationService.Format("Neues Cover gewählt: {0}", fileName)
  End Sub
  Private Async Sub OnSaveClick(sender As Object, e As RoutedEventArgs)
   Dim artist = FindControl(Of TextBox)("ArtistBox").Text : Dim albumArtist = If(AppSettingsService.Current.TagAlbumArtistFollowsArtist, artist, FindControl(Of TextBox)("AlbumArtistBox").Text)
   Dim year As Integer : Integer.TryParse(FindControl(Of TextBox)("YearBox").Text, year) : Dim disc As Integer : Integer.TryParse(FindControl(Of TextBox)("DiscBox").Text, disc)
   Dim album = FindControl(Of TextBox)("AlbumBox").Text : Dim genre = FindControl(Of ComboBox)("GenreBox").Text : Dim sort = If(AppSettingsService.Current.TagAlbumSortFollowsYear, If(year = 0, "", year.ToString()), FindControl(Of TextBox)("SortBox").Text)
   FindControl(Of Button)("SaveButton").IsEnabled = False
   Dim errors As New List(Of String)
   ' Die Eingabefelder gehoeren dem Oberflaechenfaden: JEDER Zugriff von einem anderen Faden wirft
   ' ("The calling thread cannot access this object"). Die Werte werden deshalb hier eingesammelt,
   ' und in den Hintergrund geht nur noch, was aus reinen Zeichenketten und Zahlen besteht.
   Dim edits = _rows.Select(Function(row)
                             Dim number As Integer
                             Integer.TryParse(row.Number.Text, number)
                             Return (Track:=row.Track, Title:=If(row.Title.Text, String.Empty), Number:=number)
                            End Function).ToList()
   Await Task.Run(Sub()
   For Each edit In edits
    Try
     Dim target = Mp3TagWriteService.Write(edit.Track.FilePath, New Mp3TagWriteService.Values With {.Artist = artist, .AlbumArtist = albumArtist, .Album = album, .Year = year, .Genre = genre, .AlbumSortOrder = sort, .DiscNumber = disc, .Title = edit.Title, .TrackNumber = edit.Number, .TotalTracks = edits.Count, .CoverSource = _cover})
     edit.Track.FilePath = target : edit.Track.Artist = artist : edit.Track.AlbumArtist = albumArtist : edit.Track.Album = album : edit.Track.Year = year : edit.Track.Genre = genre : edit.Track.AlbumSortOrder = sort : edit.Track.DiscNumber = disc : edit.Track.Title = edit.Title : edit.Track.TrackNumber = edit.Number
    Catch ex As Exception : errors.Add(Path.GetFileName(edit.Track.FilePath) & ": " & ex.Message) : End Try
   Next
   End Sub)
   ' Nach dem Schreiben zeigen die Felder wieder genau den Stand der Dateien.
   For Each row In _rows : row.Number.Text = NumberText(row.Track.TrackNumber) : Next
   RaiseEvent Saved(_tracks)
   ' Die Dateien heissen jetzt anders. Ohne dieses Speichern zeigte die gemerkte Wiedergabeliste
   ' nach dem naechsten Start auf die alten Namen, und jeder umbenannte Titel gaelte als fehlend.
   TryCast(DataContext, MainWindowViewModel)?.SavePlaylist()
   _hintShowsSummary = False
   FindControl(Of TextBlock)("Hint").Text = If(errors.Count = 0, LocalizationService.T("Änderungen wurden übernommen."), String.Join(Environment.NewLine, errors)) : FindControl(Of Button)("SaveButton").IsEnabled = True
  End Sub
  Private Sub OnCloseClick(sender As Object, e As RoutedEventArgs)
   RaiseEvent CloseRequested(Me, EventArgs.Empty)
  End Sub
 End Class
End Namespace
