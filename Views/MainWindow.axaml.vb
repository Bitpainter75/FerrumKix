Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports Avalonia.Media
Imports Avalonia.Platform.Storage
Imports FerrumPlay.Services
Imports FerrumPlay.ViewModels

Namespace Views

    Public Class MainWindow
        Inherits Window

        Public Sub New()
            InitializeComponent()
            WireWindowChrome()
            RestoreWindowPlacement()

            AddHandler Closing, AddressOf OnWindowClosing
            AddHandler KeyDown, AddressOf OnWindowKeyDown
            AddHandler Opened, AddressOf OnWindowOpened
            ' Das Wayland-Backend meldet die Bildschirme NACH dem Oeffnen nach - beim Opened steht
            ' Screens.All dort noch leer, und ohne dieses Ereignis blieben die Einstellungen ohne
            ' Bildschirmliste und das Fenster unvergroessert. Unter X11 kommt es einmal zusaetzlich
            ' und schadet nicht.
            AddHandler Screens.Changed, AddressOf OnScreensChanged
            AddHandler PositionChanged, AddressOf OnWindowPositionChanged
            ' Wann das Fenster welche Groesse bekommt und warum. Steht nur im Protokoll, kostet
            ' also ausgeschaltet nichts - und ist die einzige Stelle, an der sich eine Groesse, die
            ' erst verspaetet ankommt, von einer falsch berechneten unterscheiden laesst.
            AddHandler Resized, AddressOf OnWindowResized
            ' Kommt das Fenster wieder nach vorn, wird nachgesehen, ob fehlende Dateien wieder da
            ' sind - oder weitere fehlen. Das Viewmodel drosselt selbst.
            AddHandler Activated, Sub(sender, e) ViewModel?.RecheckMissingFiles()
            ' Ein verstellter Faktor wirkt sofort; ohne das bliebe das Fenster stehen, bis jemand
            ' die Anwendung neu startet.
            AddHandler DataContextChanged,
                Sub(sender, e)
                    Dim viewModel = Me.ViewModel
                    If viewModel Is Nothing Then Return
                    AddHandler viewModel.UiScaleChanged, AddressOf ApplyUiScale
                End Sub
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
        ''' <summary>Ein Bildschirm kam dazu, fiel weg oder aenderte sich. Beides haengt daran: die
        ''' Liste in den Einstellungen und die Vergroesserung des Fensters.</summary>
        Private Sub OnScreensChanged(sender As Object, e As EventArgs)
            OnWindowOpened(Me, EventArgs.Empty)
        End Sub

        ''' <summary>Wann das Fenster welche Groesse bekommt und warum. Der Anlass ist die
        ''' entscheidende Angabe: "Layout" heisst, dass die Anwendung selbst die Groesse betreibt,
        ''' und genau daran liess sich erkennen, dass sie gegen die Kachelverwaltung arbeitete -
        ''' und daran liess sich erkennen, wer die Groesse gerade betreibt.</summary>
        Private Sub OnWindowResized(sender As Object, e As WindowResizedEventArgs)
            DiagnosticLogService.Log("Window.Size", $"{e.ClientSize.Width:0}x{e.ClientSize.Height:0}, Anlass={e.Reason}, Zustand={WindowState}")
            ' MIT DER GROESSE AUS DEM EREIGNIS. Die Eigenschaft ClientSize traegt zu diesem
            ' Zeitpunkt unter Umstaenden noch den alten Wert; der Rahmen rechnete dann mit der
            ' Groesse von vorher weiter.
            ApplyUiScale(e.ClientSize)
        End Sub

        ''' <summary>Das Fenster steht jetzt woanders - unter Umstaenden auf einem Bildschirm mit
        ''' einem anderen Faktor.</summary>
        Private Sub OnWindowPositionChanged(sender As Object, e As PixelPointEventArgs)
            ApplyUiScale()
        End Sub

        Private Sub OnWindowOpened(sender As Object, e As EventArgs)
            ' VOR der Pruefung auf die Ansicht: die Vergroesserung haengt an den Einstellungen und
            ' nicht an ihr, und ohne Ansicht bliebe das Fenster sonst unvergroessert stehen.
            ApplyUiScale()
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

        ''' <summary>Legt die eingestellte Vergroesserung auf das Fenster.
        '''
        ''' <para>Genommen wird der Faktor des Bildschirms, auf dem das Fenster gerade steht - die
        ''' Einstellung fuehrt einen je Bildschirm, weil an einem kleinen Zweitschirm etwas anderes
        ''' noetig ist als am grossen. Findet sich keiner, bleibt es bei 1,0.</para>
        '''
        ''' <para>Frueher stand der Wert in einer Umgebungsvariablen, die nur Avalonias X11-Weg
        ''' liest; unter Wayland war der Regler damit wirkungslos. Ueber die Layout-Vergroesserung
        ''' gilt er ueberall und sofort.</para></summary>
        Friend Sub ApplyUiScale()
            ApplyUiScale(ClientSize)
        End Sub

        ''' <param name="size">Die Groesse, mit der gerechnet werden soll. Aus einem Ereignis die
        ''' dort mitgelieferte: die Eigenschaft <c>ClientSize</c> hinkt ihr unter Umstaenden noch
        ''' hinterher.</param>
        Friend Sub ApplyUiScale(size As Size)
            Try
                Dim frame = Me.FindControl(Of Border)("WindowFrame")
                Dim scale = TryCast(frame?.RenderTransform, ScaleTransform)
                If frame Is Nothing OrElse scale Is Nothing Then Return
                If size.Width <= 0 OrElse size.Height <= 0 Then Return

                Dim screenName = Screens.ScreenFromWindow(Me)?.DisplayName
                Dim factor = Math.Max(0.1, AppSettingsService.ScaleForScreen(screenName))

                ' Der Rahmen wird KLEINER als das Fenster gebaut und von der Vergroesserung wieder
                ' genau darauf gebracht. Sein Mass steht damit fest und haengt nicht daran, was der
                ' Inhalt sich wuenscht - das war die Stelle, an der es nach einem Wechsel des
                ' Arbeitsbereichs auseinanderlief.
                frame.Width = size.Width / factor
                frame.Height = size.Height / factor
                scale.ScaleX = factor
                scale.ScaleY = factor

                DiagnosticLogService.Log("Window.UiScale",
                                         $"Faktor={factor:0.##}, Fenster={size.Width:0}x{size.Height:0}, " &
                                         $"Rahmen={frame.Width:0}x{frame.Height:0}, Bildschirm={If(screenName, "?")}")
            Catch ex As Exception
                DiagnosticLogService.LogException("Window.UiScale", ex)
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
