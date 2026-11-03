using GlassRadio.Domain;
using Xunit;

namespace GlassRadio.Domain.Tests;

public sealed class CountryPresetTests
{
    [Fact]
    public void CuratedCountryFixtureContainsAllSupportedUniqueEntries()
    {
        Assert.Equal(56, CountryPreset.All.Count);
        Assert.Equal(56, CountryPreset.All.Select(country => country.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(CountryPreset.All, country =>
        {
            Assert.Equal(2, country.CountryCode.Length);
            Assert.Equal(2, country.FlagEmoji.EnumerateRunes().Count());
            Assert.NotEmpty(country.TopBrands);
        });
        Assert.Equal("GB", CountryPreset.All.Single(country => country.Id == "uk").CountryCode);
    }

    [Fact]
    public void CuratedTopStationsUseTheActiveCountryCatalogueAndMetrics()
    {
        var france = CountryPreset.All.Single(country => country.Id == "fr");
        var stations = Enumerable.Range(1, 60)
            .Select(index => new RadioStation(
                $"station-{index}",
                index == 1 ? "France Inter" : $"Local France {index}",
                "France",
                $"https://example.com/{index}.mp3",
                null,
                index == 1 ? "https://example.com/logo.png" : null,
                null,
                index == 1 ? 1000 : 100 - index,
                index == 1 ? 10000 : 1000 - index,
                null,
                null,
                "MP3",
                128,
                1))
            .ToArray();

        var curated = CuratedStationRules.Build(stations, france, 50);

        Assert.Equal(50, curated.Count);
        Assert.Equal(50, curated.Select(station => station.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("France Inter", curated[0].Name);
        Assert.Contains(curated, station => station.Name == "France Inter");
        Assert.DoesNotContain(curated, station => station.Name.Contains("United Kingdom", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CuratedTopStationsRankRecognisedRepresentativesByLiveMetrics()
    {
        var country = new CountryPreset("xx", "Exampleland", "Exampleland", ["Alpha", "Beta"]);
        var stations = new[]
        {
            new RadioStation("beta", "Beta Radio", "Exampleland", "https://example.com/beta", null, null, null, 10_000, 10_000, null, null, null, 128, 1),
            new RadioStation("alpha", "Alpha Radio", "Exampleland", "https://example.com/alpha", null, null, null, 1, 1, null, null, null, 128, 1)
        };

        var curated = CuratedStationRules.Build(stations, country, 2);

        Assert.Equal(["beta", "alpha"], curated.Select(static station => station.Id));
    }

    [Fact]
    public void CuratedTopStationsUseApiRankAndPreferTheCanonicalNetworkRecord()
    {
        var country = new CountryPreset("gb", "Exampleland", "Exampleland", ["Capital", "BBC Radio 1", "BBC Radio 1 Anthems"]);
        var stations = new[]
        {
            new RadioStation("capital-regional", "Capital FM London", "Exampleland", "https://example.com/capital-london", null, null, null, 50, 1000, null, null, null, 128, 1)
            {
                CountryTopRank = 1
            },
            new RadioStation("bbc-1", "BBC Radio 1", "Exampleland", "https://example.com/bbc-1", null, null, null, 1, 1, null, null, null, 128, 1)
            {
                CountryTopRank = 2
            },
            new RadioStation("capital-main", "CAPITAL - The UK's No.1 Hit Music Station", "Exampleland", "https://example.com/capital", null, null, null, 2, 2, null, null, null, 128, 1)
            {
                CountryTopRank = 3
            },
            new RadioStation("bbc-anthems", "BBC Radio 1 Anthems", "Exampleland", "https://example.com/anthems", null, null, null, 1, 1, null, null, null, 128, 1)
            {
                CountryTopRank = 4
            }
        };

        var curated = CuratedStationRules.Build(stations, country, 10);

        Assert.Equal("capital-main", curated.Single(station => station.Id == "capital-main").Id);
        Assert.DoesNotContain(curated, station => station.Id == "capital-regional");
        Assert.Contains(curated, station => station.Id == "bbc-anthems");
        Assert.Equal(["bbc-1", "capital-main", "bbc-anthems"], curated.Select(static station => station.Id));
    }

    [Fact]
    public void CuratedTopStationsDoNotPromoteUnrankedBrandEnrichmentResults()
    {
        var country = new CountryPreset("xx", "Exampleland", "Exampleland", ["Alpha", "Beta"]);
        var ranked = new RadioStation(
            "ranked-alpha",
            "Alpha Radio",
            "Exampleland",
            "https://example.com/alpha",
            null,
            null,
            null,
            1,
            1,
            null,
            null,
            "MP3",
            128,
            1)
        {
            CountryTopRank = 1
        };
        var unrankedBrandMatch = new RadioStation(
            "unranked-beta",
            "Beta Radio",
            "Exampleland",
            "https://example.com/beta",
            null,
            null,
            null,
            100_000,
            100_000,
            null,
            null,
            "MP3",
            128,
            1);

        var curated = CuratedStationRules.Build([ranked, unrankedBrandMatch], country, 50);

        Assert.Equal(["ranked-alpha"], curated.Select(static station => station.Id));
    }
}
