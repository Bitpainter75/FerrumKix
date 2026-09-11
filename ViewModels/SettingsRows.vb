Imports System
Imports Avalonia.Media
Imports FerrumPlay.Services

Namespace ViewModels

    ''' <summary>Eine Stufe der Schriftgroesse in den Einstellungen.
    '''
    ''' <para>Jede Stufe zeigt ihre Probe in GENAU DER Groesse, die sie einstellt: man soll sehen,
    ''' was man waehlt, statt eine Zahl zu deuten. Die Groesse kommt dabei aus dem
    ''' Erscheinungsbild und steht nicht ein zweites Mal im Quelltext.</para></summary>
    Public NotInheritable Class FontSizeChoice
        Inherits ViewModelBase

        Private _isActive As Boolean

        Public Sub New(offset As Integer)
            Me.Offset = offset
            SampleSize = FontScaleService.BaseSize("FP.Font.Body") + offset
        End Sub

        Public ReadOnly Property Offset As Integer
        Public ReadOnly Property SampleSize As Double

        Public ReadOnly Property Tip As String
            Get
                If Offset = 0 Then Return LocalizationService.T("Auslieferung")
                Return If(Offset > 0, $"Plus {Offset}", $"Minus {Math.Abs(Offset)}")
            End Get
        End Property

        Public Sub RefreshText()
            RaisePropertyChanged(NameOf(Tip))
        End Sub

        Public Property IsActive As Boolean
            Get
                Return _isActive
            End Get
            Set(value As Boolean)
                SetField(_isActive, value)
            End Set
        End Property

    End Class

    ''' <summary>Eine Farbe der Akzentauswahl.</summary>
    Public NotInheritable Class AccentChoice
        Inherits ViewModelBase

        Private _isActive As Boolean

        Public Sub New(hexColor As String)
            Me.HexColor = hexColor
            ' Der Pinsel wird HIER gebaut und nicht in der Ansicht: eine Bindung auf eine
            ' Zeichenkette muesste dort erst gewandelt werden, und ob das gelingt, sieht man
            ' einer stillen leeren Flaeche nicht an.
            Swatch = New SolidColorBrush(Color.Parse(hexColor))
        End Sub

        Public ReadOnly Property HexColor As String
        Public ReadOnly Property Swatch As IBrush

        Public Property IsActive As Boolean
            Get
                Return _isActive
            End Get
            Set(value As Boolean)
                SetField(_isActive, value)
            End Set
        End Property

    End Class

    ''' <summary>Ein angeschlossener Bildschirm und sein Vergroesserungsfaktor.
    '''
    ''' <para>Die Liste entsteht erst, wenn das Fenster steht: vorher kennt die Anwendung keine
    ''' Bildschirme. Der Faktor wirkt umgekehrt erst beim NAECHSTEN Start, weil Avalonia die
    ''' Umgebungsvariable nur beim Hochfahren liest.</para></summary>
    Public NotInheritable Class ScreenScaleRow
        Inherits ViewModelBase

        Private _scale As Double = 1.0

        Public Sub New(screenName As String, description As String, scale As Double)
            Me.ScreenName = screenName
            Me.Description = description
            _scale = AppSettingsService.NormalizeScale(scale)
        End Sub

        Public ReadOnly Property ScreenName As String

        ''' <summary>Aufloesung und Punktdichte, damit sich zwei gleich benannte Bildschirme
        ''' auseinanderhalten lassen.</summary>
        Public ReadOnly Property Description As String

        Public Property Scale As Double
            Get
                Return _scale
            End Get
            Set(value As Double)
                If SetField(_scale, AppSettingsService.NormalizeScale(value)) Then
                    RaisePropertyChanged(NameOf(ScaleText))
                End If
            End Set
        End Property

        Public ReadOnly Property ScaleText As String
            Get
                Return LocalizationService.Format("{0} Prozent", CInt(Math.Round(_scale * 100)))
            End Get
        End Property

        Public Sub RefreshText()
            RaisePropertyChanged(NameOf(ScaleText))
        End Sub

    End Class

    ''' <summary>Eine Stufe der Lautstaerkeangleichung. <see cref="Mode"/> ist der Wert aus den
    ''' Einstellungen (<see cref="AppSettings.ReplayGainMode"/>), der Name wird im Code
    ''' uebersetzt: er steht gebunden in einer Auswahlliste, und die erreicht der Durchlauf ueber
    ''' den Baum nicht.</summary>
    Public NotInheritable Class ReplayGainChoice
        Inherits ViewModelBase

        Private ReadOnly _label As String

        Public Sub New(mode As Integer, label As String)
            Me.Mode = mode
            _label = label
        End Sub

        Public ReadOnly Property Mode As Integer

        Public ReadOnly Property Name As String
            Get
                Return LocalizationService.T(_label)
            End Get
        End Property

        Public Sub RefreshText()
            RaisePropertyChanged(NameOf(Name))
        End Sub

    End Class

    ''' <summary>Eine Sprache der Auswahlliste. Der Name steht in der Sprache selbst und wird
    ''' nicht uebersetzt - nur der Eintrag fuer die Systemsprache hat keinen eigenen und folgt der
    ''' Oberflaeche.</summary>
    Public NotInheritable Class LanguageChoice
        Inherits ViewModelBase

        Private ReadOnly _nativeName As String

        Public Sub New(key As String, nativeName As String)
            Me.Key = key
            _nativeName = nativeName
        End Sub

        Public ReadOnly Property Key As String

        Public ReadOnly Property Name As String
            Get
                Return If(String.IsNullOrEmpty(_nativeName), LocalizationService.T("Systemsprache"), _nativeName)
            End Get
        End Property

        Public Sub RefreshText()
            RaisePropertyChanged(NameOf(Name))
        End Sub

    End Class

End Namespace
