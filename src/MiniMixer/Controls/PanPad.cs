using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MiniMixer.Controls;

/// <summary>
/// Pad XY "Intellipan". X = -1..1 (paneo), Y = -1..1 (abajo..arriba). Doble clic = centro.
/// </summary>
public sealed class PanPad : FrameworkElement
{
    public static readonly DependencyProperty XProperty = DependencyProperty.Register(
        nameof(X), typeof(double), typeof(PanPad),
        new FrameworkPropertyMetadata(0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YProperty = DependencyProperty.Register(
        nameof(Y), typeof(double), typeof(PanPad),
        new FrameworkPropertyMetadata(0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TopLabelProperty = RegisterLabel(nameof(TopLabel));
    public static readonly DependencyProperty BottomLabelProperty = RegisterLabel(nameof(BottomLabel));
    public static readonly DependencyProperty LeftLabelProperty = RegisterLabel(nameof(LeftLabel));
    public static readonly DependencyProperty RightLabelProperty = RegisterLabel(nameof(RightLabel));

    private static DependencyProperty RegisterLabel(string name) => DependencyProperty.Register(
        name, typeof(string), typeof(PanPad),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public double X { get => (double)GetValue(XProperty); set => SetValue(XProperty, value); }
    public double Y { get => (double)GetValue(YProperty); set => SetValue(YProperty, value); }
    public string TopLabel { get => (string)GetValue(TopLabelProperty); set => SetValue(TopLabelProperty, value); }
    public string BottomLabel { get => (string)GetValue(BottomLabelProperty); set => SetValue(BottomLabelProperty, value); }
    public string LeftLabel { get => (string)GetValue(LeftLabelProperty); set => SetValue(LeftLabelProperty, value); }
    public string RightLabel { get => (string)GetValue(RightLabelProperty); set => SetValue(RightLabelProperty, value); }

    private const double Inset = 9;

    private static readonly Brush Bg = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x23, 0x29)));
    private static readonly Pen BorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x4C, 0x57)), 1));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x3A, 0x43)), 1));
    private static readonly Brush Handle = Freeze(new SolidColorBrush(Color.FromRgb(0x9F, 0xB7, 0xC5)));
    private static readonly Pen HandlePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x79, 0xDB, 0xA6)), 1.5));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x7E, 0x92, 0x9F)));
    private static readonly Typeface Font = new("Segoe UI");

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public PanPad() => Cursor = Cursors.Hand;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            SetCurrentValue(XProperty, 0d);
            SetCurrentValue(YProperty, 0d);
        }
        else
        {
            CaptureMouse();
            SetFromPoint(e.GetPosition(this));
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) SetFromPoint(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void SetFromPoint(Point p)
    {
        double iw = Math.Max(1, ActualWidth - 2 * Inset), ih = Math.Max(1, ActualHeight - 2 * Inset);
        double x = (p.X - Inset) / iw * 2 - 1;
        double y = 1 - (p.Y - Inset) / ih * 2;
        SetCurrentValue(XProperty, Math.Round(Math.Clamp(x, -1, 1), 3));
        SetCurrentValue(YProperty, Math.Round(Math.Clamp(y, -1, 1), 3));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRoundedRectangle(Bg, BorderPen, new Rect(0.5, 0.5, w - 1, h - 1), 4, 4);
        dc.DrawLine(GridPen, new Point(w / 2, Inset), new Point(w / 2, h - Inset));
        dc.DrawLine(GridPen, new Point(Inset, h / 2), new Point(w - Inset, h / 2));

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        DrawLabel(dc, TopLabel, dpi, (tw, th) => new Point(w / 2 - tw / 2, 2));
        DrawLabel(dc, BottomLabel, dpi, (tw, th) => new Point(w / 2 - tw / 2, h - th - 2));
        DrawLabel(dc, LeftLabel, dpi, (tw, th) => new Point(4, h / 2 - th / 2));
        DrawLabel(dc, RightLabel, dpi, (tw, th) => new Point(w - tw - 4, h / 2 - th / 2));

        double hx = Inset + (X + 1) / 2 * (w - 2 * Inset);
        double hy = Inset + (1 - (Y + 1) / 2) * (h - 2 * Inset);
        dc.DrawRoundedRectangle(Handle, HandlePen, new Rect(hx - 7, hy - 7, 14, 14), 2, 2);
    }

    private void DrawLabel(DrawingContext dc, string text, double dpi, Func<double, double, Point> position)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Font, 9, LabelBrush, dpi);
        dc.DrawText(ft, position(ft.Width, ft.Height));
    }
}
