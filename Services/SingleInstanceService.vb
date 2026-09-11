Imports System
Imports System.Collections.Generic

Namespace Services

    ''' <summary>Sorgt dafuer, dass FerrumPlay nur einmal laeuft.
    '''
    ''' <para>Der Weg ist der uebliche unter Linux: die erste Instanz haelt den Namen
    ''' <see cref="BusName"/> auf dem Sitzungsbus. Jeder weitere Aufruf findet ihn belegt, reicht
    ''' seine Dateien und Ordner ueber <c>Open</c> an die laufende Instanz weiter und beendet sich,
    ''' noch bevor Avalonia hochfaehrt - es blitzt kein zweites Fenster auf. Die laufende Instanz holt
    ''' ihr Fenster nach vorn und spielt den ersten uebergebenen Titel. Ein Aufruf ohne Pfade holt nur
    ''' das Fenster nach vorn.</para>
    '''
    ''' <para>Den Namen vergibt der Bus atomar, und er gibt ihn frei, sobald der Prozess endet - auch
    ''' nach einem Absturz. Eine Sperrdatei, die liegen bleiben koennte, gibt es nicht.</para>
    '''
    ''' <para>Ohne Sitzungsbus (Windows, macOS, eine nackte Konsole) laeuft jede Instanz fuer sich,
    ''' wie bisher.</para></summary>
    Friend NotInheritable Class SingleInstanceService
        Private Sub New()
        End Sub

        Public Const BusName As String = "io.github.Bitpainter75.FerrumPlay"
        Private Const ObjectPath As String = "/io/github/Bitpainter75/FerrumPlay"
        Private Const InterfaceName As String = "io.github.Bitpainter75.FerrumPlay"

        Private Const IntrospectionXml As String =
"<node>
  <interface name='org.freedesktop.DBus.Introspectable'>
    <method name='Introspect'><arg name='xml_data' type='s' direction='out'/></method>
  </interface>
  <interface name='org.freedesktop.DBus.Peer'>
    <method name='Ping'/>
    <method name='GetMachineId'><arg name='machine_uuid' type='s' direction='out'/></method>
  </interface>
  <interface name='io.github.Bitpainter75.FerrumPlay'>
    <method name='Open'><arg name='paths' type='as' direction='in'/></method>
  </interface>
</node>"

        Private Shared ReadOnly Gate As New Object()
        Private Shared ReadOnly Pending As New List(Of List(Of String))()
        Private Shared _receiver As Action(Of List(Of String))
        Private Shared _connection As DBusSessionConnection

        ''' <summary>Die Verbindung zum Sitzungsbus, sofern es einen gibt. MPRIS lebt auf derselben.</summary>
        Public Shared ReadOnly Property Connection As DBusSessionConnection
            Get
                SyncLock Gate
                    Return _connection
                End SyncLock
            End Get
        End Property

        ''' <summary>Klaert beim Start, wer die Instanz ist. True heisst: eine andere laeuft schon
        ''' und hat die Pfade bekommen - dieser Prozess ist fertig. False heisst: weitermachen,
        ''' entweder als die eine Instanz oder, ohne Bus, einfach fuer sich.</summary>
        ''' <param name="paths">Die Pfade aus dem Aufruf, bereits absolut: die laufende Instanz hat
        ''' einen anderen aktuellen Ordner.</param>
        Public Shared Function ClaimOrHandOver(paths As IEnumerable(Of String)) As Boolean
            If Not OperatingSystem.IsLinux() AndAlso Not OperatingSystem.IsFreeBSD() Then Return False

            Dim connection As DBusSessionConnection = Nothing
            Try
                connection = DBusSessionConnection.ConnectSession()
                If connection Is Nothing Then
                    DiagnosticLogService.Log("SingleInstance", "Kein Sitzungsbus gefunden.")
                    Return False
                End If

                ' Das Objekt steht, BEVOR der Name beantragt wird. Startet ein zweiter Aufruf im
                ' selben Augenblick, kommt sein Open womoeglich an, noch ehe diese Zeile hier
                ' zurueckkehrt; er landet dann in der Warteschlange und nicht im Leeren.
                Dim owner = connection
                connection.RegisterObject(ObjectPath, Sub(message) HandleCall(owner, message))

                If connection.RequestName(BusName) Then
                    SyncLock Gate
                        _connection = connection
                    End SyncLock
                    Return False
                End If

                Dim body As New DBusWriter()
                body.WriteStringArray(paths)
                connection.CallMethod(BusName, ObjectPath, InterfaceName, "Open", "as", body.ToArray())
                connection.Dispose()
                Return True
            Catch ex As Exception
                ' Die Uebergabe ist gescheitert, etwa weil die laufende Instanz haengt. Dann startet
                ' diese hier eben selbst; die Verbindung bleibt fuer MPRIS, solange sie steht.
                DiagnosticLogService.Log("SingleInstance", $"Keine Uebergabe an eine laufende Instanz: {ex.Message}")
                If connection IsNot Nothing AndAlso connection.IsOpen Then
                    SyncLock Gate
                        _connection = connection
                    End SyncLock
                Else
                    connection?.Dispose()
                End If
                Return False
            End Try
        End Function

        ''' <summary>Meldet den Empfaenger fuer uebergebene Pfade an. Was schon angekommen ist,
        ''' bevor die Oberflaeche stand, wird sofort nachgereicht. Der Empfaenger wird auf dem
        ''' Lesefaden des Busses gerufen und muss selbst auf den Anzeigefaden wechseln.</summary>
        Public Shared Sub AttachReceiver(receiver As Action(Of List(Of String)))
            Dim queued As List(Of List(Of String))
            SyncLock Gate
                _receiver = receiver
                queued = New List(Of List(Of String))(Pending)
                Pending.Clear()
            End SyncLock
            If receiver Is Nothing Then Return
            For Each paths In queued
                receiver(paths)
            Next
        End Sub

        Public Shared Sub Shutdown()
            Dim connection As DBusSessionConnection
            SyncLock Gate
                connection = _connection
                _connection = Nothing
                _receiver = Nothing
            End SyncLock
            connection?.Dispose()
        End Sub

        Private Shared Sub HandleCall(connection As DBusSessionConnection, message As DBusMessage)
            If message.Member = "Introspect" Then
                Dim body As New DBusWriter()
                body.WriteString(IntrospectionXml)
                connection.SendReply(message, "s", body.ToArray())
                Return
            End If

            If message.Member <> "Open" OrElse
               (message.InterfaceName.Length > 0 AndAlso message.InterfaceName <> InterfaceName) Then
                connection.SendError(message, "org.freedesktop.DBus.Error.UnknownMethod",
                                     $"Unbekannte Methode {message.InterfaceName}.{message.Member}.")
                Return
            End If

            If message.Signature <> "as" Then
                connection.SendError(message, "org.freedesktop.DBus.Error.InvalidArgs", "Open erwartet eine Liste von Pfaden (as).")
                Return
            End If

            Dim paths = message.CreateBodyReader().ReadStringArray()
            ' Erst antworten, dann ausliefern: der Aufrufer wartet nur darauf, sich beenden zu duerfen.
            connection.SendReply(message, String.Empty, Nothing)
            Deliver(paths)
        End Sub

        Private Shared Sub Deliver(paths As List(Of String))
            Dim receiver As Action(Of List(Of String))
            SyncLock Gate
                receiver = _receiver
                If receiver Is Nothing Then
                    Pending.Add(paths)
                    Return
                End If
            End SyncLock
            receiver(paths)
        End Sub

    End Class

End Namespace
