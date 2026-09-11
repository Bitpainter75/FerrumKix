Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Data
Imports Avalonia.Input
Imports Avalonia.Media

Namespace Controls

    ''' <summary>Der Lautstaerkeregler der Fussleiste: ein Drehknopf wie im Zeichen der Anwendung.
    '''
    ''' <para>Ein eigenes Steuerelement und kein umgestalteter Slider: der Slider kennt nur eine
    ''' gerade Bahn. Der Knopf zeichnet einen Bogen von drei Vierteln eines Kreises, darin den
    ''' Knopf mit einem Strich, der auf den Wert zeigt.</para>
    '''
    ''' <para>GEDREHT WIRD NICHT IM KREIS. Einem Kreisbogen mit dem Zeiger zu folgen ist muehsam;
    ''' der Knopf folgt deshalb der Bewegung nach oben und nach rechts (lauter) oder nach unten und
    ''' nach links (leiser). Dazu das Mausrad und die Pfeiltasten.</para>
    '''
    ''' <para>Der Wert reicht fest von 0 bis 100, wie die Lautstaerke im ViewModel.</para></summary>
    Public Class VolumeKnob
        Inherits Control

        Public Shared ReadOnly ValueProperty As StyledProperty(Of Double) =
            AvaloniaProperty.Register(Of VolumeKnob, Double)(NameOf(Value), 0.0,
                                                             defaultBindingMode:=BindingMode.TwoWay)

        Public Shared ReadOnly TrackBrushProperty As StyledProperty(Of IBrush) =
            AvaloniaProperty.Register(Of VolumeKnob, IBrush)(NameOf(TrackBrush))

        Public Shared ReadOnly ValueBrushProperty As StyledProperty(Of IBrush) =
            AvaloniaProperty.Register(Of VolumeKnob, IBrush)(NameOf(ValueBrush))

        Public Shared ReadOnly KnobBrushProperty As StyledProperty(Of IBrush) =
            AvaloniaProperty.Register(Of VolumeKnob, IBrush)(NameOf(KnobBrush))

        Public Shared ReadOnly RimBrushProperty As StyledProperty(Of IBrush) =
            AvaloniaProperty.Register(Of VolumeKnob, IBrush)(NameOf(RimBrush))

        ''' <summary>Der Bogen beginnt links unten und endet rechts unten; die Luecke unten zeigt,
        ''' wo leise und wo laut ist. Winkel im Uhrzeigersinn ab der Waagerechten nach rechts.</summary>
        Private Const StartAngle As Double = 135
        Private Const SweepAngle As Double = 270

        ''' <summary>Wie viele Bildpunkte Zug einen Prozentpunkt ausmachen. Bei 2 reicht ein Zug
        ''' ueber 200 Punkte von stumm bis ganz laut.</summary>
        Private Const PixelsPerStep As Double = 2

        Private _dragging As Boolean = False
        Private _dragOrigin As Point
        Private _dragStartValue As Double

        Shared Sub New()
            AffectsRender(Of VolumeKnob)(ValueProperty, TrackBrushProperty, ValueBrushProperty,
                                         KnobBrushProperty, RimBrushProperty)
            FocusableProperty.OverrideDefaultValue(Of VolumeKnob)(True)
        End Sub

        Public Sub New()
            Cursor = New Cursor(StandardCursorType.SizeNorthSouth)
        End Sub

        Public Property Value As Double
            Get
                Return GetValue(ValueProperty)
            End Get
            Set(value As Double)
                SetValue(ValueProperty, value)
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

        Public Property ValueBrush As IBrush
            Get
                Return GetValue(ValueBrushProperty)
            End Get
            Set(value As IBrush)
                SetValue(ValueBrushProperty, value)
            End Set
        End Property

        Public Property KnobBrush As IBrush
            Get
                Return GetValue(KnobBrushProperty)
            End Get
            Set(value As IBrush)
                SetValue(KnobBrushProperty, value)
            End Set
        End Property

        Public Property RimBrush As IBrush
            Get
                Return GetValue(RimBrushProperty)
            End Get
            Set(value As IBrush)
                SetValue(RimBrushProperty, value)
            End Set
        End Property

        Private Sub SetClamped(proposed As Double)
            Value = Math.Round(Math.Clamp(proposed, 0, 100))
        End Sub

        Protected Overrides Sub OnPointerPressed(e As PointerPressedEventArgs)
            MyBase.OnPointerPressed(e)
            If Not e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then Return

            _dragging = True
            _dragOrigin = e.GetPosition(Me)
            _dragStartValue = Value
            e.Pointer.Capture(Me)
            Focus()
            e.Handled = True
        End Sub

        Protected Overrides Sub OnPointerMoved(e As PointerEventArgs)
            MyBase.OnPointerMoved(e)
            If Not _dragging Then Return
            Dim point = e.GetPosition(Me)
            ' Nach rechts und nach oben ist lauter. Die y-Achse zeigt nach unten, daher das
            ' umgekehrte Vorzeichen.
            Dim travel = (point.X - _dragOrigin.X) - (point.Y - _dragOrigin.Y)
            SetClamped(_dragStartValue + travel / PixelsPerStep)
            e.Handled = True
        End Sub

        Protected Overrides Sub OnPointerReleased(e As PointerReleasedEventArgs)
            MyBase.OnPointerReleased(e)
            If Not _dragging Then Return
            _dragging = False
            e.Pointer.Capture(Nothing)
            e.Handled = True
        End Sub

        Protected Overrides Sub OnPointerCaptureLost(e As PointerCaptureLostEventArgs)
            MyBase.OnPointerCaptureLost(e)
            _dragging = False
        End Sub

        Protected Overrides Sub OnPointerWheelChanged(e As PointerWheelEventArgs)
            MyBase.OnPointerWheelChanged(e)
            If e.Delta.Y = 0 Then Return
            SetClamped(Value + Math.Sign(e.Delta.Y) * 5)
            e.Handled = True
        End Sub

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            Select Case e.Key
                Case Key.Up, Key.Right
                    SetClamped(Value + 2)
                    e.Handled = True
                Case Key.Down, Key.Left
                    SetClamped(Value - 2)
                    e.Handled = True
            End Select
        End Sub

        Public Overrides Sub Render(context As DrawingContext)
            MyBase.Render(context)

            Dim size = Math.Min(Bounds.Width, Bounds.Height)
            If size <= 0 Then Return

            Dim center = New Point(Bounds.Width / 2, Bounds.Height / 2)
            Dim arcThickness = Math.Max(2, size * 0.075)
            Dim arcRadius = size / 2 - arcThickness / 2
            Dim knobRadius = arcRadius - arcThickness - Math.Max(2, size * 0.06)
            Dim fraction = Math.Clamp(Value / 100, 0, 1)

            Dim track = If(TrackBrush, Brushes.DimGray)
            Dim accent = If(ValueBrush, Brushes.Orange)

            context.DrawGeometry(Nothing, New Pen(track, arcThickness, lineCap:=PenLineCap.Round),
                                 Arc(center, arcRadius, StartAngle, SweepAngle))
            If fraction > 0.001 Then
                context.DrawGeometry(Nothing, New Pen(accent, arcThickness, lineCap:=PenLineCap.Round),
                                     Arc(center, arcRadius, StartAngle, SweepAngle * fraction))
            End If

            If knobRadius <= 2 Then Return

            Dim rim = New Pen(If(RimBrush, Brushes.Gray), Math.Max(1.5, size * 0.04))
            context.DrawEllipse(If(KnobBrush, Brushes.Black), rim, center, knobRadius, knobRadius)

            ' Der Zeiger im Knopf. Er beginnt nicht in der Mitte, sondern ein Stueck davor: so liest
            ' er sich als Kerbe im Knopf und nicht als Uhrzeiger.
            Dim angle = StartAngle + SweepAngle * fraction
            Dim indicator = New Pen(accent, Math.Max(1.5, size * 0.05), lineCap:=PenLineCap.Round)
            context.DrawLine(indicator, PointAt(center, knobRadius * 0.3, angle), PointAt(center, knobRadius * 0.72, angle))
        End Sub

        Private Shared Function PointAt(center As Point, radius As Double, degrees As Double) As Point
            Dim radians = degrees * Math.PI / 180
            Return New Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians))
        End Function

        ''' <summary>Ein Kreisbogen im Uhrzeigersinn. Die y-Achse zeigt nach unten; ein wachsender
        ''' Winkel laeuft auf dem Bildschirm deshalb im Uhrzeigersinn.</summary>
        Private Shared Function Arc(center As Point, radius As Double, startDegrees As Double, sweepDegrees As Double) As Geometry
            Dim geometry = New StreamGeometry()
            Using figure = geometry.Open()
                figure.BeginFigure(PointAt(center, radius, startDegrees), False)
                figure.ArcTo(PointAt(center, radius, startDegrees + sweepDegrees),
                             New Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise)
                figure.EndFigure(False)
            End Using
            Return geometry
        End Function

    End Class

End Namespace
