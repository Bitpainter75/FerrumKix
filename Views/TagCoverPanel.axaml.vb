Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Markup.Xaml
Imports Avalonia.Media.Imaging
Imports Avalonia.Platform.Storage
Imports Avalonia.Threading
Imports FerrumPlay.Models
Imports FerrumPlay.Services

Namespace Views

    ''' <summary>Die Coverspalte waehrend des MP3-Taggens.
    '''
    ''' <para>Sie tritt an die Stelle der laufenden Wiedergabe, statt sich dazuzustellen: waehrend
    ''' des Taggens gehoert die Spalte den Dateien, die bearbeitet werden. Lief gerade etwas
    ''' anderes, waere dessen Bild hier eine Einladung, das falsche Cover zu ziehen.</para>
    '''
    ''' <para>Das Ablegen eines Bildes gehoert deshalb ebenfalls hierher. Die gelesenen Bilddaten
    ''' gehen ueber <see cref="CoverChosen"/> an den Tag-Bereich, der sie beim Speichern
    ''' schreibt.</para></summary>
    Public Class TagCoverPanel
        Inherits UserControl

        ''' <summary>Ein neues Cover wurde abgelegt. Die Daten sind bereits gelesen und als Bild
        ''' geprueft.</summary>
        Public Event CoverChosen(bytes As Byte(), fileName As String)

        Public Sub New()
            AvaloniaXamlLoader.Load(Me)
        End Sub

        ''' <summary>Fuellt die Spalte aus den Dateien, die getaggt werden. Das Cover kommt aus dem
        ''' ERSTEN Titel: ein Album traegt ueblicherweise ueberall dasselbe Bild, und getaggt wird
        ''' ohnehin ein Album auf einmal.</summary>
        Public Sub Show(tracks As IReadOnlyList(Of Track))
            Dim first = If(tracks, Array.Empty(Of Track)()).FirstOrDefault()
            FindControl(Of TextBlock)("AlbumText").Text = If(first Is Nothing, String.Empty, first.Album)
            FindControl(Of TextBlock)("ArtistText").Text = If(first Is Nothing, String.Empty, If(String.IsNullOrWhiteSpace(first.AlbumArtist), first.Artist, first.AlbumArtist))
            FindControl(Of TextBlock)("CountText").Text = LocalizationService.Format("{0} Titel", If(tracks Is Nothing, 0, tracks.Count))
            ShowBitmap(Nothing, ownsIt:=False)
            If first Is Nothing Then Return
            LoadEmbeddedCoverAsync(first.FilePath)
        End Sub

        ''' <summary>Das eingebettete Bild des ersten Titels. Ueber Platte und Decoder, also nicht
        ''' auf dem Anzeigefaden; das Ergebnis gehoert dem Zwischenspeicher und wird hier nur
        ''' gezeigt, nicht freigegeben.</summary>
        Private Async Sub LoadEmbeddedCoverAsync(filePath As String)
            Try
                Dim bitmap = Await Task.Run(Function() CoverArtService.Load(filePath))
                If bitmap Is Nothing Then Return
                ' Ein inzwischen abgelegtes Bild ist das genauere: es wird nicht wieder verdraengt.
                If _ownsCover Then Return
                ShowBitmap(bitmap, ownsIt:=False)
            Catch ex As Exception
                DiagnosticLogService.LogException("TagCover.Load", ex)
            End Try
        End Sub

        ''' <summary>Hebt den Coverrahmen hervor, solange ein Bild ueber der Spalte haengt.</summary>
        Public Sub SetDropActive(active As Boolean)
            Dim area = FindControl(Of Border)("CoverArea")
            If area Is Nothing Then Return
            If active Then area.Classes.Add("drop-target") Else area.Classes.Remove("drop-target")
        End Sub

        ''' <summary>Uebernimmt ein abgelegtes Bild: liest es, zeigt es und meldet es weiter.
        ''' Das Ablegen selbst behandelt <see cref="PlayerView"/> - dort kommt das geroutete
        ''' Ereignis auf seinem Weg zum Fenster verlaesslich vorbei, waehrend es innerhalb dieser
        ''' Spalte davon abhinge, welches Element unter dem Zeiger gerade getroffen wird.</summary>
        Public Async Function ApplyCoverAsync(filePath As String) As Task
            Dim bytes = Await File.ReadAllBytesAsync(filePath)
            ' Einmal entpacken: eine unbrauchbare Datei faellt damit hier auf und nicht erst beim
            ' Schreiben der Tags. Das Bild wird zugleich angezeigt.
            Dim preview As Bitmap
            Using stream As New MemoryStream(bytes)
                preview = New Bitmap(stream)
            End Using
            ShowBitmap(preview, ownsIt:=True, fileSize:=bytes.LongLength)
            RaiseEvent CoverChosen(bytes, Path.GetFileName(filePath))
        End Function

        ''' <summary>Zeigt ein Bild und darunter sein Mass. Die Punktzahl ist beim Taggen die
        ''' wichtigste Angabe: aus ihr ergibt sich, ob das Bild ueberhaupt genug hergibt fuer
        ''' die eingestellte Kantenlaenge oder ob es dafuer hochgerechnet wuerde.</summary>
        Private Sub ShowBitmap(bitmap As Bitmap, ownsIt As Boolean, Optional fileSize As Long = 0)
            Dim image = FindControl(Of Image)("CoverImage")
            Dim previous = TryCast(image.Source, Bitmap)
            Dim previousOwned = _ownsCover
            image.Source = bitmap
            _ownsCover = ownsIt
            Dim parts As New List(Of String)()
            If bitmap IsNot Nothing Then parts.Add($"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}")
            If fileSize > 0 Then parts.Add(Track.FormatFileSize(fileSize))
            FindControl(Of TextBlock)("SizeText").Text = String.Join(" · ", parts)
            ' Nur ein selbst geladenes Bild darf freigegeben werden; das eingebettete gehoert dem
            ' Zwischenspeicher des Coverdienstes. Und erst im naechsten Durchgang: das alte Bild
            ' kann im laufenden Zeichendurchgang noch haengen.
            If previous IsNot Nothing AndAlso previousOwned AndAlso Not Object.ReferenceEquals(previous, bitmap) Then
                Dispatcher.UIThread.Post(Sub() previous.Dispose(), DispatcherPriority.Background)
            End If
        End Sub

        ''' <summary>Ob das gezeigte Bild dieser Spalte gehoert (abgelegt) oder dem Zwischenspeicher
        ''' des Coverdienstes (eingebettet). Nur das eigene darf freigegeben werden.</summary>
        Private _ownsCover As Boolean

    End Class

End Namespace
