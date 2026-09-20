Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

Namespace Services

    ''' <summary>Was ueber MPRIS nach aussen geht, als Schnappschuss. Die Oberflaeche baut ihn bei
    ''' jeder Aenderung neu; der Dienst vergleicht mit dem vorigen und meldet nur, was sich
    ''' tatsaechlich geaendert hat.</summary>
    Friend NotInheritable Class MprisState
        ''' <summary>"Playing", "Paused" oder "Stopped".</summary>
        Public Property PlaybackStatus As String = "Stopped"
        ''' <summary>"None", "Track" oder "Playlist".</summary>
        Public Property LoopStatus As String = "None"
        Public Property Shuffle As Boolean
        ''' <summary>0 bis 1.</summary>
        Public Property Volume As Double
        Public Property CanGoNext As Boolean
        Public Property CanGoPrevious As Boolean
        Public Property CanPlay As Boolean
        Public Property CanPause As Boolean
        Public Property CanSeek As Boolean

        ''' <summary>Der Objektpfad des Titels. Nothing heisst: kein Titel.</summary>
        Public Property TrackId As String
        Public Property LengthSeconds As Double
        Public Property Title As String = String.Empty
        Public Property Album As String = String.Empty
        Public Property Artist As String = String.Empty
        Public Property AlbumArtist As String = String.Empty
        Public Property Genre As String = String.Empty
        Public Property TrackNumber As Integer
        Public Property DiscNumber As Integer
        Public Property Url As String = String.Empty
        Public Property ArtUrl As String = String.Empty
    End Class

    ''' <summary>Was MPRIS von der Anwendung verlangen kann. Die Aufrufe kommen auf dem Lesefaden
    ''' des Busses; wer das umsetzt, wechselt selbst auf den Anzeigefaden.</summary>
    Friend Interface IMprisPlayer
        Sub Raise()
        Sub Quit()
        Sub PlayPause()
        Sub Play()
        Sub Pause()
        Sub [Stop]()
        Sub [Next]()
        Sub Previous()
        Sub SeekBy(offsetSeconds As Double)
        Sub SetPosition(trackId As String, seconds As Double)
        Sub OpenUri(uri As String)
        Sub SetVolume(value As Double)
        Sub SetShuffle(value As Boolean)
        Sub SetLoopStatus(value As String)
    End Interface

    ''' <summary>FerrumKix als MPRIS-Spieler (org.mpris.MediaPlayer2).
    '''
    ''' <para>Darueber finden Waybar, playerctl, die Benachrichtigungen und die Multimedia-Tasten
    ''' des Systems die Anwendung - auch dann, wenn ihr Fenster nicht den Eingabefokus hat. Die
    ''' Schnittstelle ist die der Spezifikation 2.2 ohne Titelliste (HasTrackList = False): die
    ''' Wiedergabeliste bleibt in der Anwendung.</para>
    '''
    ''' <para>Die Eigenschaften kommen aus einem Schnappschuss (<see cref="MprisState"/>), den die
    ''' Oberflaeche mit <see cref="Publish"/> liefert. Position ist die Ausnahme: sie aendert sich
    ''' laufend, wird deshalb nur mitgeschrieben (<see cref="UpdatePosition"/>) und laut
    ''' Spezifikation NICHT als Aenderung gemeldet. Wer sie braucht, fragt nach; ein Sprung wird
    ''' mit dem Signal Seeked angesagt.</para></summary>
    Friend NotInheritable Class MprisService
        Implements IDisposable

        Private Const BusName As String = "org.mpris.MediaPlayer2.FerrumKix"
        Private Const ObjectPath As String = "/org/mpris/MediaPlayer2"
        Private Const RootInterface As String = "org.mpris.MediaPlayer2"
        Private Const PlayerInterface As String = "org.mpris.MediaPlayer2.Player"
        Private Const PropertiesInterface As String = "org.freedesktop.DBus.Properties"
        Private Const IntrospectableInterface As String = "org.freedesktop.DBus.Introspectable"
        Private Const PeerInterface As String = "org.freedesktop.DBus.Peer"
        Private Const DesktopEntry As String = "io.github.Bitpainter75.FerrumKix"

        ''' <summary>Der Pfad, den die Spezifikation fuer "kein Titel" vorsieht.</summary>
        Private Const NoTrackId As String = "/org/mpris/MediaPlayer2/TrackList/NoTrack"

        Private Const ErrorUnknownMethod As String = "org.freedesktop.DBus.Error.UnknownMethod"
        Private Const ErrorUnknownInterface As String = "org.freedesktop.DBus.Error.UnknownInterface"
        Private Const ErrorUnknownProperty As String = "org.freedesktop.DBus.Error.UnknownProperty"
        Private Const ErrorPropertyReadOnly As String = "org.freedesktop.DBus.Error.PropertyReadOnly"
        Private Const ErrorInvalidArgs As String = "org.freedesktop.DBus.Error.InvalidArgs"

        Private Shared ReadOnly SupportedMimeTypes As String() = {
            "audio/mpeg", "audio/flac", "audio/x-flac", "audio/ogg", "audio/opus", "audio/mp4",
            "audio/aac", "audio/x-m4a", "audio/wav", "audio/x-wav", "audio/x-wavpack", "audio/x-ape",
            "audio/x-ms-wma", "audio/x-musepack", "audio/aiff", "audio/x-aiff", "audio/x-dsf"
        }

        ''' <summary>Zu welcher Schnittstelle eine Methode gehoert. Ein Aufruf darf die
        ''' Schnittstelle weglassen; gibt er eine an, muss sie stimmen.</summary>
        Private Shared ReadOnly MemberInterfaces As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
            {"Introspect", IntrospectableInterface},
            {"Get", PropertiesInterface}, {"GetAll", PropertiesInterface}, {"Set", PropertiesInterface},
            {"Raise", RootInterface}, {"Quit", RootInterface},
            {"Next", PlayerInterface}, {"Previous", PlayerInterface}, {"Pause", PlayerInterface},
            {"PlayPause", PlayerInterface}, {"Stop", PlayerInterface}, {"Play", PlayerInterface},
            {"Seek", PlayerInterface}, {"SetPosition", PlayerInterface}, {"OpenUri", PlayerInterface}
        }

        Private ReadOnly _player As IMprisPlayer
        Private ReadOnly _gate As New Object()
        Private _connection As DBusSessionConnection
        Private _state As New MprisState()

        ''' <summary>Was die Gegenseite zuletzt gesehen hat, ohne Position. Daran misst
        ''' <see cref="Publish"/>, was als geaendert gemeldet wird.</summary>
        Private _published As Dictionary(Of String, DBusVariant)

        Private _positionMicros As Long
        Private _disposed As Boolean

        Public Sub New(player As IMprisPlayer)
            _player = player
        End Sub

        ''' <summary>Stellt das Objekt auf die Verbindung und beantragt den Namen. Ist der schon
        ''' vergeben - etwa, weil eine zweite Instanz ausnahmsweise doch laeuft -, kommt die
        ''' Prozessnummer dazu, wie es die Spezifikation vorsieht.</summary>
        Public Sub Start(connection As DBusSessionConnection)
            If connection Is Nothing OrElse Not connection.IsOpen Then Return

            SyncLock _gate
                If _disposed Then Return
                _connection = connection
                _published = BuildPlayerProperties(_state, includePosition:=False)
            End SyncLock
            connection.RegisterObject(ObjectPath, Sub(message) HandleCall(connection, message))

            ' Das Beantragen wartet auf den Bus und gehoert deshalb nicht auf den Anzeigefaden.
            Task.Run(Sub() ClaimName(connection))
        End Sub

        Private Shared Sub ClaimName(connection As DBusSessionConnection)
            Try
                For Each candidate In {BusName, $"{BusName}.instance{Environment.ProcessId}"}
                    If connection.RequestName(candidate) Then
                        DiagnosticLogService.Log("Mpris", $"Angemeldet als {candidate}.")
                        Return
                    End If
                Next
                DiagnosticLogService.Log("Mpris", "Kein MPRIS-Name zu bekommen.")
            Catch ex As Exception
                DiagnosticLogService.Log("Mpris", $"MPRIS steht nicht zur Verfuegung: {ex.Message}")
            End Try
        End Sub

        ''' <summary>Uebernimmt einen neuen Schnappschuss und meldet, was sich gegenueber dem
        ''' vorigen geaendert hat. Vom Anzeigefaden aus; kostet nichts, wenn sich nichts
        ''' geaendert hat.</summary>
        Public Sub Publish(state As MprisState)
            If state Is Nothing Then Return

            Dim connection As DBusSessionConnection
            Dim changed As New Dictionary(Of String, DBusVariant)(StringComparer.Ordinal)
            SyncLock _gate
                _state = state
                connection = _connection
                If connection Is Nothing Then Return

                Dim current = BuildPlayerProperties(state, includePosition:=False)
                For Each entry In current
                    Dim before As DBusVariant = Nothing
                    If _published Is Nothing OrElse Not _published.TryGetValue(entry.Key, before) OrElse
                       Not entry.Value.SameAs(before) Then changed(entry.Key) = entry.Value
                Next
                _published = current
            End SyncLock

            If changed.Count = 0 Then Return
            Dim body As New DBusWriter()
            body.WriteString(PlayerInterface)
            body.WriteDictionary(changed)
            body.WriteStringArray(Array.Empty(Of String)())
            connection.SendSignal(ObjectPath, PropertiesInterface, "PropertiesChanged", "sa{sv}as", body.ToArray())
        End Sub

        ''' <summary>Die laufende Stelle. Von jedem Faden aus, so oft wie noetig.</summary>
        Public Sub UpdatePosition(seconds As Double)
            Interlocked.Exchange(_positionMicros, ToMicros(seconds))
        End Sub

        ''' <summary>Ein Sprung im Titel. Die Spezifikation verlangt dafuer das Signal Seeked,
        ''' weil Position selbst nicht als Aenderung gemeldet wird.</summary>
        Public Sub NotifySeeked(seconds As Double)
            UpdatePosition(seconds)
            Dim connection As DBusSessionConnection
            SyncLock _gate
                connection = _connection
            End SyncLock
            If connection Is Nothing Then Return

            Dim body As New DBusWriter()
            body.WriteInt64(ToMicros(seconds))
            connection.SendSignal(ObjectPath, PlayerInterface, "Seeked", "x", body.ToArray())
        End Sub

        ''' <summary>Die Verbindung gehoert nicht dem Dienst, sondern der Anwendung
        ''' (<see cref="SingleInstanceService"/>). Hier wird nur aufgehoert, sie zu benutzen.</summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            SyncLock _gate
                _disposed = True
                _connection = Nothing
            End SyncLock
        End Sub

        ' Aufrufe von aussen, auf dem Lesefaden des Busses

        Private Sub HandleCall(connection As DBusSessionConnection, message As DBusMessage)
            Try
                If Not InterfaceMatches(message) Then
                    connection.SendError(message, ErrorUnknownMethod, $"Unbekannte Methode {message.InterfaceName}.{message.Member}.")
                    Return
                End If

                Select Case message.Member
                    Case "Introspect"
                        Dim body As New DBusWriter()
                        body.WriteString(IntrospectionXml)
                        connection.SendReply(message, "s", body.ToArray())
                        Return
                    Case "Get" : HandleGet(connection, message) : Return
                    Case "GetAll" : HandleGetAll(connection, message) : Return
                    Case "Set" : HandleSet(connection, message) : Return
                    Case "Raise" : _player.Raise()
                    Case "Quit" : _player.Quit()
                    Case "Next" : _player.Next()
                    Case "Previous" : _player.Previous()
                    Case "Pause" : _player.Pause()
                    Case "PlayPause" : _player.PlayPause()
                    Case "Stop" : _player.Stop()
                    Case "Play" : _player.Play()
                    Case "Seek"
                        RequireSignature(message, "x")
                        _player.SeekBy(message.CreateBodyReader().ReadInt64() / 1000000.0)
                    Case "SetPosition"
                        RequireSignature(message, "ox")
                        Dim reader = message.CreateBodyReader()
                        Dim trackId = reader.ReadObjectPath()
                        Dim position = reader.ReadInt64()
                        _player.SetPosition(trackId, position / 1000000.0)
                    Case "OpenUri"
                        RequireSignature(message, "s")
                        _player.OpenUri(message.CreateBodyReader().ReadString())
                End Select
                connection.SendReply(message, String.Empty, Nothing)
            Catch ex As Exception
                connection.SendError(message, ErrorInvalidArgs, ex.Message)
            End Try
        End Sub

        Private Shared Function InterfaceMatches(message As DBusMessage) As Boolean
            Dim expected As String = Nothing
            If Not MemberInterfaces.TryGetValue(message.Member, expected) Then Return False
            Return message.InterfaceName.Length = 0 OrElse message.InterfaceName = expected
        End Function

        Private Shared Sub RequireSignature(message As DBusMessage, expected As String)
            If message.Signature <> expected Then
                Throw New InvalidDataException($"{message.Member} erwartet ({expected}), bekommen ({message.Signature}).")
            End If
        End Sub

        Private Sub HandleGet(connection As DBusSessionConnection, message As DBusMessage)
            RequireSignature(message, "ss")
            Dim reader = message.CreateBodyReader()
            Dim interfaceName = reader.ReadString()
            Dim name = reader.ReadString()

            Dim properties = PropertiesOf(interfaceName)
            If properties Is Nothing Then
                connection.SendError(message, ErrorUnknownInterface, $"Unbekannte Schnittstelle {interfaceName}.")
                Return
            End If
            Dim value As DBusVariant = Nothing
            If Not properties.TryGetValue(name, value) Then
                connection.SendError(message, ErrorUnknownProperty, $"Unbekannte Eigenschaft {interfaceName}.{name}.")
                Return
            End If

            Dim body As New DBusWriter()
            body.WriteVariant(value)
            connection.SendReply(message, "v", body.ToArray())
        End Sub

        Private Sub HandleGetAll(connection As DBusSessionConnection, message As DBusMessage)
            RequireSignature(message, "s")
            Dim interfaceName = message.CreateBodyReader().ReadString()
            Dim properties = PropertiesOf(interfaceName)
            If properties Is Nothing Then
                connection.SendError(message, ErrorUnknownInterface, $"Unbekannte Schnittstelle {interfaceName}.")
                Return
            End If

            Dim body As New DBusWriter()
            body.WriteDictionary(properties)
            connection.SendReply(message, "a{sv}", body.ToArray())
        End Sub

        ''' <summary>Die beschreibbaren Eigenschaften: Lautstaerke, Zufall, Wiederholen. Rate und
        ''' Fullscreen werden angenommen und nicht umgesetzt - die Spezifikation erlaubt das, weil
        ''' MinimumRate = MaximumRate = 1 und CanSetFullscreen = False gemeldet werden.</summary>
        Private Sub HandleSet(connection As DBusSessionConnection, message As DBusMessage)
            RequireSignature(message, "ssv")
            Dim reader = message.CreateBodyReader()
            Dim interfaceName = reader.ReadString()
            Dim name = reader.ReadString()
            Dim value = reader.ReadVariant()

            Dim properties = PropertiesOf(interfaceName)
            If properties Is Nothing Then
                connection.SendError(message, ErrorUnknownInterface, $"Unbekannte Schnittstelle {interfaceName}.")
                Return
            End If
            If Not properties.ContainsKey(name) Then
                connection.SendError(message, ErrorUnknownProperty, $"Unbekannte Eigenschaft {interfaceName}.{name}.")
                Return
            End If

            Dim accepted = True
            If interfaceName = PlayerInterface Then
                Select Case name
                    Case "Volume" : _player.SetVolume(Convert.ToDouble(value.Value, CultureInfo.InvariantCulture))
                    Case "Shuffle" : _player.SetShuffle(Convert.ToBoolean(value.Value, CultureInfo.InvariantCulture))
                    Case "LoopStatus" : _player.SetLoopStatus(Convert.ToString(value.Value, CultureInfo.InvariantCulture))
                    Case "Rate"
                    Case Else : accepted = False
                End Select
            ElseIf interfaceName = RootInterface Then
                accepted = name = "Fullscreen"
            Else
                accepted = False
            End If

            If Not accepted Then
                connection.SendError(message, ErrorPropertyReadOnly, $"{interfaceName}.{name} ist nur lesbar.")
                Return
            End If
            connection.SendReply(message, String.Empty, Nothing)
        End Sub

        Private Function PropertiesOf(interfaceName As String) As Dictionary(Of String, DBusVariant)
            Select Case interfaceName
                Case RootInterface
                    Return BuildRootProperties()
                Case PlayerInterface
                    SyncLock _gate
                        Return BuildPlayerProperties(_state, includePosition:=True)
                    End SyncLock
                Case PropertiesInterface, IntrospectableInterface, PeerInterface
                    Return New Dictionary(Of String, DBusVariant)(StringComparer.Ordinal)
                Case Else
                    Return Nothing
            End Select
        End Function

        ' Die Eigenschaften

        Private Shared Function BuildRootProperties() As Dictionary(Of String, DBusVariant)
            Return New Dictionary(Of String, DBusVariant)(StringComparer.Ordinal) From {
                {"CanQuit", DBusVariant.FromBoolean(True)},
                {"Fullscreen", DBusVariant.FromBoolean(False)},
                {"CanSetFullscreen", DBusVariant.FromBoolean(False)},
                {"CanRaise", DBusVariant.FromBoolean(True)},
                {"HasTrackList", DBusVariant.FromBoolean(False)},
                {"Identity", DBusVariant.FromString("FerrumKix")},
                {"DesktopEntry", DBusVariant.FromString(DesktopEntry)},
                {"SupportedUriSchemes", DBusVariant.FromStrings({"file"})},
                {"SupportedMimeTypes", DBusVariant.FromStrings(SupportedMimeTypes)}
            }
        End Function

        Private Function BuildPlayerProperties(state As MprisState, includePosition As Boolean) As Dictionary(Of String, DBusVariant)
            Dim properties As New Dictionary(Of String, DBusVariant)(StringComparer.Ordinal) From {
                {"PlaybackStatus", DBusVariant.FromString(state.PlaybackStatus)},
                {"LoopStatus", DBusVariant.FromString(state.LoopStatus)},
                {"Rate", DBusVariant.FromDouble(1.0)},
                {"Shuffle", DBusVariant.FromBoolean(state.Shuffle)},
                {"Metadata", DBusVariant.FromDictionary(BuildMetadata(state))},
                {"Volume", DBusVariant.FromDouble(Math.Clamp(state.Volume, 0.0, 1.0))},
                {"MinimumRate", DBusVariant.FromDouble(1.0)},
                {"MaximumRate", DBusVariant.FromDouble(1.0)},
                {"CanGoNext", DBusVariant.FromBoolean(state.CanGoNext)},
                {"CanGoPrevious", DBusVariant.FromBoolean(state.CanGoPrevious)},
                {"CanPlay", DBusVariant.FromBoolean(state.CanPlay)},
                {"CanPause", DBusVariant.FromBoolean(state.CanPause)},
                {"CanSeek", DBusVariant.FromBoolean(state.CanSeek)},
                {"CanControl", DBusVariant.FromBoolean(True)}
            }
            If includePosition Then properties("Position") = DBusVariant.FromInt64(Interlocked.Read(_positionMicros))
            Return properties
        End Function

        ''' <summary>Die Angaben zum Titel in den Namen, die MPRIS und xesam vorgeben. Leere Angaben
        ''' fallen weg, statt als leere Zeichenkette dazustehen: Waybar und Co. zeigen sonst
        ''' Trennzeichen ohne etwas dazwischen.</summary>
        Private Shared Function BuildMetadata(state As MprisState) As Dictionary(Of String, DBusVariant)
            Dim metadata As New Dictionary(Of String, DBusVariant)(StringComparer.Ordinal)
            If String.IsNullOrEmpty(state.TrackId) Then
                metadata("mpris:trackid") = DBusVariant.FromObjectPath(NoTrackId)
                Return metadata
            End If

            metadata("mpris:trackid") = DBusVariant.FromObjectPath(state.TrackId)
            If state.LengthSeconds > 0 Then metadata("mpris:length") = DBusVariant.FromInt64(ToMicros(state.LengthSeconds))
            AddText(metadata, "mpris:artUrl", state.ArtUrl)
            AddText(metadata, "xesam:title", state.Title)
            AddText(metadata, "xesam:album", state.Album)
            AddList(metadata, "xesam:artist", state.Artist)
            AddList(metadata, "xesam:albumArtist", state.AlbumArtist)
            AddList(metadata, "xesam:genre", state.Genre)
            If state.TrackNumber > 0 Then metadata("xesam:trackNumber") = DBusVariant.FromInt32(state.TrackNumber)
            If state.DiscNumber > 0 Then metadata("xesam:discNumber") = DBusVariant.FromInt32(state.DiscNumber)
            AddText(metadata, "xesam:url", state.Url)
            Return metadata
        End Function

        Private Shared Sub AddText(metadata As Dictionary(Of String, DBusVariant), key As String, value As String)
            If Not String.IsNullOrWhiteSpace(value) Then metadata(key) = DBusVariant.FromString(value)
        End Sub

        ''' <summary>xesam fuehrt Interpreten und Genre als Liste. Die Kennzeichen sind beim Lesen
        ''' schon zu einer Zeile verbunden worden; sie wieder am Komma aufzutrennen, zerlegte
        ''' "Crosby, Stills &amp; Nash". Es bleibt deshalb EIN Eintrag.</summary>
        Private Shared Sub AddList(metadata As Dictionary(Of String, DBusVariant), key As String, value As String)
            If Not String.IsNullOrWhiteSpace(value) Then metadata(key) = DBusVariant.FromStrings({value})
        End Sub

        Private Shared Function ToMicros(seconds As Double) As Long
            If Double.IsNaN(seconds) OrElse Double.IsInfinity(seconds) OrElse seconds <= 0 Then Return 0
            Return CLng(Math.Min(seconds, 1.0E+12) * 1000000.0)
        End Function

        ' Die Selbstbeschreibung. Einfache Anfuehrungszeichen, damit sie in VB ohne Verdoppeln
        ' dastehen; XML erlaubt beide.
        Private Const IntrospectionXml As String =
"<node>
  <interface name='org.freedesktop.DBus.Introspectable'>
    <method name='Introspect'><arg name='xml_data' type='s' direction='out'/></method>
  </interface>
  <interface name='org.freedesktop.DBus.Peer'>
    <method name='Ping'/>
    <method name='GetMachineId'><arg name='machine_uuid' type='s' direction='out'/></method>
  </interface>
  <interface name='org.freedesktop.DBus.Properties'>
    <method name='Get'>
      <arg name='interface_name' type='s' direction='in'/>
      <arg name='property_name' type='s' direction='in'/>
      <arg name='value' type='v' direction='out'/>
    </method>
    <method name='GetAll'>
      <arg name='interface_name' type='s' direction='in'/>
      <arg name='properties' type='a{sv}' direction='out'/>
    </method>
    <method name='Set'>
      <arg name='interface_name' type='s' direction='in'/>
      <arg name='property_name' type='s' direction='in'/>
      <arg name='value' type='v' direction='in'/>
    </method>
    <signal name='PropertiesChanged'>
      <arg name='interface_name' type='s'/>
      <arg name='changed_properties' type='a{sv}'/>
      <arg name='invalidated_properties' type='as'/>
    </signal>
  </interface>
  <interface name='org.mpris.MediaPlayer2'>
    <method name='Raise'/>
    <method name='Quit'/>
    <property name='CanQuit' type='b' access='read'/>
    <property name='Fullscreen' type='b' access='readwrite'/>
    <property name='CanSetFullscreen' type='b' access='read'/>
    <property name='CanRaise' type='b' access='read'/>
    <property name='HasTrackList' type='b' access='read'/>
    <property name='Identity' type='s' access='read'/>
    <property name='DesktopEntry' type='s' access='read'/>
    <property name='SupportedUriSchemes' type='as' access='read'/>
    <property name='SupportedMimeTypes' type='as' access='read'/>
  </interface>
  <interface name='org.mpris.MediaPlayer2.Player'>
    <method name='Next'/>
    <method name='Previous'/>
    <method name='Pause'/>
    <method name='PlayPause'/>
    <method name='Stop'/>
    <method name='Play'/>
    <method name='Seek'><arg name='Offset' type='x' direction='in'/></method>
    <method name='SetPosition'>
      <arg name='TrackId' type='o' direction='in'/>
      <arg name='Position' type='x' direction='in'/>
    </method>
    <method name='OpenUri'><arg name='Uri' type='s' direction='in'/></method>
    <signal name='Seeked'><arg name='Position' type='x'/></signal>
    <property name='PlaybackStatus' type='s' access='read'/>
    <property name='LoopStatus' type='s' access='readwrite'/>
    <property name='Rate' type='d' access='readwrite'/>
    <property name='Shuffle' type='b' access='readwrite'/>
    <property name='Metadata' type='a{sv}' access='read'/>
    <property name='Volume' type='d' access='readwrite'/>
    <property name='Position' type='x' access='read'/>
    <property name='MinimumRate' type='d' access='read'/>
    <property name='MaximumRate' type='d' access='read'/>
    <property name='CanGoNext' type='b' access='read'/>
    <property name='CanGoPrevious' type='b' access='read'/>
    <property name='CanPlay' type='b' access='read'/>
    <property name='CanPause' type='b' access='read'/>
    <property name='CanSeek' type='b' access='read'/>
    <property name='CanControl' type='b' access='read'/>
  </interface>
</node>"

    End Class

End Namespace
