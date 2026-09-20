Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text.Json
Imports FerrumKix.Models

Namespace Services

    ''' <summary>Die gespeicherte Wiedergabeliste.
    '''
    ''' <para>Gespeichert werden die gelesenen Kennzeichen mit, nicht nur die Pfade. Eine Liste mit
    ''' tausend Titeln beim Start erneut einzulesen dauert Sekunden, in denen die Anwendung mit
    ''' leeren Zeilen dastuende. Ob ein Eintrag noch stimmt, entscheidet die Anwendung beim
    ''' Abspielen: die Datei kann ohnehin jederzeit verschwunden sein.</para></summary>
    Public NotInheritable Class PlaylistStore
        Private Sub New()
        End Sub

        Private Shared ReadOnly SerializerOptions As New JsonSerializerOptions With {
            .WriteIndented = False
        }

        Public Shared ReadOnly Property PlaylistPath As String
            Get
                Return Path.Combine(DiagnosticLogService.AppDataDirectory, "playlist.json")
            End Get
        End Property

        Public Shared Function Load() As List(Of Track)
            Try
                Dim path = PlaylistPath
                If Not File.Exists(path) Then Return New List(Of Track)()
                Dim json = File.ReadAllText(path)
                If String.IsNullOrWhiteSpace(json) Then Return New List(Of Track)()
                Dim loaded = JsonSerializer.Deserialize(Of List(Of Track))(json, SerializerOptions)
                Return If(loaded, New List(Of Track)())
            Catch ex As Exception
                DiagnosticLogService.LogException("Playlist.Load", ex)
                Return New List(Of Track)()
            End Try
        End Function

        Public Shared Sub Save(tracks As IEnumerable(Of Track))
            Try
                Directory.CreateDirectory(DiagnosticLogService.AppDataDirectory)
                Dim json = JsonSerializer.Serialize(New List(Of Track)(tracks), SerializerOptions)
                Dim path = PlaylistPath
                Dim temporary = path & ".tmp"
                File.WriteAllText(temporary, json)
                File.Move(temporary, path, overwrite:=True)
            Catch ex As Exception
                DiagnosticLogService.LogException("Playlist.Save", ex)
            End Try
        End Sub

    End Class

End Namespace
