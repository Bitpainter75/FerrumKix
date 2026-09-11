Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Namespace Services

    ' EIN EIGENER, KLEINER D-BUS-CLIENT. Die naheliegende Bibliothek, Tmds.DBus.Protocol, kommt mit
    ' Avalonia ohnehin mit, ist aus VB aber nicht benutzbar: ihr Leser und ihr Schreiber sind
    ' "ref structs", und die kennt der VB-Compiler nicht (er meldet sie als veraltet und bricht ab).
    ' Fuer MPRIS braucht es nur einen schmalen Ausschnitt des Protokolls - Anmelden, Methoden
    ' beantworten, Signale senden -, und der steht hier. Siehe Audits/FALLEN_UND_ENTSCHEIDUNGEN.md.
    '
    ' Ausgerichtet wird in D-Bus immer ab dem Anfang der Nachricht. Der Rumpf beginnt dort auf einer
    ' durch 8 teilbaren Stelle; ein Rumpf, der fuer sich ab 0 geschrieben wird, ist damit richtig
    ' ausgerichtet. Davon leben DBusWriter und DBusReader.

    ''' <summary>Ein Wert samt seiner D-Bus-Signatur. Nur die Formen, die MPRIS braucht: die
    ''' Grundtypen, Listen von Zeichenketten und das Woerterbuch "a{sv}".</summary>
    Friend NotInheritable Class DBusVariant

        Public Sub New(signature As String, value As Object)
            Me.Signature = signature
            Me.Value = value
        End Sub

        Public ReadOnly Property Signature As String
        Public ReadOnly Property Value As Object

        Public Shared Function FromString(value As String) As DBusVariant
            Return New DBusVariant("s", If(value, String.Empty))
        End Function

        Public Shared Function FromObjectPath(value As String) As DBusVariant
            Return New DBusVariant("o", value)
        End Function

        Public Shared Function FromBoolean(value As Boolean) As DBusVariant
            Return New DBusVariant("b", value)
        End Function

        Public Shared Function FromDouble(value As Double) As DBusVariant
            Return New DBusVariant("d", value)
        End Function

        Public Shared Function FromInt32(value As Integer) As DBusVariant
            Return New DBusVariant("i", value)
        End Function

        Public Shared Function FromInt64(value As Long) As DBusVariant
            Return New DBusVariant("x", value)
        End Function

        Public Shared Function FromStrings(values As IEnumerable(Of String)) As DBusVariant
            Return New DBusVariant("as", New List(Of String)(values))
        End Function

        Public Shared Function FromDictionary(values As Dictionary(Of String, DBusVariant)) As DBusVariant
            Return New DBusVariant("a{sv}", values)
        End Function

        ''' <summary>Gleich heisst: dieselben Bytes auf der Leitung. Das ist die einzige Gleichheit,
        ''' die fuer das Melden geaenderter Eigenschaften zaehlt, und sie gilt ohne Sonderfall auch
        ''' fuer Listen und verschachtelte Woerterbuecher.</summary>
        Public Function SameAs(other As DBusVariant) As Boolean
            If other Is Nothing OrElse Not String.Equals(Signature, other.Signature, StringComparison.Ordinal) Then Return False
            Dim mine = Serialize()
            Dim theirs = other.Serialize()
            If mine.Length <> theirs.Length Then Return False
            For index = 0 To mine.Length - 1
                If mine(index) <> theirs(index) Then Return False
            Next
            Return True
        End Function

        Private Function Serialize() As Byte()
            Dim writer As New DBusWriter()
            writer.WriteValue(Signature, Value)
            Return writer.ToArray()
        End Function

    End Class

    ''' <summary>Die Stelle, an der die Laenge einer Liste nachgetragen wird.</summary>
    Friend Structure DBusArrayMark
        Public ReadOnly LengthOffset As Integer
        Public ReadOnly Start As Integer

        Public Sub New(lengthOffset As Integer, start As Integer)
            Me.LengthOffset = lengthOffset
            Me.Start = start
        End Sub
    End Structure

    ''' <summary>Schreibt Werte im D-Bus-Format, immer little-endian.</summary>
    Friend NotInheritable Class DBusWriter

        Private ReadOnly _buffer As New MemoryStream()

        Public ReadOnly Property Length As Integer
            Get
                Return CInt(_buffer.Length)
            End Get
        End Property

        Public Function ToArray() As Byte()
            Return _buffer.ToArray()
        End Function

        Public Sub Align(boundary As Integer)
            Do While Length Mod boundary <> 0
                _buffer.WriteByte(0)
            Loop
        End Sub

        Public Sub WriteByte(value As Byte)
            _buffer.WriteByte(value)
        End Sub

        Public Sub WriteBytes(value As Byte())
            If value Is Nothing OrElse value.Length = 0 Then Return
            _buffer.Write(value, 0, value.Length)
        End Sub

        Public Sub WriteBoolean(value As Boolean)
            WriteUInt32(If(value, 1UI, 0UI))
        End Sub

        Public Sub WriteInt32(value As Integer)
            Align(4)
            WriteOrdered(BitConverter.GetBytes(value))
        End Sub

        Public Sub WriteUInt32(value As UInteger)
            Align(4)
            WriteOrdered(BitConverter.GetBytes(value))
        End Sub

        Public Sub WriteInt64(value As Long)
            Align(8)
            WriteOrdered(BitConverter.GetBytes(value))
        End Sub

        Public Sub WriteUInt64(value As ULong)
            Align(8)
            WriteOrdered(BitConverter.GetBytes(value))
        End Sub

        Public Sub WriteDouble(value As Double)
            Align(8)
            WriteOrdered(BitConverter.GetBytes(value))
        End Sub

        ''' <summary>Das Kopfbyte "l" sagt dem Empfaenger little-endian. Auf einer Maschine mit
        ''' anderer Byte-Reihenfolge wird deshalb umgedreht.</summary>
        Private Sub WriteOrdered(bytes As Byte())
            If Not BitConverter.IsLittleEndian Then Array.Reverse(bytes)
            WriteBytes(bytes)
        End Sub

        ''' <summary>Zeichenkette und Objektpfad: Laenge, UTF-8, abschliessende Null.</summary>
        Public Sub WriteString(value As String)
            Dim bytes = Encoding.UTF8.GetBytes(If(value, String.Empty))
            WriteUInt32(CUInt(bytes.Length))
            WriteBytes(bytes)
            WriteByte(0)
        End Sub

        ''' <summary>Eine Signatur: ein Byte Laenge statt vier, sonst wie eine Zeichenkette.</summary>
        Public Sub WriteSignature(value As String)
            Dim bytes = Encoding.ASCII.GetBytes(If(value, String.Empty))
            WriteByte(CByte(bytes.Length))
            WriteBytes(bytes)
            WriteByte(0)
        End Sub

        ''' <summary>Beginnt eine Liste. Die Laenge steht VOR den Elementen und ist erst am Ende
        ''' bekannt; sie wird deshalb mit 0 vorgelegt und von <see cref="EndArray"/> nachgetragen.
        ''' Das Auffuellen bis zum ersten Element gehoert NICHT zur Laenge, steht aber auch bei einer
        ''' leeren Liste da.</summary>
        Public Function BeginArray(elementAlignment As Integer) As DBusArrayMark
            Align(4)
            Dim lengthOffset = Length
            WriteUInt32(0UI)
            Align(elementAlignment)
            Return New DBusArrayMark(lengthOffset, Length)
        End Function

        Public Sub EndArray(mark As DBusArrayMark)
            Dim bytes = BitConverter.GetBytes(CUInt(Length - mark.Start))
            If Not BitConverter.IsLittleEndian Then Array.Reverse(bytes)
            Dim position = _buffer.Position
            _buffer.Position = mark.LengthOffset
            _buffer.Write(bytes, 0, bytes.Length)
            _buffer.Position = position
        End Sub

        Public Sub WriteStringArray(values As IEnumerable(Of String))
            Dim mark = BeginArray(4)
            If values IsNot Nothing Then
                For Each value In values
                    WriteString(value)
                Next
            End If
            EndArray(mark)
        End Sub

        Public Sub WriteDictionary(entries As IEnumerable(Of KeyValuePair(Of String, DBusVariant)))
            Dim mark = BeginArray(8)
            If entries IsNot Nothing Then
                For Each entry In entries
                    Align(8)
                    WriteString(entry.Key)
                    WriteVariant(entry.Value)
                Next
            End If
            EndArray(mark)
        End Sub

        Public Sub WriteVariant(value As DBusVariant)
            WriteSignature(value.Signature)
            WriteValue(value.Signature, value.Value)
        End Sub

        Public Sub WriteValue(signature As String, value As Object)
            Select Case signature
                Case "y" : WriteByte(DirectCast(value, Byte))
                Case "b" : WriteBoolean(DirectCast(value, Boolean))
                Case "i" : WriteInt32(DirectCast(value, Integer))
                Case "u" : WriteUInt32(DirectCast(value, UInteger))
                Case "x" : WriteInt64(DirectCast(value, Long))
                Case "t" : WriteUInt64(DirectCast(value, ULong))
                Case "d" : WriteDouble(DirectCast(value, Double))
                Case "s", "o" : WriteString(DirectCast(value, String))
                Case "g" : WriteSignature(DirectCast(value, String))
                Case "v" : WriteVariant(DirectCast(value, DBusVariant))
                Case "as", "ao" : WriteStringArray(DirectCast(value, IEnumerable(Of String)))
                Case "a{sv}" : WriteDictionary(DirectCast(value, IEnumerable(Of KeyValuePair(Of String, DBusVariant))))
                Case Else : Throw New NotSupportedException($"D-Bus-Signatur {signature} wird nicht geschrieben.")
            End Select
        End Sub

    End Class

    ''' <summary>Liest Werte im D-Bus-Format. Beide Byte-Reihenfolgen: der Bus reicht Nachrichten
    ''' so weiter, wie ihr Absender sie geschrieben hat.</summary>
    Friend NotInheritable Class DBusReader

        Private ReadOnly _data As Byte()
        Private ReadOnly _origin As Integer
        Private ReadOnly _end As Integer
        Private ReadOnly _bigEndian As Boolean
        Private _position As Integer

        Public Sub New(data As Byte(), origin As Integer, count As Integer, bigEndian As Boolean)
            _data = data
            _origin = origin
            _position = origin
            _end = origin + count
            _bigEndian = bigEndian
        End Sub

        Public ReadOnly Property Position As Integer
            Get
                Return _position
            End Get
        End Property

        Public Sub Align(boundary As Integer)
            Dim remainder = (_position - _origin) Mod boundary
            If remainder <> 0 Then _position += boundary - remainder
        End Sub

        Private Function Take(count As Integer) As Integer
            If count < 0 OrElse _position + count > _end Then
                Throw New InvalidDataException("Die D-Bus-Nachricht ist kuerzer als angegeben.")
            End If
            Dim start = _position
            _position += count
            Return start
        End Function

        Private Function TakeOrdered(size As Integer) As Byte()
            Align(size)
            Dim start = Take(size)
            Dim bytes(size - 1) As Byte
            Array.Copy(_data, start, bytes, 0, size)
            If _bigEndian = BitConverter.IsLittleEndian Then Array.Reverse(bytes)
            Return bytes
        End Function

        Public Function ReadByte() As Byte
            Return _data(Take(1))
        End Function

        Public Function ReadBoolean() As Boolean
            Return ReadUInt32() <> 0UI
        End Function

        Public Function ReadInt16() As Short
            Return BitConverter.ToInt16(TakeOrdered(2), 0)
        End Function

        Public Function ReadUInt16() As UShort
            Return BitConverter.ToUInt16(TakeOrdered(2), 0)
        End Function

        Public Function ReadInt32() As Integer
            Return BitConverter.ToInt32(TakeOrdered(4), 0)
        End Function

        Public Function ReadUInt32() As UInteger
            Return BitConverter.ToUInt32(TakeOrdered(4), 0)
        End Function

        Public Function ReadInt64() As Long
            Return BitConverter.ToInt64(TakeOrdered(8), 0)
        End Function

        Public Function ReadUInt64() As ULong
            Return BitConverter.ToUInt64(TakeOrdered(8), 0)
        End Function

        Public Function ReadDouble() As Double
            Return BitConverter.ToDouble(TakeOrdered(8), 0)
        End Function

        Public Function ReadString() As String
            Dim length = ReadUInt32()
            If length > Integer.MaxValue Then Throw New InvalidDataException("Die D-Bus-Zeichenkette ist zu lang.")
            Dim start = Take(CInt(length))
            Take(1)
            Return Encoding.UTF8.GetString(_data, start, CInt(length))
        End Function

        Public Function ReadObjectPath() As String
            Return ReadString()
        End Function

        Public Function ReadSignature() As String
            Dim length = ReadByte()
            Dim start = Take(length)
            Take(1)
            Return Encoding.ASCII.GetString(_data, start, length)
        End Function

        Public Function ReadVariant() As DBusVariant
            Dim signature = ReadSignature()
            Return New DBusVariant(signature, ReadValue(signature))
        End Function

        Public Function ReadValue(signature As String) As Object
            Select Case signature
                Case "y" : Return ReadByte()
                Case "b" : Return ReadBoolean()
                Case "n" : Return ReadInt16()
                Case "q" : Return ReadUInt16()
                Case "i" : Return ReadInt32()
                Case "u" : Return ReadUInt32()
                Case "x" : Return ReadInt64()
                Case "t" : Return ReadUInt64()
                Case "d" : Return ReadDouble()
                Case "s", "o" : Return ReadString()
                Case "g" : Return ReadSignature()
                Case "v" : Return ReadVariant()
                Case "as", "ao" : Return ReadStringArray()
                Case Else : Throw New NotSupportedException($"D-Bus-Signatur {signature} wird nicht gelesen.")
            End Select
        End Function

        Public Function ReadStringArray() As List(Of String)
            Dim length = ReadUInt32()
            Align(4)
            Dim arrayEnd = _position + CInt(Math.Min(length, CUInt(_end - _position)))
            Dim values As New List(Of String)()
            Do While _position < arrayEnd
                values.Add(ReadString())
            Loop
            Return values
        End Function

    End Class

    ''' <summary>Eine D-Bus-Nachricht: Kopf und Rumpf.</summary>
    Friend NotInheritable Class DBusMessage

        Public Const TypeMethodCall As Byte = 1
        Public Const TypeMethodReturn As Byte = 2
        Public Const TypeError As Byte = 3
        Public Const TypeSignal As Byte = 4

        Public Const FlagNoReplyExpected As Byte = 1

        ''' <summary>Hoechstgrenze des Protokolls fuer eine Nachricht: 128 MiB.</summary>
        Private Const MaximumMessageLength As Long = 134217728L

        Public Property MessageType As Byte
        Public Property Flags As Byte
        Public Property Serial As UInteger
        Public Property ReplySerial As UInteger
        Public Property HasReplySerial As Boolean
        Public Property ObjectPath As String = String.Empty
        Public Property InterfaceName As String = String.Empty
        Public Property Member As String = String.Empty
        Public Property ErrorName As String = String.Empty
        Public Property Destination As String = String.Empty
        Public Property Sender As String = String.Empty
        Public Property Signature As String = String.Empty
        Public Property Body As Byte() = Array.Empty(Of Byte)()
        Public Property BigEndian As Boolean

        Public ReadOnly Property ExpectsReply As Boolean
            Get
                Return MessageType = TypeMethodCall AndAlso (Flags And FlagNoReplyExpected) = 0
            End Get
        End Property

        Public Function CreateBodyReader() As DBusReader
            Return New DBusReader(Body, 0, Body.Length, BigEndian)
        End Function

        ''' <summary>Die Gesamtlaenge einer Nachricht aus ihren ersten 16 Bytes. So viel muss man
        ''' kennen, um die Nachricht vollstaendig vom Socket zu holen.</summary>
        Public Shared Function MeasureFrame(prefix As Byte()) As Integer
            Dim reader As New DBusReader(prefix, 0, 16, prefix(0) = AsciiB)
            reader.ReadUInt32()
            Dim bodyLength = reader.ReadUInt32()
            reader.ReadUInt32()
            Dim fieldsLength = reader.ReadUInt32()
            ' KLAMMERN NOETIG: in VB bindet "*" staerker als die Ganzzahldivision "\". Ohne sie
            ' stuende hier "\ 64", und jede Nachricht waere zu kurz gelesen.
            Dim headerLength = ((16L + fieldsLength + 7L) \ 8L) * 8L
            Dim total = headerLength + bodyLength
            If total > MaximumMessageLength Then Throw New InvalidDataException("Die D-Bus-Nachricht ist zu gross.")
            Return CInt(total)
        End Function

        Public Shared Function Parse(frame As Byte()) As DBusMessage
            Dim bigEndian = frame(0) = AsciiB
            Dim reader As New DBusReader(frame, 0, frame.Length, bigEndian)
            Dim message As New DBusMessage With {.BigEndian = bigEndian}

            reader.ReadByte()
            message.MessageType = reader.ReadByte()
            message.Flags = reader.ReadByte()
            reader.ReadByte()
            Dim bodyLength = reader.ReadUInt32()
            message.Serial = reader.ReadUInt32()

            ' Die Kopffelder: eine Liste von (Byte, Variante). Unbekannte werden gelesen und
            ' verworfen - das Protokoll erlaubt neue.
            Dim fieldsLength = reader.ReadUInt32()
            reader.Align(8)
            Dim fieldsEnd = reader.Position + CInt(fieldsLength)
            Do While reader.Position < fieldsEnd
                reader.Align(8)
                Dim code = reader.ReadByte()
                Dim field = reader.ReadVariant()
                Dim text = TryCast(field.Value, String)
                Select Case code
                    Case 1 : message.ObjectPath = If(text, String.Empty)
                    Case 2 : message.InterfaceName = If(text, String.Empty)
                    Case 3 : message.Member = If(text, String.Empty)
                    Case 4 : message.ErrorName = If(text, String.Empty)
                    Case 5
                        If TypeOf field.Value Is UInteger Then
                            message.ReplySerial = DirectCast(field.Value, UInteger)
                            message.HasReplySerial = True
                        End If
                    Case 6 : message.Destination = If(text, String.Empty)
                    Case 7 : message.Sender = If(text, String.Empty)
                    Case 8 : message.Signature = If(text, String.Empty)
                End Select
            Loop

            reader.Align(8)
            Dim bodyStart = reader.Position
            If bodyStart + CLng(bodyLength) > frame.Length Then Throw New InvalidDataException("Der Rumpf der D-Bus-Nachricht fehlt.")
            Dim body(CInt(bodyLength) - 1) As Byte
            Array.Copy(frame, bodyStart, body, 0, CInt(bodyLength))
            message.Body = body
            Return message
        End Function

        Public Function Serialize() As Byte()
            Dim writer As New DBusWriter()
            writer.WriteByte(AsciiL)
            writer.WriteByte(MessageType)
            writer.WriteByte(Flags)
            writer.WriteByte(1)
            writer.WriteUInt32(CUInt(Body.Length))
            writer.WriteUInt32(Serial)

            Dim mark = writer.BeginArray(8)
            If ObjectPath.Length > 0 Then WriteField(writer, 1, "o", ObjectPath)
            If InterfaceName.Length > 0 Then WriteField(writer, 2, "s", InterfaceName)
            If Member.Length > 0 Then WriteField(writer, 3, "s", Member)
            If ErrorName.Length > 0 Then WriteField(writer, 4, "s", ErrorName)
            If HasReplySerial Then WriteField(writer, 5, "u", ReplySerial)
            If Destination.Length > 0 Then WriteField(writer, 6, "s", Destination)
            If Signature.Length > 0 Then WriteField(writer, 8, "g", Signature)
            writer.EndArray(mark)

            writer.Align(8)
            writer.WriteBytes(Body)
            Return writer.ToArray()
        End Function

        Private Shared Sub WriteField(writer As DBusWriter, code As Byte, signature As String, value As Object)
            writer.Align(8)
            writer.WriteByte(code)
            writer.WriteVariant(New DBusVariant(signature, value))
        End Sub

        Private Shared ReadOnly AsciiL As Byte = CByte(AscW("l"c))
        Private Shared ReadOnly AsciiB As Byte = CByte(AscW("B"c))

    End Class

    ''' <summary>Die Verbindung zum Sitzungsbus.
    '''
    ''' <para>Ein Lesefaden nimmt alle Nachrichten an: Antworten gehen an den, der auf sie wartet,
    ''' Methodenaufrufe an das Objekt, das unter ihrem Pfad angemeldet ist
    ''' (<see cref="RegisterObject"/>). Die Behandlung laeuft AUF DEM LESEFADEN; sie muss schnell
    ''' sein und darf NICHT <see cref="CallMethod"/> rufen - die Antwort darauf kaeme ueber denselben
    ''' Faden, der gerade wartet.</para>
    '''
    ''' <para>Geschrieben wird von mehreren Faeden (Antworten vom Lesefaden, Signale von der
    ''' Oberflaeche), deshalb unter einer Sperre und immer eine ganze Nachricht am Stueck.</para>
    '''
    ''' <para>EINE Verbindung fuer die ganze Anwendung: sie haelt den Namen fuer die einzelne
    ''' Instanz und den fuer MPRIS. Beide Objekte leben darauf nebeneinander.</para></summary>
    Friend NotInheritable Class DBusSessionConnection
        Implements IDisposable

        Private Const BusService As String = "org.freedesktop.DBus"
        Private Const BusObjectPath As String = "/org/freedesktop/DBus"
        Private Const PeerInterface As String = "org.freedesktop.DBus.Peer"

        ' Antworten auf RequestName
        Private Const RequestNameDoNotQueue As UInteger = 4UI
        Private Const RequestNamePrimaryOwner As UInteger = 1UI
        Private Const RequestNameAlreadyOwner As UInteger = 4UI

        Private Shared ReadOnly CallTimeout As TimeSpan = TimeSpan.FromSeconds(5)

        Private ReadOnly _socket As Socket
        Private ReadOnly _stream As NetworkStream
        Private ReadOnly _writeGate As New Object()
        Private ReadOnly _pending As New Dictionary(Of UInteger, TaskCompletionSource(Of DBusMessage))()
        Private ReadOnly _objects As New Dictionary(Of String, Action(Of DBusMessage))(StringComparer.Ordinal)
        Private _serial As Integer
        Private _closed As Integer
        Private _uniqueName As String = String.Empty

        Public Event Disconnected()

        Private Sub New(socket As Socket)
            _socket = socket
            _stream = New NetworkStream(socket, ownsSocket:=True)
        End Sub

        Public ReadOnly Property UniqueName As String
            Get
                Return _uniqueName
            End Get
        End Property

        Public ReadOnly Property IsOpen As Boolean
            Get
                Return Volatile.Read(_closed) = 0
            End Get
        End Property

        ''' <summary>Meldet ein Objekt an. <paramref name="handler"/> bekommt jeden Aufruf an diesen
        ''' Pfad, laeuft auf dem Lesefaden und antwortet selbst. Anmelden, BEVOR der zugehoerige
        ''' Name beantragt wird: ab dem Augenblick, in dem der Bus ihn vergibt, koennen Aufrufe
        ''' kommen.</summary>
        Public Sub RegisterObject(objectPath As String, handler As Action(Of DBusMessage))
            SyncLock _objects
                _objects(objectPath) = handler
            End SyncLock
        End Sub

        ''' <summary>Beantragt einen Namen auf dem Bus, ohne sich anzustellen. True, wenn er jetzt
        ''' dieser Verbindung gehoert; False, wenn ihn schon ein anderer haelt.</summary>
        Public Function RequestName(name As String) As Boolean
            Dim body As New DBusWriter()
            body.WriteString(name)
            body.WriteUInt32(RequestNameDoNotQueue)
            Dim reply = CallMethod(BusService, BusObjectPath, BusService, "RequestName", "su", body.ToArray())
            Dim result = reply.CreateBodyReader().ReadUInt32()
            Return result = RequestNamePrimaryOwner OrElse result = RequestNameAlreadyOwner
        End Function

        ''' <summary>Verbindet, meldet sich an und stellt sich dem Bus vor. Blockiert und gehoert
        ''' deshalb nicht auf den Anzeigefaden. Nothing, wenn kein Sitzungsbus zu finden ist;
        ''' eine Ausnahme, wenn er da ist, aber nicht mitspielt.</summary>
        Public Shared Function ConnectSession() As DBusSessionConnection
            Dim endPoint = ResolveSessionEndPoint()
            If endPoint Is Nothing Then Return Nothing

            Dim socket As New Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            Try
                socket.Connect(endPoint)
            Catch
                socket.Dispose()
                Throw
            End Try

            Dim connection As New DBusSessionConnection(socket)
            Try
                connection.Authenticate()
                connection.StartReading()
                Dim reply = connection.CallMethod(BusService, BusObjectPath, BusService, "Hello", String.Empty, Nothing)
                connection._uniqueName = reply.CreateBodyReader().ReadString()
                Return connection
            Catch
                connection.Dispose()
                Throw
            End Try
        End Function

        ''' <summary>Die Adresse des Sitzungsbusses. Steht in DBUS_SESSION_BUS_ADDRESS, bei
        ''' mehreren durch Semikolon getrennt; genommen wird die erste mit einem Unix-Socket. Fehlt
        ''' die Variable, liegt der Bus nach heutiger Uebung unter $XDG_RUNTIME_DIR/bus.</summary>
        Private Shared Function ResolveSessionEndPoint() As EndPoint
            Dim address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")
            If Not String.IsNullOrWhiteSpace(address) Then
                For Each entry In address.Split(";"c)
                    Dim colon = entry.IndexOf(":"c)
                    If colon < 0 OrElse Not String.Equals(entry.Substring(0, colon), "unix", StringComparison.Ordinal) Then Continue For
                    For Each pair In entry.Substring(colon + 1).Split(","c)
                        Dim separator = pair.IndexOf("="c)
                        If separator < 0 Then Continue For
                        Dim key = pair.Substring(0, separator)
                        Dim value = Uri.UnescapeDataString(pair.Substring(separator + 1))
                        If key = "path" Then Return New UnixDomainSocketEndPoint(value)
                        ' Ein abstrakter Socket hat keinen Eintrag im Dateisystem. .NET erkennt
                        ' ihn an der fuehrenden Null.
                        If key = "abstract" Then Return New UnixDomainSocketEndPoint(ChrW(0) & value)
                    Next
                Next
            End If

            Dim runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            If Not String.IsNullOrEmpty(runtimeDirectory) Then
                Dim candidate = Path.Combine(runtimeDirectory, "bus")
                If File.Exists(candidate) Then Return New UnixDomainSocketEndPoint(candidate)
            End If
            Return Nothing
        End Function

        ''' <summary>Die Anmeldung mit EXTERNAL: der Bus prueft die Benutzerkennung des Sockets
        ''' selbst, gesagt wird sie ihm nur, hexadezimal als Ziffernfolge. Vorweg geht ein
        ''' Null-Byte, so will es das Protokoll.</summary>
        Private Sub Authenticate()
            _socket.ReceiveTimeout = CInt(CallTimeout.TotalMilliseconds)
            Dim userId = NativeGetUid().ToString(CultureInfo.InvariantCulture)
            Dim hex = Convert.ToHexString(Encoding.ASCII.GetBytes(userId)).ToLowerInvariant()

            WriteRaw(New Byte() {0})
            WriteRaw(Encoding.ASCII.GetBytes("AUTH EXTERNAL " & hex & vbCrLf))
            Dim answer = ReadAuthLine()
            If Not answer.StartsWith("OK ", StringComparison.Ordinal) Then
                Throw New IOException($"Der Sitzungsbus lehnt die Anmeldung ab: {answer}")
            End If
            WriteRaw(Encoding.ASCII.GetBytes("BEGIN" & vbCrLf))
            _socket.ReceiveTimeout = 0
        End Sub

        ''' <summary>Eine Zeile der Anmeldung, Byte fuer Byte gelesen: danach beginnt der
        ''' Nachrichtenstrom, und davon darf nichts verschluckt werden.</summary>
        Private Function ReadAuthLine() As String
            Dim bytes As New List(Of Byte)()
            Do
                Dim value = _stream.ReadByte()
                If value < 0 Then Throw New EndOfStreamException("Der Sitzungsbus hat die Verbindung waehrend der Anmeldung geschlossen.")
                If value = 10 Then Exit Do
                If value <> 13 Then bytes.Add(CByte(value))
                If bytes.Count > 1024 Then Throw New IOException("Die Antwort des Sitzungsbusses ist zu lang.")
            Loop
            Return Encoding.ASCII.GetString(bytes.ToArray())
        End Function

        <DllImport("libc", EntryPoint:="getuid")>
        Private Shared Function NativeGetUid() As UInteger
        End Function

        Private Sub StartReading()
            Dim thread As New Thread(AddressOf ReadLoop) With {
                .IsBackground = True,
                .Name = "dbus-session"
            }
            thread.Start()
        End Sub

        Private Sub ReadLoop()
            Try
                Dim prefix(15) As Byte
                Do
                    _stream.ReadExactly(prefix, 0, 16)
                    Dim total = DBusMessage.MeasureFrame(prefix)
                    Dim frame(total - 1) As Byte
                    Array.Copy(prefix, frame, 16)
                    If total > 16 Then _stream.ReadExactly(frame, 16, total - 16)
                    Dispatch(DBusMessage.Parse(frame))
                Loop
            Catch ex As Exception
                If Volatile.Read(_closed) = 0 Then DiagnosticLogService.Log("DBus", $"Verbindung verloren: {ex.Message}")
            End Try
            Close()
        End Sub

        Private Sub Dispatch(message As DBusMessage)
            Select Case message.MessageType
                Case DBusMessage.TypeMethodReturn, DBusMessage.TypeError
                    If Not message.HasReplySerial Then Return
                    Dim waiter As TaskCompletionSource(Of DBusMessage) = Nothing
                    SyncLock _pending
                        If _pending.TryGetValue(message.ReplySerial, waiter) Then _pending.Remove(message.ReplySerial)
                    End SyncLock
                    waiter?.TrySetResult(message)

                Case DBusMessage.TypeMethodCall
                    Try
                        DispatchCall(message)
                    Catch ex As Exception
                        DiagnosticLogService.LogException("DBus.MethodCall", ex)
                        SendError(message, "org.freedesktop.DBus.Error.Failed", ex.Message)
                    End Try

                ' Signale (NameAcquired und dergleichen) braucht hier niemand.
            End Select
        End Sub

        Private Sub DispatchCall(message As DBusMessage)
            ' Peer gilt fuer jeden Pfad und gehoert der Verbindung, nicht einem Objekt.
            If (message.InterfaceName.Length = 0 OrElse message.InterfaceName = PeerInterface) Then
                If message.Member = "Ping" Then
                    SendReply(message, String.Empty, Nothing)
                    Return
                End If
                If message.Member = "GetMachineId" Then
                    Dim body As New DBusWriter()
                    body.WriteString(ReadMachineId())
                    SendReply(message, "s", body.ToArray())
                    Return
                End If
            End If

            Dim handler As Action(Of DBusMessage) = Nothing
            Dim children As List(Of String)
            SyncLock _objects
                If _objects.TryGetValue(message.ObjectPath, handler) Then
                    children = Nothing
                Else
                    children = ChildNodes(message.ObjectPath)
                End If
            End SyncLock

            If handler IsNot Nothing Then
                handler(message)
                Return
            End If

            ' Die Pfade UEBER einem Objekt ("/", "/org", "/org/mpris") gibt es nur, damit man sich
            ' mit einem Werkzeug wie busctl zu ihm durchklicken kann.
            If message.Member = "Introspect" AndAlso children.Count > 0 Then
                Dim xml As New StringBuilder("<node>")
                For Each child In children
                    xml.Append("<node name='").Append(child).Append("'/>")
                Next
                xml.Append("</node>")
                Dim body As New DBusWriter()
                body.WriteString(xml.ToString())
                SendReply(message, "s", body.ToArray())
                Return
            End If

            SendError(message, "org.freedesktop.DBus.Error.UnknownObject", $"Kein Objekt unter {message.ObjectPath}.")
        End Sub

        ''' <summary>Die naechste Stufe unter <paramref name="parent"/> fuer jedes angemeldete
        ''' Objekt darunter. Nur unter der Sperre von _objects rufen.</summary>
        Private Function ChildNodes(parent As String) As List(Of String)
            Dim prefix = If(parent = "/", "/", parent & "/")
            Dim names As New List(Of String)()
            For Each registered In _objects.Keys
                If Not registered.StartsWith(prefix, StringComparison.Ordinal) OrElse registered.Length = prefix.Length Then Continue For
                Dim name = registered.Substring(prefix.Length).Split("/"c)(0)
                If Not names.Contains(name) Then names.Add(name)
            Next
            Return names
        End Function

        Private Shared Function ReadMachineId() As String
            For Each candidate In {"/etc/machine-id", "/var/lib/dbus/machine-id"}
                Try
                    If File.Exists(candidate) Then Return File.ReadAllText(candidate).Trim()
                Catch
                End Try
            Next
            Return String.Empty
        End Function

        ''' <summary>Ruft eine Methode und wartet auf die Antwort. NIE vom Lesefaden aus rufen.</summary>
        Public Function CallMethod(destination As String, objectPath As String, interfaceName As String,
                                   member As String, signature As String, body As Byte()) As DBusMessage
            Dim message As New DBusMessage With {
                .MessageType = DBusMessage.TypeMethodCall,
                .Serial = NextSerial(),
                .Destination = destination,
                .ObjectPath = objectPath,
                .InterfaceName = interfaceName,
                .Member = member,
                .Signature = If(signature, String.Empty),
                .Body = If(body, Array.Empty(Of Byte)())
            }

            Dim waiter As New TaskCompletionSource(Of DBusMessage)(TaskCreationOptions.RunContinuationsAsynchronously)
            SyncLock _pending
                _pending(message.Serial) = waiter
            End SyncLock

            Send(message)
            If Not waiter.Task.Wait(CallTimeout) Then
                SyncLock _pending
                    _pending.Remove(message.Serial)
                End SyncLock
                Throw New TimeoutException($"Keine Antwort auf {interfaceName}.{member}.")
            End If

            Dim reply = waiter.Task.Result
            If reply.MessageType = DBusMessage.TypeError Then
                Throw New IOException($"{interfaceName}.{member}: {reply.ErrorName}")
            End If
            Return reply
        End Function

        Public Sub SendReply(request As DBusMessage, signature As String, body As Byte())
            If Not request.ExpectsReply Then Return
            TrySend(New DBusMessage With {
                .MessageType = DBusMessage.TypeMethodReturn,
                .ReplySerial = request.Serial,
                .HasReplySerial = True,
                .Destination = request.Sender,
                .Signature = If(signature, String.Empty),
                .Body = If(body, Array.Empty(Of Byte)())
            })
        End Sub

        Public Sub SendError(request As DBusMessage, errorName As String, text As String)
            If Not request.ExpectsReply Then Return
            Dim body As New DBusWriter()
            body.WriteString(text)
            TrySend(New DBusMessage With {
                .MessageType = DBusMessage.TypeError,
                .ErrorName = errorName,
                .ReplySerial = request.Serial,
                .HasReplySerial = True,
                .Destination = request.Sender,
                .Signature = "s",
                .Body = body.ToArray()
            })
        End Sub

        Public Sub SendSignal(objectPath As String, interfaceName As String, member As String, signature As String, body As Byte())
            TrySend(New DBusMessage With {
                .MessageType = DBusMessage.TypeSignal,
                .ObjectPath = objectPath,
                .InterfaceName = interfaceName,
                .Member = member,
                .Signature = If(signature, String.Empty),
                .Body = If(body, Array.Empty(Of Byte)())
            })
        End Sub

        ''' <summary>Antworten und Signale duerfen scheitern, ohne dass der Aufrufer davon erfaehrt:
        ''' eine abgerissene Verbindung meldet der Lesefaden ohnehin.</summary>
        Private Sub TrySend(message As DBusMessage)
            Try
                Send(message)
            Catch ex As Exception
                If Volatile.Read(_closed) = 0 Then DiagnosticLogService.Log("DBus", $"Senden fehlgeschlagen: {ex.Message}")
            End Try
        End Sub

        Private Sub Send(message As DBusMessage)
            If Volatile.Read(_closed) <> 0 Then Throw New ObjectDisposedException(NameOf(DBusSessionConnection))
            If message.Serial = 0UI Then message.Serial = NextSerial()
            WriteRaw(message.Serialize())
        End Sub

        Private Sub WriteRaw(bytes As Byte())
            SyncLock _writeGate
                _stream.Write(bytes, 0, bytes.Length)
            End SyncLock
        End Sub

        Private Function NextSerial() As UInteger
            Return CUInt(Interlocked.Increment(_serial))
        End Function

        Private Sub Close()
            If Interlocked.Exchange(_closed, 1) = 1 Then Return

            Try
                _socket.Shutdown(SocketShutdown.Both)
            Catch
            End Try
            Try
                _stream.Dispose()
            Catch
            End Try

            Dim waiters As List(Of TaskCompletionSource(Of DBusMessage))
            SyncLock _pending
                waiters = New List(Of TaskCompletionSource(Of DBusMessage))(_pending.Values)
                _pending.Clear()
            End SyncLock
            For Each waiter In waiters
                waiter.TrySetException(New IOException("Die Verbindung zum Sitzungsbus ist geschlossen."))
            Next

            RaiseEvent Disconnected()
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Close()
        End Sub

    End Class

End Namespace
