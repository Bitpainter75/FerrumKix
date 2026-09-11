Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading.Tasks
Imports Avalonia.Threading
Imports FerrumPlay.Models
Imports FerrumPlay.Services

Namespace ViewModels

    ' Was von aussen kommt und was nach aussen geht: MPRIS und die Pfade aus einem zweiten Aufruf.
    ' Eine eigene Datei, damit der Bauplan der Oberflaeche nicht noch laenger wird; es ist
    ' dieselbe Klasse wie in MainWindowViewModel.vb.
    Partial Public NotInheritable Class MainWindowViewModel

        Private _mpris As MprisService

        ''' <summary>Das Titelbild fuer MPRIS als Adresse, und zu welchem Titel es gehoert. Beides
        ''' zusammen, weil das Bild erst nach dem Titel ankommt: bis dahin geht der neue Titel ohne
        ''' Bild hinaus statt mit dem des vorigen.</summary>
        Private _mprisArtTrack As Track
        Private _mprisArtUrl As String = String.Empty

        ''' <summary>Das Fenster soll nach vorn kommen: MPRIS Raise oder ein zweiter Aufruf.</summary>
        Public Event RaiseRequested As EventHandler

        ''' <summary>MPRIS verlangt, die Anwendung zu beenden.</summary>
        Public Event QuitRequested As EventHandler

        ''' <summary>Die Eigenschaften, deren Aenderung MPRIS etwas angeht. Die Position gehoert
        ''' NICHT dazu: sie aendert sich laufend und wird nur mitgeschrieben.</summary>
        Private Shared ReadOnly MprisRelevantProperties As New HashSet(Of String)(StringComparer.Ordinal) From {
            NameOf(IsPlaying), NameOf(CurrentTrack), NameOf(DurationSeconds), NameOf(Volume),
            NameOf(IsShuffle), NameOf(Repeat), NameOf(PlaylistSummary), NameOf(HasMissingTracks)
        }

        ''' <summary>Haengt die Anwendung an den Sitzungsbus: der Empfang von Pfaden aus einem
        ''' zweiten Aufruf und MPRIS. Ohne Bus (Windows, macOS) bleibt es beim Empfang, und der
        ''' bekommt nie etwas.</summary>
        Private Sub ConnectToSession()
            SingleInstanceService.AttachReceiver(
                Sub(paths) Dispatcher.UIThread.Post(Sub() OpenFromOutside(paths)))

            Dim connection = SingleInstanceService.Connection
            If connection Is Nothing Then Return

            _mpris = New MprisService(New MprisBridge(Me))
            AddHandler PropertyChanged, AddressOf OnPropertyChangedForMpris
            _mpris.UpdatePosition(_positionSeconds)
            _mpris.Publish(BuildMprisState())
            _mpris.Start(connection)
            Task.Run(AddressOf CoverArtService.PruneArtCache)
        End Sub

        Private Sub DisconnectFromSession()
            SingleInstanceService.AttachReceiver(Nothing)
            If _mpris Is Nothing Then Return
            RemoveHandler PropertyChanged, AddressOf OnPropertyChangedForMpris
            _mpris.Dispose()
            _mpris = Nothing
        End Sub

        ''' <summary>Pfade aus einem zweiten Aufruf, etwa "Oeffnen mit" im Dateimanager. Das Fenster
        ''' kommt nach vorn, und der erste Titel spielt sofort - steht er schon in der Liste, der
        ''' vorhandene Eintrag. Ohne Pfade kommt nur das Fenster nach vorn.</summary>
        Private Async Sub OpenFromOutside(paths As List(Of String))
            Mode = AppMode.Player
            RaiseEvent RaiseRequested(Me, EventArgs.Empty)
            If paths Is Nothing OrElse paths.Count = 0 Then Return
            Try
                Await AddPathsAsync(paths, playFirst:=True)
            Catch ex As Exception
                DiagnosticLogService.LogException("App.OpenFromOutside", ex)
            End Try
        End Sub

        Private Sub OnPropertyChangedForMpris(sender As Object, e As PropertyChangedEventArgs)
            If e.PropertyName IsNot Nothing AndAlso MprisRelevantProperties.Contains(e.PropertyName) Then PublishMpris()
        End Sub

        Private Sub PublishMpris()
            _mpris?.Publish(BuildMprisState())
        End Sub

        Private Sub SetMprisArt(track As Track, artFile As String)
            _mprisArtTrack = track
            _mprisArtUrl = If(String.IsNullOrEmpty(artFile), String.Empty, ToFileUri(artFile))
            PublishMpris()
        End Sub

        Private Function BuildMprisState() As MprisState
            Dim track = _currentTrack
            Dim anyPlayable = _tracks.Any(AddressOf IsPlayable)

            Dim status As String
            If track Is Nothing OrElse _isStopped Then
                status = "Stopped"
            ElseIf _isPlaying Then
                status = "Playing"
            Else
                status = "Paused"
            End If

            Dim loopStatus As String
            Select Case _repeat
                Case RepeatMode.Single : loopStatus = "Track"
                Case RepeatMode.All : loopStatus = "Playlist"
                Case Else : loopStatus = "None"
            End Select

            Dim state As New MprisState With {
                .PlaybackStatus = status,
                .LoopStatus = loopStatus,
                .Shuffle = _isShuffle,
                .Volume = _volume / 100.0,
                .CanGoNext = anyPlayable,
                .CanGoPrevious = anyPlayable,
                .CanPlay = anyPlayable OrElse track IsNot Nothing,
                .CanPause = track IsNot Nothing,
                .CanSeek = track IsNot Nothing AndAlso Not _isStopped AndAlso _durationSeconds > 0
            }
            If track Is Nothing Then Return state

            state.TrackId = MprisTrackId(track)
            state.LengthSeconds = If(_durationSeconds > 0, _durationSeconds, track.DurationSeconds)
            state.Title = track.ShortTitle
            state.Album = track.Album
            state.Artist = track.Artist
            state.AlbumArtist = track.AlbumArtist
            state.Genre = track.Genre
            state.TrackNumber = track.TrackNumber
            state.DiscNumber = track.DiscNumber
            state.Url = ToFileUri(track.FilePath)
            If Object.ReferenceEquals(_mprisArtTrack, track) Then state.ArtUrl = _mprisArtUrl
            Return state
        End Function

        ''' <summary>Die Kennung eines Titels fuer MPRIS: ein Objektpfad, der sich aus dem
        ''' Dateipfad ergibt und deshalb ueber Umsortieren und Neustarts gleich bleibt. Der Pfad
        ''' selbst taugt nicht, ein Objektpfad erlaubt nur Buchstaben, Ziffern und Unterstrich.</summary>
        Private Shared Function MprisTrackId(track As Track) As String
            Using sha = SHA1.Create()
                Dim hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(track.FilePath))).Substring(0, 16)
                Return "/io/github/Bitpainter75/FerrumPlay/Track/T" & hash
            End Using
        End Function

        ''' <summary>file:///... mit jedem Pfadteil einzeln maskiert. Uri aus einem Pfad zu bauen
        ''' laesst ein "#" im Dateinamen als Sprungmarke stehen.</summary>
        Private Shared Function ToFileUri(filePath As String) As String
            If String.IsNullOrEmpty(filePath) Then Return String.Empty
            Dim parts = filePath.Replace(IO.Path.DirectorySeparatorChar, "/"c).Split("/"c).Select(AddressOf Uri.EscapeDataString)
            Dim joined = String.Join("/", parts)
            Return If(joined.StartsWith("/", StringComparison.Ordinal), "file://" & joined, "file:///" & joined)
        End Function

        ' Was MPRIS verlangt, auf dem Anzeigefaden

        Private Sub RaiseWindow()
            RaiseEvent RaiseRequested(Me, EventArgs.Empty)
        End Sub

        Private Sub RequestQuit()
            RaiseEvent QuitRequested(Me, EventArgs.Empty)
        End Sub

        Private Sub MprisPlay()
            If _isPlaying Then Return
            TogglePlayPause()
        End Sub

        Private Sub MprisPause()
            If Not _isPlaying Then Return
            _player.Pause()
        End Sub

        ''' <summary>Seek aus MPRIS ist relativ. Ueber das Ende hinaus heisst laut Spezifikation:
        ''' naechster Titel; vor den Anfang: an den Anfang.</summary>
        Private Sub MprisSeekBy(offsetSeconds As Double)
            If _currentTrack Is Nothing OrElse _isStopped Then Return
            Dim target = _positionSeconds + offsetSeconds
            If _durationSeconds > 0 AndAlso target >= _durationSeconds Then
                PlayNext(userRequested:=True)
                Return
            End If
            SeekTo(Math.Max(0, target))
        End Sub

        ''' <summary>SetPosition gilt nur, wenn die Kennung zum laufenden Titel passt - so will es
        ''' die Spezifikation, damit ein veralteter Befehl nicht im naechsten Titel landet.</summary>
        Private Sub MprisSetPosition(trackId As String, seconds As Double)
            If _currentTrack Is Nothing OrElse _isStopped Then Return
            If Not String.Equals(trackId, MprisTrackId(_currentTrack), StringComparison.Ordinal) Then Return
            If seconds < 0 OrElse (_durationSeconds > 0 AndAlso seconds > _durationSeconds) Then Return
            SeekTo(seconds)
        End Sub

        Private Sub MprisOpenUri(uri As String)
            If String.IsNullOrWhiteSpace(uri) Then Return
            Dim localPath As String
            Try
                localPath = If(uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase), New Uri(uri).LocalPath, uri)
            Catch ex As Exception
                DiagnosticLogService.Log("Mpris.OpenUri", $"{uri}: {ex.Message}")
                Return
            End Try
            OpenFromOutside(New List(Of String) From {localPath})
        End Sub

        Private Sub MprisSetLoopStatus(value As String)
            Select Case value
                Case "Track" : Repeat = RepeatMode.Single
                Case "Playlist" : Repeat = RepeatMode.All
                Case "None" : Repeat = RepeatMode.Off
            End Select
        End Sub

        ''' <summary>Nimmt die Aufrufe vom Lesefaden des Busses an und reicht sie auf den
        ''' Anzeigefaden weiter. Nichts davon wartet auf das Ergebnis: MPRIS-Befehle haben keine
        ''' Antwort ausser "angekommen".</summary>
        Private NotInheritable Class MprisBridge
            Implements IMprisPlayer

            Private ReadOnly _owner As MainWindowViewModel

            Public Sub New(owner As MainWindowViewModel)
                _owner = owner
            End Sub

            Private Shared Sub Post(action As Action)
                Dispatcher.UIThread.Post(
                    Sub()
                        Try
                            action()
                        Catch ex As Exception
                            DiagnosticLogService.LogException("Mpris.Command", ex)
                        End Try
                    End Sub)
            End Sub

            Public Sub Raise() Implements IMprisPlayer.Raise
                Post(Sub() _owner.RaiseWindow())
            End Sub

            Public Sub Quit() Implements IMprisPlayer.Quit
                Post(Sub() _owner.RequestQuit())
            End Sub

            Public Sub PlayPause() Implements IMprisPlayer.PlayPause
                Post(Sub() _owner.TogglePlayPause())
            End Sub

            Public Sub Play() Implements IMprisPlayer.Play
                Post(Sub() _owner.MprisPlay())
            End Sub

            Public Sub Pause() Implements IMprisPlayer.Pause
                Post(Sub() _owner.MprisPause())
            End Sub

            Public Sub [Stop]() Implements IMprisPlayer.Stop
                Post(Sub() _owner.StopPlayback())
            End Sub

            Public Sub [Next]() Implements IMprisPlayer.Next
                Post(Sub() _owner.PlayNext(userRequested:=True))
            End Sub

            Public Sub Previous() Implements IMprisPlayer.Previous
                Post(Sub() _owner.PlayPrevious())
            End Sub

            Public Sub SeekBy(offsetSeconds As Double) Implements IMprisPlayer.SeekBy
                Post(Sub() _owner.MprisSeekBy(offsetSeconds))
            End Sub

            Public Sub SetPosition(trackId As String, seconds As Double) Implements IMprisPlayer.SetPosition
                Post(Sub() _owner.MprisSetPosition(trackId, seconds))
            End Sub

            Public Sub OpenUri(uri As String) Implements IMprisPlayer.OpenUri
                Post(Sub() _owner.MprisOpenUri(uri))
            End Sub

            Public Sub SetVolume(value As Double) Implements IMprisPlayer.SetVolume
                Post(Sub() _owner.Volume = Math.Clamp(value, 0.0, 1.0) * 100.0)
            End Sub

            Public Sub SetShuffle(value As Boolean) Implements IMprisPlayer.SetShuffle
                Post(Sub() _owner.IsShuffle = value)
            End Sub

            Public Sub SetLoopStatus(value As String) Implements IMprisPlayer.SetLoopStatus
                Post(Sub() _owner.MprisSetLoopStatus(value))
            End Sub

        End Class

    End Class

End Namespace
