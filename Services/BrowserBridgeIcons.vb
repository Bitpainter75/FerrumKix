Imports System
Imports System.Collections.Concurrent
Imports System.IO
Imports System.Net
Imports System.Threading.Tasks
Imports Avalonia.Platform
Imports SkiaSharp

Namespace Services

    ''' <summary>Das Symbol der Anwendung fuer den Tab des Browsers und den Startbildschirm eines
    ''' Telefons: /favicon.ico (das fragt ein Browser von selbst ab), /favicon.png in 32 Punkten und
    ''' /apple-touch-icon.png in 180 Punkten, dazu /icon-192.png. Gerechnet aus dem Programmsymbol
    ''' (Assets/FerrumKix_Icon_256.png) beim ersten Abruf und danach gehalten. Die ICO-Datei traegt
    ''' PNG-Bilder in 16, 32 und 48 Punkten; das verstehen alle heutigen Browser.
    '''
    ''' <para>OHNE ANMELDUNG ABRUFBAR: das Symbol verraet nichts, was nicht in jedem Paket der
    ''' Anwendung liegt. Die Pruefung des Wegs (IsAcceptedRoute) gilt trotzdem.</para></summary>
    Partial Public NotInheritable Class BrowserBridge

        Friend Const IconIcoRoute As String = "/favicon.ico"
        Friend Const IconPngRoute As String = "/favicon.png"
        Friend Const TouchIconRoute As String = "/apple-touch-icon.png"
        Friend Const LargeIconRoute As String = "/icon-192.png"

        Private Const AppIconPngAsset As String = "avares://FerrumKix/Assets/FerrumKix_Icon_256.png"

        Private Shared ReadOnly IcoSizes As Integer() = {16, 32, 48}

        Private Shared ReadOnly IconCache As New ConcurrentDictionary(Of String, Byte())(StringComparer.Ordinal)

        ''' <summary>Gehoert der Pfad einem der Symbole?</summary>
        Friend Shared Function IsIconRoute(path As String) As Boolean
            Return path = IconIcoRoute OrElse path = IconPngRoute OrElse path = TouchIconRoute OrElse path = LargeIconRoute
        End Function

        ''' <summary>Das Symbol in der Groesse des Pfads, als PNG (bzw. ICO fuer /favicon.ico).</summary>
        Friend Shared Function IconBytes(path As String) As Byte()
            Return IconCache.GetOrAdd(path, AddressOf BuildIcon)
        End Function

        Private Shared Function BuildIcon(path As String) As Byte()
            Using source = SKBitmap.Decode(ReadAsset(AppIconPngAsset))
                If path = IconIcoRoute Then Return BuildIco(source)
                Return ScaledPng(source, If(path = IconPngRoute, 32, If(path = TouchIconRoute, 180, 192)))
            End Using
        End Function

        Private Shared Function ScaledPng(source As SKBitmap, size As Integer) As Byte()
            Using scaled = source.Resize(New SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul),
                                         New SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                Using image = SKImage.FromBitmap(scaled), data = image.Encode(SKEncodedImageFormat.Png, 100)
                    Return data.ToArray()
                End Using
            End Using
        End Function

        ''' <summary>Eine ICO-Datei: Kopf, je Bild ein Verzeichniseintrag, danach die PNG-Daten.</summary>
        Private Shared Function BuildIco(source As SKBitmap) As Byte()
            Dim images = Array.ConvertAll(IcoSizes, Function(size) ScaledPng(source, size))
            Using output As New MemoryStream(), writer As New BinaryWriter(output)
                writer.Write(CUShort(0))                ' reserviert
                writer.Write(CUShort(1))                ' Typ: Symbol
                writer.Write(CUShort(images.Length))
                Dim offset = 6 + 16 * images.Length
                For i = 0 To images.Length - 1
                    writer.Write(CByte(IcoSizes(i) And &HFF))   ' Breite (0 hiesse 256)
                    writer.Write(CByte(IcoSizes(i) And &HFF))   ' Hoehe
                    writer.Write(CByte(0))              ' keine Palette
                    writer.Write(CByte(0))              ' reserviert
                    writer.Write(CUShort(1))            ' Farbebenen
                    writer.Write(CUShort(32))           ' Bit je Punkt
                    writer.Write(CUInt(images(i).Length))
                    writer.Write(CUInt(offset))
                    offset += images(i).Length
                Next
                For Each image In images
                    writer.Write(image)
                Next
                writer.Flush()
                Return output.ToArray()
            End Using
        End Function

        Private Shared Function ReadAsset(asset As String) As Byte()
            Using stream = AssetLoader.Open(New Uri(asset)), copy As New MemoryStream()
                stream.CopyTo(copy)
                Return copy.ToArray()
            End Using
        End Function

        Private Shared Async Function ServeIconAsync(context As HttpListenerContext, path As String) As Task
            Dim bytes = IconBytes(path)
            Dim response = context.Response
            response.StatusCode = 200
            response.ContentType = If(path = IconIcoRoute, "image/x-icon", "image/png")
            response.Headers("Cache-Control") = "public, max-age=86400"
            response.Headers("X-Content-Type-Options") = "nosniff"
            response.ContentLength64 = bytes.Length
            Await response.OutputStream.WriteAsync(bytes)
            response.Close()
        End Function
    End Class
End Namespace
