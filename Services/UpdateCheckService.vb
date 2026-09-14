Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks

Namespace Services

    ''' <summary>Fragt die im Projekt hinterlegte Versionsnummer ab und vergleicht sie mit der
    ''' laufenden Fassung.
    '''
    ''' <para>Gelesen wird die Datei VERSION im Hauptzweig - dieselbe Datei, aus der auch das
    ''' Bauskript die Nummer zieht. Damit gibt es keine zweite Stelle, die gepflegt werden muesste
    ''' und auseinanderlaufen koennte.</para>
    '''
    ''' <para>Der Abruf holt eine kurze Textdatei und schickt nichts mit, was ueber die Anwendung
    ''' hinaus etwas verraet: kein Rechnername, keine Kennung, keine Zaehlung. Er laeuft nur beim
    ''' Oeffnen der Einstellungen, und wenn er scheitert, bleibt es still - eine fehlende
    ''' Verbindung ist kein Fehler, den der Nutzer wegklicken muesste. Uebernommen aus
    ''' FerrumPix.</para></summary>
    Public NotInheritable Class UpdateCheckService

        Private Sub New()
        End Sub

        Public Const VersionAddress As String = "https://raw.githubusercontent.com/Bitpainter75/FerrumPlay/main/VERSION"

        ''' <summary>Wohin der Hinweis fuehrt: die zuletzt veroeffentlichte Fassung mit allen Paketen.</summary>
        Public Const ReleasesAddress As String = "https://github.com/Bitpainter75/FerrumPlay/releases/latest"

        Private Shared ReadOnly _client As New Lazy(Of HttpClient)(
            Function()
                Dim client = New HttpClient()
                ' Kurz gehalten: die Anzeige darf niemand aufhalten, und wer offline ist, wartet
                ' sonst eine halbe Minute auf ein Ergebnis, das ohnehin nicht kommt.
                client.Timeout = TimeSpan.FromSeconds(10)
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FerrumPlay")
                ' Ohne diese Bitte liefert ein Zwischenspeicher auf dem Weg unter Umstaenden noch
                ' die Nummer von gestern, und die Anzeige bliebe nach einer Veroeffentlichung leer.
                client.DefaultRequestHeaders.CacheControl = New Net.Http.Headers.CacheControlHeaderValue With {.NoCache = True}
                Return client
            End Function)

        ''' <summary>Holt die veroeffentlichte Nummer. Leerer Text heisst: nicht erreichbar oder
        ''' unbrauchbare Antwort - in beiden Faellen wird nichts angezeigt.</summary>
        Public Shared Async Function FetchLatestVersionAsync(Optional cancel As CancellationToken = Nothing) As Task(Of String)
            Try
                Using response = Await _client.Value.GetAsync(VersionAddress, cancel)
                    If Not response.IsSuccessStatusCode Then Return ""
                    Dim body = Await response.Content.ReadAsStringAsync(cancel)
                    Return Sanitize(body)
                End Using
            Catch ex As Exception
                ' Absichtlich nur ins Diagnoselog: eine gescheiterte Abfrage ist ein Nichtereignis.
                DiagnosticLogService.LogException("UpdateCheckService.FetchLatestVersionAsync", ex)
                Return ""
            End Try
        End Function

        ''' <summary>Uebernimmt aus der Antwort nur, was wie eine Versionsnummer aussieht. Was am
        ''' anderen Ende steht, bestimmt nicht die Anwendung - eine Fehlerseite oder ein
        ''' Anmeldeformular darf nicht als Version in der Oberflaeche landen.</summary>
        Public Shared Function Sanitize(raw As String) As String
            If String.IsNullOrWhiteSpace(raw) Then Return ""
            Dim first = raw.Split(New Char() {ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            If first Is Nothing Then Return ""
            first = first.Trim()
            If first.Length = 0 OrElse first.Length > 32 Then Return ""
            ' Zahlengruppen mit Punkt, dahinter hoechstens zwei Anhaengsel wie "-6" oder "-beta2".
            If Not Regex.IsMatch(first, "^[0-9]{1,5}(\.[0-9]{1,5}){0,3}([-.][0-9A-Za-z]{1,12}){0,2}$") Then Return ""
            Return first
        End Function

        ''' <summary>True, wenn <paramref name="published"/> eine ANDERE Fassung bezeichnet als die
        ''' laufende - nicht nur eine hoehere.
        '''
        ''' <para>Absicht: auch ein geplanter Rueckschritt soll ankommen. Wird eine Fassung
        ''' zurueckgezogen und eine aeltere veroeffentlicht, ist das fuer den Nutzer genauso ein
        ''' Handlungsgrund wie eine neue, und ein Vergleich auf "hoeher" wuerde ihn stumm
        ''' verschlucken.</para>
        '''
        ''' <para>Verglichen werden die Zahlengruppen der eigentlichen Versionsnummer; fehlende
        ''' Gruppen zaehlen als Null. Damit sind 0.8.1 und 0.8.1.0 dieselbe Fassung. Alles nach
        ''' dem Bindestrich ist der Paketstand (PKrel) und wird absichtlich ignoriert: 0.8.1-2
        ''' und 0.8.1-3 bezeichnen dieselbe Programmversion.</para></summary>
        Public Shared Function IsDifferent(published As String, current As String) As Boolean
            Dim theirs = NumberGroups(published)
            Dim ours = NumberGroups(current)
            ' Ohne eine der beiden Nummern gibt es nichts zu vergleichen, und dann wird geschwiegen.
            If theirs.Count = 0 OrElse ours.Count = 0 Then Return False
            For i = 0 To Math.Max(theirs.Count, ours.Count) - 1
                Dim left As Long = If(i < theirs.Count, theirs(i), 0L)
                Dim right As Long = If(i < ours.Count, ours(i), 0L)
                If left <> right Then Return True
            Next
            Return False
        End Function

        ''' <summary>Die Zahlengruppen der Programmversion, ohne den Paketstand (PKrel).</summary>
        Private Shared Function NumberGroups(text As String) As List(Of Long)
            Dim groups As New List(Of Long)()
            If String.IsNullOrWhiteSpace(text) Then Return groups

            ' VERSION wird auch fuer die Paketierung genutzt: bei "0.8.1-3" ist "-3" nur die
            ' Revision des Pakets, keine neue Programmfassung. Es darf daher nicht in den
            ' Updatevergleich eingehen.
            Dim version = text.Trim()
            Dim packageRelease = version.IndexOf("-"c)
            If packageRelease >= 0 Then version = version.Substring(0, packageRelease)

            For Each hit As Match In Regex.Matches(version, "[0-9]+")
                Dim value As Long
                ' Eine absurd lange Ziffernfolge fliegt raus, statt den Vergleich zu sprengen.
                If Not Long.TryParse(hit.Value, NumberStyles.None, CultureInfo.InvariantCulture, value) Then Continue For
                groups.Add(value)
                If groups.Count >= 6 Then Exit For
            Next
            Return groups
        End Function

    End Class

End Namespace
