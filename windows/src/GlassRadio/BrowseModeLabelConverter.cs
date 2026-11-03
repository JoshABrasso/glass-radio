using Microsoft.UI.Xaml.Data;

namespace GlassRadio;

public sealed class BrowseModeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        GlassRadio.Domain.StationFilterMode.All => "All Stations",
        GlassRadio.Domain.StationFilterMode.FavoritesOnly => "Favorites",
        GlassRadio.Domain.StationFilterMode.Popular => "Popular Picks",
        GlassRadio.Domain.StationFilterMode.Reliable => "Stable Streams",
        GlassRadio.Domain.StationFilterMode.MajorBrands => "Big Networks",
        GlassRadio.Domain.StationFilterMode.WithGenres => "Genre Tagged",
        GlassRadio.Domain.StationSortMode.Name => "A–Z",
        GlassRadio.Domain.StationSortMode.Popularity => "Most Popular",
        GlassRadio.Domain.StationSortMode.Clicks => "Most Listened",
        GlassRadio.Domain.StationSortMode.BitrateHigh => "Highest Bitrate",
        GlassRadio.Domain.StationSortMode.BitrateLow => "Lowest Bitrate",
        GlassRadio.Domain.StationSortMode.FavoritesFirst => "Favorites First",
        GlassRadio.Domain.StationSortMode.NetworksFirst => "Networks First",
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
