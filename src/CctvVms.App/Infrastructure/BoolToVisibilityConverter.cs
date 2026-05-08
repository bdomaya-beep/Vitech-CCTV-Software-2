using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CctvVms.App.Infrastructure;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isTrue = value is true;
        if (Invert)
        {
            isTrue = !isTrue;
        }

        return isTrue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}


// Returns Hidden (not Collapsed) so HwndHost/VideoView HWNDs stay alive in the visual tree.
// Used for the tile grid during zoom — keeps VLC render targets intact for pre-warming.
public sealed class BoolToHiddenConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isTrue = value is true;
        if (Invert) isTrue = !isTrue;
        return isTrue ? Visibility.Hidden : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
