namespace GlassRadio.Domain;

public enum StationFilterMode
{
    All,
    FavoritesOnly,
    Popular,
    Reliable,
    MajorBrands,
    WithGenres
}

public enum StationSortMode
{
    Name,
    Popularity,
    Clicks,
    BitrateHigh,
    BitrateLow,
    FavoritesFirst,
    NetworksFirst
}

public static class StationBrowseRules
{
    public static bool MatchesGenre(RadioStation station, string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre) || genre.Equals("All", StringComparison.OrdinalIgnoreCase)) return true;

        var target = CompactGenre(genre);
        return target.Length > 0 && station.Genres.Any(item => CompactGenre(item).Contains(target, StringComparison.Ordinal));
    }

    public static IReadOnlyList<RadioStation> Apply(
        IEnumerable<RadioStation> stations,
        CountryPreset? country,
        StationFilterMode filter,
        StationSortMode sort,
        string? genre,
        IReadOnlySet<string> favoriteIds)
    {
        var source = stations;
        var materialized = source as IReadOnlyList<RadioStation> ?? source.ToArray();
        source = filter switch
        {
            StationFilterMode.Popular => materialized
                .OrderByDescending(station => PopularityScore(station, country))
                .ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(400),
            StationFilterMode.Reliable => materialized.Where(static station => (station.Votes ?? 0) + (station.ClickCount ?? 0) >= 50),
            StationFilterMode.MajorBrands when country is not null => source.Where(station => country.TopBrands.Any(brand => StationRules.BrandMatchQuality(station, brand) > 0)),
            StationFilterMode.WithGenres => source.Where(static station => station.Genres.Count > 0),
            StationFilterMode.FavoritesOnly => source.Where(station => favoriteIds.Contains(station.Id)),
            _ => source
        };

        if (!string.IsNullOrWhiteSpace(genre) && !genre.Equals("All", StringComparison.OrdinalIgnoreCase))
            source = source.Where(station => MatchesGenre(station, genre));

        return sort switch
        {
            StationSortMode.Name => source.OrderBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            StationSortMode.Clicks => source.OrderByDescending(static station => station.ClickCount ?? 0).ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            StationSortMode.BitrateHigh => source.OrderByDescending(static station => EffectiveBitrate(station)).ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            StationSortMode.BitrateLow => source.OrderBy(static station => EffectiveBitrate(station)).ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            StationSortMode.FavoritesFirst => source.OrderByDescending(station => favoriteIds.Contains(station.Id)).ThenByDescending(station => PopularityScore(station, country)).ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            StationSortMode.NetworksFirst => source.OrderByDescending(station => country?.TopBrands.Max(brand => StationRules.BrandMatchQuality(station, brand)) ?? 0).ThenByDescending(station => PopularityScore(station, country)).ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            _ => source.OrderByDescending(station => PopularityScore(station, country)).ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase).ToArray()
        };
    }

    private static double PopularityScore(RadioStation station, CountryPreset? country)
    {
        var brandScore = country?.TopBrands.Max(brand => StationRules.BrandMatchQuality(station, brand)) ?? 0;
        return (station.Votes ?? 0) + ((station.ClickCount ?? 0) / 3d) + brandScore;
    }

    private static int EffectiveBitrate(RadioStation station)
    {
        var name = station.Name.ToLowerInvariant();
        foreach (var suffix in new[] { "kbps", "kbit", "kb", "k" })
        {
            var suffixStart = name.IndexOf(suffix, StringComparison.Ordinal);
            if (suffixStart <= 0) continue;

            var numberStart = suffixStart;
            while (numberStart > 0 && char.IsDigit(name[numberStart - 1])) numberStart--;
            if (numberStart == suffixStart) continue;

            if (int.TryParse(name[numberStart..suffixStart], out var bitrate) && bitrate is >= 32 and <= 320)
                return bitrate;
        }

        return station.Bitrate is >= 32 and <= 320 ? station.Bitrate.Value : 128;
    }

    private static string CompactGenre(string value) => StationRules.Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal);
}
