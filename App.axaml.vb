Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Controls.ApplicationLifetimes
Imports Avalonia.Markup.Xaml
Imports FerrumKix.Services
Imports FerrumKix.ViewModels
Imports FerrumKix.Views

Public Class App
    Inherits Application

    Public Overrides Sub Initialize()
        AvaloniaXamlLoader.Load(Me)
        UseAvailableUiFont()
    End Sub

    ''' <summary>Traegt keine der Schriften des Erscheinungsbilds, bekommt die Oberflaeche eine
    ''' Schrift, die auf diesem System wirklich da ist. Wie in FerrumPix.
    '''
    ''' <para>Die Ressource <c>FP.Font.Family</c> nennt serifenlose Schriften. Auf einem System
    ''' ohne eine einzige davon (schmale Systeme, AppImage-Umgebungen ohne vollstaendige
    ''' Fontconfig-Einrichtung) warf in FerrumPix der erste Textblock beim ersten Layout, und die
    ''' Anwendung endete vor dem Fenster. Der Ersatz steht in den Ressourcen der Anwendung, die vor
    ''' denen der Stile gefragt werden; alles, was die Schrift ueber die Ressource bezieht, folgt
    ''' also von selbst. Vor dem ersten Fenster, weil genau dessen erstes Layout sonst wirft.</para></summary>
    Private Sub UseAvailableUiFont()
        Try
            Dim found As Object = Nothing
            If Not TryGetResource("FP.Font.Family", Nothing, found) Then Return
            Dim replacement = Controls.UiTypeface.ResolveFallbackFamily(TryCast(found, Avalonia.Media.FontFamily))
            If replacement Is Nothing Then Return
            Resources("FP.Font.Family") = replacement
            DiagnosticLogService.LogAlways("App.Font", $"UI font list not available, using {replacement.Name}")
        Catch ex As Exception
            DiagnosticLogService.LogException("App.Font", ex)
        End Try
    End Sub

    Public Overrides Sub OnFrameworkInitializationCompleted()
        ' SCHRIFTGROESSE UND ERSCHEINUNGSBILD ZUERST. Beide ueberschreiben Ressourcen des
        ' Erscheinungsbilds, und das muss stehen, bevor das erste Fenster daraus liest - sonst
        ' blitzt beim Start kurz das ausgelieferte Bild auf. Die Akzentfarbe zieht der Theme-Dienst
        ' selbst nach: wie sie aufgehellt wird, haengt am Bild.
        FontScaleService.Apply(AppSettingsService.Current.FontSizeOffset)
        ThemeService.Apply(AppSettingsService.Current.ThemeMode)

        ' DIE SPRACHE VOR DEM VIEWMODEL: es baut Texte schon im Konstruktor.
        LocalizationService.LanguageMode = AppSettingsService.Current.LanguageMode

        ' KURZHINWEISE IN LISTENZEILEN entstehen erst, wenn die Zeile sichtbar wird - lange nach
        ' dem Durchlauf ueber das Fenster. Uebersetzt wird deshalb beim Oeffnen jedes Hinweises,
        ' mit EINEM Klassen-Handler fuer die ganze Anwendung. Weil das jedes Mal neu geschieht,
        ' kommt auch ein Sprachwechsel mitten in der Sitzung an.
        ToolTip.ToolTipOpeningEvent.AddClassHandler(Of Control)(
            Sub(control, e) LocalizationService.ApplyToTip(control))

        ' KONTEXTMENUES IN LISTENZEILEN ebenso: sie haengen nicht im Baum des Fensters, und die
        ' Zeile entsteht ohnehin erst spaeter. Der Klassen-Handler laeuft vor dem Handler, mit dem
        ' das Steuerelement sein Menue oeffnet - die Eintraege stehen also schon richtig da.
        Control.ContextRequestedEvent.AddClassHandler(Of Control)(
            Sub(control, e) LocalizationService.ApplyTo(control.ContextMenu))

        Dim desktop = TryCast(ApplicationLifetime, IClassicDesktopStyleApplicationLifetime)
        If desktop IsNot Nothing Then
            Dim viewModel As New MainWindowViewModel(Program.StartupPaths)
            Dim window As New MainWindow With {.DataContext = viewModel}

            ' NACH dem Setzen des Datenzusammenhangs: erst dann tragen die gebundenen Anzeigen ihre
            ' Bindung, und der Durchlauf erkennt und uebergeht sie.
            LocalizationService.ApplyTo(window)
            AddHandler LocalizationService.LanguageChanged, Sub(sender, e) LocalizationService.ApplyTo(window)

            ' MPRIS und ein zweiter Aufruf duerfen das Fenster nach vorn holen, MPRIS darf die
            ' Anwendung beenden. Ob das Fenster unter Wayland auch den Fokus bekommt, entscheidet
            ' der Compositor; Hyprland markiert es sonst nur als dringend.
            ' Ein im Infobereich verborgenes Fenster kommt dabei ebenfalls zurueck.
            AddHandler viewModel.RaiseRequested, Sub(sender, e) window.ShowFromTray()
            AddHandler viewModel.QuitRequested, Sub(sender, e) window.QuitApplication()

            ' DAS SYMBOL IM INFOBEREICH. Es wird immer gebaut und nur nach der Einstellung
            ' eingeblendet, damit der Schalter ohne Neustart wirkt.
            ' Im Browser nicht: die Plattform ohne Bildschirm hat keinen Infobereich.
            Dim tray As TrayIconService = Nothing
            If Not BrowserBridge.IsActive Then
                Try
                    tray = New TrayIconService(Me, window, viewModel)
                Catch ex As Exception
                    DiagnosticLogService.LogException("App.Tray", ex)
                End Try
            End If

            ' AUFRAEUMEN BEIM BEENDEN, und zwar hier und nicht im Fenster: das Fenster kann auch
            ' ohne Programmende geschlossen werden, und der Spieler haelt einen Fremdfaden. Wird er
            ' nicht abgebaut, bleibt libmpv mit seinem Ausgabegeraet stehen.
            AddHandler desktop.ShutdownRequested,
                Sub(sender, e)
                    Try
                        tray?.Dispose()
                    Catch ex As Exception
                        DiagnosticLogService.LogException("App.Shutdown", ex)
                    End Try
                    Try
                        viewModel.Dispose()
                    Catch ex As Exception
                        DiagnosticLogService.LogException("App.Shutdown", ex)
                    End Try
                End Sub

            desktop.MainWindow = window
        End If

        MyBase.OnFrameworkInitializationCompleted()
    End Sub

End Class
