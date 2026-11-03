using GlassRadio.Domain;
using Xunit;

namespace GlassRadio.Domain.Tests;

public sealed class StationRulesTests
{
    [Fact]
    public void BrowseRulesApplyFavoriteGenreBrandAndSortSemantics()
    {
        var country = new CountryPreset("uk", "United Kingdom", "United Kingdom", ["BBC Radio 2"]);
        var stations = new[]
        {
            new RadioStation("a", "BBC Radio 2", "UK", "https://a", null, null, null, 2, 10, null, "pop", null, 128),
            new RadioStation("b", "Local News", "UK", "https://b", null, null, null, 20, 2, null, "news", null, 320),
            new RadioStation("c", "Ambient", "UK", "https://c", null, null, null, 1, 1, null, "ambient", null, 64)
        };

        var favorites = StationBrowseRules.Apply(stations, country, StationFilterMode.FavoritesOnly, StationSortMode.Name, "All", new HashSet<string>(["b", "a"]));
        Assert.Equal(["a", "b"], favorites.Select(static station => station.Id));

        var brands = StationBrowseRules.Apply(stations, country, StationFilterMode.MajorBrands, StationSortMode.Popularity, "All", new HashSet<string>());
        Assert.Equal("a", Assert.Single(brands).Id);

        var genre = StationBrowseRules.Apply(stations, country, StationFilterMode.All, StationSortMode.BitrateHigh, "news", new HashSet<string>());
        Assert.Equal("b", Assert.Single(genre).Id);
    }

    [Fact]
    public void BrowseRulesMatchMacPopularReliableAndPriorityModes()
    {
        var country = new CountryPreset("uk", "United Kingdom", "United Kingdom", ["BBC Radio"]);
        var stations = Enumerable.Range(0, 405)
            .Select(index => new RadioStation(
                $"station-{index}",
                index == 0 ? "BBC Radio One" : $"Local {index}",
                "UK",
                $"https://station-{index}",
                null,
                null,
                null,
                index == 0 ? 100 : 1,
                index == 0 ? 100 : 0,
                null,
                index % 2 == 0 ? "pop" : null,
                null,
                128))
            .ToArray();

        var favorites = new HashSet<string>(["station-404"]);
        var popular = StationBrowseRules.Apply(stations, country, StationFilterMode.Popular, StationSortMode.Popularity, "All", favorites);
        var reliable = StationBrowseRules.Apply(stations, country, StationFilterMode.Reliable, StationSortMode.Name, "All", favorites);
        var favoritesFirst = StationBrowseRules.Apply(stations, country, StationFilterMode.All, StationSortMode.FavoritesFirst, "All", favorites);

        Assert.True(popular.Count == 400);
        Assert.Equal("station-0", popular[0].Id);
        Assert.Equal("station-0", Assert.Single(reliable).Id);
        Assert.Equal("station-404", favoritesFirst[0].Id);
    }

    [Fact]
    public void BrowseRulesGenreMatchingIgnoresPunctuationSpacing()
    {
        var station = new RadioStation("a", "80s Station", "UK", "https://a", null, null, null, null, null, null, "80's, pop", null, 128);

        var result = StationBrowseRules.Apply([station], null, StationFilterMode.All, StationSortMode.Name, "80s", new HashSet<string>());

        Assert.Equal("a", Assert.Single(result).Id);
        Assert.True(StationBrowseRules.MatchesGenre(station, "80s"));
        Assert.True(StationBrowseRules.MatchesGenre(station, "Pop"));
    }

    [Fact]
    public void BrowseRulesGenreFilterKeepsRequestedSortOrder()
    {
        var stations = new[]
        {
            new RadioStation("z", "Zulu Ambient", "UK", "https://z", null, null, null, 1, 2, null, "ambient", null, 128),
            new RadioStation("a", "Alpha Ambient", "UK", "https://a", null, null, null, 1, 2, null, "ambient", null, 128),
            new RadioStation("n", "News", "UK", "https://n", null, null, null, 9, 9, null, "news", null, 128)
        };

        var result = StationBrowseRules.Apply(stations, null, StationFilterMode.All, StationSortMode.Name, "Ambient", new HashSet<string>());

        Assert.Equal(["a", "z"], result.Select(static station => station.Id));
    }

    [Fact]
    public void BrowseRulesBitrateSortUsesMacStyleNameMarkersAndStableFallback()
    {
        var stations = new[]
        {
            new RadioStation("fallback", "Fallback", "UK", "https://fallback", null, null, null, null, null, null, null, null, null),
            new RadioStation("high", "High 320kbps", "UK", "https://high", null, null, null, null, null, null, null, null, 64),
            new RadioStation("low", "Low 64k", "UK", "https://low", null, null, null, null, null, null, null, null, 320)
        };

        var highToLow = StationBrowseRules.Apply(stations, null, StationFilterMode.All, StationSortMode.BitrateHigh, "All", new HashSet<string>());
        var lowToHigh = StationBrowseRules.Apply(stations, null, StationFilterMode.All, StationSortMode.BitrateLow, "All", new HashSet<string>());

        Assert.Equal(["high", "fallback", "low"], highToLow.Select(static station => station.Id));
        Assert.Equal(["low", "fallback", "high"], lowToHigh.Select(static station => station.Id));
    }

    [Fact]
    public void CatalogueRetainsMacCountrySet()
    {
        Assert.Equal(56, CountryPreset.All.Count);
        Assert.Contains(CountryPreset.All, country => country.Id == "uk");
    }

    [Fact]
    public void NormalizationIsUnicodeAwareAndStable()
    {
        Assert.Equal("hitradio o3", StationRules.Normalize("Hitradio Ö3"));
        Assert.Equal("hitradio o3", StationRules.Normalize("Hitradio O3"));
        Assert.Equal("bbc radio 1 dance", StationRules.Normalize("BBC Radio 1: Dance"));
    }

    [Fact]
    public void BestBrandMatchPrefersSpecificVariantOverBroadNetworkBrand()
    {
        var station = new RadioStation("capital-dance", "Capital Dance", "UK", "https://station", null, null, null, null, null, null, null, null, 128);

        var index = StationRules.BestBrandIndex(station, ["Capital", "Capital Anthems", "Capital Chill", "Capital Dance"]);

        Assert.Equal(3, index);

        var descriptive = new RadioStation("bbc-dance", "BBC RADIO 1 DANCE: The biggest current, future and classic dance vibes", "UK", "https://station", null, null, null, null, null, null, null, null, 128);
        Assert.Equal(2, StationRules.BestBrandIndex(descriptive, ["BBC Radio 1", "BBC Radio 1 Anthems", "BBC Radio 1 Dance", "BBC Radio 1Xtra"]));
    }

    [Fact]
    public void DeduplicationKeepsHealthyMostPopularRecord()
    {
        var stations = new[]
        {
            new RadioStation("same", "Station", "UK", "https://one.example", null, null, null, 2, 5, null, null, null, 96, 0),
            new RadioStation("same", "Station", "UK", "https://two.example", null, null, null, 1, 20, null, null, null, 128, 1)
        };

        var result = StationRules.DeduplicateAndSort(stations);

        var station = Assert.Single(result);
        Assert.Equal("https://two.example", station.Url);
    }

    [Fact]
    public void DeduplicationRetainsSameIdentityStreamVariantsForPlaybackFailover()
    {
        var stations = new[]
        {
            new RadioStation("same", "Station", "UK", "https://fallback.example/stream", null, null, null, 2, 5, null, null, null, 96, 1),
            new RadioStation("same", "Station", "UK", "https://preferred.example/stream", "https://resolved.example/stream", null, null, 1, 20, null, null, null, 128, 1)
        };

        var station = Assert.Single(StationRules.DeduplicateAndSort(stations));

        Assert.Equal("https://preferred.example/stream", station.Url);
        Assert.Equal(
            ["https://resolved.example/stream", "https://preferred.example/stream", "https://fallback.example/stream"],
            station.AllStreamCandidates);
        Assert.Equal(2, station.Variants.Count);
    }

    [Fact]
    public void DeduplicationPreservesVariantsWhenAConsolidatedStationCrossesAnotherBoundary()
    {
        var stations = new[]
        {
            new RadioStation("plain", "BBC Radio 4", "UK", "https://plain.example/stream", null, null, null, 10, 10, null, null, null, 320),
            new RadioStation("suffixed", "BBC Radio 4 (128k)", "UK", "https://suffixed.example/stream", null, null, null, 1, 1, null, null, null, 128)
        };

        var consolidated = StationRules.ConsolidateVariants(StationRules.DeduplicateAndSort(stations));
        var replayed = StationRules.DeduplicateAndSort(consolidated);

        var station = Assert.Single(replayed);
        Assert.Equal(2, station.Variants.Count);
        Assert.Contains("https://plain.example/stream", station.AllStreamCandidates);
        Assert.Contains("https://suffixed.example/stream", station.AllStreamCandidates);
    }

    [Fact]
    public void ConsolidateVariantsMergesQualitySuffixedDuplicatesIntoOneStationWithBothCandidates()
    {
        var stations = new[]
        {
            new RadioStation("plain", "BBC Radio 4", "UK", "https://plain.example/stream", null, null, null, 10, 10, null, null, null, 320),
            new RadioStation("suffixed", "BBC Radio 4 (128k)", "UK", "https://suffixed.example/stream", null, null, null, 1, 1, null, null, null, 128)
        };

        var result = StationRules.ConsolidateVariants(StationRules.DeduplicateAndSort(stations));

        var station = Assert.Single(result);
        Assert.Equal("BBC Radio 4", station.Name);
        Assert.Equal(2, station.Variants.Count);
        Assert.Contains("https://plain.example/stream", station.AllStreamCandidates);
        Assert.Contains("https://suffixed.example/stream", station.AllStreamCandidates);
    }

    [Fact]
    public void ConsolidateVariantsDoesNotMergeGenuinelyDifferentStationsSharingAPrefix()
    {
        var stations = new[]
        {
            new RadioStation("dance", "Heart Dance", "UK", "https://dance.example/stream", null, null, null, 5, 5, null, null, null, 128),
            new RadioStation("eighties", "Heart 80s", "UK", "https://eighties.example/stream", null, null, null, 5, 5, null, null, null, 128)
        };

        var result = StationRules.ConsolidateVariants(StationRules.DeduplicateAndSort(stations));

        Assert.Equal(2, result.Count);
        Assert.Equal(["Heart 80s", "Heart Dance"], result.Select(static station => station.Name).OrderBy(static name => name));
    }

    [Fact]
    public void ContentRulesExcludeDisallowedCategories()
    {
        var station = new RadioStation("id", "Example Sports News", "UK", "https://example.test/stream", null, null, null, null, null, null, "sports, news", null, null);

        Assert.False(StationRules.IsEligibleForContent(station, false, false, false));
        Assert.False(StationRules.IsEligibleForContent(station, false, true, false));
        Assert.True(StationRules.IsEligibleForContent(station, false, true, true));
    }

    [Fact]
    public void StreamCandidatesAcceptHttpAndHttpsInStableOrder()
    {
        var station = new RadioStation("id", "Station", "UK", "http://example.test/stream", "https://secure.example/stream", null, null, null, null, null, null, null, null);

        Assert.Equal(["https://secure.example/stream", "http://example.test/stream"], station.StreamCandidates);
    }
}
