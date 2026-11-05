Imports System
Imports System.IO
Imports System.Threading.Tasks
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Media.Imaging
Imports Avalonia.Platform.Storage
Imports FerrumKix.Services

Namespace Views

    ''' <summary>"Cover speichern unter" fuer beide Coverflaechen: die der Wiedergabe und die des
    ''' Taggens. Gespeichert werden die Bilddaten, wie sie vorliegen; nur wo es keine gibt (ein
    ''' Cover, das allein als angezeigtes Bild existiert), geht das Bild als PNG hinaus.</summary>
    Friend NotInheritable Class CoverSaveDialog
        Private Sub New()
        End Sub

        ''' <summary>Fragt nach dem Ziel und schreibt das Bild. <paramref name="data"/> darf leer
        ''' sein, dann wird <paramref name="fallback"/> als PNG gespeichert. Liefert, ob etwas
        ''' geschrieben wurde.</summary>
        Public Shared Async Function SaveAsync(owner As Visual, data As Byte(), extension As String, fallback As Bitmap, suggestedName As String) As Task(Of Boolean)
            Dim storage = TopLevel.GetTopLevel(owner)?.StorageProvider
            If storage Is Nothing Then Return False
            Dim hasData = data IsNot Nothing AndAlso data.Length > 0
            If Not hasData AndAlso fallback Is Nothing Then Return False
            Dim ext = If(hasData, If(String.IsNullOrWhiteSpace(extension), CoverArtService.ExtensionForData(data), extension), ".png")
            Dim file = Await storage.SaveFilePickerAsync(New FilePickerSaveOptions With {
                .Title = LocalizationService.T("Cover speichern unter"),
                .SuggestedFileName = SafeName(suggestedName) & ext,
                .DefaultExtension = ext.TrimStart("."c),
                .ShowOverwritePrompt = True,
                .FileTypeChoices = {New FilePickerFileType(LocalizationService.T("Bilder")) With {.Patterns = {"*" & ext}}}})
            Dim target = file?.TryGetLocalPath()
            If String.IsNullOrWhiteSpace(target) Then Return False
            If hasData Then
                Await IO.File.WriteAllBytesAsync(target, data)
            Else
                fallback.Save(target)
            End If
            Return True
        End Function

        Private Shared Function SafeName(name As String) As String
            Dim text = If(name, String.Empty).Trim()
            For Each bad In Path.GetInvalidFileNameChars() : text = text.Replace(bad, " "c) : Next
            text = text.Trim().Trim("."c).Trim()
            Return If(text.Length = 0, "Cover", text)
        End Function
    End Class

End Namespace
