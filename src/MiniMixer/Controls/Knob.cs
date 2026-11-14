using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MiniMixer.Controls;

/// <summary>
/// Perilla rotativa estilo Voicemeeter. Arrastrar vertical = cambiar (Shift = fino),
/// rueda = pasos, doble clic = valor por defecto.
/// </summary>
public sealed class Knob : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
            null, CoerceValue));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender, OnRangeChanged));

    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(Knob), new PropertyMetadata(0d));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(Knob),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat), typeof(string), typeof(Knob),
        new FrameworkPropertyMetadata("0.#", FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double DefaultValue { get => (double)GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string ValueFormat { get => (string)GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }

    private static readonly Brush Face = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x3A, 0x42)));
    private static readonly Pen Ring = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x8F, 0xA3, 0xAE)), 1.5));
    private static readonly Pen Arc = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x79, 0xDB, 0xA6)), 2.5)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    private static readonly Brush Dot = Freeze(new SolidColorBrush(Color.FromRgb(0xDD, 0xE7, 0xEC)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC9, 0xD6, 0xDE)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x7E, 0x92, 0x9F)));
    private static readonly Typeface Font = new("Segoe UI");

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private double _dragStartY, _dragStartValue;

    public Knob()
    {
        Width = 46;
        Height = 58;
        Cursor = Cursors.SizeNS;
        ToolTip = null;
    }

    private static object CoerceValue(DependencyObject d, object value)
    {
        var k = (Knob)d;
        return Math.Clamp((double)value, Math.Min(k.Minimum, k.Maximum), Math.Max(k.Minimum, k.Maximum));
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        d.CoerceValue(ValueProperty);

    private double Range => Math.Max(1e-9, Maximum - Minimum);

    private void SetValueFromUi(double v) =>
        SetCurrentValue(ValueProperty, Math.Clamp(v, Minimum, Maximum));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            SetValueFromUi(DefaultValue);
        }
        else
        {
            _dragStartY = e.GetPosition(this).Y;
            _dragStartValue = Value;
            CaptureMouse();
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        double dy = _dragStartY - e.GetPosition(this).Y;
        double pixelsForFullRange = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 600 : 150;
        SetValueFromUi(_dragStartValue + dy / pixelsForFullRange * Range);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        SetValueFromUi(Value + Math.Sign(e.Delta) * Range / 40);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // área de hit-test

        double size = Math.Min(w, h - 14);
        var c = new Point(w / 2, size / 2 + 1);
        double r = size / 2 - 3;
        if (r <= 2) return;

        dc.DrawEllipse(Face, Ring, c, r, r);

        // Arco de valor: bipolar (desde 0) si el rango cruza el cero, si no desde el mínimo
        double t = (Value - Minimum) / Range;
        double aValue = -135 + t * 270;
        double aStart = Minimum < 0 && Maximum > 0 ? -135 + (-Minimum / Range) * 270 : -135;
        DrawArc(dc, c, r + 0.5, aStart, aValue);

        var dot = PointAt(c, r - 6, aValue);
        dc.DrawEllipse(Dot, null, dot, 2.2, 2.2);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var valueText = new FormattedText(Value.ToString(ValueFormat, CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 9, TextBrush, dpi);
        dc.DrawText(valueText, new Point(c.X - valueText.Width / 2, c.Y + r * 0.15));

        if (!string.IsNullOrEmpty(Label))
        {
            var label = new FormattedText(Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Font, 10, LabelBrush, dpi);
            dc.DrawText(label, new Point(w / 2 - label.Width / 2, h - label.Height));
        }
    }

    private static Point PointAt(Point c, double r, double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180;
        return new Point(c.X + r * Math.Sin(rad), c.Y - r * Math.Cos(rad));
    }

    private static void DrawArc(DrawingContext dc, Point c, double r, double a0, double a1)
    {
        if (Math.Abs(a1 - a0) < 0.5) return;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(PointAt(c, r, a0), false, false);
            ctx.ArcTo(PointAt(c, r, a1), new Size(r, r), 0,
                Math.Abs(a1 - a0) > 180,
                a1 > a0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, Arc, geo);
    }
}
