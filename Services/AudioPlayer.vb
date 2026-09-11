Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Runtime.InteropServices
Imports System.Threading

Namespace Services

    ''' <summary>Die Tonwiedergabe ueber libmpv.
    '''
    ''' <para>KEIN libmpv-Aufruf laeuft auf dem Anzeigefaden. Alle laufen auf einem eigenen
    ''' Befehlsfaden, und die oeffentlichen Methoden reihen nur ein. Der Grund steht in FerrumPix
    ''' ausfuehrlich: <c>mpv_set_property_string</c> geht in mpv ueber <c>lock_core</c> und WARTET
    ''' auf den Kernfaden; wer von der Oberflaeche aus setzt, kann die ganze Anwendung einfrieren.
    ''' Beim Ton ist die Kette kuerzer als beim Bild, aber der Riegel kostet nichts und die
    ''' Reihenfolge der Befehle bleibt so gesichert: Stop vor Laden vor Abspielen.</para>
    '''
    ''' <para>Anders als beim Video gibt es hier KEIN Ausgabeziel, auf das gewartet werden muesste.
    ''' mpv steht, sobald <see cref="Start"/> durchgelaufen ist, und alles davor Eingereihte laeuft
    ''' danach in der Reihenfolge ab, in der es kam.</para></summary>
    Public NotInheritable Class AudioPlayer
        Implements IDisposable

        Private Const PropTimePos As ULong = 1UL
        Private Const PropDuration As ULong = 2UL
        Private Const PropPause As ULong = 3UL
        Private Const PropMute As ULong = 4UL
        Private Const PropVolume As ULong = 5UL

        ''' <summary>Schuetzt den Zustand, der zwischen Anzeige-, Befehls- und Ereignisfaden geteilt
        ''' wird. Ueber einem libmpv-Aufruf darf diese Sperre NIE gehalten werden.</summary>
        Private ReadOnly _syncRoot As New Object()

        ''' <summary>Die Befehlswarteschlange. Dient zugleich als eigene Sperre.</summary>
        Private ReadOnly _queue As New Queue(Of Action)()

        Private _commandThread As Thread
        Private _queueClosed As Boolean = False

        ' Nur der Befehlsfaden fasst diese Felder an.
        Private _handle As IntPtr = IntPtr.Zero
        Private _eventThread As Thread
        Private _initialized As Boolean = False
        Private _initializationFailed As Boolean = False
        Private _eventLoopStopping As Boolean = False

        ''' <summary>Der zuletzt tatsaechlich an mpv gegebene Pfad, und damit die EINZIGE Antwort
        ''' auf die Frage, welcher Titel gerade laeuft. Geschrieben nur auf dem Befehlsfaden,
        ''' gelesen auch vom Anzeigefaden ueber <c>Volatile</c>.</summary>
        Private _loadedPath As String = Nothing

        ' Geteilter Zustand unter _syncRoot.
        Private _disposed As Boolean = False
        Private _initializationError As Exception = Nothing
        Private _pendingPlay As Boolean = False
        Private _isPaused As Boolean = True
        Private _isMuted As Boolean = False
        Private _volume As Double = 100
        Private _gapless As Boolean = True
        Private _replayGainMode As String = "no"
        Private _replayGainPreamp As Double = 0

        Public Event TimeChanged(seconds As Double)
        Public Event DurationChanged(seconds As Double)
        Public Event PauseChanged(isPaused As Boolean)
        Public Event MuteChanged(isMuted As Boolean)
        Public Event VolumeChanged(percent As Double)

        ''' <summary>Der Titel ist zu Ende oder wurde abgebrochen. <paramref name="reason"/> ist
        ''' <see cref="MpvInterop.MpvEndFileReason"/>; nur bei <c>Eof</c> darf die Liste
        ''' weiterlaufen, sonst wuerde ein Stop den naechsten Titel starten.</summary>
        Public Event EndReached(reason As Integer, [error] As Integer)

        ''' <summary>Ein Ladebefehl ist WIRKLICH an mpv gegangen. Die Oberflaeche setzt daran ihre
        ''' Anzeige zurueck; das darf nur geschehen, wenn auch wirklich ein anderer Titel kommt.
        ''' Die Frage ist hier richtig aufgehoben, weil die Meldung in demselben Faden entsteht,
        ''' der auch stoppt und laedt, und in derselben Reihenfolge.</summary>
        Public Event FileLoaded(path As String)

        ''' <summary>Die Datei zu einem Ladebefehl gibt es nicht. Sie ist gar nicht erst an mpv
        ''' gegangen, und was vorher lief, ist angehalten. Anders als ein Fehler von mpv traegt
        ''' diese Meldung den Pfad: sie entsteht auf dem Befehlsfaden, in der Reihenfolge der
        ''' Befehle, und laesst sich deshalb eindeutig einem Titel zuordnen.</summary>
        Public Event LoadFailed(path As String)

        Public Event InitializationFailed([error] As Exception)

        ''' <summary>mpv selbst hat sich beendet. Der Spieler ist danach unbrauchbar.</summary>
        Public Event PlaybackTerminated()

        Public Sub New()
            _commandThread = New Thread(AddressOf CommandLoop) With {
                .IsBackground = True,
                .Name = "libmpv-commands"
            }
            _commandThread.Start()
        End Sub

        ''' <summary>Startet den Aufbau. MUSS gerufen werden, und zwar ERST, nachdem der Aufrufer
        ''' seine Ereignisbehandlungen angehaengt hat: der Aufbau laeuft auf dem Befehlsfaden und
        ''' kann scheitern, bevor der Aufrufer die naechste Zeile erreicht hat.</summary>
        Public Sub Start()
            SyncLock _syncRoot
                If _disposed Then Return
            End SyncLock
            Enqueue(AddressOf InitializeCore)
        End Sub

        ''' <summary>Der Fehler, an dem der Aufbau gescheitert ist, sonst Nothing. Bleibt stehen,
        ''' damit ihn auch findet, wer erst nach dem Scheitern nachsieht.</summary>
        Public ReadOnly Property InitializationError As Exception
            Get
                SyncLock _syncRoot
                    Return _initializationError
                End SyncLock
            End Get
        End Property

        ''' <summary>Welcher Titel gerade geladen ist. Nothing heisst: keiner. Die Antwort kann im
        ''' selben Augenblick veralten, in dem sie gegeben wird, und taugt deshalb nur fuer
        ''' Anzeigen und niemals als Riegel gegen ein zweites Laden.</summary>
        Public ReadOnly Property LoadedPath As String
            Get
                Return Volatile.Read(_loadedPath)
            End Get
        End Property

        ''' <summary><paramref name="force"/> laedt auch dann neu, wenn genau dieser Titel schon
        ''' laeuft. Gebraucht fuer das Wiederholen eines einzelnen Titels: dort ist das erneute
        ''' Laden der ganze Zweck.</summary>
        Public Sub Load(path As String, Optional force As Boolean = False)
            If String.IsNullOrWhiteSpace(path) Then Return
            SyncLock _syncRoot
                If _disposed Then Return
            End SyncLock
            Enqueue(Sub() LoadCore(path, force))
        End Sub

        Public Sub Play()
            SyncLock _syncRoot
                If _disposed Then Return
                _pendingPlay = True
            End SyncLock
            Enqueue(Sub() SetPauseCore(False))
        End Sub

        Public Sub Pause()
            SyncLock _syncRoot
                If _disposed Then Return
                _pendingPlay = False
            End SyncLock
            Enqueue(Sub() SetPauseCore(True))
        End Sub

        Public Sub TogglePause()
            Enqueue(AddressOf TogglePauseCore)
        End Sub

        Public Sub [Stop]()
            SyncLock _syncRoot
                If _disposed Then Return
                _pendingPlay = False
            End SyncLock
            Enqueue(AddressOf StopCore)
        End Sub

        Public Sub Seek(seconds As Double)
            Dim target = Math.Max(0, seconds)
            Enqueue(Sub()
                        If Not ReadyForPlayback() Then Return
                        SetPropertyStringRaw("time-pos", target.ToString(CultureInfo.InvariantCulture))
                    End Sub)
        End Sub

        ''' <summary>Die Lautstaerke in Prozent. mpv laesst mehr als 100 zu; die Oberflaeche
        ''' begrenzt auf 100, weil darueber alles uebersteuert.</summary>
        Public Sub SetVolume(percent As Double)
            Dim value = Math.Clamp(percent, 0, 100)
            SyncLock _syncRoot
                If _disposed Then Return
                _volume = value
            End SyncLock
            Enqueue(Sub()
                        If Not ReadyForPlayback() Then Return
                        SetPropertyStringRaw("volume", value.ToString("0.##", CultureInfo.InvariantCulture))
                    End Sub)
        End Sub

        Public Sub SetMuted(value As Boolean)
            SyncLock _syncRoot
                If _disposed Then Return
                _isMuted = value
            End SyncLock
            Enqueue(Sub()
                        If Not ReadyForPlayback() Then Return
                        SetPropertyStringRaw("mute", If(value, "yes", "no"))
                    End Sub)
        End Sub

        ''' <summary>Ohne Luecke von Titel zu Titel. Die Umstellung greift ab dem naechsten Titel;
        ''' der laufende wird davon nicht angefasst.</summary>
        Public Sub SetGapless(value As Boolean)
            SyncLock _syncRoot
                If _disposed Then Return
                _gapless = value
            End SyncLock
            Enqueue(Sub()
                        If Not ReadyForPlayback() Then Return
                        SetPropertyStringRaw("gapless-audio", If(value, "yes", "weak"))
                    End Sub)
        End Sub

        ''' <summary>Die Lautstaerkeangleichung nach den ReplayGain-Angaben der Dateien. mpv rechnet
        ''' sie selbst ein und verhindert dabei von sich aus das Uebersteuern (replaygain-clip steht
        ''' ab Werk auf "no"). <paramref name="mode"/> ist mpvs Wert: "no", "track" oder "album";
        ''' "album" nimmt bei einer Datei ohne Albumangabe die des Titels. Die Umstellung wirkt
        ''' sofort, auch auf den laufenden Titel.</summary>
        Public Sub SetReplayGain(mode As String, preampDb As Double)
            Dim modeValue = If(mode = "track" OrElse mode = "album", mode, "no")
            Dim preamp = If(Double.IsNaN(preampDb), 0, Math.Clamp(preampDb, -15, 15))
            SyncLock _syncRoot
                If _disposed Then Return
                _replayGainMode = modeValue
                _replayGainPreamp = preamp
            End SyncLock
            Enqueue(Sub()
                        If Not ReadyForPlayback() Then Return
                        SetPropertyStringRaw("replaygain", modeValue)
                        SetPropertyStringRaw("replaygain-preamp", preamp.ToString("0.##", CultureInfo.InvariantCulture))
                    End Sub)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            SyncLock _syncRoot
                If _disposed Then Return
                _disposed = True
            End SyncLock

            ' Der Abbau laeuft als LETZTER Auftrag auf dem Befehlsfaden. Er darf warten, solange er
            ' will: ein eigener Hintergrundfaden haelt den Prozess beim Beenden nicht auf.
            EnqueueFinal(AddressOf ShutdownNative)
        End Sub

        ' Befehlsfaden

        Private Sub Enqueue(work As Action)
            SyncLock _queue
                If _queueClosed Then Return
                _queue.Enqueue(work)
                Monitor.Pulse(_queue)
            End SyncLock
        End Sub

        Private Sub EnqueueFinal(work As Action)
            SyncLock _queue
                If _queueClosed Then Return
                _queue.Enqueue(work)
                _queueClosed = True
                Monitor.Pulse(_queue)
            End SyncLock
        End Sub

        Private Sub CommandLoop()
            Do
                Dim work As Action = Nothing
                SyncLock _queue
                    Do While _queue.Count = 0
                        If _queueClosed Then Exit Do
                        Monitor.Wait(_queue)
                    Loop
                    If _queue.Count > 0 Then work = _queue.Dequeue()
                End SyncLock

                If work Is Nothing Then Exit Do

                Try
                    work()
                Catch ex As Exception
                    DiagnosticLogService.LogException("Audio.Command", ex)
                End Try
            Loop
        End Sub

        Private Function ReadyForPlayback() As Boolean
            Return _initialized AndAlso Not _initializationFailed AndAlso _handle <> IntPtr.Zero
        End Function

        Private Sub InitializeCore()
            If _initializationFailed OrElse _initialized Then Return

            Try
                MpvInterop.EnsureResolver()
                _handle = MpvInterop.Create()
                If _handle = IntPtr.Zero Then Throw New InvalidOperationException("libmpv konnte nicht erstellt werden.")

                SetOptionStringRaw("terminal", "no")
                SetOptionStringRaw("msg-level", "all=no")
                SetOptionStringRaw("config", "no")
                SetOptionStringRaw("input-default-bindings", "no")
                SetOptionStringRaw("osc", "no")
                ' Am Ende eines Titels soll mpv leerlaufen und nicht auf dem letzten Bild stehen
                ' bleiben: die Liste entscheidet, was als naechstes kommt.
                SetOptionStringRaw("keep-open", "no")

                ' KEIN BILD. Ein Titelbild in der Datei ist fuer mpv eine Videospur; ohne diese
                ' beiden Zeilen macht mpv dafuer ein eigenes Fenster auf. Das Bild holt sich die
                ' Anwendung selbst aus den Kennzeichen (siehe CoverArtService).
                SetOptionStringRaw("vid", "no")
                SetOptionStringRaw("audio-display", "no")
                SetOptionStringRaw("vo", "null")

                ' Ohne Luecke von Titel zu Titel. mpv steht ab Werk auf "weak" und laesst dabei die
                ' Stille am Dateiende stehen; bei einem durchgehend gemischten Album hoert man das.
                Dim gapless As Boolean
                SyncLock _syncRoot
                    gapless = _gapless
                End SyncLock
                SetOptionStringRaw("gapless-audio", If(gapless, "yes", "weak"))

                Dim replayGainMode As String
                Dim replayGainPreamp As Double
                SyncLock _syncRoot
                    replayGainMode = _replayGainMode
                    replayGainPreamp = _replayGainPreamp
                End SyncLock
                SetOptionStringRaw("replaygain", replayGainMode)
                SetOptionStringRaw("replaygain-preamp", replayGainPreamp.ToString("0.##", CultureInfo.InvariantCulture))

                Dim result = MpvInterop.Initialize(_handle)
                If result < 0 Then Throw New InvalidOperationException($"libmpv konnte nicht initialisiert werden ({result}).")

                ObservePropertyRaw(PropTimePos, "time-pos", MpvInterop.MpvFormat.Double)
                ObservePropertyRaw(PropDuration, "duration", MpvInterop.MpvFormat.Double)
                ObservePropertyRaw(PropPause, "pause", MpvInterop.MpvFormat.Flag)
                ObservePropertyRaw(PropMute, "mute", MpvInterop.MpvFormat.Flag)
                ObservePropertyRaw(PropVolume, "volume", MpvInterop.MpvFormat.Double)

                Dim muted As Boolean
                Dim volume As Double
                SyncLock _syncRoot
                    muted = _isMuted
                    volume = _volume
                End SyncLock
                SetPropertyStringRaw("mute", If(muted, "yes", "no"))
                SetPropertyStringRaw("volume", volume.ToString("0.##", CultureInfo.InvariantCulture))

                Dim handle = _handle
                _eventLoopStopping = False
                _eventThread = New Thread(Sub() EventLoop(handle)) With {
                    .IsBackground = True,
                    .Name = "libmpv-event-loop"
                }
                _initialized = True
                _eventThread.Start()
            Catch ex As Exception
                HandleInitializationFailure(ex)
            End Try
        End Sub

        Private Sub HandleInitializationFailure(ex As Exception)
            _initializationFailed = True
            _initialized = False
            SyncLock _syncRoot
                _disposed = True
                _initializationError = ex
            End SyncLock
            ' Nach einem gescheiterten Aufbau kommt nichts mehr: die Warteschlange wird
            ' geschlossen, damit der Befehlsfaden nicht bis zum Programmende wartet.
            SyncLock _queue
                _queueClosed = True
                Monitor.Pulse(_queue)
            End SyncLock

            ShutdownNative()
            RaiseEvent InitializationFailed(ex)
        End Sub

        Private Sub LoadCore(path As String, force As Boolean)
            ' Dasselbe zweimal zu laden setzt den Titel sichtbar auf den Anfang zurueck, und beim
            ' Video war es sogar ein Absturz. Der Riegel steht HIER, weil jeder Weg zu mpv durch
            ' diese Stelle geht und der Wert, an dem er haengt, derselbe ist, den mpv gerade
            ' wirklich spielt. Das Wiederholen eines Titels laedt mit Absicht neu: dafuer ist
            ' "force" da.
            If Not force AndAlso ReadyForPlayback() AndAlso
               String.Equals(path, _loadedPath, StringComparison.Ordinal) Then Return

            _loadedPath = Nothing
            If Not ReadyForPlayback() Then Return

            ' Eine Datei, die es nicht mehr gibt, geht gar nicht erst an mpv. mpv meldete sie nur
            ' als allgemeinen Fehler, ohne zu sagen, welcher Titel es war; hier ist der Pfad noch
            ' bekannt. Was vorher lief, wird angehalten - sonst spielte der alte Titel unter dem
            ' neuen weiter. Die Pruefung laeuft auf diesem Faden und nicht auf dem der Oberflaeche:
            ' ein haengendes Netzlaufwerk haelt dann nur den Ton auf. Adressen (cdda://, http://)
            ' sind keine Dateien und gehen ungeprueft durch.
            If Not path.Contains("://", StringComparison.Ordinal) AndAlso Not IO.File.Exists(path) Then
                CommandAsyncRaw(_handle, "stop")
                RaiseEvent LoadFailed(path)
                Return
            End If

            SetPauseCore(True)
            If CommandAsyncRaw(_handle, "loadfile", path, "replace") < 0 Then Return
            _loadedPath = path
            ' VOR dem Abspielen melden: die Oberflaeche setzt daran ihre Anzeige zurueck, und ein
            ' Zuruecksetzen NACH dem ersten Zeitbericht loeschte genau den wieder.
            RaiseEvent FileLoaded(path)
            If PendingPlay() Then SetPauseCore(False)
        End Sub

        Private Sub TogglePauseCore()
            If _initializationFailed Then Return

            Dim paused As Boolean
            SyncLock _syncRoot
                If _disposed Then Return
                paused = _isPaused
                _pendingPlay = paused
            End SyncLock

            SetPauseCore(Not paused)
        End Sub

        Private Sub StopCore()
            _loadedPath = Nothing
            If Not ReadyForPlayback() Then Return
            CommandAsyncRaw(_handle, "stop")
        End Sub

        Private Function PendingPlay() As Boolean
            SyncLock _syncRoot
                Return _pendingPlay
            End SyncLock
        End Function

        Private Sub SetPauseCore(value As Boolean)
            ' Der vorgemerkte Zustand steht in _pendingPlay; _isPaused beschreibt, was mpv
            ' TATSAECHLICH tut, und darf deshalb erst gesetzt werden, wenn der Befehl auch abgeht.
            If Not ReadyForPlayback() Then Return
            SyncLock _syncRoot
                _isPaused = value
            End SyncLock
            SetPropertyStringRaw("pause", If(value, "yes", "no"))
        End Sub

        ''' <summary>Baut alles Native ab. Laeuft ausschliesslich auf dem Befehlsfaden.</summary>
        Private Sub ShutdownNative()
            _initialized = False

            Dim handle = _handle
            _handle = IntPtr.Zero
            If handle = IntPtr.Zero Then Return

            Try
                CommandAsyncRaw(handle, "quit")
            Catch
            End Try

            Dim eventThread = _eventThread
            _eventThread = Nothing
            Volatile.Write(_eventLoopStopping, True)

            Try
                If eventThread IsNot Nothing AndAlso eventThread.IsAlive AndAlso
                   Not Object.ReferenceEquals(Thread.CurrentThread, eventThread) Then
                    ' Erst kurz warten, damit der Normalfall nichts protokolliert. Danach wird der
                    ' Nachzuegler gemeldet, aber nicht aufgegeben: den Handle stehen zu lassen
                    ' hiesse, ihn samt seiner nativen Puffer bis zum Programmende zu verlieren.
                    If Not eventThread.Join(2000) Then
                        DiagnosticLogService.LogException("Audio.Dispose",
                            New TimeoutException("Die Ereignisschleife von mpv hat nicht binnen zwei Sekunden angehalten."))
                        eventThread.Join()
                    End If
                End If
                MpvInterop.TerminateDestroy(handle)
            Catch ex As Exception
                DiagnosticLogService.LogException("Audio.Dispose", ex)
            End Try
        End Sub

        ' Ereignisfaden

        Private Sub EventLoop(handle As IntPtr)
            Do
                If Volatile.Read(_eventLoopStopping) Then Exit Do

                Dim eventPtr = MpvInterop.WaitEvent(handle, 0.2)
                If eventPtr = IntPtr.Zero Then Continue Do

                Dim ev = Marshal.PtrToStructure(Of MpvInterop.MpvEvent)(eventPtr)
                Select Case ev.EventId
                    Case MpvInterop.MpvEventId.None
                    Case MpvInterop.MpvEventId.PropertyChange
                        HandlePropertyChange(ev)
                    Case MpvInterop.MpvEventId.EndFile
                        Dim endData = Marshal.PtrToStructure(Of MpvInterop.MpvEventEndFile)(ev.Data)
                        RaiseEvent EndReached(CInt(endData.Reason), endData.Error)
                    Case MpvInterop.MpvEventId.Shutdown
                        ' mpv beendet sich selbst. Der Handle ist danach nur noch zum Wegwerfen
                        ' gut, und wer das nicht erfaehrt, schickt Befehle ins Leere.
                        RaiseEvent PlaybackTerminated()
                        Exit Do
                End Select
            Loop
        End Sub

        Private Sub HandlePropertyChange(ev As MpvInterop.MpvEvent)
            If ev.Data = IntPtr.Zero Then Return
            Dim prop = Marshal.PtrToStructure(Of MpvInterop.MpvEventProperty)(ev.Data)

            Select Case ev.ReplyUserData
                Case PropTimePos
                    If prop.Format <> MpvInterop.MpvFormat.Double OrElse prop.Data = IntPtr.Zero Then Return
                    RaiseEvent TimeChanged(Marshal.PtrToStructure(Of Double)(prop.Data))
                Case PropDuration
                    If prop.Format <> MpvInterop.MpvFormat.Double OrElse prop.Data = IntPtr.Zero Then Return
                    RaiseEvent DurationChanged(Marshal.PtrToStructure(Of Double)(prop.Data))
                Case PropPause
                    If prop.Format <> MpvInterop.MpvFormat.Flag OrElse prop.Data = IntPtr.Zero Then Return
                    Dim paused = Marshal.ReadInt32(prop.Data) <> 0
                    SyncLock _syncRoot
                        _isPaused = paused
                    End SyncLock
                    RaiseEvent PauseChanged(paused)
                Case PropMute
                    If prop.Format <> MpvInterop.MpvFormat.Flag OrElse prop.Data = IntPtr.Zero Then Return
                    Dim muted = Marshal.ReadInt32(prop.Data) <> 0
                    SyncLock _syncRoot
                        _isMuted = muted
                    End SyncLock
                    RaiseEvent MuteChanged(muted)
                Case PropVolume
                    If prop.Format <> MpvInterop.MpvFormat.Double OrElse prop.Data = IntPtr.Zero Then Return
                    Dim value = Marshal.PtrToStructure(Of Double)(prop.Data)
                    SyncLock _syncRoot
                        _volume = value
                    End SyncLock
                    RaiseEvent VolumeChanged(value)
            End Select
        End Sub

        ' Native Aufrufe, ausschliesslich vom Befehlsfaden

        Private Sub ObservePropertyRaw(replyUserData As ULong, propertyName As String, fileFormat As MpvInterop.MpvFormat)
            Using namePtr = New Utf8String(propertyName)
                Dim result = MpvInterop.ObserveProperty(_handle, replyUserData, namePtr.Pointer, fileFormat)
                If result < 0 Then Throw New InvalidOperationException($"libmpv observe_property({propertyName}) fehlgeschlagen ({result}).")
            End Using
        End Sub

        Private Shared Function CommandAsyncRaw(handle As IntPtr, ParamArray args As String()) As Integer
            If handle = IntPtr.Zero Then Return -1

            Dim allocations As New List(Of Utf8String)()
            Dim ptrs As New List(Of IntPtr)()
            Try
                For Each arg In args
                    Dim utf8 = New Utf8String(arg)
                    allocations.Add(utf8)
                    ptrs.Add(utf8.Pointer)
                Next
                ptrs.Add(IntPtr.Zero)

                Dim arrayPtr = Marshal.AllocHGlobal(IntPtr.Size * ptrs.Count)
                Try
                    For i = 0 To ptrs.Count - 1
                        Marshal.WriteIntPtr(arrayPtr, i * IntPtr.Size, ptrs(i))
                    Next
                    Return MpvInterop.CommandAsync(handle, 0UL, arrayPtr)
                Finally
                    Marshal.FreeHGlobal(arrayPtr)
                End Try
            Finally
                For Each allocation In allocations
                    allocation.Dispose()
                Next
            End Try
        End Function

        Private Sub SetOptionStringRaw(name As String, value As String)
            Using namePtr = New Utf8String(name), valuePtr = New Utf8String(value)
                Dim result = MpvInterop.SetOptionString(_handle, namePtr.Pointer, valuePtr.Pointer)
                If result < 0 Then Throw New InvalidOperationException($"libmpv option {name} fehlgeschlagen ({result}).")
            End Using
        End Sub

        Private Sub SetPropertyStringRaw(name As String, value As String)
            Using namePtr = New Utf8String(name), valuePtr = New Utf8String(value)
                MpvInterop.SetPropertyString(_handle, namePtr.Pointer, valuePtr.Pointer)
            End Using
        End Sub

        Private NotInheritable Class Utf8String
            Implements IDisposable

            Public ReadOnly Property Pointer As IntPtr

            Public Sub New(value As String)
                Dim bytes = System.Text.Encoding.UTF8.GetBytes(If(value, String.Empty) & ChrW(0))
                Pointer = Marshal.AllocHGlobal(bytes.Length)
                Marshal.Copy(bytes, 0, Pointer, bytes.Length)
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If Pointer <> IntPtr.Zero Then Marshal.FreeHGlobal(Pointer)
            End Sub
        End Class
    End Class

End Namespace
