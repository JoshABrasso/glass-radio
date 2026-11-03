using System.Net;
using System.Net.Http;
using GlassRadio.Infrastructure.Windows;
using Xunit;

namespace GlassRadio.Infrastructure.Tests;

public sealed class RadioBrowserClientTests
{
    [Fact]
    public async Task CountryLoadUsesIsoCodeAndAcceptsRadioBrowserTimestampFields()
    {
        using var http = new HttpClient(new StubHandler()) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new RadioBrowserClient(http);
        var country = GlassRadio.Domain.CountryPreset.All.Single(candidate => candidate.Id == "uk");

        var stations = await client.GetCountryStationsAsync(country, CancellationToken.None);

        Assert.Contains(stations, station => station.Name == "BBC World Service");
        Assert.Contains(stations, station => station.Name == "BBC Radio 1 Dance");
        var station = stations.Single(candidate => candidate.Name == "BBC World Service");
        Assert.Equal("London", station.City);
        Assert.Equal("England", station.State);
        Assert.Equal(1, station.CountryTopRank);
        Assert.Contains(StubHandler.Requests, request => request.Contains("order=clickcount", StringComparison.OrdinalIgnoreCase) && request.Contains("limit=200", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CountryPreviewUsesTheRankedIsoRouteWithTheMacSizedInitialPull()
    {
        using var http = new HttpClient(new StubHandler()) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new RadioBrowserClient(http);
        var country = GlassRadio.Domain.CountryPreset.All.Single(candidate => candidate.Id == "uk");

        var stations = await client.GetCountryStationsPreviewAsync(country, CancellationToken.None);

        Assert.Contains(stations, station => station.Name == "BBC World Service");
        Assert.Contains(StubHandler.Requests, request => request.Contains("limit=600", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchQueriesStationNameAndStateAndMergesDuplicateResults()
    {
        using var http = new HttpClient(new StubHandler()) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new RadioBrowserClient(http);

        var stations = await client.SearchAsync("London", CancellationToken.None);

        Assert.Equal(2, stations.Count);
        Assert.Contains(stations, station => station.Name == "London Radio");
        Assert.Contains(stations, station => station.Name == "London State Radio");
    }

    [Fact]
    public async Task SearchKeepsSuccessfulResultsWhenTheOtherQueryRouteFails()
    {
        using var http = new HttpClient(new PartialSearchHandler()) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new RadioBrowserClient(http);

        var stations = await client.SearchAsync("London", CancellationToken.None);

        var station = Assert.Single(stations);
        Assert.Equal("London Radio", station.Name);
    }

    [Fact]
    public async Task SearchIncludesCityAndCountryMetadataRoutes()
    {
        using var http = new HttpClient(new CitySearchHandler()) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new RadioBrowserClient(http);

        var stations = await client.SearchAsync("London", CancellationToken.None);

        var station = Assert.Single(stations);
        Assert.Equal("London City Radio", station.Name);
        Assert.Contains(CitySearchHandler.Requests, request => request.Contains("city=London", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(CitySearchHandler.Requests, request => request.Contains("country=London", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public static List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri?.ToString() ?? string.Empty);
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var query = request.RequestUri?.Query ?? string.Empty;
            var body = path.Contains("bycountrycodeexact/GB", StringComparison.OrdinalIgnoreCase)
                ? "[{\"stationuuid\":\"bbc-1\",\"name\":\"BBC World Service\",\"country\":\"The United Kingdom\",\"countrycode\":\"GB\",\"city\":\"London\",\"state\":\"England\",\"url\":\"https://example.test/stream\",\"url_resolved\":\"https://example.test/stream\",\"favicon\":null,\"homepage\":\"https://bbc.co.uk\",\"votes\":164027,\"clickcount\":593,\"language\":\"english\",\"tags\":\"news,talk\",\"codec\":\"MP3\",\"bitrate\":56,\"lastcheckok\":1,\"lastchecktime\":\"2026-09-25 20:56:09\"}]"
                : query.Contains("name=BBC%20Radio%201%20Dance", StringComparison.OrdinalIgnoreCase)
                    ? "[{\"stationuuid\":\"bbc-dance\",\"name\":\"BBC Radio 1 Dance\",\"country\":\"The United Kingdom\",\"countrycode\":\"GB\",\"url\":\"https://example.test/dance\"}]"
                : query.Contains("name=London", StringComparison.OrdinalIgnoreCase)
                    ? "[{\"stationuuid\":\"search-1\",\"name\":\"London Radio\",\"country\":\"United Kingdom\",\"url\":\"https://example.test/london\"}]"
                    : query.Contains("state=London", StringComparison.OrdinalIgnoreCase)
                        ? "[{\"stationuuid\":\"search-1\",\"name\":\"London Radio\",\"country\":\"United Kingdom\",\"url\":\"https://example.test/london\"},{\"stationuuid\":\"search-2\",\"name\":\"London State Radio\",\"country\":\"United Kingdom\",\"url\":\"https://example.test/state\"}]"
                : "[]";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }

    private sealed class PartialSearchHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            if (query.Contains("state=London", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"stationuuid\":\"partial-1\",\"name\":\"London Radio\",\"country\":\"United Kingdom\",\"url\":\"https://example.test/london\"}]")
            });
        }
    }

    private sealed class CitySearchHandler : HttpMessageHandler
    {
        public static List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            Requests.Add(query);
            var body = query.Contains("city=London", StringComparison.OrdinalIgnoreCase)
                ? "[{\"stationuuid\":\"city-1\",\"name\":\"London City Radio\",\"country\":\"United Kingdom\",\"city\":\"London\",\"url\":\"https://example.test/city\"}]"
                : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}
