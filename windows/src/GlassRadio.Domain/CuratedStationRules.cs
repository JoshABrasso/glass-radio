namespace GlassRadio.Domain;

/// <summary>
/// Builds the featured rail from the active country's ranked API feed.
/// Country metadata identifies network families; it is not a hard-coded list
/// of station records. The expanded catalogue is never allowed to promote a
/// regional or stream variant into the featured rail by popularity alone.
/// </summary>
public static class CuratedStationRules
{
    private static readonly string[] RegionalTerms =
    [
        "london", "manchester", "birmingham", "glasgow", "edinburgh", "cardiff",
        "bristol", "leeds", "liverpool", "newcastle", "scotland", "wales", "england",
        "ireland", "northern ireland", "south east", "south west", "midlands"
    ];

    // These terms identify a branded substation when the requested brand is
    // the parent network (for example Capital vs Capital Dance). They are a
    // ranking penalty only: an explicitly requested top-list entry such as
    // Capital Dance remains eligible in its own right.
    private static readonly string[] SubstationTerms =
    [
        "dance", "anthems", "xtra", "chill", "kisstory", "extra", "90s", "80s", "70s",
        "rock", "country", "classics", "gold", "hits", "smooth", "kids"
    ];

    public static IReadOnlyList<RadioStation> Build(IEnumerable<RadioStation> stations, CountryPreset country, int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(country);
        if (limit <= 0) return Array.Empty<RadioStation>();

        var eligible = stations
            .Where(static station => !string.IsNullOrWhiteSpace(station.Name))
            .Where(static station => station.StreamCandidates.Count > 0)
            .Where(static station => IsUsableStationName(station.Name))
            .Where(static station => !StationRules.ContainsAny(station,
                ["church", "christian", "gospel", "islam", "islamic", "quran", "koran", "bible", "catholic", "orthodox", "hindu", "jewish", "torah", "spiritual", "worship", "religious"]))
            .Where(station => IsEligibleForCountry(station, country))
            .GroupBy(StationIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                // A country-rank value belongs to the station identity, even
                // when Radio Browser returns several stream/codec variants for
                // that identity. Never let an unranked variant erase the API's
                // authoritative top-list position during deduplication.
                .OrderByDescending(static station => station.CountryTopRank.HasValue)
                .ThenBy(static station => station.CountryTopRank ?? int.MaxValue)
                .ThenByDescending(static station => station.LastCheckOk ?? 0)
                .ThenByDescending(static station => Popularity(station))
                .ThenByDescending(static station => ArtworkQuality(station))
                .ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase)
                .First())
            .ToArray();

        if (eligible.Length == 0) return Array.Empty<RadioStation>();

        // A live load carries the provider's country rank. A recovered snapshot
        // from before this field existed uses the old ranking only as an offline
        // compatibility path; it is never the live curation authority.
        var hasApiTopFeed = eligible.Any(static station => station.CountryTopRank.HasValue);
        var ranked = hasApiTopFeed
            ? eligible.Where(static station => station.CountryTopRank.HasValue)
                .OrderBy(static station => station.CountryTopRank!.Value)
                .ToArray()
            : eligible.OrderByDescending(static station => Popularity(station))
                .ThenByDescending(static station => station.LastCheckOk ?? 0)
                .ThenByDescending(static station => ArtworkQuality(station))
                .ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        var representatives = country.TopBrands
            .Where(static brand => !string.IsNullOrWhiteSpace(brand))
            .Select(brand => ranked
                .Where(station => StationRules.BrandMatchQuality(station, brand) > 0)
                .OrderByDescending(station => CanonicalScore(station, brand))
                .ThenBy(static station => station.CountryTopRank ?? int.MaxValue)
                .ThenByDescending(static station => Popularity(station))
                .ThenByDescending(static station => ArtworkQuality(station))
                .FirstOrDefault())
            .Where(static station => station is not null)
            .GroupBy(station => NetworkKey(station!, country), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderBy(static station => station!.CountryTopRank ?? int.MaxValue)
                .ThenBy(static station => station!.Name, StringComparer.CurrentCultureIgnoreCase)
                .First()!)
            .ToArray();

        representatives = hasApiTopFeed
            ? representatives.OrderBy(static station => station.CountryTopRank ?? int.MaxValue)
                .ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray()
            : representatives.OrderByDescending(static station => Popularity(station))
                .ThenByDescending(static station => station.LastCheckOk ?? 0)
                .ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        var result = new List<RadioStation>(Math.Min(limit, representatives.Length));
        var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedNetworkKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var station in representatives)
        {
            AddIfNew(station);
            if (result.Count == limit) return result;
        }

        // Fill only from the same API top pool, in provider order. This keeps
        // country metrics authoritative and prevents brand-search results from
        // silently becoming featured stations.
        foreach (var station in ranked)
        {
            if (result.Count == limit) break;
            if (!selectedNetworkKeys.Contains(NetworkKey(station, country))) AddIfNew(station);
        }

        foreach (var station in ranked)
        {
            if (result.Count == limit) break;
            if (!selectedNetworkKeys.Contains(NetworkKey(station, country))) AddIfNew(station);
        }

        return result;

        void AddIfNew(RadioStation station)
        {
            if (!selectedIds.Add(station.Id)) return;
            selectedNetworkKeys.Add(NetworkKey(station, country));
            result.Add(station);
        }
    }

    private static int CanonicalScore(RadioStation station, string brand)
    {
        var name = StationRules.Normalize(station.Name);
        var target = StationRules.Normalize(brand);
        var score = string.IsNullOrEmpty(target)
            ? 0
            : name.Equals(target, StringComparison.Ordinal) ? 1000
            : name.StartsWith(target + " ", StringComparison.Ordinal) ? 800
            : StationRules.BrandMatchQuality(station, brand) * 100;

        if (RegionalTerms.Any(term => name.Contains(term, StringComparison.Ordinal))) score -= 300;
        if (SubstationTerms.Any(term => name.Contains(term, StringComparison.Ordinal))) score -= 500;
        if (name.Contains("mp3", StringComparison.Ordinal) || name.Contains("aac", StringComparison.Ordinal) || name.Contains("stream", StringComparison.Ordinal)) score -= 80;
        return score - Math.Min(name.Length, 80);
    }

    private static bool IsUsableStationName(string name)
    {
        var normalized = StationRules.Normalize(name);
        return !normalized.Contains("scanner", StringComparison.Ordinal) &&
               !normalized.Contains("test", StringComparison.Ordinal) &&
               !normalized.Contains("localhost", StringComparison.Ordinal);
    }

    private static bool IsEligibleForCountry(RadioStation station, CountryPreset country)
    {
        if (!country.Id.Equals("uk", StringComparison.OrdinalIgnoreCase)) return true;
        var name = StationRules.Normalize(station.Name);
        if (name.Contains("bbc asian network", StringComparison.Ordinal)) return false;
        if (name.StartsWith("bbc radio ", StringComparison.Ordinal) &&
            !new[] { "bbc radio 1", "bbc radio 1xtra", "bbc radio 2", "bbc radio 3", "bbc radio 4", "bbc radio 5 live", "bbc radio 6 music", "bbc world service" }.Any(name.Contains)) return false;
        return true;
    }

    private static string NetworkKey(RadioStation station, CountryPreset country)
    {
        var brandIndex = StationRules.BestBrandIndex(station, country.TopBrands);
        return brandIndex >= 0 ? $"brand:{brandIndex}" : $"station:{CanonicalName(station.Name)}";
    }

    private static string StationIdentity(RadioStation station) =>
        string.IsNullOrWhiteSpace(station.StationUuid) ? StationRules.Normalize(station.Name) : station.StationUuid;

    private static string CanonicalName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"\([^)]*\)|\[[^\]]*\]|\b\d{2,3}\s?(kb?|kbps)\b|\b(aac\+?|mp3|ogg|stream|live|hq|lq)\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static int Popularity(RadioStation station) => (station.Votes ?? 0) + ((station.ClickCount ?? 0) / 3);

    private static int ArtworkQuality(RadioStation station)
    {
        var favicon = station.Favicon?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(favicon)) return 0;
        if (favicon.Contains(".png", StringComparison.Ordinal) || favicon.Contains(".jpg", StringComparison.Ordinal) || favicon.Contains(".jpeg", StringComparison.Ordinal) || favicon.Contains(".webp", StringComparison.Ordinal)) return 3;
        if (favicon.Contains(".svg", StringComparison.Ordinal)) return 2;
        return 1;
    }
}
