Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Runtime.InteropServices
Imports Microsoft.Win32.SafeHandles
Imports FerrumPlay.Models

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

        Private Const CdromReadTocHeader As UInteger = &H5305UI
        Private Const CdromReadTocEntry As UInteger = &H5306UI
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

        <DllImport("libc", SetLastError:=True)>
        Private Shared Function ioctl(handle As SafeFileHandle, request As UInteger, ByRef value As TocHeader) As Integer
        End Function

        <DllImport("libc", SetLastError:=True)>
        Private Shared Function ioctl(handle As SafeFileHandle, request As UInteger, ByRef value As TocEntry) As Integer
        End Function

        ''' <summary>Findet die erste eingelegte Audio-CD und gibt ihre Audiotitel zurueck.
        ''' Ein leeres Ergebnis ist kein Fehler: Es gibt kein Laufwerk, kein Medium, keine
        ''' Audio-CD, oder der Benutzer hat keine Leseberechtigung fuer das Laufwerk.</summary>
        Public Shared Function ReadFirstDisc() As List(Of Track)
            If Not OperatingSystem.IsLinux() Then Return New List(Of Track)()

            For Each devicePath In LinuxOpticalDevices()
                Dim tracks = ReadDisc(devicePath)
                If tracks.Count > 0 Then Return tracks
            Next
            Return New List(Of Track)()
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

        Private Shared Function ReadDisc(devicePath As String) As List(Of Track)
            Dim result As New List(Of Track)()
            Try
                Using stream As New FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                    Dim header As TocHeader
                    If ioctl(stream.SafeFileHandle, CdromReadTocHeader, header) <> 0 OrElse
                       header.FirstTrack = 0 OrElse header.LastTrack < header.FirstTrack Then Return result

                    Dim entries As New Dictionary(Of Integer, TocEntry)()
                    For number = CInt(header.FirstTrack) To CInt(header.LastTrack)
                        Dim entry As New TocEntry With {.Track = CByte(number), .Format = 1} ' CDROM_LBA
                        If ioctl(stream.SafeFileHandle, CdromReadTocEntry, entry) = 0 Then entries(number) = entry
                    Next

                    Dim leadout As New TocEntry With {.Track = CdromLeadout, .Format = 1}
                    If ioctl(stream.SafeFileHandle, CdromReadTocEntry, leadout) <> 0 Then Return result

                    Dim hasAudioTrack = False
                    For Each pair In entries
                        If Not IsDataTrack(pair.Value) Then hasAudioTrack = True
                    Next
                    If Not hasAudioTrack Then Return result

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
                        result.Add(New Track With {
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
