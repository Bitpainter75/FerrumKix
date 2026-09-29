Imports System
Imports System.ComponentModel
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Platform
Imports FerrumKix.ViewModels
Imports FerrumKix.Views

Namespace Services

    ''' <summary>Das Symbol im Infobereich der Kontrollleiste (Systray). Wunsch aus Issue #1.
    '''
    ''' <para>Avalonia bringt das Symbol selbst mit; unter Linux meldet es sich als
    ''' StatusNotifierItem ueber den Sitzungsbus an, unter X11 wie unter Wayland. Ein Klick holt das
    ''' Fenster nach vorn oder verbirgt es, das Menue steuert die Wiedergabe und beendet die
    ''' Anwendung - der einzige Weg dorthin, solange das Schliessen nur verbirgt.</para>
    '''
    ''' <para>Das Symbol wird EINMAL gebaut und danach nur ein- und ausgeblendet. Ein Symbol, das
    ''' bei jedem Umschalten neu entsteht, meldet sich jedes Mal neu am Bus an, und manche
    ''' Leisten zeigen es dann doppelt.</para></summary>
    Public NotInheritable Class TrayIconService
        Implements IDisposable

        ''' <summary>Der Dienst, bei dem sich Symbole unter Linux anmelden. Laeuft keiner - GNOME
        ''' ohne AppIndicator-Erweiterung, ein nackter Fenstermanager -, gibt es auch kein Symbol,
        ''' selbst wenn Avalonia eines anmeldet.</summary>
        Private Const StatusNotifierWatcher As String = "org.kde.StatusNotifierWatcher"

        Private ReadOnly _window As MainWindow
        Private ReadOnly _viewModel As MainWindowViewModel
        Private ReadOnly _trayIcon As TrayIcon
        Private ReadOnly _toggleWindowItem As NativeMenuItem
        Private ReadOnly _playPauseItem As NativeMenuItem
        Private ReadOnly _nextItem As NativeMenuItem
        Private ReadOnly _previousItem As NativeMenuItem
        Private ReadOnly _quitItem As NativeMenuItem

        Public Sub New(application As Application, window As MainWindow, viewModel As MainWindowViewModel)
            _window = window
            _viewModel = viewModel

            _toggleWindowItem = New NativeMenuItem()
            AddHandler _toggleWindowItem.Click, Sub(sender, e) _window.ToggleFromTray()
            _playPauseItem = New NativeMenuItem()
            AddHandler _playPauseItem.Click, Sub(sender, e) _viewModel.PlayPauseCommand.Execute(Nothing)
            _nextItem = New NativeMenuItem()
            AddHandler _nextItem.Click, Sub(sender, e) _viewModel.NextCommand.Execute(Nothing)
            _previousItem = New NativeMenuItem()
            AddHandler _previousItem.Click, Sub(sender, e) _viewModel.PreviousCommand.Execute(Nothing)
            _quitItem = New NativeMenuItem()
            AddHandler _quitItem.Click, Sub(sender, e) _window.QuitApplication()

            Dim menu As New NativeMenu()
            menu.Items.Add(_toggleWindowItem)
            menu.Items.Add(New NativeMenuItemSeparator())
            menu.Items.Add(_playPauseItem)
            menu.Items.Add(_nextItem)
            menu.Items.Add(_previousItem)
            menu.Items.Add(New NativeMenuItemSeparator())
            menu.Items.Add(_quitItem)

            _trayIcon = New TrayIcon With {
                .Icon = New WindowIcon(AssetLoader.Open(New Uri("avares://FerrumKix/Assets/FerrumKix_Icon_256.png"))),
                .Menu = menu,
                .IsVisible = AppSettingsService.Current.ShowTrayIcon
            }
            AddHandler _trayIcon.Clicked, Sub(sender, e) _window.ToggleFromTray()
            TrayIcon.SetIcons(application, New TrayIcons From {_trayIcon})

            AddHandler _viewModel.PropertyChanged, AddressOf OnViewModelPropertyChanged
            AddHandler _window.TrayVisibilityChanged, AddressOf OnTrayVisibilityChanged
            AddHandler LocalizationService.LanguageChanged, AddressOf OnLanguageChanged
            RefreshTexts()
        End Sub

        ''' <summary>Ob ein Symbol gerade ueberhaupt zu sehen waere. Nur dann darf das Fenster sich
        ''' verbergen: ohne Infobereich kaeme man danach nicht mehr heran, ausser ueber einen
        ''' zweiten Aufruf. Unter Windows und macOS gibt es den Bereich immer.</summary>
        Public Shared Function IsHostAvailable() As Boolean
            If Not OperatingSystem.IsLinux() Then Return True
            Dim connection = SingleInstanceService.Connection
            If connection Is Nothing Then Return False
            Try
                Return connection.NameHasOwner(StatusNotifierWatcher)
            Catch ex As Exception
                DiagnosticLogService.LogException("Tray.Host", ex)
                Return False
            End Try
        End Function

        ''' <summary>Ob das Fenster sich jetzt in den Infobereich verbergen darf.</summary>
        Public Shared Function CanHideToTray() As Boolean
            Return AppSettingsService.Current.ShowTrayIcon AndAlso IsHostAvailable()
        End Function

        Private Sub OnViewModelPropertyChanged(sender As Object, e As PropertyChangedEventArgs)
            Select Case e.PropertyName
                Case NameOf(MainWindowViewModel.ShowTrayIcon)
                    _trayIcon.IsVisible = AppSettingsService.Current.ShowTrayIcon
                Case NameOf(MainWindowViewModel.IsPlaying), NameOf(MainWindowViewModel.CurrentTrack), NameOf(MainWindowViewModel.CurrentTitle)
                    RefreshTexts()
            End Select
        End Sub

        Private Sub OnTrayVisibilityChanged(sender As Object, e As EventArgs)
            RefreshTexts()
        End Sub

        Private Sub OnLanguageChanged(sender As Object, e As EventArgs)
            RefreshTexts()
        End Sub

        ''' <summary>Die Beschriftungen folgen dem Zustand: "Pause" nur, wenn etwas spielt, und
        ''' der Hinweis am Symbol nennt den laufenden Titel.</summary>
        Private Sub RefreshTexts()
            _toggleWindowItem.Header = LocalizationService.T(If(_window.IsHiddenToTray, "Fenster zeigen", "Fenster verbergen"))
            _playPauseItem.Header = LocalizationService.T(If(_viewModel.IsPlaying, "Pause", "Abspielen"))
            _nextItem.Header = LocalizationService.T("Nächster Titel")
            _previousItem.Header = LocalizationService.T("Voriger Titel")
            _quitItem.Header = LocalizationService.T("Beenden")

            Dim title = _viewModel.CurrentTitle
            Dim artist = _viewModel.CurrentArtist
            If String.IsNullOrWhiteSpace(title) Then
                _trayIcon.ToolTipText = "FerrumKix"
            ElseIf String.IsNullOrWhiteSpace(artist) Then
                _trayIcon.ToolTipText = $"FerrumKix{Environment.NewLine}{title}"
            Else
                _trayIcon.ToolTipText = $"FerrumKix{Environment.NewLine}{artist} – {title}"
            End If
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            RemoveHandler _viewModel.PropertyChanged, AddressOf OnViewModelPropertyChanged
            RemoveHandler _window.TrayVisibilityChanged, AddressOf OnTrayVisibilityChanged
            RemoveHandler LocalizationService.LanguageChanged, AddressOf OnLanguageChanged
            _trayIcon.IsVisible = False
            _trayIcon.Dispose()
        End Sub

    End Class

End Namespace
