using System.Text.Json.Serialization;

namespace GlassRadio.Domain;

public sealed record RadioStation(
    [property: JsonPropertyName("stationuuid")] string StationUuid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("url_resolved")] string? UrlResolved,
    [property: JsonPropertyName("favicon")] string? Favicon,
    [property: JsonPropertyName("homepage")] string? Homepage,
    [property: JsonPropertyName("votes")] int? Votes,
    [property: JsonPropertyName("clickcount")] int? ClickCount,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("tags")] string? Tags,
    [property: JsonPropertyName("codec")] string? Codec,
    [property: JsonPropertyName("bitrate")] int? Bitrate,
    [property: JsonPropertyName("lastcheckok")] int? LastCheckOk = null,
    // Radio Browser exposes this as a formatted timestamp string, not an epoch.
    // It is retained as text because the app does not perform time arithmetic on it.
    [property: JsonPropertyName("lastchecktime")] string? LastCheckTime = null,
    [property: JsonPropertyName("city")] string? City = null,
    [property: JsonPropertyName("state")] string? State = null,
    [property: JsonPropertyName("countrycode")] string? CountryCode = null)
{
    [JsonPropertyName("country_top_rank")]
    public int? CountryTopRank { get; init; }

    // Radio Browser can return several records for one station identity, each
    // with a different working stream URL/bitrate. The Mac player keeps those
    // variants available for failover. Keep the relationship in memory only;
    // snapshots still persist the canonical station record without a recursive
    // variant payload.
    [JsonIgnore]
    public IReadOnlyList<RadioStation> Variants { get; init; } = Array.Empty<RadioStation>();

    public string Id => StationUuid;

    public string LocationText => string.Join(", ", new[] { City, State, Country }.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));

    public IReadOnlyList<string> Genres =>
        string.IsNullOrWhiteSpace(Tags)
            ? Array.Empty<string>()
            : Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    public IReadOnlyList<string> StreamCandidates
    {
        get
        {
            var candidates = new[] { UrlResolved, Url }
                .Where(static url => url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                                     (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                .Select(static url => url!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return candidates;
        }
    }

    [JsonIgnore]
    public IReadOnlyList<string> AllStreamCandidates =>
        Variants
            .SelectMany(static station => station.StreamCandidates)
            .Concat(StreamCandidates)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
