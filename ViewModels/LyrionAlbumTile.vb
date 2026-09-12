Imports System
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Avalonia.Media.Imaging
Imports Avalonia.Threading
Imports FerrumPlay.Services

Namespace ViewModels

    ''' <summary>Eine Kachel der Lyrion-Albenuebersicht.
    '''
    ''' <para>Die Kacheln liegen vollstaendig in der Liste, die Bilder aber nicht: das Cover wird
    ''' erst geholt, wenn der ItemsRepeater die Kachel wirklich baut, und wieder losgelassen, sobald
    ''' er sie beim Rollen weiterreicht. So kostet eine Bibliothek mit tausenden Alben nur so viel
    ''' Bildspeicher wie der sichtbare Ausschnitt, und die geladenen Bilddaten bleiben im
    ''' Zwischenspeicher des Dienstes fuer den Weg zurueck liegen.</para></summary>
    Public NotInheritable Class LyrionAlbumTile
        Inherits ViewModelBase

        ''' <summary>Die Kantenlaenge des angeforderten Covers. Groesser als die Kachel, damit es
        ''' auf einem vergroesserten Bildschirm nicht ausfranst.</summary>
        Public Const CoverPixelSize As Integer = 320

        Private _cover As Bitmap
        Private _loading As CancellationTokenSource

        Public Sub New(album As LyrionMediaServerService.Album)
            Me.Album = album
        End Sub

        Public ReadOnly Property Album As LyrionMediaServerService.Album

        Public ReadOnly Property Title As String
            Get
                Dim name = If(Album?.Title, String.Empty)
                Dim year = If(Album?.Year, String.Empty)
                Return If(String.IsNullOrWhiteSpace(year), name, name & " (" & year & ")")
            End Get
        End Property

        Public ReadOnly Property Artist As String
            Get
                Return If(Album?.Artist, String.Empty)
            End Get
        End Property

        Public Property Cover As Bitmap
            Get
                Return _cover
            End Get
            Private Set(value As Bitmap)
                SetField(_cover, value)
            End Set
        End Property

        ''' <summary>Holt das Cover, sofern es nicht schon da ist. Mehrfache Aufrufe waehrend eines
        ''' laufenden Ladevorgangs bleiben folgenlos.</summary>
        Public Sub RequestCover()
            If _cover IsNot Nothing OrElse _loading IsNot Nothing Then Return
            Dim source As New CancellationTokenSource()
            _loading = source
            LoadAsync(source)
        End Sub

        ''' <summary>Gibt das Bild wieder frei, wenn die Kachel aus dem Sichtbereich rollt. Die
        ''' Bilddaten selbst bleiben im Zwischenspeicher des Dienstes; nur das entpackte Bild, das
        ''' ein Vielfaches davon belegt, wird losgelassen.</summary>
        Public Sub ReleaseCover()
            ' Nur abbrechen, nicht wegwerfen: der laufende Ladevorgang haelt dieselbe Quelle noch
            ' in der Hand und raeumt sie am Ende selbst auf.
            _loading?.Cancel()
            _loading = Nothing
            Dim released = _cover
            Cover = Nothing
            ' Freigegeben wird erst im naechsten Durchgang: ein gerade weitergereichtes Element kann
            ' im laufenden Zeichendurchgang noch daran haengen. Ohne die Freigabe bliebe von jeder
            ' vorbeigerollten Kachel eine entpackte Bildflaeche liegen - genau das, was das
            ' Loslassen verhindern soll.
            If released IsNot Nothing Then Dispatcher.UIThread.Post(Sub() released.Dispose(), DispatcherPriority.Background)
        End Sub

        Private Async Sub LoadAsync(source As CancellationTokenSource)
            Try
                Dim bytes = Await LyrionMediaServerService.GetThumbnailAsync(Album, CoverPixelSize, source.Token)
                If bytes Is Nothing OrElse bytes.Length = 0 OrElse source.IsCancellationRequested Then Return
                ' Das Entpacken gehoert nicht auf den Oberflaechenfaden: bei zwoelf Kacheln je
                ' Rollschritt waere es dort sichtbar.
                Dim image = Await Task.Run(Function()
                                               Try
                                                   Using stream As New MemoryStream(bytes)
                                                       Return Bitmap.DecodeToWidth(stream, CoverPixelSize)
                                                   End Using
                                               Catch
                                                   Return Nothing
                                               End Try
                                           End Function)
                If image Is Nothing Then Return
                If source.IsCancellationRequested Then image.Dispose() : Return
                Await Dispatcher.UIThread.InvokeAsync(Sub()
                                                          If source.IsCancellationRequested Then image.Dispose() Else Cover = image
                                                      End Sub)
            Catch ex As OperationCanceledException
            Catch ex As Exception
                DiagnosticLogService.LogException("Lyrion.Cover", ex)
            Finally
                If Object.ReferenceEquals(_loading, source) Then _loading = Nothing
                source.Dispose()
            End Try
        End Sub

    End Class

End Namespace
