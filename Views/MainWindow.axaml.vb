Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports Avalonia.Platform.Storage
Imports FerrumKix.Services
Imports FerrumKix.ViewModels

Namespace Views

    Public Class MainWindow
        Inherits Window

        ''' <summary>Das ViewModel, an dessen Meldungen das Fenster haengt. Gemerkt, damit der
        ''' Handler beim naechsten Wechsel des DataContext wieder abgehaengt werden kann.</summary>
        Private _observedViewModel As MainWindowViewModel

        ''' <summary>Gesetzt, sobald die Anwendung wirklich enden soll: ueber das Menue des Symbols
        ''' oder MPRIS. Dann verbirgt das Schliessen nicht, auch wenn es so eingestellt ist.</summary>
        Private _quitRequested As Boolean

        ''' <summary>Der Zustand vor dem Verbergen, damit ein maximiertes Fenster maximiert
        ''' zurueckkommt. Minimiert zaehlt nicht: wer das Fenster zurueckholt, will es sehen.</summary>
        Private _stateBeforeHide As WindowState = WindowState.Normal

        ''' <summary>Das Fenster ging in den Infobereich oder kam von dort zurueck.</summary>
        Public Event TrayVisibilityChanged As EventHandler

        ''' <summary>Hoehe der schmalen Leiste. Steht ebenso in MiniBarView.axaml; wer eine
        ''' aendert, aendert beide.</summary>
        Private Const BarHeight As Double = 48

        ''' <summary>So schmal darf die Leiste werden. Darunter haetten Titel und Fortschritt
        ''' keinen Platz mehr neben den Knoepfen.</summary>
        Private Const BarMinWidth As Double = 560

        ''' <summary>Ob die Leiste gerade angewandt ist. Getrennt vom ViewModel gefuehrt, weil das
        ''' Umschalten Groesse und Lage merkt und zurueckgibt und dafuer wissen muss, woher es kommt.</summary>
        Private _barApplied As Boolean

        ' Die volle Ansicht, wie sie vor der Leiste war: dorthin geht es zurueck.
        Private _normalWidth As Double
        Private _normalHeight As Double
        Private _normalPosition As PixelPoint
        Private _normalMaximized As Boolean
        Private _fullMinWidth As Double
        Private _fullMinHeight As Double

        Public Sub New()
            InitializeComponent()
            _fullMinWidth = MinWidth
            _fullMinHeight = MinHeight
            WireWindowChrome()
            RestoreWindowPlacement()

            AddHandler DataContextChanged, AddressOf OnWindowDataContextChanged
            AddHandler Closing, AddressOf OnWindowClosing
            AddHandler KeyDown, AddressOf OnWindowKeyDown
            AddHandler Opened, AddressOf OnWindowOpened
            ' Auch das Minimieren durch den Fenstermanager - Tastenkuerzel, Taskleiste - soll in den
            ' Infobereich fuehren, nicht nur der eigene Knopf.
            AddHandler PropertyChanged,
                Sub(sender, e)
                    If e.Property IsNot WindowStateProperty Then Return
                    If WindowState <> WindowState.Minimized Then
                        _stateBeforeHide = WindowState
                    ElseIf AppSettingsService.Current.MinimizeToTray AndAlso TrayIconService.CanHideToTray() Then
                        HideToTray()
                    End If
                End Sub
            ' Das Wayland-Backend meldet die Bildschirme NACH dem Oeffnen nach - beim Opened steht
            ' Screens.All dort noch leer, und ohne dieses Ereignis blieben die Einstellungen ohne
            ' Bildschirmliste und das Fenster unvergroessert. Unter X11 kommt es einmal zusaetzlich
            ' und schadet nicht.
            AddHandler Screens.Changed, AddressOf OnScreensChanged
            ' Kommt das Fenster wieder nach vorn, wird nachgesehen, ob fehlende Dateien wieder da
            ' sind - oder weitere fehlen. Das Viewmodel drosselt selbst.
            AddHandler Activated, Sub(sender, e) ViewModel?.RecheckMissingFiles()
            ' Die Beschreibung der Bildschirme ist ein fertiger Satz aus dem Code und wird beim
            ' Sprachwechsel neu gebaut. Die Faktoren kommen dabei aus den Einstellungen und bleiben.
            AddHandler LocalizationService.LanguageChanged, Sub(sender, e) OnWindowOpened(Me, EventArgs.Empty)

            ' ABLEGEN VON DATEIEN UND ORDNERN. Das Fenster nimmt sie ueberall an und nicht nur
            ' ueber der Liste: wer eine Datei auf ein Fenster zieht, zielt auf die Anwendung.
            '
            ' Die beiden Ereignisse sind ROUTED EVENTS und keine gewoehnlichen. Sie muessen deshalb
            ' ueber die Methode von Interactive angemeldet werden - die AddHandler-Anweisung von VB
            ' kennt sie nicht.
            DragDrop.SetAllowDrop(Me, True)
            Me.AddHandler(DragDrop.DragOverEvent, New EventHandler(Of DragEventArgs)(AddressOf OnDragOver))
            Me.AddHandler(DragDrop.DropEvent, New EventHandler(Of DragEventArgs)(AddressOf OnDrop))
        End Sub

        Private Sub InitializeComponent()
            AvaloniaXamlLoader.Load(Me)
        End Sub

        Private ReadOnly Property ViewModel As MainWindowViewModel
            Get
                Return TryCast(DataContext, MainWindowViewModel)
            End Get
        End Property

        ' Fensterrahmen

        Private Sub WireWindowChrome()
            WireButton("MinimizeButton", AddressOf MinimizeWindow)
            WireButton("MaximizeButton", AddressOf ToggleMaximized)
            WireButton("CloseButton", Sub() Close())

            WireResizeBorder("ResizeTop", WindowEdge.North)
            WireResizeBorder("ResizeBottom", WindowEdge.South)
            WireResizeBorder("ResizeLeft", WindowEdge.West)
            WireResizeBorder("ResizeRight", WindowEdge.East)
            WireResizeBorder("ResizeTopLeft", WindowEdge.NorthWest)
            WireResizeBorder("ResizeTopRight", WindowEdge.NorthEast)
            WireResizeBorder("ResizeBottomLeft", WindowEdge.SouthWest)
            WireResizeBorder("ResizeBottomRight", WindowEdge.SouthEast)

            Dim frame = Me.FindControl(Of Border)("WindowFrame")
            If frame IsNot Nothing Then AddHandler frame.PointerPressed, AddressOf OnFramePointerPressed

            ' Noch ohne ViewModel - die Seite kommt hier direkt aus der Einstellung. Das ist
            ' dieselbe Quelle, und die Knoepfe stehen damit schon beim ersten Zeichnen richtig.
            ApplyWindowButtonsSide()
        End Sub

        ''' <summary>Haengt sich an das ViewModel, sobald es steht. Gebraucht wird genau eine
        ''' Meldung: die umgestellte Seite der Fensterknoepfe.</summary>
        Private Sub OnWindowDataContextChanged(sender As Object, e As EventArgs)
            If _observedViewModel IsNot Nothing Then
                RemoveHandler _observedViewModel.PropertyChanged, AddressOf OnViewModelPropertyChanged
            End If
            _observedViewModel = Me.ViewModel
            If _observedViewModel IsNot Nothing Then
                AddHandler _observedViewModel.PropertyChanged, AddressOf OnViewModelPropertyChanged
            End If
            ApplyBarMode()
            ApplyWindowButtonsSide()
        End Sub

        Private Sub OnViewModelPropertyChanged(sender As Object, e As System.ComponentModel.PropertyChangedEventArgs)
            Select Case e.PropertyName
                Case NameOf(MainWindowViewModel.WindowButtonLayout)
                    ApplyWindowButtonsSide()
                Case NameOf(MainWindowViewModel.IsBarMode)
                    ApplyBarMode()
                    ApplyWindowButtonsSide()
                Case NameOf(MainWindowViewModel.IsBarPinned)
                    Topmost = _barApplied AndAlso Me.ViewModel.IsBarPinned
            End Select
        End Sub

        ' Die schmale Leiste

        ''' <summary>Setzt das Fenster auf die Leiste oder zurueck auf die volle Ansicht.
        '''
        ''' <para>HIN: Groesse, Lage und Maximiert der vollen Ansicht werden gemerkt. Das Fenster
        ''' wird so hoch wie die Leiste und laesst sich nur noch in der Breite ziehen; Breite und
        ''' Lage der Leiste kommen aus den Einstellungen, damit sie wieder dort steht, wo man sie
        ''' hingelegt hat. Festgepinnt bleibt sie ueber allen Fenstern (Topmost).</para>
        '''
        ''' <para>ZURUECK: Breite und Lage der Leiste werden gemerkt, die volle Ansicht kommt in
        ''' ihrer alten Groesse an ihrer alten Stelle wieder, maximiert, wenn sie es war.</para>
        '''
        ''' <para>Unter Wayland setzt der Compositor Lage und "oben halten" nach eigenen Regeln;
        ''' beides ist dort ein Wunsch und keine Zusage.</para></summary>
        Private Sub ApplyBarMode()
            Dim viewModel = Me.ViewModel
            Dim wanted = viewModel IsNot Nothing AndAlso viewModel.IsBarMode
            If wanted = _barApplied Then
                Topmost = _barApplied AndAlso viewModel IsNot Nothing AndAlso viewModel.IsBarPinned
                Return
            End If

            Dim settings = AppSettingsService.Current
            If wanted Then
                _normalMaximized = WindowState = WindowState.Maximized OrElse WindowState = WindowState.FullScreen
                If _normalMaximized Then
                    ' Maximiert ist die Groesse die des Bildschirms. Die eigene steht noch in den
                    ' Einstellungen, vom letzten Mal, als das Fenster normal war.
                    _normalWidth = settings.WindowWidth
                    _normalHeight = settings.WindowHeight
                    _normalPosition = New PixelPoint(settings.WindowLeft, settings.WindowTop)
                Else
                    _normalWidth = Width
                    _normalHeight = Height
                    _normalPosition = Position
                End If

                WindowState = WindowState.Normal
                MinWidth = BarMinWidth
                MinHeight = BarHeight
                MaxHeight = BarHeight
                Height = BarHeight
                Width = Math.Max(BarMinWidth, settings.BarWidth)
                Dim barPosition = New PixelPoint(settings.BarLeft, settings.BarTop)
                If settings.BarLeft >= 0 AndAlso settings.BarTop >= 0 AndAlso IsOnAnyScreen(barPosition) Then Position = barPosition
                Topmost = viewModel.IsBarPinned
            Else
                RememberBarPlacement()
                Topmost = False
                MaxHeight = Double.PositiveInfinity
                MinHeight = _fullMinHeight
                MinWidth = _fullMinWidth
                Width = Math.Max(_fullMinWidth, _normalWidth)
                Height = Math.Max(_fullMinHeight, _normalHeight)
                If _normalPosition.X >= 0 AndAlso _normalPosition.Y >= 0 AndAlso IsOnAnyScreen(_normalPosition) Then Position = _normalPosition
                If _normalMaximized Then WindowState = WindowState.Maximized
            End If
            _barApplied = wanted

            ' Die Leiste laesst sich nur in der Breite ziehen: oben, unten und an den Ecken
            ' faengt nichts mehr die Maus ab.
            For Each handleName In {"ResizeTop", "ResizeBottom", "ResizeTopLeft", "ResizeTopRight", "ResizeBottomLeft", "ResizeBottomRight"}
                Dim border = Me.FindControl(Of Border)(handleName)
                If border IsNot Nothing Then border.IsVisible = Not wanted
            Next
            UpdateMaximizeGlyph()
        End Sub

        Private Sub RememberBarPlacement()
            Dim settings = AppSettingsService.Current
            settings.BarWidth = Width
            settings.BarLeft = Position.X
            settings.BarTop = Position.Y
        End Sub

        ''' <summary>Setzt die Fensterknoepfe auf die eingestellte Seite - rechts wie ausgeliefert
        ''' oder links, wenn die Einstellung oder der Arbeitsplatz es so will (siehe
        ''' <see cref="WindowButtonSideService"/>). Uebernommen aus FerrumPix.
        '''
        ''' Drei Dinge wechseln gemeinsam, sonst stimmt das Bild nicht: die AUSRICHTUNG des
        ''' Knopfblocks samt seinem Rand zur Fensterkante, die REIHENFOLGE der Knoepfe (auch sie
        ''' kommt vom Arbeitsplatz) und der NAMENSZUG, der auf die frei gewordene Seite
        ''' rueckt.</summary>
        Private Sub ApplyWindowButtonsSide()
            Dim host = Me.FindControl(Of Border)("CustomWindowControlsHost")
            Dim panel = Me.FindControl(Of StackPanel)("CustomWindowControls")
            Dim logo = Me.FindControl(Of StackPanel)("WindowLogoPanel")
            If host Is Nothing OrElse panel Is Nothing Then Return

            ' Im Browser gibt es nichts zu minimieren, zu maximieren oder zu schliessen: das Fenster
            ' fuellt den Tab, und der gehoert dem Browser.
            If BrowserBridge.IsActive Then
                host.IsVisible = False
                Return
            End If
            ' Die Leiste bringt ihre eigenen Knoepfe mit.
            host.IsVisible = Not _barApplied

            ' Beim Bauen des Fensters gibt es noch kein ViewModel - dann direkt ueber die
            ' Einstellung, es ist dieselbe Quelle.
            Dim layout = Me.ViewModel?.WindowButtonLayout
            If layout Is Nothing Then layout = WindowButtonSideService.Resolve(AppSettingsService.Current.WindowButtonsSide)
            Dim onLeft = layout.OnLeft

            host.HorizontalAlignment = If(onLeft, Avalonia.Layout.HorizontalAlignment.Left,
                                                  Avalonia.Layout.HorizontalAlignment.Right)
            ' Derselbe Abstand zur Fensterkante, nur gespiegelt. Die 4 oben halten die 24 hohen
            ' Knoepfe in der Mitte der 32 hohen Leiste - siehe TopWindowDragArea.
            host.Margin = If(onLeft, New Thickness(8, 4, 0, 0), New Thickness(0, 4, 8, 0))

            ' Die Knoepfe neu einhaengen statt sie zu tauschen: der Block ist drei Kinder gross,
            ' und eine Reihenfolge zu beschreiben ist weniger fehleranfaellig, als sie zu sortieren.
            ' Die Rollennamen sind so gewaehlt, dass Name plus "Button" der Name im Markup ist.
            Dim buttons = layout.Order.
                Select(Function(role) Me.FindControl(Of Button)(role & "Button")).
                Where(Function(button) button IsNot Nothing).ToList()
            If buttons.Count = panel.Children.Count Then
                panel.Children.Clear()
                For Each button In buttons
                    panel.Children.Add(button)
                Next
            End If

            ' Der Namenszug weicht den Knoepfen aus. Links stehende Knoepfe schoeben ihn sonst vor
            ' sich her.
            If logo IsNot Nothing Then
                Grid.SetColumn(logo, If(onLeft, 2, 0))
                logo.HorizontalAlignment = If(onLeft, Avalonia.Layout.HorizontalAlignment.Right,
                                                      Avalonia.Layout.HorizontalAlignment.Left)
                logo.Margin = If(onLeft, New Thickness(0, 0, 16, 0), New Thickness(16, 0, 0, 0))
            End If
        End Sub

        ' Infobereich

        Public ReadOnly Property IsHiddenToTray As Boolean
            Get
                Return Not IsVisible
            End Get
        End Property

        Private Sub MinimizeWindow()
            If BrowserBridge.IsActive Then Return
            If AppSettingsService.Current.MinimizeToTray AndAlso TrayIconService.CanHideToTray() Then
                HideToTray()
            Else
                WindowState = WindowState.Minimized
            End If
        End Sub

        ''' <summary>Verbirgt das Fenster. Die Wiedergabe laeuft weiter; zurueck kommt es ueber das
        ''' Symbol, MPRIS oder einen zweiten Aufruf.</summary>
        Public Sub HideToTray()
            If WindowState <> WindowState.Minimized Then _stateBeforeHide = WindowState
            Hide()
            RaiseEvent TrayVisibilityChanged(Me, EventArgs.Empty)
        End Sub

        ''' <summary>Holt das Fenster zurueck - aus dem Infobereich ebenso wie aus der Taskleiste.</summary>
        Public Sub ShowFromTray()
            Dim wasHidden = Not IsVisible
            If wasHidden Then Show()
            If WindowState = WindowState.Minimized OrElse wasHidden Then WindowState = _stateBeforeHide
            Activate()
            If wasHidden Then RaiseEvent TrayVisibilityChanged(Me, EventArgs.Empty)
        End Sub

        Public Sub ToggleFromTray()
            If IsVisible AndAlso WindowState <> WindowState.Minimized Then
                HideToTray()
            Else
                ShowFromTray()
            End If
        End Sub

        ''' <summary>Beendet die Anwendung, auch wenn das Schliessen sonst nur verbirgt.</summary>
        Public Sub QuitApplication()
            _quitRequested = True
            Close()
        End Sub

        Private Sub WireButton(name As String, action As Action)
            Dim button = Me.FindControl(Of Button)(name)
            If button IsNot Nothing Then AddHandler button.Click, Sub(sender, e) action()
        End Sub

        Private Sub WireResizeBorder(name As String, edge As WindowEdge)
            Dim border = Me.FindControl(Of Border)(name)
            If border Is Nothing Then Return
            AddHandler border.PointerPressed,
                Sub(sender, e)
                    If BrowserBridge.IsActive Then Return
                    If WindowState = WindowState.Maximized Then Return
                    If Not e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then Return
                    BeginResizeDrag(edge, e)
                    e.Handled = True
                End Sub
        End Sub

        ''' <summary>Das Fenster verschieben. Der Handler haengt am Rahmen und damit an der ganzen
        ''' Fensterflaeche; ohne die Pruefung unten liesse sich das Fenster an jeder Stelle des
        ''' Inhalts greifen. Gezogen wird an der Titelzeile (Klasse "window-drag") und in den
        ''' Randstreifen von <see cref="EdgeDragZone"/> Punkten: unten immer, oben nur, wenn die
        ''' Titelzeile ausgeblendet ist. Alles Bedienbare gewinnt gegen den Zug.</summary>
        Private Sub OnFramePointerPressed(sender As Object, e As PointerPressedEventArgs)
            ' Im Browser weder verschieben noch per Doppelklick umschalten: das Fenster fuellt den Tab.
            If BrowserBridge.IsActive Then Return
            If Not e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then Return
            Dim source = TryCast(e.Source, Control)
            If Not IsInWindowDragArea(source) AndAlso Not IsInEdgeDragZone(source, e.GetPosition(Me)) Then Return

            ' In der Leiste holt ein Doppelklick auf eine freie Stelle die volle Ansicht zurueck.
            If _barApplied AndAlso e.ClickCount > 0 AndAlso e.ClickCount Mod 2 = 0 Then
                Me.ViewModel.IsBarMode = False
                e.Handled = True
                Return
            End If

            ' Doppelklick auf die obere Leiste schaltet zwischen maximiert und Normalgroesse -
            ' dieselbe Geste wie bei den Fenstern des Systems. Die Pruefung steht VOR dem Zug,
            ' sonst begaenne der zweite Klick nur eine weitere Fensterbewegung. Jeder zweite
            ' Klick zaehlt, damit vier Klicks zweimal umschalten statt gar nicht.
            If IsInTopBar(source) AndAlso e.ClickCount > 0 AndAlso e.ClickCount Mod 2 = 0 Then
                ToggleMaximized()
                e.Handled = True
                Return
            End If

            BeginMoveDrag(e)
        End Sub

        Private Function IsInTopBar(source As Control) As Boolean
            Dim topArea = Me.FindControl(Of Border)("TopWindowDragArea")
            Dim control = source
            While control IsNot Nothing
                If control Is topArea Then Return True
                control = TryCast(control.Parent, Control)
            End While
            Return False
        End Function

        ''' <summary>Breite der Randstreifen, an denen sich das Fenster ausser an der Titelzeile
        ''' greifen laesst (unten immer, oben bei ausgeblendeter Titelzeile).</summary>
        Private Const EdgeDragZone As Double = 20

        ''' <summary>Liegt der Druck im unteren Randstreifen oder - bei ausgeblendeter Titelzeile -
        ''' im oberen? Die Lage zaehlt hier und keine Klasse: der Streifen geht quer ueber alles,
        ''' was an der Kante liegt. Bedienbares darin gewinnt wie ueberall.</summary>
        Private Function IsInEdgeDragZone(source As Control, position As Point) As Boolean
            If source Is Nothing OrElse IsOverInteractiveControl(source) Then Return False
            Dim height = ClientSize.Height
            If position.Y >= height - EdgeDragZone Then Return True
            Dim topArea = Me.FindControl(Of Border)("TopWindowDragArea")
            Return topArea IsNot Nothing AndAlso Not topArea.IsEffectivelyVisible AndAlso position.Y < EdgeDragZone
        End Function

        Private Shared Function IsInWindowDragArea(source As Control) As Boolean
            If IsOverInteractiveControl(source) Then Return False
            Dim control = source
            While control IsNot Nothing
                If control.Classes.Contains("window-drag") Then Return True
                control = TryCast(control.Parent, Control)
            End While
            Return False
        End Function

        ''' <summary>Alles Bedienbare gewinnt gegen den Zug: sonst waere ein Knopf im Ziehbereich
        ''' nicht mehr klickbar, weil der Zug schon beim Druecken beginnt. Listen und rollender
        ''' Inhalt zaehlen mit, damit der untere Randstreifen keine Zeile oder Kachel schluckt.</summary>
        Private Shared Function IsOverInteractiveControl(source As Control) As Boolean
            Dim control = source
            While control IsNot Nothing
                If TypeOf control Is Button OrElse TypeOf control Is TextBox OrElse
                   TypeOf control Is Slider OrElse TypeOf control Is ComboBox OrElse
                   TypeOf control Is ListBox OrElse TypeOf control Is CheckBox OrElse
                   TypeOf control Is Controls.SeekBar OrElse TypeOf control Is Controls.VolumeKnob OrElse
                   TypeOf control Is Controls.RoundSlider OrElse
                   TypeOf control Is ItemsControl OrElse TypeOf control Is ItemsRepeater OrElse
                   TypeOf control Is ScrollViewer OrElse TypeOf control Is SelectableTextBlock OrElse
                   TypeOf control Is Avalonia.Controls.Primitives.ToggleButton OrElse
                   TypeOf control Is Avalonia.Controls.Primitives.ScrollBar Then Return True
                control = TryCast(control.Parent, Control)
            End While
            Return False
        End Function

        Private Sub ToggleMaximized()
            If BrowserBridge.IsActive OrElse _barApplied Then Return
            WindowState = If(WindowState = WindowState.Maximized, WindowState.Normal, WindowState.Maximized)
            UpdateMaximizeGlyph()
        End Sub

        Private Sub UpdateMaximizeGlyph()
            Dim glyph = Me.FindControl(Of TextBlock)("MaximizeGlyph")
            If glyph IsNot Nothing Then glyph.Text = If(WindowState = WindowState.Maximized, "❐", "□")
        End Sub

        ' Fensterlage

        ''' <summary>Stellt Groesse und Stelle des Fensters wieder her. Ein Fenster, das auf keinem
        ''' angeschlossenen Bildschirm mehr liegt, kommt in die Mitte des ersten: eine Lage von
        ''' einem Aufbau mit zwei Bildschirmen ist auf einem einzelnen unerreichbar.</summary>
        Private Sub RestoreWindowPlacement()
            ' Im Browser bestimmt der Tab Lage und Groesse, siehe BrowserBridge.ApplyViewport.
            If BrowserBridge.IsActive Then Return
            Dim settings = AppSettingsService.Current
            Width = Math.Max(MinWidth, settings.WindowWidth)
            Height = Math.Max(MinHeight, settings.WindowHeight)

            Dim target = New PixelPoint(settings.WindowLeft, settings.WindowTop)
            Dim usable = settings.WindowLeft >= 0 AndAlso settings.WindowTop >= 0 AndAlso IsOnAnyScreen(target)

            If usable Then
                Position = target
            Else
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            End If

            If settings.WindowMaximized Then WindowState = WindowState.Maximized
            UpdateMaximizeGlyph()
        End Sub

        Private Function IsOnAnyScreen(point As PixelPoint) As Boolean
            Try
                For Each screen In Screens.All
                    If screen.Bounds.Contains(point) Then Return True
                Next
            Catch ex As Exception
                DiagnosticLogService.LogException("Window.Screens", ex)
            End Try
            Return False
        End Function

        ''' <summary>Die Bildschirme kennt Avalonia erst, wenn das Fenster steht. Die Einstellungen
        ''' brauchen sie, um je Bildschirm einen Vergroesserungsfaktor anbieten zu koennen.</summary>
        ''' <summary>Ein Bildschirm kam dazu, fiel weg oder aenderte sich. Die Liste in den
        ''' Einstellungen muss dann neu aufgebaut werden.</summary>
        Private Sub OnScreensChanged(sender As Object, e As EventArgs)
            OnWindowOpened(Me, EventArgs.Empty)
        End Sub

        Private Sub OnWindowOpened(sender As Object, e As EventArgs)
            Dim viewModel = Me.ViewModel
            If viewModel Is Nothing Then Return

            Try
                Dim rows As New List(Of (Name As String, Description As String))()
                For Each screen In Screens.All
                    Dim name = If(screen.DisplayName, String.Empty)
                    If String.IsNullOrWhiteSpace(name) Then Continue For
                    rows.Add((name,
                              LocalizationService.Format("{0} mal {1}, Systemfaktor {2:0.##}",
                                                         screen.Bounds.Width, screen.Bounds.Height, screen.Scaling)))
                Next
                viewModel.SetScreens(rows)
            Catch ex As Exception
                DiagnosticLogService.LogException("Window.Screens", ex)
            End Try
        End Sub


        Private Sub OnWindowClosing(sender As Object, e As WindowClosingEventArgs)
            ' Schliessen verbirgt nur, wenn es so eingestellt ist und der Anwender es war. Faehrt
            ' die Sitzung herunter, muss das Fenster wirklich gehen - sonst haelt es die Abmeldung auf.
            If Not _quitRequested AndAlso e.CloseReason = WindowCloseReason.WindowClosing AndAlso
               AppSettingsService.Current.CloseToTray AndAlso TrayIconService.CanHideToTray() Then
                e.Cancel = True
                HideToTray()
                Return
            End If

            Try
                Dim settings = AppSettingsService.Current
                ' Im Browser gehoert die Fenstergroesse dem Tab. Gemerkt, kaeme das Fenster beim
                ' naechsten gewoehnlichen Start in der Groesse des Tabs wieder.
                If BrowserBridge.IsActive Then
                    Dim browserViewModel = Me.ViewModel
                    If browserViewModel IsNot Nothing Then settings.SidePanelWidth = browserViewModel.SidePanelWidth
                    AppSettingsService.Save()
                    Return
                End If
                ' In der Leiste gilt fuer die volle Ansicht, was vor dem Umschalten gemerkt wurde.
                If _barApplied Then
                    RememberBarPlacement()
                    settings.WindowMaximized = _normalMaximized
                    If Not _normalMaximized Then
                        settings.WindowWidth = _normalWidth
                        settings.WindowHeight = _normalHeight
                        settings.WindowLeft = _normalPosition.X
                        settings.WindowTop = _normalPosition.Y
                    End If
                    Dim barViewModel = Me.ViewModel
                    If barViewModel IsNot Nothing Then settings.SidePanelWidth = barViewModel.SidePanelWidth
                    AppSettingsService.Save()
                    Return
                End If
                settings.WindowMaximized = WindowState = WindowState.Maximized OrElse WindowState = WindowState.FullScreen

                ' Die Groesse im maximierten Zustand ist die des Bildschirms und nicht die, die der
                ' Anwender gewaehlt hat. Sie zu merken hiesse, das Fenster beim naechsten Start
                ' bildschirmfuellend zu oeffnen, auch wenn es gar nicht maximiert werden soll.
                If Not settings.WindowMaximized Then
                    settings.WindowWidth = Width
                    settings.WindowHeight = Height
                    settings.WindowLeft = Position.X
                    settings.WindowTop = Position.Y
                End If

                Dim viewModel = Me.ViewModel
                If viewModel IsNot Nothing Then settings.SidePanelWidth = viewModel.SidePanelWidth
                AppSettingsService.Save()
            Catch ex As Exception
                DiagnosticLogService.LogException("Window.Closing", ex)
            End Try
        End Sub

        ' Tastatur

        Private Sub OnWindowKeyDown(sender As Object, e As KeyEventArgs)
            Dim viewModel = Me.ViewModel
            If viewModel Is Nothing Then Return

            ' Steht eine Sicherheitsabfrage offen, gehoert ihr die Tastatur - und zwar GANZ.
            ' Ohne dieses Handled hielte die Leertaste hinter der Frage die Wiedergabe an.
            If viewModel.IsDialogOpen Then
                Select Case e.Key
                    Case Key.Escape : viewModel.CancelDialog()
                    Case Key.Enter : viewModel.ConfirmDialog()
                End Select
                e.Handled = True
                Return
            End If

            ' In einem Eingabefeld gehoert jede Taste dem Feld. Sonst hielte die Leertaste die
            ' Wiedergabe an, statt ein Leerzeichen in die Suche zu schreiben.
            If TypeOf TopLevel.GetTopLevel(Me)?.FocusManager?.GetFocusedElement() Is TextBox Then Return

            Select Case e.Key
                Case Key.Space
                    viewModel.PlayPauseCommand.Execute(Nothing)
                    e.Handled = True
                Case Key.MediaPlayPause
                    viewModel.PlayPauseCommand.Execute(Nothing)
                    e.Handled = True
                Case Key.MediaNextTrack
                    viewModel.NextCommand.Execute(Nothing)
                    e.Handled = True
                Case Key.MediaPreviousTrack
                    viewModel.PreviousCommand.Execute(Nothing)
                    e.Handled = True
                Case Key.MediaStop
                    viewModel.StopCommand.Execute(Nothing)
                    e.Handled = True
                Case Key.Escape
                    If viewModel.Mode <> AppMode.Player Then
                        viewModel.ClosePanelCommand.Execute(Nothing)
                        e.Handled = True
                    End If
            End Select
        End Sub

        ' Ablegen

        Private Sub OnDragOver(sender As Object, e As DragEventArgs)
            e.DragEffects = If(e.DataTransfer.Contains(DataFormat.File), DragDropEffects.Copy, DragDropEffects.None)
        End Sub

        Private Async Sub OnDrop(sender As Object, e As DragEventArgs)
            Dim viewModel = Me.ViewModel
            If viewModel Is Nothing Then Return

            Dim items = e.DataTransfer.TryGetFiles()
            If items Is Nothing Then Return

            ' Die Schleifenvariable heisst NICHT "item": "Item" ist die Standardeigenschaft von
            ' AvaloniaObject, von dem auch das Fenster abstammt, und VB haelt den Namen dann fuer
            ' diese Eigenschaft und verlangt ein Argument.
            Dim paths As New List(Of String)()
            For Each entry In items
                Dim localPath = entry.TryGetLocalPath()
                If Not String.IsNullOrWhiteSpace(localPath) Then paths.Add(localPath)
            Next
            If paths.Count = 0 Then Return

            Await viewModel.AddPathsAsync(paths)
        End Sub

    End Class

End Namespace
