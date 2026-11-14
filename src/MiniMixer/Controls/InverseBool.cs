using System.Globalization;
using System.Windows.Data;

namespace MiniMixer.Controls;

/// <summary>Convierte true ↔ false (en ambos sentidos).</summary>
public sealed class InverseBool : IValueConverter
{
    public static InverseBool Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;
}
