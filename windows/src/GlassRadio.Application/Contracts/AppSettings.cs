namespace GlassRadio.Application.Contracts;

public sealed record AppSettings(
    double Volume = 0.72,
    int ConnectionTimeoutSeconds = 12,
    string DefaultCountryId = "uk",
    bool AllowReligiousContent = false,
    bool AllowSportsContent = false,
    bool AllowNewsContent = true,
    IReadOnlyList<string>? RecentCountryIds = null,
    bool LocalApiEnabled = false,
    int LocalApiPort = 7373,
    bool LocalApiAllowLanAccess = false,
    bool OnboardingCompleted = false,
    bool PreferLowerBitrate = false,
    double LeftPaneWidth = 220,
    double RightPaneWidth = 260,
    bool ShowTopStations = true,
    bool ShowFavoritesNav = true,
    bool ShowCountriesPanel = true,
    bool ShowGenreFilter = true,
    AllStationsViewStyle AllStationsViewStyle = AllStationsViewStyle.List,
    IReadOnlyList<string>? PreferredLanguages = null,
    string LastActiveSection = "Home")
{
    public AppSettings Normalize() => this with
    {
        Volume = Math.Clamp(Volume, 0, 1),
        ConnectionTimeoutSeconds = Math.Clamp(ConnectionTimeoutSeconds, 6, 30),
        LocalApiPort = Math.Clamp(LocalApiPort, 1024, 65535),
        LeftPaneWidth = Math.Clamp(LeftPaneWidth, 180, 340),
        RightPaneWidth = Math.Clamp(RightPaneWidth, 220, 380),
        DefaultCountryId = string.IsNullOrWhiteSpace(DefaultCountryId) ? "uk" : DefaultCountryId.Trim().ToLowerInvariant(),
        RecentCountryIds = (RecentCountryIds ?? Array.Empty<string>())
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray(),
        PreferredLanguages = (PreferredLanguages ?? Array.Empty<string>())
            .Where(static language => !string.IsNullOrWhiteSpace(language))
            .Select(static language => language.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray(),
        AllStationsViewStyle = Enum.IsDefined(AllStationsViewStyle) ? AllStationsViewStyle : AllStationsViewStyle.List,
        LastActiveSection = NormalizeLastActiveSection(LastActiveSection)
    };

    private static string NormalizeLastActiveSection(string? value)
    {
        var normalized = value?.Trim();
        return normalized is not null &&
               (normalized.Equals("Explore", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Search", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Discover", StringComparison.OrdinalIgnoreCase))
            ? "Explore"
            : "Home";
    }
}
