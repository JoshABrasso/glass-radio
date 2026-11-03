using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GlassRadio.Domain;

public static partial class StationRules
{
    private static readonly string[] ReligiousTerms = ["church", "christian", "gospel", "islam", "islamic", "quran", "koran", "bible", "catholic", "orthodox", "hindu", "jewish", "torah", "spiritual", "worship", "religious"];
    private static readonly string[] SportsTerms = ["sport", "sports", "football", "soccer", "match", "talksport", "espn", "cricket", "rugby", "baseball", "basketball", "nfl", "nba", "nhl", "formula 1", "f1"];
    private static readonly string[] NewsTerms = ["news", "bbc world service", "headline", "current affairs", "politics", "business news", "talk radio", "public radio"];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return MultiSpaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    public static bool ContainsAny(RadioStation station, IReadOnlyList<string> terms)
    {
        var text = Normalize(string.Join(" ", station.Name, station.Tags, station.Language));
        return terms.Any(term => text.Contains(Normalize(term), StringComparison.Ordinal));
    }

    public static bool MatchesSearch(RadioStation station, string query)
    {
        var target = Normalize(query);
        if (target.Length == 0) return false;

        var searchable = Normalize(string.Join(" ",
            station.Name,
            station.City,
            station.State,
            station.Country,
            station.CountryCode,
            station.Tags,
            station.Language,
            station.Codec,
            station.Bitrate?.ToString(CultureInfo.InvariantCulture)));
        return searchable.Contains(target, StringComparison.Ordinal);
    }

    public static bool IsEligibleForContent(RadioStation station, bool allowReligious, bool allowSports, bool allowNews) =>
        (allowReligious || !ContainsAny(station, ReligiousTerms)) &&
        (allowSports || !ContainsAny(station, SportsTerms)) &&
        (allowNews || !ContainsAny(station, NewsTerms));

    public static int BrandMatchQuality(RadioStation station, string brand)
    {
        var name = Normalize(station.Name);
        var target = Normalize(brand);
        if (name.Length == 0 || target.Length == 0) return 0;
        if (name.Equals(target, StringComparison.Ordinal)) return 5;
        if (name.StartsWith(target + " ", StringComparison.Ordinal)) return 4;
        if (name.Contains(target, StringComparison.Ordinal)) return 3;
        var tokens = target.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 1 && tokens.All(name.Contains) ? 2 : 0;
    }

    public static int BestBrandIndex(RadioStation station, IReadOnlyList<string> brands)
    {
        return brands
            .Select((brand, index) =>
            {
                var normalized = Normalize(brand);
                return (Index: index, Quality: BrandMatchQuality(station, brand), BrandLength: normalized.Length);
            })
            .Where(static match => match.Quality > 0)
            .OrderByDescending(static match => match.Quality)
            .ThenByDescending(static match => match.BrandLength)
            .ThenBy(static match => match.Index)
            .Select(static match => match.Index)
            .FirstOrDefault(-1);
    }

    public static IReadOnlyList<RadioStation> DeduplicateAndSort(IEnumerable<RadioStation> stations)
    {
        return stations
            .Where(static station => !string.IsNullOrWhiteSpace(station.StationUuid))
            .GroupBy(static station => station.StationUuid, StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                // A catalogue service may already have consolidated this station before it reaches
                // another display/filter boundary. Expand the retained variants before selecting a
                // primary record; otherwise a second DeduplicateAndSort pass silently replaces the
                // failover set with the primary row alone and playback loses its recovery URLs.
                var variants = group
                    .SelectMany(static station => station.Variants.Count > 0 ? station.Variants : [station])
                    .ToArray();
                var orderedVariants = variants
                    .OrderByDescending(static station => station.LastCheckOk ?? 0)
                    .ThenByDescending(static station => station.ClickCount ?? 0)
                    .ThenByDescending(static station => station.Votes ?? 0)
                    .ToArray();
                return orderedVariants[0] with { Variants = orderedVariants };
            })
            .OrderByDescending(static station => station.ClickCount ?? 0)
            .ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    // Radio Browser catalogues the same real-world station under many distinct UUIDs - different
    // community submissions, different CDN/edge URLs, a legacy Icecast mirror alongside a newer HLS
    // one, quality/format suffixes like "(128k)" on otherwise-identical entries. DeduplicateAndSort
    // only removes literal duplicate UUIDs, so each of those distinct-but-equivalent entries survives
    // as its own separate "station" with no awareness of the others - meaning playback has no real
    // fallback URL to fail over to when the one entry the user picked has a bad/flaky/exhausted
    // stream. Confirmed via the Mac source (AudioPlayerManager.swift's buildStreamURLs /
    // AppViewModel.swift's consolidateStationVariants): the Mac app groups these near-duplicates by a
    // canonicalized name into one logical station and keeps every grouped entry's URL as a fallback
    // candidate (AudioPlayerManager.tryNextStream walks them on any stall/failure) - this redundancy,
    // not anything clever in its buffering code, is why Mac playback recovers from a bad stream URL
    // far more often than this port did before this change. This performs the same consolidation:
    // group the already-UUID-deduped list by a conservative canonical name (only strips trailing
    // "(128k)"/"[HD]"-style quality/format suffixes - deliberately does NOT strip taglines after a
    // dash/colon, since that risks merging genuinely different stations that share a prefix, e.g.
    // "Heart Dance" and "Heart 80s"), picks the entry with the plainest name as the visible primary,
    // and attaches every grouped entry as a `Variants` fallback candidate via `AllStreamCandidates`.
    public static IReadOnlyList<RadioStation> ConsolidateVariants(IReadOnlyList<RadioStation> stations)
    {
        return stations
            .GroupBy(CanonicalStationName, StringComparer.Ordinal)
            .Select(static group =>
            {
                var members = group
                    .OrderBy(static station => LooksLikeVariantName(station.Name) ? 1 : 0)
                    .ThenByDescending(static station => (station.Votes ?? 0) + (station.ClickCount ?? 0))
                    .ThenBy(static station => station.Name.Length)
                    .ToArray();
                var primary = members[0];
                // A station already carries its own exact-UUID-duplicate Variants from
                // DeduplicateAndSort (usually just itself) - fold those in too rather than
                // discarding them, so no fallback candidate gathered earlier is lost here.
                var allVariants = members
                    .SelectMany(static station => station.Variants.Count > 0 ? station.Variants : [station])
                    .GroupBy(static station => station.StationUuid, StringComparer.OrdinalIgnoreCase)
                    .Select(static group => group.First())
                    .ToArray();
                return primary with { Variants = allVariants };
            })
            .OrderByDescending(static station => station.ClickCount ?? 0)
            .ThenBy(static station => station.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool LooksLikeVariantName(string name) =>
        !string.Equals(StripQualitySuffixes(name), name.Trim(), StringComparison.Ordinal);

    private static string CanonicalStationName(RadioStation station) =>
        Normalize(StripQualitySuffixes(station.Name));

    [GeneratedRegex(@"\s*[\(\[][^()\[\]]{1,40}[\)\]]\s*$")]
    private static partial Regex TrailingQualitySuffixRegex();

    private static string StripQualitySuffixes(string name)
    {
        var current = name.Trim();
        while (true)
        {
            var stripped = TrailingQualitySuffixRegex().Replace(current, string.Empty).TrimEnd();
            if (stripped.Length == 0 || stripped == current) return current;
            current = stripped;
        }
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex MultiSpaceRegex();
}
