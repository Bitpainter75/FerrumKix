Imports System
Imports System.Collections.Generic
Imports Avalonia

Namespace Services

    ''' <summary>Verschiebt alle Schriftgroessen der Oberflaeche um einen ganzzahligen Betrag,
    ''' indem die FP.Font.*-Ressourcen aus dem Erscheinungsbild zur Laufzeit ueberschrieben werden.
    '''
    ''' <para>Die FP.Glyph.*-Ressourcen bleiben bewusst unberuehrt: dort sitzen die Symbolzeichen
    ''' der Fensterknoepfe. Sie sind Grafik in fester Knopfgroesse und wuerden ueber ihren Rand
    ''' hinausragen, wenn sie mitwuechsen.</para>
    '''
    ''' <para>Uebernommen aus FerrumPix.</para></summary>
    Public NotInheritable Class FontScaleService
        Private Sub New()
        End Sub

        Private Shared ReadOnly TextSizeKeys As String() = {
            "FP.Font.Label",
            "FP.Font.Caption",
            "FP.Font.Small",
            "FP.Font.Body",
            "FP.Font.ItemTitle",
            "FP.Font.Subtitle",
            "FP.Font.Title",
            "FP.Font.Heading",
            "FP.Font.Display"
        }

        ''' <summary>Die im Erscheinungsbild erklaerten Ausgangsgroessen. Sie werden EINMALIG aus
        ''' den Ressourcen gelesen, bevor der erste Versatz sie ueberschreibt. Damit bleibt das
        ''' AXAML die einzige Stelle, an der die Zahlen stehen.</summary>
        Private Shared _baseSizes As Dictionary(Of String, Double)

        Public Shared ReadOnly Property CurrentOffset As Integer

        ''' <summary>Der zulaessige Bereich. Groesser als plus vier sprengt die Fussleiste, kleiner
        ''' als minus zwei ist nicht mehr lesbar.</summary>
        Public Shared Function Normalize(offset As Integer) As Integer
            Return Math.Clamp(offset, -2, 6)
        End Function

        Public Shared Sub Apply(offset As Integer)
            Dim app = Application.Current
            If app Is Nothing Then Return

            EnsureBaseSizes(app)
            offset = Normalize(offset)
            _CurrentOffset = offset

            For Each key In TextSizeKeys
                Dim baseSize As Double
                If Not _baseSizes.TryGetValue(key, baseSize) Then Continue For
                app.Resources(key) = baseSize + offset
            Next
        End Sub

        ''' <summary>Die Ausgangsgroesse einer Schriftgroessen-Ressource, also der Wert OHNE jeden
        ''' Versatz. Die Einstellungen zeigen damit jede Stufe in ihrer eigenen Groesse an: man soll
        ''' sehen, was man waehlt, statt eine Zahl zu deuten.</summary>
        Public Shared Function BaseSize(key As String) As Double
            Dim app = Application.Current
            If app Is Nothing OrElse String.IsNullOrEmpty(key) Then Return 0
            EnsureBaseSizes(app)
            Dim value As Double
            If _baseSizes IsNot Nothing AndAlso _baseSizes.TryGetValue(key, value) Then Return value
            Return 0
        End Function

        Private Shared Sub EnsureBaseSizes(app As Application)
            If _baseSizes IsNot Nothing Then Return

            _baseSizes = New Dictionary(Of String, Double)(StringComparer.Ordinal)
            For Each key In TextSizeKeys
                Dim value As Object = Nothing
                If app.TryGetResource(key, Nothing, value) AndAlso TypeOf value Is Double Then
                    _baseSizes(key) = CDbl(value)
                End If
            Next
        End Sub

    End Class

End Namespace
