using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace GlassRadio;

public sealed class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isEmpty = value is int count && count == 0;
        if (parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase)) isEmpty = !isEmpty;
        return isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
