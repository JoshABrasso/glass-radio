using GlassRadio.Domain;
using Xunit;

namespace GlassRadio.Domain.Tests;

public sealed class StationSearchTests
{
    [Fact]
    public void MatchesCityStateCountryAndStationMetadata()
    {
        var station = new RadioStation(
            "city-search", "Local FM", "United Kingdom", "https://example.test/stream", null,
            null, null, 1, 1, "English", "indie, local, 97.1 fm", "MP3", 128,
            City: "Leeds", State: "England", CountryCode: "GB");

        Assert.True(StationRules.MatchesSearch(station, "Leeds"));
        Assert.True(StationRules.MatchesSearch(station, "England"));
        Assert.True(StationRules.MatchesSearch(station, "local fm"));
        Assert.True(StationRules.MatchesSearch(station, "97.1 fm"));
        Assert.False(StationRules.MatchesSearch(station, "Manchester"));
    }
}
