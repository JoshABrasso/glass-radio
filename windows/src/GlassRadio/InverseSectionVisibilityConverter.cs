using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace GlassRadio;

public sealed class InverseSectionVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var requested = parameter?.ToString();
        return string.Equals(value?.ToString(), requested, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
