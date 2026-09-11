Imports System
Imports System.Globalization
Imports System.Resources
Imports System.Runtime.CompilerServices
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Data
Imports Avalonia.LogicalTree

Namespace Services

    ''' <summary>Die Uebersetzung der Oberflaeche. Dasselbe Prinzip wie in FerrumPix.
    '''
    ''' <para>DIE QUELLTEXTE SIND DEUTSCH und stehen woertlich im AXAML und im Code. Uebersetzt wird
    ''' ueber einen Schluessel, der aus dem deutschen Text BERECHNET wird (lesbarer Teil plus ein
    ''' Kuerzel aus SHA1, siehe <see cref="MakeKey"/>). Die Uebersetzungen liegen in
    ''' Resources/Strings.resx; die neutrale Datei traegt Englisch. Aendert sich ein deutscher Text,
    ''' aendert sich sein Schluessel - die englische Zeile muss dann mitgezogen werden, sonst steht
    ''' der Text in beiden Sprachen deutsch da.</para>
    '''
    ''' <para>Anders als in FerrumPix gibt es keine Strings.de.resx: Deutsch IST die Quelle, und
    ''' <see cref="T"/> gibt den Text dann unveraendert zurueck.</para>
    '''
    ''' <para>Das AXAML wird durch einen Durchlauf ueber den Baum uebersetzt
    ''' (<see cref="ApplyTo"/>). GEBUNDENE Eigenschaften bleiben dabei unberuehrt: das Zuweisen
    ''' eines Textes loescht die Bindung, und der laufende Titel stuende danach fuer immer auf dem
    ''' Wert vom Zeitpunkt des Durchlaufs. In FerrumPix musste man solche Anzeigen von Hand mit
    ''' "no-translate" kennzeichnen; hier wird die Bindung selbst geprueft. Die Klasse wird trotzdem
    ''' beachtet.</para></summary>
    Public NotInheritable Class LocalizationService
        Private Sub New()
        End Sub

        Public Shared Event LanguageChanged As EventHandler

        Private Shared _languageMode As String = "System"
        Private Shared ReadOnly Strings As New ResourceManager("FerrumPlay.Strings", GetType(LocalizationService).Assembly)

        ''' <summary>Der URSPRUNGStext je Anzeige, schwach referenziert. Ohne ihn laese der
        ''' Durchlauf den ANGEZEIGTEN Text als Quelle: nach dem ersten Wechsel steht dort die
        ''' Uebersetzung, und die Rueckkehr zu Deutsch faende keinen Schluessel. Befund aus FerrumPix.</summary>
        Private Shared ReadOnly Ursprungstexte As New ConditionalWeakTable(Of ILogical, NodeTexts)()

        Public Const KeineUebersetzung As String = "no-translate"

        Public Shared Property LanguageMode As String
            Get
                Return _languageMode
            End Get
            Set(value As String)
                Dim normalized = NormalizeLanguageMode(value)
                If _languageMode = normalized Then Return
                _languageMode = normalized
                RaiseEvent LanguageChanged(Nothing, EventArgs.Empty)
            End Set
        End Property

        ''' <summary>Die Kultur der gewählten Sprache. Englisch nutzt die neutrale Ressource,
        ''' Deutsch bleibt der deutsche Quelltext.</summary>
        Public Shared ReadOnly Property EffectiveCulture As CultureInfo
            Get
                Dim code = ResolveCultureCode(_languageMode)
                Return If(String.IsNullOrEmpty(code), CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo(code))
            End Get
        End Property

        ''' <summary>Die waehlbaren Sprachen, mit ihrem Namen in der jeweiligen Sprache SELBST: wer
        ''' die Oberflaeche in einer Sprache sieht, die er nicht versteht, findet "Deutsch" wieder.
        ''' Der leere Name steht fuer die Systemsprache und wird in der Anzeige uebersetzt.</summary>
        Public Shared ReadOnly Property Languages As (Key As String, Name As String)()
            Get
                Return {
                    ("System", ""),
                    ("German", "Deutsch"),
                    ("English", "English"),
                    ("Dutch", "Nederlands"),
                    ("Swedish", "Svenska"),
                    ("Danish", "Dansk"),
                    ("Norwegian", "Norsk bokmål"),
                    ("Finnish", "Suomi"),
                    ("Spanish", "Español"),
                    ("French", "Français"),
                    ("Italian", "Italiano"),
                    ("Portuguese", "Português"),
                    ("Polish", "Polski"),
                    ("Czech", "Čeština"),
                    ("Russian", "Русский"),
                    ("Chinese", "简体中文"),
                    ("Japanese", "日本語"),
                    ("Korean", "한국어"),
                    ("Indonesian", "Bahasa Indonesia"),
                    ("Turkish", "Türkçe"),
                    ("Thai", "ไทย"),
                    ("Hindi", "हिन्दी")}
            End Get
        End Property

        Public Shared Function NormalizeLanguageMode(value As String) As String
            Dim normalized = If(value, "").Trim()
            For Each sprache In Languages
                If sprache.Key <> "System" AndAlso
                   String.Equals(sprache.Key, normalized, StringComparison.Ordinal) Then Return sprache.Key
            Next
            Return "System"
        End Function

        ''' <summary>Deutsch ist die Quellsprache: dann gibt es nichts nachzuschlagen.</summary>
        Private Shared ReadOnly Property IsSourceLanguage As Boolean
            Get
                Return ResolveCultureCode(_languageMode) = "de"
            End Get
        End Property

        Public Shared Function T(text As String) As String
            If String.IsNullOrEmpty(text) OrElse IsSourceLanguage Then Return text
            Try
                Dim translated = Strings.GetString(MakeKey(text), EffectiveCulture)
                If String.IsNullOrEmpty(translated) Then translated = Strings.GetString(MakeKey(text), CultureInfo.InvariantCulture)
                Return If(String.IsNullOrEmpty(translated), text, translated)
            Catch ex As MissingManifestResourceException
                Return text
            End Try
        End Function

        ''' <summary>Uebersetzt eine Vorlage mit Platzhaltern und setzt die Werte ein. Der Schluessel
        ''' kommt aus der VORLAGE ("{0} Titel eingelesen."), nicht aus dem fertigen Satz - sonst
        ''' braeuchte jede Zahl einen eigenen Eintrag.</summary>
        Public Shared Function Format(template As String, ParamArray values As Object()) As String
            Return String.Format(CultureInfo.CurrentCulture, T(template), values)
        End Function

        ' Der Durchlauf ueber den Baum

        Public Shared Sub ApplyTo(root As ILogical)
            If root Is Nothing Then Return
            ApplyOne(root)
            For Each child In root.GetLogicalChildren()
                ApplyTo(child)
            Next
        End Sub

        ''' <summary>Uebersetzt nur den Kurzhinweis eines Steuerelements. Fuer Hinweise in
        ''' Listenzeilen: die entstehen erst, wenn die Zeile sichtbar wird, lange nach dem Durchlauf
        ''' ueber das Fenster. Die Anwendung ruft das beim Oeffnen jedes Hinweises auf.</summary>
        Public Shared Sub ApplyToTip(control As Control)
            If control Is Nothing Then Return
            TranslateTip(control, Ursprungstexte.GetValue(control, Function(ignoriert) New NodeTexts()))
        End Sub

        Private Shared Sub ApplyOne(node As ILogical)
            Dim merker = Ursprungstexte.GetValue(node, Function(ignoriert) New NodeTexts())
            Dim suppressed = TypeOf node Is StyledElement AndAlso
                             DirectCast(node, StyledElement).Classes.Contains(KeineUebersetzung)

            Dim textBlock = TryCast(node, TextBlock)
            If textBlock IsNot Nothing AndAlso Not suppressed AndAlso
               Not String.IsNullOrEmpty(textBlock.Text) AndAlso
               Not IsBound(textBlock, TextBlock.TextProperty) Then
                Assign(textBlock.Text, TranslateRemembered(textBlock.Text, merker.Text), Sub(v) textBlock.Text = v)
            End If

            ' Vorlagenteile bleiben unangetastet: ihr Inhalt haengt an einer Vorlagenbindung des
            ' Steuerelements, dem die Vorlage gehoert. Befund aus FerrumPix (ComboBox).
            Dim content = TryCast(node, ContentControl)
            If content IsNot Nothing AndAlso Not suppressed AndAlso content.TemplatedParent Is Nothing AndAlso
               TypeOf content.Content Is String AndAlso Not IsBound(content, ContentControl.ContentProperty) Then
                Dim current = CStr(content.Content)
                Assign(current, TranslateRemembered(current, merker.Inhalt), Sub(v) content.Content = v)
            End If

            Dim textBox = TryCast(node, TextBox)
            If textBox IsNot Nothing AndAlso Not String.IsNullOrEmpty(textBox.PlaceholderText) AndAlso
               Not IsBound(textBox, TextBox.PlaceholderTextProperty) Then
                Assign(textBox.PlaceholderText, TranslateRemembered(textBox.PlaceholderText, merker.Platzhalter),
                       Sub(v) textBox.PlaceholderText = v)
            End If

            Dim control = TryCast(node, Control)
            If control IsNot Nothing Then TranslateTip(control, merker)
        End Sub

        Private Shared Sub TranslateTip(control As Control, merker As NodeTexts)
            Dim tip = TryCast(ToolTip.GetTip(control), String)
            If String.IsNullOrEmpty(tip) OrElse IsBound(control, ToolTip.TipProperty) Then Return
            Assign(tip, TranslateRemembered(tip, merker.Tipp, appendSpace:=True), Sub(v) ToolTip.SetTip(control, v))
        End Sub

        Private Shared Function IsBound(target As AvaloniaObject, [property] As AvaloniaProperty) As Boolean
            Return BindingOperations.GetBindingExpressionBase(target, [property]) IsNot Nothing
        End Function

        ''' <summary>Schreibt nur, wenn sich etwas aendert. Ein Text ohne Eintrag ("Aa", "Version")
        ''' kommt unveraendert zurueck, und ihn trotzdem zu setzen waere eine leere Aenderung, die
        ''' das Layout neu anstoesst.</summary>
        Private Shared Sub Assign(current As String, translated As String, setter As Action(Of String))
            If Not String.Equals(current, translated, StringComparison.Ordinal) Then setter(translated)
        End Sub

        ''' <summary>Uebersetzt gegen den GEMERKTEN Ursprungstext. Weicht der aktuelle Text von dem
        ''' ab, was zuletzt gesetzt wurde, hat ihn jemand anders geschrieben - dann ist er die neue
        ''' Quelle.</summary>
        ''' <param name="appendSpace">Ein Leerzeichen ans Ende, NUR fuer Kurzhinweise. Bei krummer
        ''' Skalierung fehlte dem Hinweis sonst beim Anordnen ein Bruchteil, und das letzte Zeichen
        ''' verschwand. Befund aus FerrumPix.</param>
        Private Shared Function TranslateRemembered(current As String, merker As TextMerker,
                                                  Optional appendSpace As Boolean = False) As String
            If merker.Quelle Is Nothing OrElse
               Not String.Equals(current, merker.Zuletzt, StringComparison.Ordinal) Then
                merker.Quelle = If(appendSpace, If(current, "").TrimEnd(), current)
            End If
            Dim uebersetzt = T(merker.Quelle)
            If appendSpace Then uebersetzt &= " "
            merker.Zuletzt = uebersetzt
            Return uebersetzt
        End Function

        ' Welche Sprache gilt

        ''' <summary>Ordnet die Sprachwahl den Ressourcenkulturen zu. Chinesisch braucht eine
        ''' Region; für Norwegisch wird die vorhandene Bokmål-Ressource verwendet.</summary>
        Private Shared Function ResolveCultureCode(mode As String) As String
            Select Case NormalizeLanguageMode(mode)
                Case "German" : Return "de"
                Case "Dutch" : Return "nl"
                Case "Swedish" : Return "sv"
                Case "Danish" : Return "da"
                Case "Norwegian" : Return "nb"
                Case "Finnish" : Return "fi"
                Case "Spanish" : Return "es"
                Case "French" : Return "fr"
                Case "Italian" : Return "it"
                Case "Portuguese" : Return "pt"
                Case "Polish" : Return "pl"
                Case "Czech" : Return "cs"
                Case "Russian" : Return "ru"
                Case "Chinese" : Return "zh-CN"
                Case "Japanese" : Return "ja"
                Case "Korean" : Return "ko"
                Case "Indonesian" : Return "id"
                Case "Turkish" : Return "tr"
                Case "Thai" : Return "th"
                Case "Hindi" : Return "hi"
                Case "English" : Return ""
                Case Else
                    Dim current = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant()
                    If IsSupportedCultureCode(current) Then Return ResourceCultureFor(current)
                    For Each variableName In {"LANGUAGE", "LC_MESSAGES", "LANG"}
                        Dim code = ExtractCultureCode(Environment.GetEnvironmentVariable(variableName))
                        If IsSupportedCultureCode(code) Then Return ResourceCultureFor(code)
                    Next
                    Return ""
            End Select
        End Function

        Private Shared Function IsSupportedCultureCode(code As String) As Boolean
            Select Case If(code, "").ToLowerInvariant()
                Case "de", "nl", "sv", "da", "nb", "nn", "no", "fi", "es", "fr", "it", "pt", "pl", "cs", "ru", "zh", "ja", "ko", "id", "tr", "th", "hi"
                    Return True
                Case Else
                    Return False
            End Select
        End Function

        Private Shared Function ResourceCultureFor(code As String) As String
            Select Case If(code, "").ToLowerInvariant()
                Case "zh" : Return "zh-CN"
                Case "no", "nn" : Return "nb"
                Case Else : Return code
            End Select
        End Function

        Private Shared Function ExtractCultureCode(value As String) As String
            value = If(value, "").Trim()
            If String.IsNullOrEmpty(value) Then Return ""
            Dim first = value.Split(":"c)(0).Trim()
            If String.Equals(first, "C", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(first, "POSIX", StringComparison.OrdinalIgnoreCase) Then Return ""
            Dim normalized = first.Split("."c)(0).Split("@"c)(0).Replace("-"c, "_"c)
            Dim separator = normalized.IndexOf("_"c)
            If separator >= 0 Then normalized = normalized.Substring(0, separator)
            Return normalized.ToLowerInvariant()
        End Function

        ''' <summary>Der Schluessel zu einem deutschen Text. MUSS mit FerrumPix und mit dem
        ''' Erzeuger der resx-Datei uebereinstimmen: Nicht-ASCII-Zeichen werden zu "_", hoechstens 48
        ''' Zeichen, dahinter die ersten 8 Stellen des SHA1 ueber den UTF-8-Text.</summary>
        Private Shared Function MakeKey(text As String) As String
            Dim baseName = Regex.Replace(text, "[^A-Za-z0-9]+", "_").Trim("_"c)
            If baseName.Length = 0 Then baseName = "Text"
            If baseName.Length > 48 Then baseName = baseName.Substring(0, 48)

            Using sha = SHA1.Create()
                Dim hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text))
                Dim hash = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 8).ToLowerInvariant()
                Return $"{baseName}_{hash}"
            End Using
        End Function

        Private NotInheritable Class NodeTexts
            Public ReadOnly Text As New TextMerker()
            Public ReadOnly Inhalt As New TextMerker()
            Public ReadOnly Platzhalter As New TextMerker()
            Public ReadOnly Tipp As New TextMerker()
        End Class

        Private NotInheritable Class TextMerker
            Public Quelle As String
            Public Zuletzt As String
        End Class

    End Class

End Namespace
