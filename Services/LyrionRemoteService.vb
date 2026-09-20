Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

Namespace Services

    ''' <summary>FerrumKix als Fernbedienung: statt selbst zu spielen, steuert es einen Player,
    ''' der am Lyrion-Server angemeldet ist.
    '''
    ''' <para>Dafuer braucht es KEIN SlimProto. Der Server nimmt jeden Befehl mit der Kennung des
    ''' Players entgegen (<c>playerid</c> als erster Parameter von <c>slim.request</c>), und
    ''' <c>status</c> gibt seinen ganzen Zustand heraus. Die drei Wege aus
    ''' <c>LYRION_MEDIA_SERVER.md</c> beantworten eine ANDERE Frage - dort ging es darum, dass
    ''' FerrumKix selbst als Player in der Lyrion-Oberflaeche steht. Hier ist es umgekehrt.</para>
    '''
    ''' <para>Der Server schickt von sich aus nichts (JSON-RPC kennt kein Abonnement), also wird
    ''' gefragt - einmal je Sekunde, solange ein Geraet gewaehlt ist. Das ist eine winzige Abfrage
    ''' und laeuft NICHT ueber <see cref="LyrionTaskState"/>: sie arbeitet nicht an der Bibliothek
    ''' und darf einen Abgleich weder blockieren noch von ihm blockiert werden.</para></summary>
    Public NotInheritable Class LyrionRemoteService

        Private Sub New()
        End Sub

        ''' <summary>Ein am Server angemeldeter Player.</summary>
        Public NotInheritable Class RemotePlayer
            Public Property Id As String = String.Empty
            Public Property Name As String = String.Empty
            ''' <summary>Was fuer ein Geraet, etwa "SqueezeLite" oder "UPnPBridge".</summary>
            Public Property Model As String = String.Empty
            Public Property Connected As Boolean
        End Class

        ''' <summary>Der Zustand des gesteuerten Geraets, wie ihn eine Abfrage liefert.</summary>
        Public NotInheritable Class RemoteStatus
            Public Property PlayerId As String = String.Empty
            ''' <summary>Falsch, wenn die Abfrage nicht durchkam. Dann sind alle uebrigen Angaben
            ''' bedeutungslos, und die Oberflaeche soll den letzten Stand NICHT als aktuell
            ''' ausgeben.</summary>
            Public Property Reachable As Boolean
            Public Property IsPlaying As Boolean
            Public Property IsStopped As Boolean
            Public Property Volume As Double
            Public Property Muted As Boolean
            Public Property PositionSeconds As Double
            Public Property DurationSeconds As Double
            Public Property Title As String = String.Empty
            Public Property Artist As String = String.Empty
            Public Property Album As String = String.Empty
            ''' <summary>Die Album-Kennung des laufenden Titels. Nur mit ihr laesst sich "zum
            ''' aktuellen Titel springen" auf das Album fuehren, das gerade auf dem Geraet
            ''' laeuft.</summary>
            Public Property AlbumId As String = String.Empty
            Public Property ArtworkTrackId As String = String.Empty
            Public Property PlaylistIndex As Integer = -1
            Public Property PlaylistCount As Integer
            Public Property Shuffle As Integer
            Public Property Repeat As Integer
        End Class

#Region "Welches Geraet"

        Private Shared ReadOnly Gate As New Object()
        Private Shared _poll As CancellationTokenSource
        Private Shared _status As RemoteStatus

        ''' <summary>Meldet einen neuen Zustand des Geraets. Kommt aus einem Hintergrundfaden.</summary>
        Public Shared Event StatusChanged As EventHandler

        ''' <summary>Das gewaehlte Geraet hat gewechselt - oder es wird wieder oertlich gespielt.</summary>
        Public Shared Event SelectionChanged As EventHandler

        ''' <summary>Die Kennung des gesteuerten Geraets. Leer heisst: oertlich abspielen, alles
        ''' bleibt wie bisher.</summary>
        Public Shared ReadOnly Property SelectedPlayerId As String
            Get
                Return AppSettingsService.ActiveLyrionServer.RemotePlayerId
            End Get
        End Property

        Public Shared ReadOnly Property SelectedPlayerName As String
            Get
                Return AppSettingsService.ActiveLyrionServer.RemotePlayerName
            End Get
        End Property

        ''' <summary>Wird ferngesteuert? Die eine Frage, an der in der Anwendung alles haengt.</summary>
        Public Shared ReadOnly Property IsRemote As Boolean
            Get
                Return Not String.IsNullOrWhiteSpace(SelectedPlayerId)
            End Get
        End Property

        ''' <summary>Der zuletzt gelesene Zustand, oder Nothing, solange keiner vorliegt.</summary>
        Public Shared ReadOnly Property Status As RemoteStatus
            Get
                SyncLock Gate
                    Return _status
                End SyncLock
            End Get
        End Property

        ''' <summary>Waehlt das Geraet. Leere Kennung heisst zurueck zur oertlichen Wiedergabe.</summary>
        Public Shared Sub SelectPlayer(playerId As String, playerName As String)
            Dim wanted = If(playerId, String.Empty).Trim()
            If String.Equals(wanted, SelectedPlayerId, StringComparison.Ordinal) Then Return

            AppSettingsService.ActiveLyrionServer.RemotePlayerId = wanted
            AppSettingsService.ActiveLyrionServer.RemotePlayerName = If(playerName, String.Empty)
            AppSettingsService.Save()

            SyncLock Gate
                _status = Nothing
            End SyncLock
            RestartPolling()
            RaiseEvent SelectionChanged(Nothing, EventArgs.Empty)
        End Sub

        ''' <summary>Faengt an zu fragen, sobald ein Geraet gewaehlt ist, und hoert auf, sobald
        ''' keines mehr gewaehlt ist. Wird beim Start der Anwendung einmal aufgerufen, damit ein
        ''' gemerktes Geraet sofort wieder bedient wird.</summary>
        Public Shared Sub RestartPolling()
            Dim previous As CancellationTokenSource
            Dim source As CancellationTokenSource = Nothing
            SyncLock Gate
                previous = _poll
                _poll = Nothing
                If IsRemote Then
                    source = New CancellationTokenSource()
                    _poll = source
                End If
            End SyncLock
            previous?.Cancel()
            previous?.Dispose()
            If source IsNot Nothing Then Task.Run(Function() PollAsync(source.Token))
        End Sub

        Private Shared Async Function PollAsync(token As CancellationToken) As Task
            Do While Not token.IsCancellationRequested
                Dim snapshot As RemoteStatus
                Try
                    snapshot = Await ReadStatusAsync(token)
                Catch ex As OperationCanceledException When token.IsCancellationRequested
                    Return
                Catch ex As Exception
                    ' Ein Aussetzer darf die Schleife nicht beenden: dann bliebe die Anzeige fuer
                    ' immer auf dem letzten Stand stehen, ohne dass jemand es merkt. Gemeldet wird
                    ' er als "nicht erreichbar", und beim naechsten Mal wird es wieder versucht.
                    snapshot = New RemoteStatus With {.PlayerId = SelectedPlayerId, .Reachable = False}
                End Try

                ' Wurde zwischenzeitlich umgeschaltet, gehoert dieser Stand nicht mehr hierher -
                ' sonst schriebe eine abgeloeste Schleife den Zustand des alten Geraets zurueck.
                If token.IsCancellationRequested Then Return
                SyncLock Gate
                    _status = snapshot
                End SyncLock
                RaiseEvent StatusChanged(Nothing, EventArgs.Empty)

                Try
                    Await Task.Delay(1000, token)
                Catch ex As OperationCanceledException
                    Return
                End Try
            Loop
        End Function

#End Region

#Region "Lesen"

        ''' <summary>Die am Server angemeldeten Player. Ein nicht verbundener steht mit in der
        ''' Liste - er laesst sich aber nicht steuern, und die Ansicht sagt das.</summary>
        Public Shared Async Function GetPlayersAsync(cancellationToken As CancellationToken) As Task(Of List(Of RemotePlayer))
            Dim players As New List(Of RemotePlayer)()
            Dim result = Await LyrionMediaServerService.RequestAsync("", {"players", "0", "100"}, cancellationToken)
            Dim rows As JsonElement
            If Not result.TryGetProperty("players_loop", rows) OrElse rows.ValueKind <> JsonValueKind.Array Then Return players
            For Each row In rows.EnumerateArray()
                players.Add(New RemotePlayer With {
                    .Id = LyrionMediaServerService.TextOf(row, "playerid"),
                    .Name = LyrionMediaServerService.TextOf(row, "name"),
                    .Model = LyrionMediaServerService.TextOf(row, "modelname"),
                    .Connected = LyrionMediaServerService.TextOf(row, "connected") = "1"})
            Next
            Return players
        End Function

        ''' <summary>Der Zustand des gewaehlten Geraets.
        '''
        ''' <para><c>status - 1</c> heisst: ab dem LAUFENDEN Titel genau einen herausgeben. Die
        ''' Kennzeichen holen dazu Interpret (a), Album (l), Album-Kennung (e), Laufzeit (d) und
        ''' die Cover-Titelkennung (J) - ohne sie kaeme nur der Dateiname.</para></summary>
        Public Shared Async Function ReadStatusAsync(cancellationToken As CancellationToken) As Task(Of RemoteStatus)
            Dim playerId = SelectedPlayerId
            Dim snapshot As New RemoteStatus With {.PlayerId = playerId}
            If String.IsNullOrWhiteSpace(playerId) Then Return snapshot

            Dim result = Await LyrionMediaServerService.RequestAsync(playerId, {"status", "-", "1", "tags:aledJ"}, cancellationToken)
            snapshot.Reachable = True

            Dim mode = LyrionMediaServerService.TextOf(result, "mode")
            snapshot.IsPlaying = mode = "play"
            snapshot.IsStopped = mode = "stop"
            ' Stummschaltung steckt im VORZEICHEN der Lautstaerke: bei stumm meldet "status"
            ' -100, und der Betrag ist der Stand, auf den das Aufheben zurueckfuehrt. Ein Feld
            ' "mixer muting" gibt es in dieser Antwort NICHT (nur als eigene Abfrage
            ' "mixer muting ?"). Aufgenommen an LMS 9.1.2 mit SqueezeLite.
            '
            ' Das Vorzeichen darf deshalb nicht weggeschnitten werden - ein Math.Clamp(..., 0, 100)
            ' machte aus "stumm bei 100" ein "auf null gedreht", und das Aufheben fuehrte danach
            ' auf null statt auf den vorigen Stand.
            Dim rawVolume = NumberOf(result, "mixer volume")
            snapshot.Muted = rawVolume < 0
            snapshot.Volume = Math.Clamp(Math.Abs(rawVolume), 0, 100)
            snapshot.PositionSeconds = NumberOf(result, "time")
            snapshot.DurationSeconds = NumberOf(result, "duration")
            snapshot.PlaylistCount = CInt(NumberOf(result, "playlist_tracks"))
            snapshot.PlaylistIndex = CInt(NumberOf(result, "playlist_cur_index"))
            snapshot.Shuffle = CInt(NumberOf(result, "playlist shuffle"))
            snapshot.Repeat = CInt(NumberOf(result, "playlist repeat"))

            Dim rows As JsonElement
            If result.TryGetProperty("playlist_loop", rows) AndAlso rows.ValueKind = JsonValueKind.Array Then
                For Each row In rows.EnumerateArray()
                    snapshot.Title = LyrionMediaServerService.TextOf(row, "title")
                    snapshot.Artist = LyrionMediaServerService.TextOf(row, "artist")
                    snapshot.Album = LyrionMediaServerService.TextOf(row, "album")
                    snapshot.AlbumId = LyrionMediaServerService.TextOf(row, "album_id")
                    snapshot.ArtworkTrackId = LyrionMediaServerService.FirstTextOf(row, "artwork_track_id", "id")
                    If snapshot.DurationSeconds <= 0 Then snapshot.DurationSeconds = NumberOf(row, "duration")
                    Exit For
                Next
            End If
            Return snapshot
        End Function

        Private Shared Function NumberOf(element As JsonElement, name As String) As Double
            Dim value = LyrionMediaServerService.TextOf(element, name)
            Dim parsed As Double
            If Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, parsed) Then Return parsed
            Return 0
        End Function

#End Region

#Region "Befehle"

        ''' <summary>Schickt einen Befehl an das gewaehlte Geraet und fragt gleich darauf den Stand
        ''' ab - sonst stuende die Anzeige bis zur naechsten Abfrage auf dem alten Wert, und ein
        ''' Druck auf Pause saehe folgenlos aus.</summary>
        Private Shared Async Function SendAsync(command As String(), cancellationToken As CancellationToken) As Task
            Dim playerId = SelectedPlayerId
            If String.IsNullOrWhiteSpace(playerId) Then Return
            Await LyrionMediaServerService.RequestAsync(playerId, command, cancellationToken)
            Await RefreshAsync(cancellationToken)
        End Function

        Public Shared Async Function RefreshAsync(cancellationToken As CancellationToken) As Task
            Dim snapshot = Await ReadStatusAsync(cancellationToken)
            SyncLock Gate
                _status = snapshot
            End SyncLock
            RaiseEvent StatusChanged(Nothing, EventArgs.Empty)
        End Function

        ''' <summary>Wiedergabe und Pause. Aus dem Halt heraus ist "pause" wirkungslos - dann muss
        ''' "play" kommen, sonst bliebe der Knopf ohne Wirkung.</summary>
        Public Shared Async Function PlayPauseAsync(cancellationToken As CancellationToken) As Task
            Dim current = Status
            If current IsNot Nothing AndAlso current.IsStopped Then
                Await SendAsync({"play"}, cancellationToken)
            Else
                Await SendAsync({"pause"}, cancellationToken)
            End If
        End Function

        Public Shared Function StopAsync(cancellationToken As CancellationToken) As Task
            Return SendAsync({"stop"}, cancellationToken)
        End Function

        Public Shared Function NextAsync(cancellationToken As CancellationToken) As Task
            Return SendAsync({"playlist", "index", "+1"}, cancellationToken)
        End Function

        Public Shared Function PreviousAsync(cancellationToken As CancellationToken) As Task
            Return SendAsync({"playlist", "index", "-1"}, cancellationToken)
        End Function

        Public Shared Function SeekAsync(seconds As Double, cancellationToken As CancellationToken) As Task
            Return SendAsync({"time", Math.Max(0, seconds).ToString("0.###", CultureInfo.InvariantCulture)}, cancellationToken)
        End Function

        Public Shared Function SetVolumeAsync(percent As Double, cancellationToken As CancellationToken) As Task
            Return SendAsync({"mixer", "volume", CInt(Math.Clamp(percent, 0, 100)).ToString(CultureInfo.InvariantCulture)}, cancellationToken)
        End Function

        Public Shared Function SetMutedAsync(muted As Boolean, cancellationToken As CancellationToken) As Task
            Return SendAsync({"mixer", "muting", If(muted, "1", "0")}, cancellationToken)
        End Function

        ''' <summary>Zufall: 0 aus, 1 nach Titeln, 2 nach Alben. FerrumKix kennt nur an und aus
        ''' und meint damit die Titel.</summary>
        Public Shared Function SetShuffleAsync(shuffle As Boolean, cancellationToken As CancellationToken) As Task
            Return SendAsync({"playlist", "shuffle", If(shuffle, "1", "0")}, cancellationToken)
        End Function

        ''' <summary>Wiederholen: 0 aus, 1 ein Titel, 2 die ganze Liste.</summary>
        Public Shared Function SetRepeatAsync(mode As Integer, cancellationToken As CancellationToken) As Task
            Return SendAsync({"playlist", "repeat", Math.Clamp(mode, 0, 2).ToString(CultureInfo.InvariantCulture)}, cancellationToken)
        End Function

        ''' <summary>Laedt ein Album auf das Geraet und springt auf den gewaehlten Titel.
        ''' <c>cmd:load</c> ersetzt die Liste des Geraets und startet - genau das, was ein
        ''' Doppelklick auf einen Titel bedeutet.</summary>
        Public Shared Async Function PlayAlbumAsync(albumId As String, trackIndex As Integer, cancellationToken As CancellationToken) As Task
            If String.IsNullOrWhiteSpace(albumId) Then Return
            Dim playerId = SelectedPlayerId
            If String.IsNullOrWhiteSpace(playerId) Then Return
            Await LyrionMediaServerService.RequestAsync(playerId, {"playlistcontrol", "cmd:load", "album_id:" & albumId}, cancellationToken)
            If trackIndex > 0 Then
                Await LyrionMediaServerService.RequestAsync(playerId, {"playlist", "index", trackIndex.ToString(CultureInfo.InvariantCulture)}, cancellationToken)
            End If
            Await RefreshAsync(cancellationToken)
        End Function

        ''' <summary>Spielt einen einzelnen Titel auf dem Geraet.</summary>
        Public Shared Async Function PlayTrackAsync(trackId As String, cancellationToken As CancellationToken) As Task
            If String.IsNullOrWhiteSpace(trackId) Then Return
            Await SendAsync({"playlistcontrol", "cmd:load", "track_id:" & trackId}, cancellationToken)
        End Function

#End Region

    End Class

End Namespace
