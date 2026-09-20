Imports System
Imports System.Linq
Imports Avalonia
Imports Avalonia.Media
Imports Avalonia.Styling

Namespace Services

    ''' <summary>Die drei Erscheinungsbilder der Oberflaeche. Dasselbe Verfahren wie in FerrumPix:
    ''' die Farben stehen NICHT in eigenen AXAML-Dateien, sondern werden zur Laufzeit in dieselben
    ''' Schluessel geschrieben, aus denen das Thema ohnehin liest (FP.Bg.*, FP.Text.*, FP.Border.*,
    ''' FP.Sel.*). Eine Ansicht muss davon nichts wissen, und ein Wechsel greift sofort - ohne
    ''' Neustart und ohne dass ein Fenster neu gebaut werden muesste.
    '''
    ''' <para>Die Werte fuer Grund, Schrift und Rahmen sind die aus FerrumPix, Zeichen fuer
    ''' Zeichen: beide Anwendungen sollen nebeneinander wie EIN Programm aussehen.</para>
    '''
    ''' <para>"GrayLight" ist trotz seines Namens KEIN helles Bild: es ist ein mittleres Grau und
    ''' traegt weiterhin helle Schrift. Alle drei Bilder sind dunkle Bilder - ein wirklich helles
    ''' gibt es nicht mehr, siehe <see cref="Modes"/>.</para>
    '''
    ''' <para>WAS HIER ZUSAETZLICH ZU FERRUMPIX STEHT, ist der Spieler: die Fussleiste ist ein
    ''' eigenes Deck mit eigenem Grund, eigener Kante und eigener Laufschiene, und der Schleier
    ''' ueber dem unscharfen Titelbild gehoert ebenfalls zum Bild. Ohne diese Werte bliebe die
    ''' Leiste in allen drei Bildern dieselbe und saesse im Grau fremd im Fenster.</para></summary>
    Public NotInheritable Class ThemeService
        Private Sub New()
        End Sub

        ''' <summary>Die Namen, wie sie in den Einstellungen stehen und gespeichert werden. Ein
        ''' HELLES Bild - dunkle Schrift auf weissem Grund - stand hier eine Fassung lang und ist
        ''' wieder weg: es sah in diesem Spieler schlicht schlecht aus. Eine gespeicherte "Light"
        ''' faellt ueber <see cref="Normalize"/> auf das dunkle Bild zurueck.</summary>
        Public Shared ReadOnly Modes As String() = {"Dark", "GrayDark", "GrayLight"}

        Public Shared ReadOnly Property Current As String = "Dark"

        ''' <summary>Nimmt nur einen der drei Namen an. Alles andere faellt auf das dunkle Bild
        ''' zurueck: eine unlesbare Einstellungsdatei darf die Oberflaeche nicht unbrauchbar
        ''' machen.</summary>
        Public Shared Function Normalize(value As String) As String
            Dim text = If(value, String.Empty).Trim()
            Dim match = Modes.FirstOrDefault(Function(mode) String.Equals(mode, text, StringComparison.OrdinalIgnoreCase))
            Return If(match, "Dark")
        End Function

        Public Shared Sub Apply(mode As String)
            Dim app = Application.Current
            If app Is Nothing Then Return

            Dim name = Normalize(mode)
            _Current = name

            ' Fluent selbst kennt nur hell und dunkel. Alle drei Bilder tragen helle Schrift und
            ' laufen deshalb in der dunklen Spielart mit.
            app.RequestedThemeVariant = ThemeVariant.Dark

            Select Case name
                Case "GrayDark"
                    SetBrush(app, "FP.Bg.Root", "#1B1F20")
                    SetBrush(app, "FP.Bg.Dark", "#1E2021")
                    SetBrush(app, "FP.Bg.Panel", "#232628")
                    SetBrush(app, "FP.Bg.Content", "#202426")
                    SetBrush(app, "FP.Bg.Elevated", "#2C3030")
                    SetBrush(app, "FP.Bg.Hover", "#323536")
                    SetBrush(app, "FP.Bg.Input", "#1F2122")
                    SetBrush(app, "FP.Bg.Active", "#323536")
                    SetBrush(app, "FP.Text.Primary", "#F1F2F2")
                    SetBrush(app, "FP.Text.Secondary", "#D0D3D4")
                    SetBrush(app, "FP.Text.Muted", "#9A9FA1")
                    SetBrush(app, "FP.Text.Danger", "#FF6B6B")
                    SetBrush(app, "FP.Border.Subtle", "#2A2D2F")
                    SetBrush(app, "FP.Border.Normal", "#3A3E40")
                    SetBrush(app, "FP.Border.Strong", "#555A5C")
                    SetBrush(app, "FP.Sel.Bg", "#323536")
                    SetBrush(app, "FP.Sel.Hover", "#3A3E40")
                    SetBrush(app, "FP.Footer.Bg", "#EB1B1F20")
                    SetBrush(app, "FP.Footer.Edge", "#2F3335")
                    SetBrush(app, "FP.Footer.Track", "#3A3E40")
                    SetBrush(app, "FP.Bg.Veil", "#661E2021")
                    SetBrush(app, "FP.Bg.Scrim", "#CC1B1F20")
                Case "GrayLight"
                    SetBrush(app, "FP.Bg.Root", "#33383B")
                    SetBrush(app, "FP.Bg.Dark", "#1E2021")
                    SetBrush(app, "FP.Bg.Panel", "#4A5057")
                    SetBrush(app, "FP.Bg.Content", "#464B50")
                    SetBrush(app, "FP.Bg.Elevated", "#404649")
                    SetBrush(app, "FP.Bg.Hover", "#5A6065")
                    SetBrush(app, "FP.Bg.Input", "#3E4348")
                    SetBrush(app, "FP.Bg.Active", "#5A6065")
                    SetBrush(app, "FP.Text.Primary", "#F1F2F2")
                    SetBrush(app, "FP.Text.Secondary", "#D7D9DB")
                    SetBrush(app, "FP.Text.Muted", "#C7CACA")
                    SetBrush(app, "FP.Text.Danger", "#FF8A8A")
                    SetBrush(app, "FP.Border.Subtle", "#4E545A")
                    SetBrush(app, "FP.Border.Normal", "#5A5F63")
                    SetBrush(app, "FP.Border.Strong", "#747A7F")
                    SetBrush(app, "FP.Sel.Bg", "#5A6065")
                    SetBrush(app, "FP.Sel.Hover", "#62686D")
                    SetBrush(app, "FP.Footer.Bg", "#EB2B3033")
                    SetBrush(app, "FP.Footer.Edge", "#5A5F63")
                    SetBrush(app, "FP.Footer.Track", "#5F656A")
                    SetBrush(app, "FP.Bg.Veil", "#662B3033")
                    SetBrush(app, "FP.Bg.Scrim", "#CC33383B")
                Case Else
                    SetBrush(app, "FP.Bg.Root", "#0B0E11")
                    SetBrush(app, "FP.Bg.Dark", "#0E1216")
                    SetBrush(app, "FP.Bg.Panel", "#11161B")
                    SetBrush(app, "FP.Bg.Content", "#141A20")
                    SetBrush(app, "FP.Bg.Elevated", "#192128")
                    SetBrush(app, "FP.Bg.Hover", "#1F2830")
                    SetBrush(app, "FP.Bg.Input", "#10161B")
                    SetBrush(app, "FP.Bg.Active", "#24303A")
                    SetBrush(app, "FP.Text.Primary", "#E7ECF0")
                    SetBrush(app, "FP.Text.Secondary", "#A6AFB7")
                    SetBrush(app, "FP.Text.Muted", "#6C7780")
                    SetBrush(app, "FP.Text.Danger", "#FF6B6B")
                    SetBrush(app, "FP.Border.Subtle", "#182028")
                    SetBrush(app, "FP.Border.Normal", "#26313B")
                    SetBrush(app, "FP.Border.Strong", "#34414C")
                    SetBrush(app, "FP.Sel.Bg", "#24303A")
                    SetBrush(app, "FP.Sel.Hover", "#2A3742")
                    SetBrush(app, "FP.Footer.Bg", "#EB0E1317")
                    SetBrush(app, "FP.Footer.Edge", "#222C35")
                    SetBrush(app, "FP.Footer.Track", "#2A353F")
                    SetBrush(app, "FP.Bg.Veil", "#66101418")
                    SetBrush(app, "FP.Bg.Scrim", "#CC0B0E11")
            End Select

            ' Aus der Akzentfarbe haengen weitere Farben ab - die Zeile des laufenden Titels, das
            ' Symbol im Wiedergabeknopf. Sie werden gegen Schwarz und Weiss gemischt und nicht gegen
            ' den Grund, stehen aber in denselben Ressourcen: nach dem Setzen der Palette muessen sie
            ' deshalb neu geschrieben werden.
            AccentColorService.Apply(AppSettingsService.Current.AccentColor)

            MirrorFluentBrushes(app)
        End Sub

        ''' <summary>DAS STANDARDERSCHEINUNGSBILD HOLT SEINE FARBEN WOANDERS HER. Aufklapplisten,
        ''' Menues und Listenzeilen lesen nicht unsere Schluessel, sondern die des Fluent-Themas.
        ''' Ohne dieses Nachziehen stuenden sie nach einem Wechsel in den Farben des vorigen Bildes
        ''' da. Uebernommen aus FerrumPix, wo derselbe Befund gemacht wurde.</summary>
        Private Shared Sub MirrorFluentBrushes(app As Application)
            For Each pair In MirroredBrushKeys
                Dim value As Object = Nothing
                If app.TryGetResource(pair.Source, app.ActualThemeVariant, value) AndAlso value IsNot Nothing Then
                    app.Resources(pair.Target) = value
                End If
            Next
        End Sub

        Private Shared ReadOnly MirroredBrushKeys As (Target As String, Source As String)() = {
            ("MenuFlyoutPresenterBackground", "FP.Bg.Elevated"),
            ("MenuFlyoutPresenterBorderBrush", "FP.Border.Normal"),
            ("ComboBoxDropDownBackground", "FP.Bg.Elevated"),
            ("ComboBoxDropDownBorderBrush", "FP.Border.Normal"),
            ("ComboBoxItemBackgroundPointerOver", "FP.Bg.Hover"),
            ("ComboBoxItemBackgroundPressed", "FP.Sel.Bg"),
            ("ComboBoxItemBackgroundSelected", "FP.Sel.Bg"),
            ("ComboBoxItemBackgroundSelectedPointerOver", "FP.Sel.Hover"),
            ("ComboBoxItemBackgroundSelectedPressed", "FP.Sel.Hover"),
            ("ComboBoxItemForeground", "FP.Text.Primary"),
            ("ComboBoxItemForegroundPointerOver", "FP.Text.Primary"),
            ("ComboBoxItemForegroundSelected", "FP.Text.Primary"),
            ("ComboBoxItemForegroundSelectedPointerOver", "FP.Text.Primary"),
            ("ComboBoxItemForegroundSelectedPressed", "FP.Text.Primary"),
            ("ListBoxItemBackgroundPointerOver", "FP.Bg.Hover"),
            ("ListBoxItemBackgroundPressed", "FP.Sel.Bg"),
            ("ListBoxItemBackgroundSelected", "FP.Sel.Bg"),
            ("ListBoxItemBackgroundSelectedPointerOver", "FP.Sel.Hover"),
            ("ListBoxItemBackgroundSelectedPressed", "FP.Sel.Hover"),
            ("ListBoxItemForeground", "FP.Text.Primary"),
            ("ListBoxItemForegroundPointerOver", "FP.Text.Primary"),
            ("ListBoxItemForegroundSelected", "FP.Text.Primary"),
            ("ListBoxItemForegroundSelectedPointerOver", "FP.Text.Primary"),
            ("ListBoxItemForegroundSelectedPressed", "FP.Text.Primary")
        }

        Private Shared Sub SetBrush(app As Application, key As String, hexColor As String)
            app.Resources(key) = New SolidColorBrush(Color.Parse(hexColor))
        End Sub

    End Class

End Namespace
