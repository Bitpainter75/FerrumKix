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
   ' Was in den Dateien steht, hat Vorrang; vorbelegt wird nur, was dort fehlt - und zwar Feld
   ' fuer Feld. Frueher entschied darueber die Auswahl als Ganzes: brachte auch nur EINE Datei
   ' irgendein Kennzeichen mit, blieb jedes leere Feld aller anderen leer und musste von Hand
   ' getippt werden. Der Vorschlag ist der Dateiname (Titel) und der Ordnername (Artist, Album),
   ' siehe FolderName. Er wird wie jede Eingabe erst beim Speichern geschrieben und laesst sich
   ' vorher ueberall aendern.
   ' Fehlt irgendwo die Tracknummer, ergibt sie sich aus der Reihenfolge der Dateinamen. Dafuer
   ' wird erst sortiert und dann durchgezaehlt: die Nummer haengt damit an den Dateinamen und
   ' nicht daran, wie die Titel gerade in der Liste angeklickt wurden. Stehen ueberall schon
   ' Nummern, bleibt die Auswahl in ihrer Reihenfolge stehen - dort ist nichts zu erraten.
   If _tracks.Any(Function(track) track.TrackNumber <= 0) Then _tracks.Sort(Function(left, right) StringComparer.CurrentCultureIgnoreCase.Compare(Path.GetFileName(left.FilePath), Path.GetFileName(right.FilePath)))
   Dim first = _tracks.FirstOrDefault() : If first Is Nothing Then Return
   Dim folder = FolderName(first)
   FindControl(Of TextBox)("ArtistBox").Text = Suggested(first.Artist, folder) : FindControl(Of TextBox)("AlbumArtistBox").Text = first.AlbumArtist : FindControl(Of TextBox)("AlbumBox").Text = Suggested(first.Album, folder)
   FindControl(Of TextBox)("YearBox").Text = If(first.Year = 0, "", first.Year.ToString()) : FindControl(Of ComboBox)("GenreBox").ItemsSource = AppSettingsService.Current.TagGenres : FindControl(Of ComboBox)("GenreBox").Text = first.Genre : FindControl(Of TextBox)("SortBox").Text = first.AlbumSortOrder : FindControl(Of TextBox)("DiscBox").Text = String.Empty
   AddHandler FindControl(Of ComboBox)("GenreBox").TemplateApplied, AddressOf OnGenreTemplateApplied
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
    Dim title As New TextBox With {.Text = Suggested(track.Title, Path.GetFileNameWithoutExtension(track.FilePath))} : Dim number As New TextBox With {.Text = NumberText(If(track.TrackNumber > 0, track.TrackNumber, position)), .Width = 90}
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
  ''' <summary>Was in der Datei steht - und wo dort nichts steht, der Vorschlag. Ein Feld, in dem
  ''' nur Leerzeichen stehen, gilt dabei als leer: es sieht im Formular aus wie nichts und waere
  ''' als Kennzeichen ebenso wenig wert.</summary>
  Private Shared Function Suggested(value As String, suggestion As String) As String
   Return If(String.IsNullOrWhiteSpace(value), suggestion, value)
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

  ' Voll ausgeschrieben statt importiert: Avalonia.Controls.Primitives enthaelt eine eigene
  ' Klasse Track (die Schiene eines Schiebereglers), und ein Import davon macht den Track
  ' dieser Anwendung in der ganzen Datei mehrdeutig.
  ''' <summary>Greift sich das Eingabefeld aus der Vorlage des Genre-Feldes. Erst hier gibt es
  ''' das Feld ueberhaupt: beim Fuellen im Erzeuger haengt das Panel noch nicht im Baum, und die
  ''' ComboBox hat ihre Vorlage noch nicht angewandt.</summary>
  Private Sub OnGenreTemplateApplied(sender As Object, e As Avalonia.Controls.Primitives.TemplateAppliedEventArgs)
   Dim input = e.NameScope.Find(Of TextBox)("PART_EditableTextBox") : If input Is Nothing Then Return
   ' Auf dem Weg nach unten (Tunnel) zugehoert: die TextBox kennzeichnet das eingetippte Zeichen
   ' in ihrem eigenen Griff als erledigt, ein Griff auf dem Rueckweg kaeme nie an.
   input.[AddHandler](InputElement.TextInputEvent, New EventHandler(Of TextInputEventArgs)(AddressOf OnGenreTextInput), RoutingStrategies.Tunnel)
  End Sub

  ''' <summary>Vervollstaendigt das Genre waehrend des Tippens aus den Vorgaben der
  ''' Einstellungen. Ergaenzt wird nur das, was hinter dem Getippten fehlt, und es steht markiert
  ''' da: das naechste Zeichen ersetzt es, Rueckschritt wirft es weg. Ein Genre, das in keiner
  ''' Vorgabe steht, laesst sich damit weiterhin frei eintippen.</summary>
  Private Sub OnGenreTextInput(sender As Object, e As TextInputEventArgs)
   Dim input = TryCast(sender, TextBox) : If input Is Nothing OrElse String.IsNullOrEmpty(e.Text) Then Return
   Dim text = If(input.Text, String.Empty)
   Dim first = Math.Min(input.SelectionStart, input.SelectionEnd) : Dim last = Math.Max(input.SelectionStart, input.SelectionEnd)
   ' Nur am Ende ergaenzen. Mitten im Wort waere der angehaengte Rest ein Vorschlag fuer einen
   ' Text, den es so gar nicht gibt, und er verschluckte beim Weitertippen den Teil dahinter.
   If last <> text.Length Then Return
   Dim typed = text.Substring(0, first) & e.Text
   Dim match = AppSettingsService.Current.TagGenres.FirstOrDefault(Function(entry) entry.Length > typed.Length AndAlso entry.StartsWith(typed, StringComparison.CurrentCultureIgnoreCase))
   If match Is Nothing Then Return
   ' Der getippte Teil bleibt Zeichen fuer Zeichen so stehen, wie er getippt wurde; nur der Rest
   ' kommt aus der Vorgabe. Sonst spraenge beim Tippen die Gross- und Kleinschreibung um.
   input.Text = typed & match.Substring(typed.Length)
   input.SelectionStart = typed.Length : input.SelectionEnd = match.Length
   e.Handled = True
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

  ''' <summary>Aus dem Abgelegten liess sich kein Bild lesen. Wortlos nichts zu tun sah aus wie
  ''' ein Fehler der Anwendung - gerade jetzt, wo die Flaeche waehrend des Ziehens ausdruecklich
  ''' zum Ablegen einlaedt. Woran es lag, steht im Protokoll.</summary>
  Public Sub ReportCoverDropFailed()
   _hintShowsSummary = False
   FindControl(Of TextBlock)("Hint").Text = LocalizationService.T("Das abgelegte Element konnte nicht als Bild gelesen werden.")
  End Sub

  ''' <summary>Uebernimmt ein Cover, das die Tag-Coverspalte entgegengenommen und bereits als Bild
  ''' geprueft hat. Geschrieben wird es erst beim Speichern.</summary>
  Public Sub SetCover(bytes As Byte(), fileName As String)
   If bytes Is Nothing OrElse bytes.Length = 0 Then Return
   _cover = bytes
   _hintShowsSummary = False
   ' Das Mass des neuen Bildes steht schon fest: die Coverspalte meldet es beim Anzeigen und
   ' damit VOR diesem Aufruf, siehe TagCoverPanel.ShowBitmap. Ohne diese Angabe naennte die
   ' Zeile nur die Datei, waehrend die eben noch dort genannte Aufloesung die des alten
   ' Bildes war - und genau die entscheidet, ob das neue genug hergibt.
   FindControl(Of TextBlock)("Hint").Text = If(_coverSize.Length = 0,
                                               LocalizationService.Format("Neues Cover gewählt: {0}", fileName),
                                               LocalizationService.Format("Neues Cover gewählt: {0} ({1})", fileName, _coverSize))
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
