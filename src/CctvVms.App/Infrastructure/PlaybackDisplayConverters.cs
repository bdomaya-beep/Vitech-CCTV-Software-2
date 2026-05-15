using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CctvVms.App.Infrastructure;

public sealed class SecondsToClockConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var totalSeconds = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            _ => 0d,
        };

        totalSeconds = Math.Clamp(totalSeconds, 0d, 86400d);
        var time = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value is null;
        if (Invert)
            isVisible = !isVisible;

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
