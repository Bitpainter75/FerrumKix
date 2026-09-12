Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Security.Cryptography
Imports Avalonia.Media.Imaging

Namespace Services

    ''' <summary>Besorgt das Titelbild zu einer Datei.
    '''
    ''' <para>Zwei Quellen, in dieser Reihenfolge: das in die Datei eingebettete Bild, sonst eine
    ''' Bilddatei im selben Ordner (cover.jpg, folder.jpg und die ueblichen Verwandten). Die erste
    ''' gehoert zum Titel, die zweite zum Album; wo beide da sind, gewinnt die genauere.</para>
    '''
    ''' <para>Gemerkt wird je Datei, denn ein Album mit wechselnden Bildern gibt es. Der Schluessel
    ''' fuer den Ordnerfund ist trotzdem der Ordner, damit dessen Bild nur einmal von der Platte
    ''' kommt.</para></summary>
    Public NotInheritable Class CoverArtService
        Private Sub New()
        End Sub

        ' Nebenlaeufig, weil das Laden auf einem Arbeitsfaden laeuft, waehrend die Oberflaeche liest.
        Private Shared ReadOnly TrackCovers As New ConcurrentDictionary(Of String, Bitmap)(StringComparer.Ordinal)
        Private Shared ReadOnly FolderCovers As New ConcurrentDictionary(Of String, Bitmap)(StringComparer.Ordinal)
        Private Shared ReadOnly ArtFiles As New ConcurrentDictionary(Of String, String)(StringComparer.Ordinal)
        ''' <summary>Die Cover gestreamter Titel, nach ihrer Adresse. Siehe <see cref="LoadRemote"/>.</summary>
        Private Shared ReadOnly RemoteCovers As New ConcurrentDictionary(Of String, Bitmap)(StringComparer.Ordinal)
        Private Shared ReadOnly HttpClient As New HttpClient With {.Timeout = TimeSpan.FromSeconds(12)}

        ''' <summary>Die Namen, unter denen ein Albumbild neben den Titeln liegt. Reihenfolge ist
        ''' Rangfolge.</summary>
        Private Shared ReadOnly FolderCoverNames As String() = {
            "cover", "folder", "front", "album", "albumart", "albumartsmall", "thumb"
        }

        Private Shared ReadOnly FolderCoverExtensions As String() = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}

        ''' <summary>Wie viele ausgelagerte Titelbilder der Zwischenspeicher behaelt. Genug fuer die
        ''' Alben einiger Abende, wenig genug, dass niemand den Ordner bemerkt.</summary>
        Private Const ArtCacheLimit As Integer = 200

        ''' <summary>Das Bild zu einer Tondatei, oder Nothing. Laeuft ueber Platte und Decoder und
        ''' gehoert deshalb NICHT auf den Anzeigefaden.</summary>
        Public Shared Function Load(filePath As String) As Bitmap
            If String.IsNullOrWhiteSpace(filePath) Then Return Nothing

            Dim cached As Bitmap = Nothing
            If TrackCovers.TryGetValue(filePath, cached) Then Return cached

            Dim bitmap = LoadEmbedded(filePath)
            If bitmap Is Nothing Then bitmap = LoadFromFolder(filePath)

            ' Auch ein Fehlschlag wird gemerkt. Sonst sucht die Anwendung bei jedem Wechsel auf
            ' denselben Titel erneut den ganzen Ordner ab, und das kostet bei Netzlaufwerken.
            TrackCovers(filePath) = bitmap
            Return bitmap
        End Function

        ''' <summary>Vergisst die gemerkten Bilder zu einer Datei.
        '''
        ''' <para>Nach dem Schreiben neuer Tags ist das gemerkte Bild das ALTE, und ohne dieses
        ''' Vergessen zeigte die Anwendung es weiter - ein frisch gesetztes Cover kaeme erst nach
        ''' einem Neustart an. Der Dateiname kann sich beim Taggen zudem geaendert haben,
        ''' deshalb nimmt die Methode mehrere Pfade auf einmal.</para>
        '''
        ''' <para>Freigegeben wird dabei NICHTS: das Bild kann noch unter dem Titelbild haengen.
        ''' Es aus der Liste zu nehmen genuegt, den Rest erledigt die Speicherbereinigung.</para></summary>
        Public Shared Sub Invalidate(ParamArray filePaths As String())
            For Each path In If(filePaths, Array.Empty(Of String)())
                If String.IsNullOrWhiteSpace(path) Then Continue For
                Dim cover As Bitmap = Nothing
                TrackCovers.TryRemove(path, cover)
                Dim artFile As String = Nothing
                ArtFiles.TryRemove(path, artFile)
            Next
        End Sub

        ''' <summary>Lädt ein extern bereitgestelltes Cover für einen Stream. Der Aufrufer führt
        ''' diese Methode auf einem Arbeitsfaden aus.</summary>
        Public Shared Function LoadRemote(url As String) As Bitmap
            If String.IsNullOrWhiteSpace(url) Then Return Nothing

            ' Gemerkt wie ein oertliches Cover: die Titel EINES Albums teilen sich dieselbe
            ' Adresse, und ohne diesen Zwischenspeicher holt jeder Titelwechsel dasselbe Bild
            ' erneut ueber das Netz - und liesse das vorige unbenutzt liegen.
            Dim cached As Bitmap = Nothing
            If RemoteCovers.TryGetValue(url, cached) Then Return cached

            Dim bitmap As Bitmap = Nothing
            Try
                Dim bytes = HttpClient.GetByteArrayAsync(url).GetAwaiter().GetResult()
                Using stream As New MemoryStream(bytes)
                    bitmap = New Bitmap(stream)
                End Using
            Catch ex As Exception
                DiagnosticLogService.Log("CoverArt.Remote", $"{url}: {ex.Message}")
                ' Nur ein Fehlschlag wird NICHT gemerkt: der Server kann beim naechsten Titel
                ' wieder da sein.
                Return Nothing
            End Try
            ' Hat ein anderer Faden dasselbe Bild zuerst abgelegt, gilt seines - das eigene wird
            ' dann gleich wieder freigegeben.
            Dim stored = RemoteCovers.GetOrAdd(url, bitmap)
            If Not Object.ReferenceEquals(stored, bitmap) Then bitmap.Dispose()
            Return stored
        End Function

        ''' <summary>Das Titelbild als DATEI, fuer MPRIS: dort geht ein Bild als Adresse hinaus und
        ''' nicht als Daten. Dieselbe Rangfolge wie <see cref="Load"/>. Steckt das Bild in der
        ''' Tondatei, wird es in den Zwischenspeicher geschrieben, benannt nach seinem Inhalt - die
        ''' Titel eines Albums tragen meist dasselbe Bild und teilen sich dann eine Datei. Liegt es
        ''' im Ordner, ist es diese Datei selbst. Leer, wenn es kein Bild gibt. Laeuft ueber die
        ''' Platte, also NICHT auf dem Anzeigefaden.</summary>
        Public Shared Function ExportArtFile(filePath As String) As String
            If String.IsNullOrWhiteSpace(filePath) Then Return String.Empty

            Dim cached As String = Nothing
            If ArtFiles.TryGetValue(filePath, cached) AndAlso (cached.Length = 0 OrElse File.Exists(cached)) Then Return cached

            Dim result = WriteEmbeddedArt(filePath)
            If result.Length = 0 Then result = FindFolderCoverFile(filePath)
            ArtFiles(filePath) = result
            Return result
        End Function

        ''' <summary>Raeumt den Zwischenspeicher der ausgelagerten Bilder auf: die zuletzt benutzten
        ''' bleiben, der Rest geht. Einmal je Start, im Hintergrund.</summary>
        Public Shared Sub PruneArtCache()
            Try
                Dim folder = ArtCacheDirectory
                If Not Directory.Exists(folder) Then Return
                Dim stale = New DirectoryInfo(folder).GetFiles().
                    OrderByDescending(Function(f) f.LastWriteTimeUtc).
                    Skip(ArtCacheLimit).
                    ToList()
                For Each entry In stale
                    entry.Delete()
                Next
            Catch ex As Exception
                DiagnosticLogService.Log("CoverArt.Prune", ex.Message)
            End Try
        End Sub

        ''' <summary>$XDG_CACHE_HOME/FerrumPlay/covers, ersatzweise ~/.cache/FerrumPlay/covers. Ein
        ''' Zwischenspeicher gehoert nicht zu den Einstellungen: wer ihn loescht, verliert nichts.</summary>
        Private Shared ReadOnly Property ArtCacheDirectory As String
            Get
                Dim baseDir = Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                If String.IsNullOrWhiteSpace(baseDir) Then
                    baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache")
                End If
                Return Path.Combine(baseDir, "FerrumPlay", "covers")
            End Get
        End Property

        Private Shared Function LoadEmbedded(filePath As String) As Bitmap
            Try
                Dim picture = ReadEmbeddedPicture(filePath)
                If picture.Data Is Nothing Then Return Nothing
                Using stream As New MemoryStream(picture.Data)
                    Return New Bitmap(stream)
                End Using
            Catch ex As Exception
                DiagnosticLogService.Log("CoverArt.Embedded", $"{filePath}: {ex.Message}")
                Return Nothing
            End Try
        End Function

        ''' <summary>Das eingebettete Bild als Bytes samt Bildformat, oder (Nothing, Nothing).</summary>
        Private Shared Function ReadEmbeddedPicture(filePath As String) As (Data As Byte(), MimeType As String)
            Using file = TagLib.File.Create(filePath)
                Dim tag = file.Tag
                If tag Is Nothing OrElse tag.Pictures Is Nothing Then Return (Nothing, Nothing)

                Dim best As TagLib.IPicture = Nothing
                For Each picture In tag.Pictures
                    If picture Is Nothing OrElse picture.Data Is Nothing OrElse picture.Data.Count = 0 Then Continue For
                    ' Die Vorderseite hat Vorrang. Viele Dateien tragen ausserdem ein
                    ' Kuenstlerfoto oder die Rueckseite, und die will hier niemand sehen.
                    If picture.Type = TagLib.PictureType.FrontCover Then
                        best = picture
                        Exit For
                    End If
                    If best Is Nothing Then best = picture
                Next

                If best Is Nothing Then Return (Nothing, Nothing)
                Return (best.Data.Data, best.MimeType)
            End Using
        End Function

        Private Shared Function WriteEmbeddedArt(filePath As String) As String
            Try
                Dim picture = ReadEmbeddedPicture(filePath)
                If picture.Data Is Nothing Then Return String.Empty

                Dim hash As String
                Using sha = SHA1.Create()
                    hash = Convert.ToHexString(sha.ComputeHash(picture.Data)).Substring(0, 16).ToLowerInvariant()
                End Using

                Dim folder = ArtCacheDirectory
                Directory.CreateDirectory(folder)
                Dim target = Path.Combine(folder, hash & ExtensionForMimeType(picture.MimeType))
                If File.Exists(target) Then
                    ' Beruehren, damit das Aufraeumen die zuletzt gebrauchten stehen laesst.
                    File.SetLastWriteTimeUtc(target, Date.UtcNow)
                Else
                    Dim temporary = target & ".tmp"
                    File.WriteAllBytes(temporary, picture.Data)
                    File.Move(temporary, target, overwrite:=True)
                End If
                Return target
            Catch ex As Exception
                DiagnosticLogService.Log("CoverArt.Export", $"{filePath}: {ex.Message}")
                Return String.Empty
            End Try
        End Function

        Private Shared Function ExtensionForMimeType(mimeType As String) As String
            Dim text = If(mimeType, String.Empty).ToLowerInvariant()
            If text.Contains("png") Then Return ".png"
            If text.Contains("webp") Then Return ".webp"
            If text.Contains("gif") Then Return ".gif"
            If text.Contains("bmp") Then Return ".bmp"
            Return ".jpg"
        End Function

        ''' <summary>Der Parameter heisst NICHT "path". VB unterscheidet keine Gross- und
        ''' Kleinschreibung, und ein so benannter Parameter verdeckt die Klasse
        ''' <see cref="IO.Path"/> im ganzen Rumpf.</summary>
        Private Shared Function LoadFromFolder(filePath As String) As Bitmap
            Dim folder = FolderOf(filePath)
            If String.IsNullOrEmpty(folder) Then Return Nothing

            Dim cached As Bitmap = Nothing
            If FolderCovers.TryGetValue(folder, cached) Then Return cached

            Dim bitmap = FindFolderCover(folder)
            FolderCovers(folder) = bitmap
            Return bitmap
        End Function

        Private Shared Function FindFolderCover(folder As String) As Bitmap
            For Each candidate In FolderCoverCandidates(folder)
                Try
                    Using stream = File.OpenRead(candidate)
                        Return New Bitmap(stream)
                    End Using
                Catch ex As Exception
                    DiagnosticLogService.Log("CoverArt.Folder", $"{candidate}: {ex.Message}")
                End Try
            Next
            Return Nothing
        End Function

        Private Shared Function FindFolderCoverFile(filePath As String) As String
            Dim folder = FolderOf(filePath)
            If String.IsNullOrEmpty(folder) Then Return String.Empty
            Return If(FolderCoverCandidates(folder).FirstOrDefault(), String.Empty)
        End Function

        ''' <summary>Die Bilddateien im Ordner, die als Albumbild in Frage kommen, in der
        ''' Rangfolge der Namen.</summary>
        Private Shared Iterator Function FolderCoverCandidates(folder As String) As IEnumerable(Of String)
            For Each name In FolderCoverNames
                For Each extension In FolderCoverExtensions
                    Dim candidate As String
                    Try
                        candidate = Path.Combine(folder, name & extension)
                        If Not File.Exists(candidate) Then Continue For
                    Catch ex As Exception
                        DiagnosticLogService.Log("CoverArt.Folder", $"{folder}: {ex.Message}")
                        Return
                    End Try
                    Yield candidate
                Next
            Next
        End Function

        Private Shared Function FolderOf(filePath As String) As String
            Try
                Return Path.GetDirectoryName(filePath)
            Catch
                Return Nothing
            End Try
        End Function

    End Class

End Namespace
