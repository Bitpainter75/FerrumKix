Imports System
Imports FerrumKix.Services

Namespace ViewModels

    ' Die schmale Leiste (MiniBarView): FerrumKix als Streifen am Bildschirmrand, auf Wunsch
    ' ueber allen Fenstern. Hier nur der Zustand; was er fuer Groesse, Lage und "oben halten" des
    ' Fensters bedeutet, setzt MainWindow.ApplyBarMode um.
    Partial Public NotInheritable Class MainWindowViewModel

        Private _toggleBarModeCommand As DelegateCommand
        Private _toggleBarPinCommand As DelegateCommand

        ''' <summary>Im Browser gibt es die Leiste nicht: dort gehoert die Groesse dem Tab, und
        ''' ueber anderen Fenstern zu stehen hat keine Bedeutung.</summary>
        Public ReadOnly Property CanUseBarMode As Boolean
            Get
                Return Not BrowserBridge.IsActive
            End Get
        End Property

        Public Property IsBarMode As Boolean
            Get
                Return CanUseBarMode AndAlso AppSettingsService.Current.BarMode
            End Get
            Set(value As Boolean)
                If Not CanUseBarMode OrElse AppSettingsService.Current.BarMode = value Then Return
                AppSettingsService.Current.BarMode = value
                RaisePropertyChanged()
            End Set
        End Property

        Public Property IsBarPinned As Boolean
            Get
                Return AppSettingsService.Current.BarPinned
            End Get
            Set(value As Boolean)
                If AppSettingsService.Current.BarPinned = value Then Return
                AppSettingsService.Current.BarPinned = value
                RaisePropertyChanged()
                RaisePropertyChanged(NameOf(BarPinIconSource))
            End Set
        End Property

        ''' <summary>Gefuellt, solange die Leiste festgepinnt ist: so sieht man den Zustand auch
        ''' ohne die Akzentfarbe.</summary>
        Public ReadOnly Property BarPinIconSource As String
            Get
                Return If(IsBarPinned,
                          "avares://FerrumKix/Assets/Icons/phosphor/fill/push-pin-fill.svg",
                          "avares://FerrumKix/Assets/Icons/phosphor/regular/push-pin.svg")
            End Get
        End Property

        Public ReadOnly Property ToggleBarModeCommand As DelegateCommand
            Get
                If _toggleBarModeCommand Is Nothing Then
                    _toggleBarModeCommand = New DelegateCommand(Sub() IsBarMode = Not IsBarMode)
                End If
                Return _toggleBarModeCommand
            End Get
        End Property

        Public ReadOnly Property ToggleBarPinCommand As DelegateCommand
            Get
                If _toggleBarPinCommand Is Nothing Then
                    _toggleBarPinCommand = New DelegateCommand(Sub() IsBarPinned = Not IsBarPinned)
                End If
                Return _toggleBarPinCommand
            End Get
        End Property
    End Class
End Namespace
