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

        Public Sub New()
            InitializeComponent()
            WireWindowChrome()
            RestoreWindowPlacement()

            AddHandler DataContextChanged, AddressOf OnWindowDataContextChanged
            AddHandler Closing, AddressOf OnWindowClosing
            AddHandler KeyDown, AddressOf OnWindowKeyDown
            AddHandler Opened, AddressOf OnWindowOpened
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
            WireButton("MinimizeButton", Sub() WindowState = WindowState.Minimized)
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
            ApplyWindowButtonsSide()
        End Sub

        Private Sub OnViewModelPropertyChanged(sender As Object, e As System.ComponentModel.PropertyChangedEventArgs)
            If e.PropertyName = NameOf(MainWindowViewModel.WindowButtonLayout) Then ApplyWindowButtonsSide()
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

        Private Sub WireButton(name As String, action As Action)
            Dim button = Me.FindControl(Of Button)(name)
            If button IsNot Nothing Then AddHandler button.Click, Sub(sender, e) action()
        End Sub

        Private Sub WireResizeBorder(name As String, edge As WindowEdge)
            Dim border = Me.FindControl(Of Border)(name)
            If border Is Nothing Then Return
            AddHandler border.PointerPressed,
                Sub(sender, e)
                    If WindowState = WindowState.Maximized Then Return
                    If Not e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then Return
                    BeginResizeDrag(edge, e)
                    e.Handled = True
                End Sub
        End Sub

        ''' <summary>Das Fenster verschieben. Der Handler haengt am Rahmen und damit an der ganzen
        ''' Fensterflaeche; ohne die Pruefung unten liesse sich das Fenster an jeder Stelle des
        ''' Inhalts greifen. Ziehbereiche tragen deshalb ausdruecklich die Klasse "window-drag",
        ''' und alles Bedienbare darin gewinnt gegen den Zug.</summary>
        Private Sub OnFramePointerPressed(sender As Object, e As PointerPressedEventArgs)
            If Not e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then Return
            Dim source = TryCast(e.Source, Control)
            If Not IsInWindowDragArea(source) Then Return

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

        Private Shared Function IsInWindowDragArea(source As Control) As Boolean
            Dim control = source
            While control IsNot Nothing
                ' Alles Bedienbare gewinnt gegen den Ziehbereich: sonst waere ein Knopf in der
                ' Fussleiste nicht mehr klickbar, weil der Zug schon beim Druecken beginnt.
                If TypeOf control Is Button OrElse TypeOf control Is TextBox OrElse
                   TypeOf control Is Slider OrElse TypeOf control Is ComboBox OrElse
                   TypeOf control Is ListBox OrElse TypeOf control Is CheckBox OrElse
                   TypeOf control Is Controls.SeekBar OrElse TypeOf control Is Controls.VolumeKnob OrElse
                   TypeOf control Is Avalonia.Controls.Primitives.ToggleButton OrElse
                   TypeOf control Is Avalonia.Controls.Primitives.ScrollBar Then Return False
                If control.Classes.Contains("window-drag") Then Return True
                control = TryCast(control.Parent, Control)
            End While
            Return False
        End Function

        Private Sub ToggleMaximized()
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
            Try
                Dim settings = AppSettingsService.Current
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
