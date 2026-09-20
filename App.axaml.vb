Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Controls.ApplicationLifetimes
Imports Avalonia.Markup.Xaml
Imports FerrumPlay.Services
Imports FerrumPlay.ViewModels
Imports FerrumPlay.Views

Public Class App
    Inherits Application

    Public Overrides Sub Initialize()
        AvaloniaXamlLoader.Load(Me)
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
            AddHandler viewModel.RaiseRequested,
                Sub(sender, e)
                    If window.WindowState = WindowState.Minimized Then window.WindowState = WindowState.Normal
                    window.Activate()
                End Sub
            AddHandler viewModel.QuitRequested, Sub(sender, e) window.Close()

            ' AUFRAEUMEN BEIM BEENDEN, und zwar hier und nicht im Fenster: das Fenster kann auch
            ' ohne Programmende geschlossen werden, und der Spieler haelt einen Fremdfaden. Wird er
            ' nicht abgebaut, bleibt libmpv mit seinem Ausgabegeraet stehen.
            AddHandler desktop.ShutdownRequested,
                Sub(sender, e)
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
