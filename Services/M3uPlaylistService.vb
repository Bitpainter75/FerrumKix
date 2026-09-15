Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports FerrumPlay.Models

Namespace Services

    ''' <summary>Schreibt einfache, portable M3U8-Dateien. Der eigene Kommentar bleibt fuer
    ''' andere Player unsichtbar, bewahrt aber das Sync-Ziel einer Liste.</summary>
    Public NotInheritable Class M3uPlaylistService
        Private Sub New()
        End Sub

        Private Const SyncTargetPrefix As String = "#FERRUMPLAY-SYNC-TARGET:"

        Public Shared Sub Save(filePath As String, tracks As IEnumerable(Of Track), syncTarget As String)
            If String.IsNullOrWhiteSpace(filePath) Then Throw New ArgumentException(NameOf(filePath))
            Dim root = Path.GetDirectoryName(Path.GetFullPath(filePath))
            Dim lines As New List(Of String) From {"#EXTM3U"}
            If Not String.IsNullOrWhiteSpace(syncTarget) Then lines.Add(SyncTargetPrefix & syncTarget.Trim())
            For Each track In tracks.Where(Function(entry) entry IsNot Nothing AndAlso Not entry.IsAudioCdTrack)
                Dim duration = Math.Max(-1, CInt(Math.Round(track.DurationSeconds)))
                lines.Add($"#EXTINF:{duration},{track.Artist} - {track.Title}")
                Dim entry = track.FilePath
                If Path.IsPathRooted(entry) Then entry = Path.GetRelativePath(root, entry)
                lines.Add(entry)
            Next
            File.WriteAllLines(filePath, lines, New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False))
        End Sub

        Public Shared Function Load(filePath As String, ByRef syncTarget As String) As List(Of String)
            syncTarget = String.Empty
            Dim root = Path.GetDirectoryName(Path.GetFullPath(filePath))
            Dim result As New List(Of String)()
            For Each raw In File.ReadLines(filePath)
                Dim line = raw.Trim()
                If line.StartsWith(SyncTargetPrefix, StringComparison.OrdinalIgnoreCase) Then
                    syncTarget = line.Substring(SyncTargetPrefix.Length).Trim()
                ElseIf line.Length > 0 AndAlso Not line.StartsWith("#", StringComparison.Ordinal) Then
                    result.Add(If(Path.IsPathRooted(line), line, Path.GetFullPath(Path.Combine(root, line))))
                End If
            Next
            Return result
        End Function
    End Class
End Namespace
