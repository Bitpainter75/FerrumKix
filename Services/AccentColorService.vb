Imports System
Imports System.Globalization
Imports Avalonia
Imports Avalonia.Media

Namespace Services

    ''' <summary>Setzt die Akzentfarbe der Anwendung zur Laufzeit.
    '''
    ''' <para>Anders als in FerrumPix haengt hier MEHR an dieser einen Farbe: der Wiedergabeknopf
    ''' ist mit ihr gefuellt, und die Zeile des laufenden Titels ist mit ihr eingefaerbt. Die Farben
    ''' dafuer werden aus der Akzentfarbe abgeleitet statt eigens eingestellt - sonst muesste man
    ''' nach jedem Wechsel der Akzentfarbe weitere von Hand nachziehen.</para>
    '''
    ''' <para>Gemischt wird gegen Schwarz und Weiss, nicht gegen den Hintergrund: der Abstand
    ''' zwischen Flaeche und Schrift soll bei jeder Farbe derselbe bleiben. Die Pinsel stehen
    ''' trotzdem in denselben Ressourcen wie die Farben des Erscheinungsbildes - der Dienst laeuft
    ''' deshalb auch nach jedem Wechsel des Bildes erneut, siehe <see cref="ThemeService"/>.</para></summary>
    Public NotInheritable Class AccentColorService
        Private Sub New()
        End Sub

        ''' <summary>Die Farben zur Auswahl. Die erste ist die der Auslieferung und dieselbe wie in
        ''' FerrumPix.</summary>
        Public Shared ReadOnly Palette As String() = {
            "#F08A1A", "#E4572E", "#D64550", "#C2478F",
            "#8A5CF0", "#3A7BD5", "#1FA9A0", "#3FA34D", "#8C8C8C"
        }

        Public Shared ReadOnly Property Current As String = "#F08A1A"

        ''' <summary>Nimmt nur eine Farbe in der Form #RRGGBB an. Alles andere faellt auf die Farbe
        ''' der Auslieferung zurueck: eine unlesbare Einstellungsdatei darf die Oberflaeche nicht
        ''' unbrauchbar machen.</summary>
        Public Shared Function Normalize(value As String) As String
            Dim text = If(value, String.Empty).Trim()
            If text.Length <> 7 OrElse Not text.StartsWith("#", StringComparison.Ordinal) Then Return "#F08A1A"
            Dim number As Integer
            If Not Integer.TryParse(text.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, number) Then Return "#F08A1A"
            Return text.ToUpperInvariant()
        End Function

        Public Shared Sub Apply(accent As String)
            Dim app = Application.Current
            If app Is Nothing Then Return

            Dim hex = Normalize(accent)
            _Current = hex
            Dim base = Color.Parse(hex)

            SetBrush(app, "FP.Accent", base)
            SetBrush(app, "FP.Accent.Light", Mix(base, Colors.White, 0.18))
            SetBrush(app, "FP.Accent.Dark", Mix(base, Colors.Black, 0.24))
            SetBrush(app, "FP.Accent.Dim", Mix(base, Color.Parse("#0B0E11"), 0.78))
            SetBrush(app, "FP.Text.Accent", base)

            ' Das Symbol im gefuellten Wiedergabeknopf und die Schrift auf den Akzentknoepfen. Sie
            ' bleibt in JEDEM Bild ein sehr dunkler Ton derselben Farbe: der Knopf traegt die
            ' Akzentfarbe, nicht den Grund, und darauf ist Dunkel bei jeder Farbe der Auswahl
            ' lesbar.
            SetBrush(app, "FP.Footer.OnAccent", Mix(base, Colors.Black, 0.82))

            ' Die Zeile des laufenden Titels. Alle drei Erscheinungsbilder tragen helle Schrift,
            ' der Balken geht deshalb in jedem nach Schwarz.
            SetBrush(app, "FP.Row.Playing", Mix(base, Colors.Black, 0.5))
            SetBrush(app, "FP.Row.PlayingHover", Mix(base, Colors.Black, 0.42))

            ' DAS STANDARDERSCHEINUNGSBILD HOLT SEINE FARBE WOANDERS HER. Aufklapplisten und
            ' Untermenues sind eigene Popups und lesen nicht unsere Schluessel, sondern die des
            ' Fluent-Themas. In FerrumPix stand deshalb nach einem Farbwechsel das Untermenue in
            ' der alten Farbe da. Dieser eine Schluessel zieht die Hervorhebung nach.
            SetBrush(app, "SystemControlHighlightAccentBrush", base)
        End Sub

        Private Shared Sub SetBrush(app As Application, key As String, color As Color)
            app.Resources(key) = New SolidColorBrush(color)
        End Sub

        ''' <summary><paramref name="amount"/> ist der Anteil des Ziels: 0 laesst die Farbe, 1 macht
        ''' das Ziel daraus.</summary>
        Private Shared Function Mix(color As Color, target As Color, amount As Double) As Color
            amount = Math.Clamp(amount, 0, 1)
            Return Color.FromArgb(
                color.A,
                CByte(Math.Round(color.R + (CInt(target.R) - CInt(color.R)) * amount)),
                CByte(Math.Round(color.G + (CInt(target.G) - CInt(color.G)) * amount)),
                CByte(Math.Round(color.B + (CInt(target.B) - CInt(color.B)) * amount)))
        End Function

    End Class

End Namespace
