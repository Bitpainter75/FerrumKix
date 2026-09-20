Imports System.Collections.Concurrent
Imports System.Globalization
Imports System.IO
Imports System.Text.RegularExpressions
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Media
Imports Avalonia.Platform

Namespace Controls

    ''' <summary>Zeichnet ein SVG-Symbol in der Farbe, die die Umgebung vorgibt.
    '''
    ''' <para>Uebernommen aus FerrumPix. Der Grund fuer den eigenen Zeichner statt einer Bibliothek
    ''' ist die Farbe: die Symbole sind Konturen, und ihr Strich soll die Farbe des Textes daneben
    ''' annehmen. Eine fertig gerasterte Grafik kann das nicht.</para>
    '''
    ''' <para>Anders als in FerrumPix wird hier NICHT vorgeladen: FerrumPix haelt mehrere tausend
    ''' Symbole, FerrumKix knapp drei Dutzend. Das erste Zeichnen zerlegt sie, danach stehen sie im
    ''' Speicher.</para></summary>
    Public Class SvgIcon
        Inherits Control

        Public Shared ReadOnly SourceProperty As StyledProperty(Of String) =
            AvaloniaProperty.Register(Of SvgIcon, String)(NameOf(Source))

        Public Shared ReadOnly IconBrushProperty As StyledProperty(Of IBrush) =
            AvaloniaProperty.Register(Of SvgIcon, IBrush)(NameOf(IconBrush), New SolidColorBrush(Color.Parse("#D6DCE1")))

        ' Nebenlaeufig, weil das Zerlegen auch von einem Arbeitsfaden kommen kann. Doppelt zerlegte
        ' Symbole sind dabei unkritisch: die Daten sind unveraenderlich.
        Private Shared ReadOnly Cache As New ConcurrentDictionary(Of String, SvgIconData)()

        Shared Sub New()
            AffectsRender(Of SvgIcon)(SourceProperty, IconBrushProperty)
        End Sub

        Public Property Source As String
            Get
                Return GetValue(SourceProperty)
            End Get
            Set(value As String)
                SetValue(SourceProperty, value)
            End Set
        End Property

        Public Property IconBrush As IBrush
            Get
                Return GetValue(IconBrushProperty)
            End Get
            Set(value As IBrush)
                SetValue(IconBrushProperty, value)
            End Set
        End Property

        Public Overrides Sub Render(context As DrawingContext)
            MyBase.Render(context)

            If Bounds.Width <= 0 OrElse Bounds.Height <= 0 OrElse String.IsNullOrWhiteSpace(Source) Then Return

            Dim icon = GetIcon(Source)
            If icon Is Nothing Then Return

            Dim fit = GetUniformFit(icon.ViewBox.Size, Bounds.Size)
            If fit.Width <= 0 OrElse fit.Height <= 0 Then Return

            Using state = context.PushTransform(Matrix.CreateTranslation(-icon.ViewBox.X, -icon.ViewBox.Y) *
                                                icon.SvgTransform *
                                                Matrix.CreateScale(fit.Scale, fit.Scale) *
                                                Matrix.CreateTranslation((Bounds.Width - fit.Width) / 2, (Bounds.Height - fit.Height) / 2))
                ' Kontur-Symbole (fill="none" und stroke) werden nur mit Stift gezeichnet, damit sie
                ' als Umriss stehen und nicht als flaechig gefuelltes Zeichen.
                If icon.IsStroked Then
                    Dim pen = New Pen(IconBrush, icon.StrokeWidth, lineCap:=PenLineCap.Round, lineJoin:=PenLineJoin.Round)
                    context.DrawGeometry(Nothing, pen, icon.Geometry)
                Else
                    context.DrawGeometry(IconBrush, Nothing, icon.Geometry)
                End If
            End Using
        End Sub

        Private Shared Function GetIcon(source As String) As SvgIconData
            Dim cached As SvgIconData = Nothing
            If Cache.TryGetValue(source, cached) Then Return cached

            Dim parsedSource = LoadSource(source)
            If parsedSource Is Nothing Then Return Nothing

            Dim icon = BuildIcon(parsedSource)
            If icon IsNot Nothing Then Cache(source) = icon
            Return icon
        End Function

        ''' <summary>Nur Lesen und Zerlegen. Erzeugt keine Avalonia-Objekte und darf daher auf jedem
        ''' Faden laufen.</summary>
        Private Shared Function LoadSource(source As String) As SvgIconSource
            Try
                Using stream = AssetLoader.Open(New Uri(source))
                    Using reader = New StreamReader(stream)
                        Return ParseSvg(reader.ReadToEnd())
                    End Using
                End Using
            Catch
                Return Nothing
            End Try
        End Function

        ''' <summary>Baut die Geometrie aus den Pfaddaten. Geometry erbt von AvaloniaObject und ist
        ''' an den Faden gebunden, der es angelegt hat: nur auf dem Anzeigefaden rufen.</summary>
        Private Shared Function BuildIcon(source As SvgIconSource) As SvgIconData
            Dim geometry = BuildGeometry(source.PathData)
            If geometry Is Nothing Then Return Nothing

            Return New SvgIconData With {
                .ViewBox = source.ViewBox,
                .SvgTransform = source.SvgTransform,
                .Geometry = geometry,
                .IsStroked = source.IsStroked,
                .StrokeWidth = source.StrokeWidth
            }
        End Function

        Private Shared Function BuildGeometry(pathData As List(Of String)) As Geometry
            Dim group As New GeometryGroup()
            For Each d In pathData
                Try
                    group.Children.Add(Geometry.Parse(d))
                Catch
                End Try
            Next

            If group.Children.Count = 0 Then Return Nothing
            If group.Children.Count = 1 Then Return group.Children(0)
            Return group
        End Function

        Private Shared Function ParseSvg(svg As String) As SvgIconSource
            Dim viewBoxMatch = Regex.Match(svg, "viewBox=""(?<x>[-0-9.]+)\s+(?<y>[-0-9.]+)\s+(?<w>[-0-9.]+)\s+(?<h>[-0-9.]+)""")
            If Not viewBoxMatch.Success Then Throw New InvalidDataException("Kein lesbares SVG-Symbol.")

            Dim viewBox = New Rect(
                ParseInvariant(viewBoxMatch.Groups("x").Value),
                ParseInvariant(viewBoxMatch.Groups("y").Value),
                ParseInvariant(viewBoxMatch.Groups("w").Value),
                ParseInvariant(viewBoxMatch.Groups("h").Value))

            Dim transform = Matrix.Identity
            Dim transformMatch = Regex.Match(svg, "transform=""translate\((?<tx>[-0-9.]+),(?<ty>[-0-9.]+)\)\s+scale\((?<sx>[-0-9.]+),(?<sy>[-0-9.]+)\)""")
            If transformMatch.Success Then
                transform = Matrix.CreateScale(
                                ParseInvariant(transformMatch.Groups("sx").Value),
                                ParseInvariant(transformMatch.Groups("sy").Value)) *
                            Matrix.CreateTranslation(
                                ParseInvariant(transformMatch.Groups("tx").Value),
                                ParseInvariant(transformMatch.Groups("ty").Value))
            End If

            Dim rootTagMatch = Regex.Match(svg, "<svg\b[^>]*>", RegexOptions.Singleline)
            Dim rootTag = If(rootTagMatch.Success, rootTagMatch.Value, "")
            Dim isStroked = Regex.IsMatch(rootTag, "fill\s*=\s*""none""", RegexOptions.IgnoreCase)
            Dim strokeWidth = 10.0
            Dim strokeWidthMatch = Regex.Match(rootTag, "stroke-width\s*=\s*""(?<w>[-0-9.]+)""")
            If strokeWidthMatch.Success Then strokeWidth = ParseInvariant(strokeWidthMatch.Groups("w").Value)

            Dim pathData = ParseShapes(svg)
            If pathData.Count = 0 Then Throw New InvalidDataException("Kein lesbares SVG-Symbol.")

            Return New SvgIconSource With {
                .ViewBox = viewBox,
                .SvgTransform = transform,
                .PathData = pathData,
                .IsStroked = isStroked,
                .StrokeWidth = strokeWidth
            }
        End Function

        ''' <summary>Sammelt die Pfaddaten aller Grundformen einer SVG-Datei. Die Kontur-Symbole
        ''' bestehen fast nie aus einem einzigen path, sondern aus mehreren Elementen.</summary>
        Private Shared Function ParseShapes(svg As String) As List(Of String)
            Dim shapeRegex = New Regex("<(?<tag>path|rect|circle|ellipse|line)\b(?<attrs>[^>]*?)/?>", RegexOptions.Singleline)
            Dim pathData As New List(Of String)()

            For Each m As Match In shapeRegex.Matches(svg)
                Dim d As String = Nothing
                Dim attrs = m.Groups("attrs").Value
                Select Case m.Groups("tag").Value
                    Case "path" : d = GetAttr(attrs, "d")
                    Case "rect" : d = RectToPath(attrs)
                    Case "circle" : d = CircleToPath(attrs)
                    Case "ellipse" : d = EllipseToPath(attrs)
                    Case "line" : d = LineToPath(attrs)
                End Select

                If Not String.IsNullOrWhiteSpace(d) Then pathData.Add(d)
            Next

            Return pathData
        End Function

        Private Shared Function GetAttr(attrs As String, name As String) As String
            Dim m = Regex.Match(attrs, name & "\s*=\s*""(?<v>[^""]*)""")
            Return If(m.Success, m.Groups("v").Value, Nothing)
        End Function

        Private Shared Function GetAttrNumber(attrs As String, name As String, fallback As Double) As Double
            Dim v = GetAttr(attrs, name)
            If v Is Nothing Then Return fallback
            Return ParseInvariant(v)
        End Function

        Private Shared Function RectToPath(attrs As String) As String
            Dim x = GetAttrNumber(attrs, "x", 0)
            Dim y = GetAttrNumber(attrs, "y", 0)
            Dim w = GetAttrNumber(attrs, "width", 0)
            Dim h = GetAttrNumber(attrs, "height", 0)
            If w <= 0 OrElse h <= 0 Then Return Nothing

            Dim rx = GetAttrNumber(attrs, "rx", GetAttrNumber(attrs, "ry", 0))
            If rx <= 0 Then
                Return $"M{Inv(x)},{Inv(y)} H{Inv(x + w)} V{Inv(y + h)} H{Inv(x)} Z"
            End If
            rx = Math.Min(rx, Math.Min(w / 2, h / 2))

            Return $"M{Inv(x + rx)},{Inv(y)} " &
                   $"H{Inv(x + w - rx)} A{Inv(rx)},{Inv(rx)} 0 0 1 {Inv(x + w)},{Inv(y + rx)} " &
                   $"V{Inv(y + h - rx)} A{Inv(rx)},{Inv(rx)} 0 0 1 {Inv(x + w - rx)},{Inv(y + h)} " &
                   $"H{Inv(x + rx)} A{Inv(rx)},{Inv(rx)} 0 0 1 {Inv(x)},{Inv(y + h - rx)} " &
                   $"V{Inv(y + rx)} A{Inv(rx)},{Inv(rx)} 0 0 1 {Inv(x + rx)},{Inv(y)} Z"
        End Function

        Private Shared Function CircleToPath(attrs As String) As String
            Dim cx = GetAttrNumber(attrs, "cx", 0)
            Dim cy = GetAttrNumber(attrs, "cy", 0)
            Dim r = GetAttrNumber(attrs, "r", 0)
            If r <= 0 Then Return Nothing
            Return $"M{Inv(cx - r)},{Inv(cy)} A{Inv(r)},{Inv(r)} 0 1 0 {Inv(cx + r)},{Inv(cy)} A{Inv(r)},{Inv(r)} 0 1 0 {Inv(cx - r)},{Inv(cy)} Z"
        End Function

        Private Shared Function EllipseToPath(attrs As String) As String
            Dim cx = GetAttrNumber(attrs, "cx", 0)
            Dim cy = GetAttrNumber(attrs, "cy", 0)
            Dim rx = GetAttrNumber(attrs, "rx", 0)
            Dim ry = GetAttrNumber(attrs, "ry", 0)
            If rx <= 0 OrElse ry <= 0 Then Return Nothing
            Return $"M{Inv(cx - rx)},{Inv(cy)} A{Inv(rx)},{Inv(ry)} 0 1 0 {Inv(cx + rx)},{Inv(cy)} A{Inv(rx)},{Inv(ry)} 0 1 0 {Inv(cx - rx)},{Inv(cy)} Z"
        End Function

        Private Shared Function LineToPath(attrs As String) As String
            Dim x1 = GetAttrNumber(attrs, "x1", 0)
            Dim y1 = GetAttrNumber(attrs, "y1", 0)
            Dim x2 = GetAttrNumber(attrs, "x2", 0)
            Dim y2 = GetAttrNumber(attrs, "y2", 0)
            Return $"M{Inv(x1)},{Inv(y1)} L{Inv(x2)},{Inv(y2)}"
        End Function

        Private Shared Function Inv(value As Double) As String
            Return value.ToString(CultureInfo.InvariantCulture)
        End Function

        Private Shared Function ParseInvariant(value As String) As Double
            Return Double.Parse(value, CultureInfo.InvariantCulture)
        End Function

        Private Shared Function GetUniformFit(sourceSize As Size, targetSize As Size) As IconFit
            If sourceSize.Width <= 0 OrElse sourceSize.Height <= 0 Then Return New IconFit()
            Dim scale = Math.Min(targetSize.Width / sourceSize.Width, targetSize.Height / sourceSize.Height)
            Return New IconFit With {
                .Scale = scale,
                .Width = sourceSize.Width * scale,
                .Height = sourceSize.Height * scale
            }
        End Function

        Private Class SvgIconData
            Public Property ViewBox As Rect
            Public Property SvgTransform As Matrix
            Public Property Geometry As Geometry
            Public Property IsStroked As Boolean
            Public Property StrokeWidth As Double
        End Class

        ''' <summary>Zwischenstufe ohne Avalonia-Objekte: darf auf jedem Faden entstehen.</summary>
        Private Class SvgIconSource
            Public Property ViewBox As Rect
            Public Property SvgTransform As Matrix
            Public Property PathData As List(Of String)
            Public Property IsStroked As Boolean
            Public Property StrokeWidth As Double
        End Class

        Private Structure IconFit
            Public Property Scale As Double
            Public Property Width As Double
            Public Property Height As Double
        End Structure

    End Class

End Namespace
