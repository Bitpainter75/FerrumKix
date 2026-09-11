Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Input
Imports Avalonia.Media

Namespace Controls

    ''' <summary>Der Fortschrittsbalken der Fussleiste, zugleich die Stelle zum Springen.
    '''
    ''' <para>Ein eigenes Steuerelement und kein Slider: der Slider hat einen Griff, und den will
    ''' hier niemand sehen. Gezogen wird an der ganzen Flaeche, und waehrend gezogen wird, folgt
    ''' die Anzeige dem Zeiger und NICHT mehr der Meldung von mpv - sonst springt der Balken
    ''' waehrend des Ziehens zwischen beiden Werten hin und her.</para>
    '''
    ''' <para>Die Wellenform ist vorbereitet: liegen in <see cref="Peaks"/> Ausschlaege, zeichnet
    ''' das Element sie statt eines glatten Balkens. Wer die Werte berechnet, ist nicht seine
    ''' Sache.</para></summary>
    Public Class SeekBar
        Inherits Control

        Public Shared ReadOnly PositionProperty As StyledProperty(Of Double) =
            AvaloniaProperty.Register(Of SeekBar, Double)(NameOf(Position))

        Public Shared ReadOnly DurationProperty As StyledProperty(Of Double) =
            AvaloniaProperty.Register(Of SeekBar, Double)(NameOf(Duration))

        Public Shared ReadOnly TrackBrushProperty As StyledProperty(Of IBrush) =
            AvaloniaProperty.Register(Of SeekBar, IBrush)(NameOf(TrackBrush))

        Public Shared ReadOnly ProgressBrushProperty As StyledProperty(Of IBrush) =
            AvaloniaProperty.Register(Of SeekBar, IBrush)(NameOf(ProgressBrush))

        ''' <summary>Die Ausschlaege der Wellenform, je Wert 0 bis 1. Nothing heisst: glatter
        ''' Balken.</summary>
        Public Shared ReadOnly PeaksProperty As StyledProperty(Of Single()) =
            AvaloniaProperty.Register(Of SeekBar, Single())(NameOf(Peaks))

        ''' <summary>Die Hoehe des glatten Balkens. Die Wellenform nimmt immer die volle Hoehe.</summary>
        Public Shared ReadOnly BarHeightProperty As StyledProperty(Of Double) =
            AvaloniaProperty.Register(Of SeekBar, Double)(NameOf(BarHeight), 6.0)

        ''' <summary>Wohin gesprungen werden soll, in Sekunden. Feuert beim Loslassen und waehrend
        ''' des Ziehens: mpv kommt mit haeufigen Spruengen zurecht, und ein Balken, der erst beim
        ''' Loslassen springt, fuehlt sich traege an.</summary>
        Public Event Seeked(seconds As Double)

        Private _dragging As Boolean = False
        Private _dragFraction As Double = 0

        ''' <summary>Unter dem Zeiger wird der Balken etwas dicker und zeigt einen Griff an der
        ''' gespielten Stelle - in Ruhe bleibt er eine schmale Linie.</summary>
        Private _hovering As Boolean = False

        Shared Sub New()
            AffectsRender(Of SeekBar)(PositionProperty, DurationProperty, PeaksProperty,
                                      TrackBrushProperty, ProgressBrushProperty, BarHeightProperty)
        End Sub

        Public Sub New()
            Cursor = New Cursor(StandardCursorType.Hand)
        End Sub

        Public Property Position As Double
            Get
                Return GetValue(PositionProperty)
            End Get
            Set(value As Double)
                SetValue(PositionProperty, value)
            End Set
        End Property

        Public Property Duration As Double
            Get
                Return GetValue(DurationProperty)
            End Get
            Set(value As Double)
                SetValue(DurationProperty, value)
            End Set
        End Property

        Public Property TrackBrush As IBrush
            Get
                Return GetValue(TrackBrushProperty)
            End Get
            Set(value As IBrush)
                SetValue(TrackBrushProperty, value)
            End Set
        End Property

        Public Property ProgressBrush As IBrush
            Get
                Return GetValue(ProgressBrushProperty)
            End Get
            Set(value As IBrush)
                SetValue(ProgressBrushProperty, value)
            End Set
        End Property

        Public Property Peaks As Single()
            Get
                Return GetValue(PeaksProperty)
            End Get
            Set(value As Single())
                SetValue(PeaksProperty, value)
            End Set
        End Property

        Public Property BarHeight As Double
            Get
                Return GetValue(BarHeightProperty)
            End Get
            Set(value As Double)
                SetValue(BarHeightProperty, value)
            End Set
        End Property

        ''' <summary>Der Anteil, der als gespielt gilt. Waehrend des Ziehens der Zeiger, sonst die
        ''' Meldung von mpv.</summary>
        Private ReadOnly Property Fraction As Double
            Get
                If _dragging Then Return _dragFraction
                Dim total = Duration
                If total <= 0 Then Return 0
                Return Math.Clamp(Position / total, 0, 1)
            End Get
        End Property

        Protected Overrides Sub OnPointerEntered(e As PointerEventArgs)
            MyBase.OnPointerEntered(e)
            _hovering = True
            InvalidateVisual()
        End Sub

        Protected Overrides Sub OnPointerExited(e As PointerEventArgs)
            MyBase.OnPointerExited(e)
            _hovering = False
            InvalidateVisual()
        End Sub

        Protected Overrides Sub OnPointerPressed(e As PointerPressedEventArgs)
            MyBase.OnPointerPressed(e)
            If Not e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then Return
            If Duration <= 0 Then Return

            _dragging = True
            e.Pointer.Capture(Me)
            UpdateFromPointer(e.GetPosition(Me).X)
            e.Handled = True
        End Sub

        Protected Overrides Sub OnPointerMoved(e As PointerEventArgs)
            MyBase.OnPointerMoved(e)
            If Not _dragging Then Return
            UpdateFromPointer(e.GetPosition(Me).X)
            e.Handled = True
        End Sub

        Protected Overrides Sub OnPointerReleased(e As PointerReleasedEventArgs)
            MyBase.OnPointerReleased(e)
            If Not _dragging Then Return
            _dragging = False
            e.Pointer.Capture(Nothing)
            InvalidateVisual()
            e.Handled = True
        End Sub

        ''' <summary>Ein abgebrochener Zug, etwa weil das Fenster den Zeiger verliert. Ohne diesen
        ''' Zweig bliebe die Anzeige am Zeiger haengen und folgte mpv nie wieder.</summary>
        Protected Overrides Sub OnPointerCaptureLost(e As PointerCaptureLostEventArgs)
            MyBase.OnPointerCaptureLost(e)
            If Not _dragging Then Return
            _dragging = False
            InvalidateVisual()
        End Sub

        Private Sub UpdateFromPointer(x As Double)
            If Bounds.Width <= 0 Then Return
            _dragFraction = Math.Clamp(x / Bounds.Width, 0, 1)
            InvalidateVisual()
            RaiseEvent Seeked(_dragFraction * Duration)
        End Sub

        Public Overrides Sub Render(context As DrawingContext)
            MyBase.Render(context)

            Dim width = Bounds.Width
            Dim height = Bounds.Height
            If width <= 0 OrElse height <= 0 Then Return

            Dim track = If(TrackBrush, Brushes.Gray)
            Dim progress = If(ProgressBrush, Brushes.White)
            Dim played = Fraction * width

            Dim samples = Peaks
            If samples IsNot Nothing AndAlso samples.Length > 1 Then
                RenderWaveform(context, samples, width, height, played, track, progress)
                Return
            End If

            ' Die oertliche Groesse heisst NICHT wie die Eigenschaft: VB unterscheidet keine Gross-
            ' und Kleinschreibung, "barHeight" waere dieselbe Bezeichnung wie "BarHeight".
            Dim active = _hovering OrElse _dragging
            Dim thickness = Math.Min(If(active, BarHeight + 2, BarHeight), height)
            Dim top = (height - thickness) / 2
            Dim radius = thickness / 2
            context.DrawRectangle(track, Nothing, New RoundedRect(New Rect(0, top, width, thickness), radius))
            If played > 0 Then
                context.DrawRectangle(progress, Nothing,
                                      New RoundedRect(New Rect(0, top, Math.Min(played, width), thickness), radius))
            End If

            If active AndAlso Duration > 0 Then
                Dim grip = Math.Min(thickness + 6, height / 2)
                context.DrawEllipse(progress, Nothing, New Point(Math.Min(played, width), height / 2), grip, grip)
            End If
        End Sub

        ''' <summary>Ein senkrechter Strich je Bildpunktspalte, um die Mittellinie gespiegelt. Die
        ''' Ausschlaege werden auf die Breite verteilt und nicht einzeln gezeichnet: es sind
        ''' typischerweise mehr Werte als Spalten.</summary>
        Private Shared Sub RenderWaveform(context As DrawingContext, samples As Single(),
                                          width As Double, height As Double, played As Double,
                                          track As IBrush, progress As IBrush)
            Dim columns = CInt(Math.Floor(width))
            If columns < 1 Then Return

            Dim middle = height / 2
            Dim playedPen = New Pen(progress, 1)
            Dim trackPen = New Pen(track, 1)

            For column = 0 To columns - 1
                Dim first = CInt(Math.Floor(column * samples.Length / CDbl(columns)))
                Dim last = CInt(Math.Floor((column + 1) * samples.Length / CDbl(columns))) - 1
                If last < first Then last = first
                If last > samples.Length - 1 Then last = samples.Length - 1

                Dim peak As Double = 0
                For index = first To last
                    Dim value = CDbl(samples(index))
                    If value > peak Then peak = value
                Next

                Dim half = Math.Max(0.5, peak * middle)
                Dim x = column + 0.5
                Dim pen = If(column < played, playedPen, trackPen)
                context.DrawLine(pen, New Point(x, middle - half), New Point(x, middle + half))
            Next
        End Sub

    End Class

End Namespace
