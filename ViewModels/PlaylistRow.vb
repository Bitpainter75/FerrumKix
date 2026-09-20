Imports System
Imports FerrumKix.Models

Namespace ViewModels

    ''' <summary>Eine Zeile der Wiedergabeliste. Es gibt zwei Arten davon, und beide stehen in
    ''' derselben Liste: die Ueberschrift eines Albums und ein Titel darunter.
    '''
    ''' <para>EINE Liste und nicht eine je Gruppe, weil sonst jede Gruppe ihren eigenen Rollbereich
    ''' braeuchte. Welche Vorlage eine Zeile bekommt, entscheidet ihr Typ.</para></summary>
    Public MustInherit Class PlaylistRow
        Inherits ViewModelBase
    End Class

    ''' <summary>Die Ueberschrift ueber den Titeln eines Ordners.</summary>
    Public NotInheritable Class PlaylistGroupRow
        Inherits PlaylistRow

        Private _isExpanded As Boolean = True

        Public Sub New(folderPath As String, title As String, trackCount As Integer, totalSeconds As Double)
            Me.FolderPath = folderPath
            Me.Title = title
            Me.TrackCount = trackCount
            Me.TotalSeconds = totalSeconds
        End Sub

        Public ReadOnly Property FolderPath As String
        Public ReadOnly Property Title As String
        Public ReadOnly Property TrackCount As Integer
        Public ReadOnly Property TotalSeconds As Double
        Public ReadOnly Property IsAudioCdGroup As Boolean
            Get
                Return FolderPath.StartsWith("Audio-CD (", StringComparison.Ordinal)
            End Get
        End Property

        ''' <summary>Die Angabe rechts in der Ueberschrift: "8 / 32:21".</summary>
        Public ReadOnly Property SummaryText As String
            Get
                Return $"{TrackCount} / {Track.FormatTotalDuration(TotalSeconds)}"
            End Get
        End Property

        ''' <summary>Zugeklappt verschwinden die Titel dieser Gruppe aus der Liste. Die Anwendung
        ''' baut die Zeilen daraufhin neu auf; die Gruppe selbst bleibt stehen.</summary>
        Public Property IsExpanded As Boolean
            Get
                Return _isExpanded
            End Get
            Set(value As Boolean)
                If SetField(_isExpanded, value) Then RaisePropertyChanged(NameOf(ChevronSource))
            End Set
        End Property

        Public ReadOnly Property ChevronSource As String
            Get
                Return If(_isExpanded,
                          "avares://FerrumKix/Assets/Icons/outline/chevron-up.svg",
                          "avares://FerrumKix/Assets/Icons/outline/chevron-down.svg")
            End Get
        End Property

    End Class

    ''' <summary>Ein Titel in der Liste.</summary>
    Public NotInheritable Class PlaylistTrackRow
        Inherits PlaylistRow

        Private _isPlaying As Boolean
        Private _isEnabled As Boolean = True
        Private _isMissing As Boolean
        Private _number As Integer

        Public Sub New(track As Track)
            Me.Track = track
        End Sub

        Public ReadOnly Property Track As Track

        ''' <summary>Die Nummer, die vor dem Titel steht. Das ist die Stelle IN DER GRUPPE und
        ''' nicht die Nummer aus den Kennzeichen: eine Liste, deren Zahlen springen, weil ein Album
        ''' unvollstaendig ist, liest sich falsch.</summary>
        Public Property Number As Integer
            Get
                Return _number
            End Get
            Set(value As Integer)
                SetField(_number, value)
            End Set
        End Property

        ''' <summary>Ob dieser Titel gerade laeuft. Faerbt die Zeile.</summary>
        Public Property IsPlaying As Boolean
            Get
                Return _isPlaying
            End Get
            Set(value As Boolean)
                SetField(_isPlaying, value)
            End Set
        End Property

        ''' <summary>Das Haekchen vor dem Titel. Ausgeschaltete Titel bleiben in der Liste stehen,
        ''' werden aber uebersprungen. So laesst sich ein Album ohne die Zwischenspiele hoeren,
        ''' ohne dass man sie loeschen muss.</summary>
        Public Property IsEnabled As Boolean
            Get
                Return _isEnabled
            End Get
            Set(value As Boolean)
                SetField(_isEnabled, value)
            End Set
        End Property

        ''' <summary>Die Datei ist nicht mehr da. Die Zeile bleibt trotzdem stehen - ein
        ''' abgezogenes Laufwerk kommt wieder -, tritt aber zurueck und wird beim Abspielen
        ''' uebersprungen wie ein abgehakter Titel.</summary>
        Public Property IsMissing As Boolean
            Get
                Return _isMissing
            End Get
            Set(value As Boolean)
                SetField(_isMissing, value)
            End Set
        End Property

        Public ReadOnly Property NumberedTitle As String
            Get
                Return $"{Number}. {Track.DisplayTitle}"
            End Get
        End Property

    End Class

End Namespace
