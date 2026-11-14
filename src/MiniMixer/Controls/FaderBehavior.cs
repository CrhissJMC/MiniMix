using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MiniMixer.Controls;

/// <summary>
/// Comportamiento adjunto para los faders: doble clic = valor por defecto, rueda = ajuste fino.
/// Uso: c:FaderBehavior.ResetValue="0"
/// </summary>
public static class FaderBehavior
{
    public static readonly DependencyProperty ResetValueProperty = DependencyProperty.RegisterAttached(
        "ResetValue", typeof(double), typeof(FaderBehavior), new PropertyMetadata(double.NaN, OnResetValueChanged));

    public static double GetResetValue(DependencyObject d) => (double)d.GetValue(ResetValueProperty);
    public static void SetResetValue(DependencyObject d, double value) => d.SetValue(ResetValueProperty, value);

    private static void OnResetValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider || !double.IsNaN((double)e.OldValue)) return;
        slider.PreviewMouseLeftButtonDown += OnMouseDown;
        slider.PreviewMouseWheel += OnMouseWheel;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not Slider s) return;
        s.SetCurrentValue(RangeBase.ValueProperty, GetResetValue(s));
        e.Handled = true;
    }

    private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Slider s) return;
        double step = (s.Maximum - s.Minimum) / 50; // 2 % por "clic" de rueda
        s.SetCurrentValue(RangeBase.ValueProperty,
            Math.Clamp(s.Value + Math.Sign(e.Delta) * step, s.Minimum, s.Maximum));
        e.Handled = true;
    }
}
