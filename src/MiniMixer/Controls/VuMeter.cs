using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MiniMixer.Controls;

/// <summary>
/// Medidor de nivel estéreo dibujado con OnRender (sin árbol visual: muy barato).
/// Vertical (barras L|R) u horizontal (barras L sobre R). Escala -60..0 dBFS.
/// </summary>
public sealed class VuMeter : FrameworkElement
{
    public static readonly DependencyProperty LevelLProperty = DependencyProperty.Register(
        nameof(LevelL), typeof(double), typeof(VuMeter),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LevelRProperty = DependencyProperty.Register(
        nameof(LevelR), typeof(double), typeof(VuMeter),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(VuMeter),
        new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsRender));

    public double LevelL { get => (double)GetValue(LevelLProperty); set => SetValue(LevelLProperty, value); }
    public double LevelR { get => (double)GetValue(LevelRProperty); set => SetValue(LevelRProperty, value); }
    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    private static readonly Color Red = Color.FromRgb(0xE0, 0x4A, 0x4A);
    private static readonly Color Yellow = Color.FromRgb(0xE8, 0xC2, 0x3C);
    private static readonly Color Green = Color.FromRgb(0x79, 0xDB, 0xA6);
    private static readonly Color DarkGreen = Color.FromRgb(0x4F, 0xB0, 0x82);

    private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(0x11, 0x17, 0x1B)));
    private static readonly Brush Unlit = Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x30)));
    private static readonly Pen SegmentPen = Freeze(new Pen(Background, 1));

    // Vertical: rojo arriba. Horizontal: rojo a la derecha.
    private static readonly Brush LitVertical = Freeze(new LinearGradientBrush(Stops(reverse: true), new Point(0, 0), new Point(0, 1)));
    private static readonly Brush LitHorizontal = Freeze(new LinearGradientBrush(Stops(reverse: false), new Point(0, 0), new Point(1, 0)));

    private static GradientStopCollection Stops(bool reverse)
    {
        // Posiciones desde el extremo "silencio" (0) al "0 dBFS" (1): verde, amarillo desde -12 dB, rojo desde -3 dB
        (Color c, double p)[] s =
        {
            (DarkGreen, 0.00), (Green, 0.79), (Yellow, 0.80), (Yellow, 0.94), (Red, 0.95), (Red, 1.00)
        };
        var stops = new GradientStopCollection();
        foreach (var (c, p) in s) stops.Add(new GradientStop(c, reverse ? 1 - p : p));
        return stops;
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static double ToFraction(double linear)
    {
        if (linear <= 1e-6) return 0;
        double db = 20 * Math.Log10(linear);
        return Math.Clamp((db + 60) / 60, 0, 1);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));

        if (Orientation == Orientation.Vertical)
        {
            double barW = (w - 1) / 2;
            DrawVertical(dc, new Rect(0, 0, barW, h), LevelL);
            DrawVertical(dc, new Rect(barW + 1, 0, barW, h), LevelR);
            for (double y = 3; y < h; y += 4) dc.DrawLine(SegmentPen, new Point(0, y), new Point(w, y));
        }
        else
        {
            double barH = (h - 1) / 2;
            DrawHorizontal(dc, new Rect(0, 0, w, barH), LevelL);
            DrawHorizontal(dc, new Rect(0, barH + 1, w, barH), LevelR);
            for (double x = 3; x < w; x += 4) dc.DrawLine(SegmentPen, new Point(x, 0), new Point(x, h));
        }
    }

    private static void DrawVertical(DrawingContext dc, Rect r, double level)
    {
        dc.DrawRectangle(Unlit, null, r);
        double frac = ToFraction(level);
        if (frac <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(r.X, r.Bottom - r.Height * frac, r.Width, r.Height * frac)));
        dc.DrawRectangle(LitVertical, null, r);
        dc.Pop();
    }

    private static void DrawHorizontal(DrawingContext dc, Rect r, double level)
    {
        dc.DrawRectangle(Unlit, null, r);
        double frac = ToFraction(level);
        if (frac <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(r.X, r.Y, r.Width * frac, r.Height)));
        dc.DrawRectangle(LitHorizontal, null, r);
        dc.Pop();
    }
}
