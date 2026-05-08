using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CctvVms.Core.Domain;
using System.Windows;

namespace CctvVms.App.Infrastructure;

// StreamType -> badge background color
public sealed class StreamTypeToBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is StreamType st)
            return st == StreamType.Main
                ? new SolidColorBrush(Color.FromRgb(0x00, 0xC4, 0x8C))
                : new SolidColorBrush(Color.FromRgb(0x26, 0x7A, 0xB8));
        return new SolidColorBrush(Colors.Gray);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

// StatusText string -> dot color
public sealed class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value?.ToString()?.ToLowerInvariant()) switch
        {
            "online"  => new SolidColorBrush(Color.FromRgb(0x00, 0xC4, 0x8C)),
            "offline" => new SolidColorBrush(Color.FromRgb(0xE5, 0x4D, 0x4D)),
            _         => new SolidColorBrush(Color.FromRgb(0x4A, 0x68, 0x80))
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

// ActiveModule string vs ConverterParameter -> accent or subtle color
public sealed class ModuleToAccentConverter : IValueConverter
{
    private static readonly SolidColorBrush Active  = new(Color.FromRgb(0x00, 0xC4, 0x8C));
    private static readonly SolidColorBrush Inactive = new(Color.FromRgb(0x5A, 0x7A, 0x9A));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Active : Inactive;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

