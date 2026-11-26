Imports System
Imports Avalonia.Controls
Imports Avalonia.Interactivity
Imports Avalonia.Markup.Xaml
Imports FerrumKix.ViewModels

Namespace Views

    ''' <summary>Die schmale Leiste, siehe MiniBarView.axaml. Was ueber eine Bindung geht, steht
    ''' dort; hier nur das Spulen, das ein Ereignis der Fortschrittsleiste ist, und das Schliessen,
    ''' das dem Fenster gehoert.</summary>
    Public Class MiniBarView
        Inherits UserControl

        Public Sub New()
            InitializeComponent()
            Dim seek = Me.FindControl(Of Controls.SeekBar)("BarSeek")
            If seek IsNot Nothing Then AddHandler seek.Seeked, Sub(seconds) TryCast(DataContext, MainWindowViewModel)?.SeekTo(seconds)
            Dim close = Me.FindControl(Of Button)("BarCloseButton")
            ' Wie der Schliessen-Knopf der vollen Ansicht: das Fenster entscheidet, ob es endet oder
            ' sich in den Infobereich verbirgt.
            If close IsNot Nothing Then AddHandler close.Click, Sub(sender As Object, e As RoutedEventArgs) TryCast(TopLevel.GetTopLevel(Me), Window)?.Close()
        End Sub

        Private Sub InitializeComponent()
            AvaloniaXamlLoader.Load(Me)
        End Sub
    End Class
End Namespace
