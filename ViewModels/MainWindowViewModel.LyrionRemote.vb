Imports System
Imports System.Threading
Imports System.Threading.Tasks
Imports Avalonia.Threading
Imports FerrumPlay.Models
Imports FerrumPlay.Services

Namespace ViewModels

    ''' <summary>FerrumPlay als Fernbedienung eines Lyrion-Players.
    '''
    ''' <para>Ist ein Geraet gewaehlt, spielt die Anwendung NICHT selbst: jeder Transportbefehl geht
    ''' an den Server, und Titel, Position, Laufzeit und Lautstaerke kommen aus dessen Antwort. Die
    ''' Anzeigefelder sind dieselben wie bei oertlicher Wiedergabe - und weil MPRIS seinen Zustand
    ''' genau aus diesen Feldern baut (<see cref="BuildMprisState"/>), zeigt Waybar den Titel des
    ''' Geraets, ohne dass hier etwas Zusaetzliches dafuer geschieht.</para></summary>
    Partial Public NotInheritable Class MainWindowViewModel

        ''' <summary>Der laufende Titel des Geraets als <see cref="Track"/>. Er wird nur neu gebaut,
        ''' wenn sich auf dem Geraet wirklich etwas geaendert hat: die Anzeige haengt an der
        ''' Objektgleichheit (<c>ReferenceEquals</c>), und ein bei jeder Abfrage neu erzeugter Titel
        ''' liesse Cover und Zeile im Sekundentakt flackern.</summary>
        Private _remoteTrack As Track
        Private _remoteKey As String = String.Empty

        ''' <summary>Bis wann eine selbst gesetzte Lautstaerke Vorrang hat.
        '''
        ''' <para>Der Regler schickt seinen Wert los, die naechste Abfrage bringt aber noch den
        ''' ALTEN - ohne diese kurze Frist zoege es den Regler beim Ziehen staendig zurueck.</para></summary>
        Private _remoteVolumeUntil As Date = Date.MinValue
        Private _remoteSeekUntil As Date = Date.MinValue

        ''' <summary>Meldet sich beim Fernsteuerungsdienst an und nimmt ein gemerktes Geraet wieder
        ''' auf. Wird beim Aufbau des Fensters aufgerufen.</summary>
        Friend Sub HookLyrionRemote()
            AddHandler LyrionRemoteService.StatusChanged, AddressOf OnRemoteStatusChanged
            AddHandler LyrionRemoteService.SelectionChanged, AddressOf OnRemoteSelectionChanged
            LyrionRemoteService.RestartPolling()
            RaisePropertyChanged(NameOf(IsRemoteControl))
            RaisePropertyChanged(NameOf(RemoteDeviceName))
        End Sub

        ''' <summary>Wird gerade ferngesteuert? Die Oberflaeche haengt daran, ob sie den Namen des
        ''' Geraets zeigt.</summary>
        Public ReadOnly Property IsRemoteControl As Boolean
            Get
                Return LyrionRemoteService.IsRemote
            End Get
        End Property

        Public ReadOnly Property RemoteDeviceName As String
            Get
                Return LyrionRemoteService.SelectedPlayerName
            End Get
        End Property

        Private Sub OnRemoteStatusChanged(sender As Object, e As EventArgs)
            Dispatcher.UIThread.Post(AddressOf ApplyRemoteStatus)
        End Sub

        Private Sub OnRemoteSelectionChanged(sender As Object, e As EventArgs)
            Dispatcher.UIThread.Post(AddressOf ApplyRemoteSelection)
        End Sub

        Private Sub ApplyRemoteSelection()
            If LyrionRemoteService.IsRemote Then
                ' Zwei Tonquellen auf einmal will niemand: wer auf ein Geraet umschaltet, will dort
                ' hoeren und nicht auch noch hier.
                _player.Stop()
                _isStopped = True
                IsPlaying = False
                PositionSeconds = 0
            Else
                ' Zurueck zur oertlichen Wiedergabe: der Titel des Geraets gehoert nicht mehr
                ' hierher, und es laeuft auch nichts mehr.
                _remoteTrack = Nothing
                _remoteKey = String.Empty
                _currentTrack = Nothing
                _isStopped = True
                IsPlaying = False
                PositionSeconds = 0
                DurationSeconds = 0
                RaiseCurrentTrackChanged()
                UpdatePlayingRow()
                PublishMpris()
            End If
            RaisePropertyChanged(NameOf(IsRemoteControl))
            RaisePropertyChanged(NameOf(RemoteDeviceName))
            RaisePropertyChanged(NameOf(IsPlayingLyrion))
        End Sub

        ''' <summary>Uebernimmt den Zustand des Geraets in die Anzeige.</summary>
        Private Sub ApplyRemoteStatus()
            If Not LyrionRemoteService.IsRemote Then Return
            Dim status = LyrionRemoteService.Status
            If status Is Nothing OrElse Not status.Reachable Then Return

            ' Der Titel nur dann neu, wenn er wirklich ein anderer ist.
            Dim key = String.Join("|", status.Title, status.Artist, status.Album, status.ArtworkTrackId)
            If _remoteTrack Is Nothing OrElse Not String.Equals(key, _remoteKey, StringComparison.Ordinal) Then
                _remoteKey = key
                ' Die Kennung ist die des laufenden Titels: der Server gibt sowohl seinen Stream
                ' als auch sein Titelbild unter ihr heraus (/music/{id}/download bzw. cover.jpg).
                _remoteTrack = New Track With {
                    .FilePath = LyrionMediaServerService.StreamUrl(status.ArtworkTrackId),
                    .Title = status.Title, .Artist = status.Artist,
                    .Album = status.Album, .AlbumArtist = status.Artist,
                    .RemoteCoverUrl = CoverUrlFor(status.ArtworkTrackId),
                    .DurationSeconds = status.DurationSeconds}
                _currentTrack = _remoteTrack
                RaiseCurrentTrackChanged()
                UpdatePlayingRow()
                ' Ohne diesen Aufruf bliebe die Coverspalte links leer: das Bild kommt nicht mit
                ' dem Titel, es wird eigens geholt. Bei oertlicher Wiedergabe erledigt das
                ' PlayCore - der Weg hierher geht daran vorbei.
                LoadCoverAsync(_remoteTrack)
            End If

            IsPlaying = status.IsPlaying
            _isStopped = status.IsStopped
            If status.DurationSeconds > 0 Then DurationSeconds = status.DurationSeconds
            ' Ein eben gesetzter Wert hat Vorrang, bis der Server ihn zurueckmeldet.
            If Date.UtcNow > _remoteSeekUntil Then PositionSeconds = status.PositionSeconds
            If Date.UtcNow > _remoteVolumeUntil Then
                If SetField(_volume, Math.Clamp(status.Volume, 0, 100), NameOf(Volume)) Then
                    RaisePropertyChanged(NameOf(VolumeIconSource))
                End If
                If SetField(_isMuted, status.Muted, NameOf(IsMuted)) Then
                    RaisePropertyChanged(NameOf(VolumeIconSource))
                End If
            End If
            PublishMpris()
        End Sub

        Private Shared Function CoverUrlFor(artworkTrackId As String) As String
            If String.IsNullOrWhiteSpace(artworkTrackId) Then Return String.Empty
            Return LyrionMediaServerService.ArtworkUrl(New LyrionMediaServerService.Album With {.ArtworkTrackId = artworkTrackId})
        End Function

        ''' <summary>Schickt einen Befehl ans Geraet, ohne auf ihn zu warten. Fehler gehoeren ins
        ''' Protokoll und nicht in eine unbeachtete Ausnahme eines Hintergrundfadens.</summary>
        Private Shared Sub RemoteCall(command As Func(Of CancellationToken, Task))
            Task.Run(Async Function()
                         Try
                             Await command(CancellationToken.None)
                         Catch ex As Exception
                             DiagnosticLogService.LogException("Lyrion.Remote", ex)
                         End Try
                     End Function)
        End Sub

        ' Die Einsprungstellen der Transportsteuerung. Jede prueft ZUERST, ob ferngesteuert wird -
        ' erst danach kommt der oertliche Weg.

        Private Function RemoteTogglePlayPause() As Boolean
            If Not LyrionRemoteService.IsRemote Then Return False
            RemoteCall(AddressOf LyrionRemoteService.PlayPauseAsync)
            Return True
        End Function

        Private Function RemoteStop() As Boolean
            If Not LyrionRemoteService.IsRemote Then Return False
            RemoteCall(AddressOf LyrionRemoteService.StopAsync)
            Return True
        End Function

        Private Function RemoteNext() As Boolean
            If Not LyrionRemoteService.IsRemote Then Return False
            RemoteCall(AddressOf LyrionRemoteService.NextAsync)
            Return True
        End Function

        Private Function RemotePrevious() As Boolean
            If Not LyrionRemoteService.IsRemote Then Return False
            RemoteCall(AddressOf LyrionRemoteService.PreviousAsync)
            Return True
        End Function

        Private Function RemoteSeek(seconds As Double) As Boolean
            If Not LyrionRemoteService.IsRemote Then Return False
            _remoteSeekUntil = Date.UtcNow.AddSeconds(2)
            PositionSeconds = seconds
            RemoteCall(Function(token) LyrionRemoteService.SeekAsync(seconds, token))
            Return True
        End Function

        Private Function RemoteSetVolume(percent As Double) As Boolean
            If Not LyrionRemoteService.IsRemote Then Return False
            _remoteVolumeUntil = Date.UtcNow.AddSeconds(2)
            RemoteCall(Function(token) LyrionRemoteService.SetVolumeAsync(percent, token))
            Return True
        End Function

        Private Function RemoteSetMuted(muted As Boolean) As Boolean
            If Not LyrionRemoteService.IsRemote Then Return False
            _remoteVolumeUntil = Date.UtcNow.AddSeconds(2)
            RemoteCall(Function(token) LyrionRemoteService.SetMutedAsync(muted, token))
            Return True
        End Function

        Private Function RemoteSetShuffle(shuffle As Boolean) As Boolean
            If Not LyrionRemoteService.IsRemote Then Return False
            RemoteCall(Function(token) LyrionRemoteService.SetShuffleAsync(shuffle, token))
            Return True
        End Function

        ''' <summary>FerrumPlay kennt aus, einen Titel, alle; der Server ebenso (0, 1, 2).</summary>
        Private Function RemoteSetRepeat(mode As RepeatMode) As Boolean
            If Not LyrionRemoteService.IsRemote Then Return False
            Dim serverMode = Select_RepeatMode(mode)
            RemoteCall(Function(token) LyrionRemoteService.SetRepeatAsync(serverMode, token))
            Return True
        End Function

        Private Shared Function Select_RepeatMode(mode As RepeatMode) As Integer
            Select Case mode
                Case RepeatMode.Single : Return 1
                Case RepeatMode.All : Return 2
                Case Else : Return 0
            End Select
        End Function

        ''' <summary>Spielt ein Album auf dem Geraet. Die Ansicht ruft das statt der oertlichen
        ''' Wiedergabe, sobald ein Geraet gewaehlt ist.</summary>
        Public Sub PlayLyrionAlbumRemote(albumId As String, trackIndex As Integer)
            If Not LyrionRemoteService.IsRemote Then Return
            RemoteCall(Function(token) LyrionRemoteService.PlayAlbumAsync(albumId, trackIndex, token))
        End Sub

    End Class

End Namespace
