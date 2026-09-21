Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Runtime.InteropServices
Imports Microsoft.Win32.SafeHandles
Imports FerrumKix.Models

Namespace Services

    ''' <summary>Liest das Inhaltsverzeichnis einer eingelegten Audio-CD.
    '''
    ''' <para>mpv spielt CDDA selbst ab; dieser Dienst liest nur die TOC, damit jeder Titel als
    ''' eigene Zeile in der Wiedergabeliste erscheint. Unter Linux erfolgt das direkt ueber die
    ''' CDROM-ioctls. Das vermeidet ein zusaetzliches Programm wie <c>cd-info</c> und funktioniert
    ''' auch in einer minimalen Desktop-Installation. Andere Systeme bleiben bewusst ohne falsche
    ''' Erkennung: mpv darf dort weiterhin sein Standardlaufwerk benutzen.</para></summary>
    Public NotInheritable Class AudioCdService
        Private Sub New()
        End Sub

        ' Das Oeffnen eines optischen Laufwerks ist KEIN harmloses Oeffnen einer Datei: viele
        ' Laufwerke ziehen die Lade dabei ein. Wer alle fuenf Sekunden nachsieht, ob eine CD drin
        ' ist, bekommt sie beim Einlegen immer wieder vor der Hand zugefahren. Mit O_NONBLOCK
        ' unterbleibt das - und der Zustand laesst sich damit trotzdem abfragen.
        Private Const OpenReadOnly As Integer = 0
        Private Const OpenNonBlock As Integer = &H800   ' O_NONBLOCK unter Linux (x86-64, aarch64)

        ''' <summary>CDROM_DRIVE_STATUS. Sagt, ob ueberhaupt eine lesbare CD da ist, ohne sie
        ''' anzufassen.</summary>
        Private Const CdromDriveStatus As UInteger = &H5326UI
        ''' <summary>CDSL_CURRENT - der gerade eingelegte Traeger.</summary>
        Private Const CurrentSlot As Integer = &H7FFFFFFF
        ''' <summary>CDS_DISC_OK. Alles andere heisst: keine CD, Lade offen oder noch nicht bereit.</summary>
        Private Const DriveDiscOk As Integer = 4

        Private Const CdromReadTocHeader As UInteger = &H5305UI
        Private Const CdromReadTocEntry As UInteger = &H5306UI
        ''' <summary>CDROMEJECT: oeffnet die Lade des optischen Laufwerks.</summary>
        Private Const CdromEject As UInteger = &H5309UI
        Private Const CdromLeadout As Byte = &HAA
        Private Const CdromDataTrack As Byte = &H4

        <StructLayout(LayoutKind.Sequential, Pack:=1)>
        Private Structure TocHeader
            Public FirstTrack As Byte
            Public LastTrack As Byte
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Private Structure TocEntry
            Public Track As Byte
            Public AdrControl As Byte
            Public Format As Byte
            Public Address As Integer
            Public DataMode As Byte
        End Structure

        <DllImport("libc", SetLastError:=True, EntryPoint:="open")>
        Private Shared Function OpenDevice(<MarshalAs(UnmanagedType.LPUTF8Str)> path As String, flags As Integer) As Integer
        End Function

        <DllImport("libc", SetLastError:=True, EntryPoint:="ioctl")>
        Private Shared Function ioctl(handle As SafeFileHandle, request As UInteger, value As Integer) As Integer
        End Function

        <DllImport("libc", SetLastError:=True)>
        Private Shared Function ioctl(handle As SafeFileHandle, request As UInteger, ByRef value As TocHeader) As Integer
        End Function

        <DllImport("libc", SetLastError:=True)>
        Private Shared Function ioctl(handle As SafeFileHandle, request As UInteger, ByRef value As TocEntry) As Integer
        End Function

        ''' <summary>Das Inhaltsverzeichnis einer CD, wie es die Erkennung braucht.
        '''
        ''' <para>Die ADRESSEN sind der Punkt: aus ihnen wird die Disc-Kennung berechnet, mit der
        ''' sich die CD bei MusicBrainz nachschlagen laesst. Zwei CDs mit denselben Spurlaengen
        ''' haben dieselbe Kennung - genau so ist es gemeint, es ist ein Fingerabdruck des
        ''' Inhaltsverzeichnisses und keine Kennung der Pressung.</para></summary>
        Public NotInheritable Class DiscToc
            Public Property FirstTrack As Integer
            Public Property LastTrack As Integer
            ''' <summary>Adresse des Lead-out, also wo die letzte Spur endet.</summary>
            Public Property LeadOutLba As Integer
            ''' <summary>Startadresse je Spurnummer - AUCH die von Datenspuren. Die Kennung wird
            ''' ueber das ganze Inhaltsverzeichnis gebildet, nicht nur ueber die Audiospuren.</summary>
            Public Property StartLba As New Dictionary(Of Integer, Integer)()
        End Class

        ''' <summary>Titel UND Inhaltsverzeichnis einer CD.</summary>
        Public NotInheritable Class DiscInfo
            Public Property Tracks As New List(Of Track)()
            Public Property Toc As DiscToc
            Public Property DevicePath As String = String.Empty
        End Class

        ''' <summary>Findet die erste eingelegte Audio-CD und gibt ihre Audiotitel zurueck.
        ''' Ein leeres Ergebnis ist kein Fehler: Es gibt kein Laufwerk, kein Medium, keine
        ''' Audio-CD, oder der Benutzer hat keine Leseberechtigung fuer das Laufwerk.</summary>
        Public Shared Function ReadFirstDisc() As List(Of Track)
            Return ReadFirstDiscInfo().Tracks
        End Function

        ''' <summary>Wie <see cref="ReadFirstDisc"/>, aber mit dem Inhaltsverzeichnis daneben.</summary>
        Public Shared Function ReadFirstDiscInfo() As DiscInfo
            If Not OperatingSystem.IsLinux() Then Return New DiscInfo()

            For Each devicePath In LinuxOpticalDevices()
                Dim info = ReadDisc(devicePath)
                If info.Tracks.Count > 0 Then Return info
            Next
            Return New DiscInfo()
        End Function

        ''' <summary>Wirft die CD aus dem angegebenen Laufwerk aus. Das Oeffnen erfolgt wie beim
        ''' TOC-Lesen nichtblockierend, damit ein nicht bereites Laufwerk die Oberflaeche nicht
        ''' festhaelt.</summary>
        Public Shared Function Eject(devicePath As String) As Boolean
            If Not OperatingSystem.IsLinux() OrElse String.IsNullOrWhiteSpace(devicePath) Then Return False
            Try
                Dim descriptor = OpenDevice(devicePath, OpenReadOnly Or OpenNonBlock)
                If descriptor < 0 Then Return False
                Using handle As New SafeFileHandle(New IntPtr(descriptor), ownsHandle:=True)
                    Return ioctl(handle, CdromEject, 0) = 0
                End Using
            Catch ex As Exception
                DiagnosticLogService.LogException("AudioCd.Eject", ex)
                Return False
            End Try
        End Function

        Private Shared Iterator Function LinuxOpticalDevices() As IEnumerable(Of String)
            Dim root = "/sys/class/block"
            If Not Directory.Exists(root) Then Return

            Dim entries As String()
            Try
                entries = Directory.GetDirectories(root)
            Catch ex As Exception
                DiagnosticLogService.Log("AudioCd.Devices", ex.Message)
                Return
            End Try

            Array.Sort(entries, StringComparer.Ordinal)
            For Each entry In entries
                Dim name = Path.GetFileName(entry)
                ' srN ist die Kernel-Bezeichnung fuer SCSI/ATAPI-Optiklaufwerke. Die Datei
                ' "device/type" enthaelt fuer CD/DVD-Laufwerke den SCSI-Typ 5.
                If Not name.StartsWith("sr", StringComparison.Ordinal) Then Continue For
                Try
                    If File.ReadAllText(Path.Combine(entry, "device", "type")).Trim() <> "5" Then Continue For
                Catch
                    Continue For
                End Try
                Dim devicePath = Path.Combine("/dev", name)
                If File.Exists(devicePath) Then Yield devicePath
            Next
        End Function

        Private Shared Function ReadDisc(devicePath As String) As DiscInfo
            Dim result As New DiscInfo With {.DevicePath = devicePath}
            Try
                ' Selbst geoeffnet statt ueber FileStream: nur so laesst sich O_NONBLOCK setzen,
                ' und ohne das faehrt die Lade beim Nachsehen zu.
                Dim descriptor = OpenDevice(devicePath, OpenReadOnly Or OpenNonBlock)
                If descriptor < 0 Then Return result
                Using handle As New SafeFileHandle(New IntPtr(descriptor), ownsHandle:=True)
                    ' Erst fragen, ob ueberhaupt etwas Lesbares drin ist. Eine offene Lade oder ein
                    ' noch anlaufendes Laufwerk soll gar nicht erst angesprochen werden.
                    If ioctl(handle, CdromDriveStatus, CurrentSlot) <> DriveDiscOk Then Return result

                    Dim header As TocHeader
                    If ioctl(handle, CdromReadTocHeader, header) <> 0 OrElse
                       header.FirstTrack = 0 OrElse header.LastTrack < header.FirstTrack Then Return result

                    Dim entries As New Dictionary(Of Integer, TocEntry)()
                    For number = CInt(header.FirstTrack) To CInt(header.LastTrack)
                        Dim entry As New TocEntry With {.Track = CByte(number), .Format = 1} ' CDROM_LBA
                        If ioctl(handle, CdromReadTocEntry, entry) = 0 Then entries(number) = entry
                    Next

                    Dim leadout As New TocEntry With {.Track = CdromLeadout, .Format = 1}
                    If ioctl(handle, CdromReadTocEntry, leadout) <> 0 Then Return result

                    Dim hasAudioTrack = False
                    For Each pair In entries
                        If Not IsDataTrack(pair.Value) Then hasAudioTrack = True
                    Next
                    If Not hasAudioTrack Then Return result

                    ' Das Inhaltsverzeichnis im Rohzustand - daraus entsteht die Disc-Kennung.
                    Dim toc As New DiscToc With {.FirstTrack = CInt(header.FirstTrack),
                                                 .LastTrack = CInt(header.LastTrack),
                                                 .LeadOutLba = leadout.Address}
                    For Each pair In entries
                        toc.StartLba(pair.Key) = pair.Value.Address
                    Next
                    result.Toc = toc

                    For number = CInt(header.FirstTrack) To CInt(header.LastTrack)
                        Dim entry As TocEntry
                        If Not entries.TryGetValue(number, entry) OrElse IsDataTrack(entry) Then Continue For

                        Dim nextLba = leadout.Address
                        For nextNumber = number + 1 To CInt(header.LastTrack)
                            Dim nextEntry As TocEntry
                            If entries.TryGetValue(nextNumber, nextEntry) Then
                                nextLba = nextEntry.Address
                                Exit For
                            End If
                        Next
                        Dim seconds = Math.Max(0, (nextLba - entry.Address) / 75.0)
                        ' Die letzte physische Spur begrenzt auch die letzte Audiospur einer
                        ' Mixed-Mode-CD sauber vor einer eventuell folgenden Datenspur.
                        result.Tracks.Add(New Track With {
                            .FilePath = Track.CreateAudioCdPath(devicePath, number, CInt(header.LastTrack)),
                            .Title = $"Titel {number:00}",
                            .Album = "Audio-CD",
                            .TrackNumber = number,
                            .DurationSeconds = seconds,
                            .Codec = "CDDA",
                            .SampleRate = 44100,
                            .Channels = 2,
                            .TagsReadUtc = Date.UtcNow
                        })
                    Next
                End Using
            Catch ex As Exception
                ' Ein leeres Fach und fehlende Gruppenrechte sind normale Situationen. Diese
                ' Methode wird vom Einlege-Monitor regelmaessig aufgerufen; ein Protokolleintrag
                ' pro Intervall wuerde die nützlichen Diagnosen darin verdraengen.
            End Try
            Return result
        End Function

        ''' <summary>Im Linux-Kernel belegen ADR und CTRL je vier Bits desselben Bytes; CTRL liegt
        ''' im hohen Halbbyte. <c>CDROM_DATA_TRACK</c> bezieht sich dagegen auf den schon
        ''' getrennten CTRL-Wert.</summary>
        Private Shared Function IsDataTrack(entry As TocEntry) As Boolean
            Dim control = CByte(entry.AdrControl >> 4)
            Return (control And CdromDataTrack) <> 0
        End Function
    End Class

End Namespace
