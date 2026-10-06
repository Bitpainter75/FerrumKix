Imports System
Imports System.Collections.Generic
Imports System.IO
Imports Avalonia.Media.Imaging
Imports Avalonia.Platform
Imports SkiaSharp

Namespace Services

    ''' <summary>Faerbt den rechten, akzentfarbenen Teil des Namenszugs in der Fensterleiste um,
    ''' ohne dessen Verlauf, Glanzkante und Schatten zu verlieren.</summary>
    Public NotInheritable Class AccentLogoService

        Private Sub New()
        End Sub

        Public Const LogoUri As String = "avares://FerrumKix/Assets/FerrumKix_TopBar.png"

        ''' <summary>Die Werksfarbe des rechten Schriftteils.</summary>
        Private Const LogoAccent As String = "#F08A1A"

        ''' <summary>"Ferrum" endet bei rund 69 Prozent der Bildbreite; ab hier beginnt "Kix".
        ''' Die Grenze bewahrt den kuehlen Schriftteil vor der Farbtondrehung.</summary>
        Private Const KixStartFraction As Double = 0.692

        Private Shared ReadOnly _cache As New Dictionary(Of String, Bitmap)(StringComparer.Ordinal)
        Private Shared ReadOnly _cacheLock As New Object()

        ''' <summary>Gibt den Namenszug mit dem eingestellten Akzent zurueck. Falls das Tönen oder
        ''' Laden fehlschlaegt, bleibt wenigstens der unveraenderte Namenszug sichtbar.</summary>
        Public Shared Function GetTintedLogo(accentColor As String) As Bitmap
            Dim key = If(accentColor, String.Empty).ToUpperInvariant()
            SyncLock _cacheLock
                Dim cached As Bitmap = Nothing
                If _cache.TryGetValue(key, cached) Then Return cached
            End SyncLock

            Dim created As Bitmap = Nothing
            Try
                created = CreateTintedLogo(accentColor)
            Catch ex As Exception
                DiagnosticLogService.LogException("AccentLogoService.GetTintedLogo", ex)
            End Try
            If created Is Nothing Then
                Try
                    Using stream = AssetLoader.Open(New Uri(LogoUri))
                        created = New Bitmap(stream)
                    End Using
                Catch ex As Exception
                    DiagnosticLogService.LogException("AccentLogoService.LoadPlainLogo", ex)
                    Return Nothing
                End Try
            End If

            SyncLock _cacheLock
                _cache(key) = created
            End SyncLock
            Return created
        End Function

        Private Shared Function CreateTintedLogo(accentColor As String) As Bitmap
            Dim accent As SKColor
            If Not SKColor.TryParse(If(accentColor, String.Empty), accent) Then Return Nothing
            Dim reference = SKColor.Parse(LogoAccent)

            Using stream = AssetLoader.Open(New Uri(LogoUri))
                Using bitmap = SKBitmap.Decode(stream)
                    If bitmap Is Nothing Then Return Nothing
                    TintPixels(bitmap, reference, accent)
                    Using image = SKImage.FromBitmap(bitmap)
                        Using data = image.Encode(SKEncodedImageFormat.Png, 100)
                            Using png = New MemoryStream(data.ToArray())
                                Return New Bitmap(png)
                            End Using
                        End Using
                    End Using
                End Using
            End Using
        End Function

        Private Shared Sub TintPixels(bitmap As SKBitmap, reference As SKColor, accent As SKColor)
            Dim referenceHue, referenceSaturation, referenceLightness As Double
            ToHsl(reference, referenceHue, referenceSaturation, referenceLightness)
            Dim accentHue, accentSaturation, accentLightness As Double
            ToHsl(accent, accentHue, accentSaturation, accentLightness)
            Dim hueShift = accentHue - referenceHue
            Dim saturationScale = If(referenceSaturation > 0, accentSaturation / referenceSaturation, 1.0)

            Dim pixels = bitmap.Pixels
            Dim width = bitmap.Width
            Dim startX = CInt(Math.Floor(width * KixStartFraction))
            For y = 0 To bitmap.Height - 1
                Dim row = y * width
                For x = startX To width - 1
                    Dim color = pixels(row + x)
                    If color.Alpha = 0 Then Continue For
                    Dim hue, saturation, lightness As Double
                    ToHsl(color, hue, saturation, lightness)
                    If Not IsWarm(hue, saturation) Then Continue For
                    Dim newHue = hue + hueShift
                    Dim newSaturation = Math.Min(1.0, saturation * saturationScale)
                    Dim newLightness = If(accentLightness <= referenceLightness,
                                          lightness * accentLightness / Math.Max(0.0001, referenceLightness),
                                          lightness + (1.0 - lightness) * (accentLightness - referenceLightness) / Math.Max(0.0001, 1.0 - referenceLightness))
                    pixels(row + x) = FromHsl(newHue, newSaturation, newLightness, color.Alpha)
                Next
            Next
            bitmap.Pixels = pixels
        End Sub

        Private Shared Function IsWarm(hue As Double, saturation As Double) As Boolean
            Return saturation >= 0.25 AndAlso (hue <= 75.0 OrElse hue >= 345.0)
        End Function

        Private Shared Sub ToHsl(color As SKColor, ByRef hue As Double, ByRef saturation As Double, ByRef lightness As Double)
            Dim red = color.Red / 255.0, green = color.Green / 255.0, blue = color.Blue / 255.0
            Dim maximum = Math.Max(red, Math.Max(green, blue))
            Dim minimum = Math.Min(red, Math.Min(green, blue))
            lightness = (maximum + minimum) / 2.0
            Dim delta = maximum - minimum
            If delta < 0.000001 Then
                hue = 0 : saturation = 0
                Return
            End If
            saturation = If(lightness > 0.5, delta / (2.0 - maximum - minimum), delta / (maximum + minimum))
            If maximum = red Then
                hue = (green - blue) / delta + If(green < blue, 6.0, 0.0)
            ElseIf maximum = green Then
                hue = (blue - red) / delta + 2.0
            Else
                hue = (red - green) / delta + 4.0
            End If
            hue *= 60.0
        End Sub

        Private Shared Function FromHsl(hue As Double, saturation As Double, lightness As Double, alpha As Byte) As SKColor
            hue = ((hue Mod 360.0) + 360.0) Mod 360.0
            saturation = Math.Max(0.0, Math.Min(1.0, saturation))
            lightness = Math.Max(0.0, Math.Min(1.0, lightness))
            If saturation <= 0 Then
                Dim value = ToByte(lightness)
                Return New SKColor(value, value, value, alpha)
            End If
            Dim q = If(lightness < 0.5, lightness * (1.0 + saturation), lightness + saturation - lightness * saturation)
            Dim p = 2.0 * lightness - q
            Dim hueFraction = hue / 360.0
            Return New SKColor(ToByte(HueToChannel(p, q, hueFraction + 1.0 / 3.0)),
                               ToByte(HueToChannel(p, q, hueFraction)),
                               ToByte(HueToChannel(p, q, hueFraction - 1.0 / 3.0)),
                               alpha)
        End Function

        Private Shared Function HueToChannel(p As Double, q As Double, value As Double) As Double
            If value < 0 Then value += 1.0
            If value > 1 Then value -= 1.0
            If value < 1.0 / 6.0 Then Return p + (q - p) * 6.0 * value
            If value < 0.5 Then Return q
            If value < 2.0 / 3.0 Then Return p + (q - p) * (2.0 / 3.0 - value) * 6.0
            Return p
        End Function

        Private Shared Function ToByte(value As Double) As Byte
            Return CByte(Math.Max(0, Math.Min(255, Math.Round(value * 255.0))))
        End Function

    End Class

End Namespace
