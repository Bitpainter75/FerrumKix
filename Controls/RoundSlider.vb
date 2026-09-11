Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Data
Imports Avalonia.Input
Imports Avalonia.Media
Imports Avalonia.Rendering

Namespace Controls

    ''' <summary>Der Schieberegler der Einstellungen, uebernommen aus FerrumPix: eine 4 Punkte
    ''' starke Spur mit rundem Griff in der Akzentfarbe.
    '''
    ''' <para>Ein eigenes Steuerelement statt eines umgestalteten Slider: dessen Vorlage bringt
    ''' eigene Masse und Zustaende mit, die sich nur ueber eine Kette von Regeln bezwingen lassen.
    ''' Hier ist das Aussehen in wenigen Zeilen gezeichnet.</para>
    '''
    ''' <para>Ein Doppelklick setzt auf <see cref="DefaultValue"/> zurueck. Mit <see cref="Step"/>
    ''' rastet der Wert auf ein Vielfaches ein (von <see cref="Minimum"/> aus gezaehlt).</para></summary>
    Public Class RoundSlider
        Inherits Control
        Implements ICustomHitTest

        Public Shared ReadOnly MinimumProperty As StyledProperty(Of Double) =
            AvaloniaProperty.Register(Of RoundSlider, Double)(NameOf(Minimum), 0)

        Public Shared ReadOnly MaximumProperty As StyledProperty(Of Double) =
            AvaloniaProperty.Register(Of RoundSlider, Double)(NameOf(Maximum), 100)

        Public Shared ReadOnly ValueProperty As StyledProperty(Of Double) =
            AvaloniaProperty.Register(Of RoundSlider, Double)(NameOf(Value), 0, defaultBindingMode:=BindingMode.TwoWay)

        Public Shared ReadOnly DefaultValueProperty As StyledProperty(Of Double) =
            AvaloniaProperty.Register(Of RoundSlider, Double)(NameOf(DefaultValue), 0)

        Public Shared ReadOnly StepProperty As StyledProperty(Of Double) =
            AvaloniaProperty.Register(Of RoundSlider, Double)(NameOf([Step]), 0)

        Public Shared ReadOnly TrackBrushProperty As StyledProperty(Of IBrush) =
            AvaloniaProperty.Register(Of RoundSlider, IBrush)(NameOf(TrackBrush), New SolidColorBrush(Color.Parse("#26313B")))

        Public Shared ReadOnly FillBrushProperty As StyledProperty(Of IBrush) =
            AvaloniaProperty.Register(Of RoundSlider, IBrush)(NameOf(FillBrush), New SolidColorBrush(Color.Parse("#F08A1A")))

        Public Shared ReadOnly ThumbBorderBrushProperty As StyledProperty(Of IBrush) =
            AvaloniaProperty.Register(Of RoundSlider, IBrush)(NameOf(ThumbBorderBrush), New SolidColorBrush(Color.Parse("#0B0E11")))

        Public Shared ReadOnly WheelIncrementProperty As StyledProperty(Of Double) =
            AvaloniaProperty.Register(Of RoundSlider, Double)(NameOf(WheelIncrement), 0)

        Private Const ThumbRadius As Double = 8.0

        Private _isDragging As Boolean

        Shared Sub New()
            AffectsRender(Of RoundSlider)(MinimumProperty, MaximumProperty, ValueProperty,
                                          TrackBrushProperty, FillBrushProperty, ThumbBorderBrushProperty)
        End Sub

        Public Sub New()
            Cursor = New Cursor(StandardCursorType.Hand)
            MinHeight = 24
        End Sub

        ''' <summary>Mindestens so hoch wie der Griff, auch wenn ein Stil die Hoehe spaeter
        ''' kleiner setzt.</summary>
        Protected Overrides Function MeasureOverride(availableSize As Size) As Size
            Dim desired = MyBase.MeasureOverride(availableSize)
            Return New Size(desired.Width, Math.Max(desired.Height, ThumbRadius * 2))
        End Function

        ''' <summary>Die GANZE Flaeche nimmt Klicks an und nicht nur die duenn gezeichnete Spur;
        ''' ausgewertet wird ohnehin nur die waagerechte Lage.</summary>
        Public Function HitTest(point As Point) As Boolean Implements ICustomHitTest.HitTest
            Return New Rect(Bounds.Size).Contains(point)
        End Function

        Public Property Minimum As Double
            Get
                Return GetValue(MinimumProperty)
            End Get
            Set(value As Double)
                SetValue(MinimumProperty, value)
            End Set
        End Property

        Public Property Maximum As Double
            Get
                Return GetValue(MaximumProperty)
            End Get
            Set(value As Double)
                SetValue(MaximumProperty, value)
            End Set
        End Property

        Public Property Value As Double
            Get
                Return GetValue(ValueProperty)
            End Get
            Set(value As Double)
                SetValue(ValueProperty, ClampValue(value))
            End Set
        End Property

        Public Property DefaultValue As Double
            Get
                Return GetValue(DefaultValueProperty)
            End Get
            Set(value As Double)
                SetValue(DefaultValueProperty, value)
            End Set
        End Property

        ''' <summary>Die Rasterweite. 0 heisst: stufenlos.</summary>
        Public Property [Step] As Double
            Get
                Return GetValue(StepProperty)
            End Get
            Set(value As Double)
                SetValue(StepProperty, Math.Max(0, value))
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

        Public Property FillBrush As IBrush
            Get
                Return GetValue(FillBrushProperty)
            End Get
            Set(value As IBrush)
                SetValue(FillBrushProperty, value)
            End Set
        End Property

        Public Property ThumbBorderBrush As IBrush
            Get
                Return GetValue(ThumbBorderBrushProperty)
            End Get
            Set(value As IBrush)
                SetValue(ThumbBorderBrushProperty, value)
            End Set
        End Property

        ''' <summary>Wie weit ein Schritt des Mausrads verschiebt. 0 heisst: eine Rasterweite,
        ''' oder 1, wenn keine gesetzt ist.</summary>
        Public Property WheelIncrement As Double
            Get
                Return GetValue(WheelIncrementProperty)
            End Get
            Set(value As Double)
                SetValue(WheelIncrementProperty, Math.Max(0, value))
            End Set
        End Property

        Public Overrides Sub Render(context As DrawingContext)
            MyBase.Render(context)

            Dim width = Bounds.Width
            Dim height = Bounds.Height
            If width <= 0 OrElse height <= 0 Then Return

            Dim trackStart = ThumbRadius
            Dim trackEnd = Math.Max(trackStart, width - ThumbRadius)
            Dim centerY = height / 2.0
            Dim thumbX = trackStart + (trackEnd - trackStart) * GetRatio()

            context.DrawLine(New Pen(TrackBrush, 4), New Point(trackStart, centerY), New Point(trackEnd, centerY))
            context.DrawLine(New Pen(FillBrush, 4), New Point(trackStart, centerY), New Point(thumbX, centerY))
            context.DrawEllipse(FillBrush, New Pen(ThumbBorderBrush, 2), New Point(thumbX, centerY), ThumbRadius, ThumbRadius)
        End Sub

        Protected Overrides Sub OnPointerPressed(e As PointerPressedEventArgs)
            MyBase.OnPointerPressed(e)
            If Not e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then Return
            If e.ClickCount >= 2 Then
                Value = DefaultValue
                e.Handled = True
                Return
            End If
            _isDragging = True
            e.Pointer.Capture(Me)
            SetValueFromPoint(e.GetPosition(Me).X)
            e.Handled = True
        End Sub

        Protected Overrides Sub OnPointerMoved(e As PointerEventArgs)
            MyBase.OnPointerMoved(e)
            If Not _isDragging Then Return

            ' EIN ZUG ENDET NICHT IMMER MIT EINEM LOSLASSEN - beim Zeichenstift etwa bleibt das
            ' Ereignis aus, wenn er das Tablett verlaesst. Ohne diese Pruefung bliebe der Regler am
            ' Zeiger haengen. Befund aus FerrumPix.
            If Not e.GetCurrentPoint(Me).Properties.IsLeftButtonPressed Then
                EndDrag(e.Pointer)
                Return
            End If

            SetValueFromPoint(e.GetPosition(Me).X)
            e.Handled = True
        End Sub

        Protected Overrides Sub OnPointerReleased(e As PointerReleasedEventArgs)
            MyBase.OnPointerReleased(e)
            EndDrag(e.Pointer)
            e.Handled = True
        End Sub

        Protected Overrides Sub OnPointerCaptureLost(e As PointerCaptureLostEventArgs)
            MyBase.OnPointerCaptureLost(e)
            EndDrag(e.Pointer)
        End Sub

        ''' <summary>Gibt den Fang nur frei, wenn er noch bei diesem Regler liegt: sonst gehoert er
        ''' schon jemand anderem, und dessen Zug wuerde abgebrochen.</summary>
        Private Sub EndDrag(pointer As IPointer)
            _isDragging = False
            If pointer IsNot Nothing AndAlso pointer.Captured Is Me Then pointer.Capture(Nothing)
        End Sub

        Protected Overrides Sub OnPointerWheelChanged(e As PointerWheelEventArgs)
            MyBase.OnPointerWheelChanged(e)
            If e.Delta.Y = 0 Then Return
            Dim increment = If(WheelIncrement > 0, WheelIncrement, If([Step] > 0, [Step], 1.0))
            Value = Value + Math.Sign(e.Delta.Y) * increment
            e.Handled = True
        End Sub

        Private Sub SetValueFromPoint(x As Double)
            Dim usableWidth = Math.Max(1, Bounds.Width - ThumbRadius * 2)
            Dim ratio = Math.Clamp((x - ThumbRadius) / usableWidth, 0, 1)
            Value = Minimum + (Maximum - Minimum) * ratio
        End Sub

        Private Function GetRatio() As Double
            If Maximum <= Minimum Then Return 0
            Return Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1)
        End Function

        Private Function ClampValue(value As Double) As Double
            If Double.IsNaN(value) OrElse Double.IsInfinity(value) Then Return Minimum
            If [Step] > 0 Then value = Minimum + Math.Round((value - Minimum) / [Step]) * [Step]
            Return Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum))
        End Function

    End Class

End Namespace
