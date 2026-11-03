using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;
using GlassRadio.Domain;
using GlassRadio.Infrastructure.Windows.LocalApi;
using Xunit;

namespace GlassRadio.Infrastructure.Tests;

public sealed class LocalApiTests
{
    [Fact]
    public async Task ServerRequiresAuthenticationAndReturnsStatusForAuthorizedRequest()
    {
        var port = GetFreePort();
        await using var server = new LocalApiServer();
        await server.StartAsync(new FakeTarget(), port, "test-key");
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };

        var unauthorized = await client.GetAsync("status");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var preflight = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "status"));
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);

        client.DefaultRequestHeaders.Add("X-Glass-Radio-Key", "test-key");
        var response = await client.GetAsync("status");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("glass radio", document.RootElement.GetProperty("app").GetString()?.ToLowerInvariant());
        Assert.Equal("stopped", document.RootElement.GetProperty("state").GetString());
        Assert.Equal(0.75, document.RootElement.GetProperty("buffering_progress").GetDouble(), precision: 2);
    }

    [Fact]
    public async Task InvalidCommandPayloadReturnsBadRequestAndDisposeReleasesPort()
    {
        var port = GetFreePort();
        await using var server = new LocalApiServer();
        await server.StartAsync(new FakeTarget(), port, "test-key");
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        client.DefaultRequestHeaders.Add("X-Glass-Radio-Key", "test-key");

        var response = await client.PostAsJsonAsync("volume", new { wrong = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await server.DisposeAsync();
        await using var replacement = new LocalApiServer();
        await replacement.StartAsync(new FakeTarget(), port, "replacement-key");
        Assert.True(replacement.IsRunning);
    }

    [Fact]
    public async Task LanBindingRequiresExplicitOptIn()
    {
        var port = GetFreePort();
        await using var server = new LocalApiServer(IPAddress.Loopback);

        await server.StartAsync(new FakeTarget(), port, "lan-key", allowLanAccess: true);

        Assert.True(server.IsRunning);
        Assert.True(server.AllowLanAccess);
        Assert.Equal(port, server.Port);
    }

    [Fact]
    public async Task MacParityMetricsAndPreferencesEndpointsRoundTripSettings()
    {
        var port = GetFreePort();
        var target = new FakeTarget();
        await using var server = new LocalApiServer();
        await server.StartAsync(target, port, "test-key");
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        client.DefaultRequestHeaders.Add("X-Glass-Radio-Key", "test-key");

        var metrics = await client.GetFromJsonAsync<JsonElement>("metrics");
        Assert.Equal(2, metrics.GetProperty("catalog").GetProperty("station_count").GetInt32());

        var update = await client.PostAsJsonAsync("preferences", new { default_volume = 0.31, api_enabled = true, allow_lan_access = true });
        update.EnsureSuccessStatusCode();
        Assert.Equal(0.31, target.Settings.Volume, precision: 2);
        Assert.True(target.Settings.LocalApiEnabled);
        Assert.True(target.Settings.LocalApiAllowLanAccess);

        var preferences = await client.GetFromJsonAsync<JsonElement>("preferences");
        Assert.Equal(0.31, preferences.GetProperty("volume").GetDouble(), precision: 2);
    }

    [Fact]
    public async Task BrowseAndStationScopesReturnValidatedStateAndPagination()
    {
        var port = GetFreePort();
        await using var server = new LocalApiServer();
        await server.StartAsync(new FakeTarget(), port, "test-key");
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        client.DefaultRequestHeaders.Add("X-Glass-Radio-Key", "test-key");

        var browse = await client.PostAsJsonAsync("browse", new { filter = "Major Brands", sort = "Most Popular", genre = "Pop" });
        browse.EnsureSuccessStatusCode();
        var stations = await client.GetFromJsonAsync<JsonElement>("stations?scope=all&offset=1&limit=1");
        Assert.Equal("all", stations.GetProperty("scope").GetString());
        Assert.Equal(1, stations.GetProperty("offset").GetInt32());
        Assert.Equal(1, stations.GetProperty("stations").GetArrayLength());
    }

    [Fact]
    public async Task MacCountryAndCollectionContractsAcceptNamesAndReturnEnvelopes()
    {
        var port = GetFreePort();
        var target = new FakeTarget();
        await using var server = new LocalApiServer();
        await server.StartAsync(target, port, "test-key");
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        client.DefaultRequestHeaders.Add("X-Glass-Radio-Key", "test-key");

        var country = await client.PostAsJsonAsync("country", new { country = "United Kingdom" });
        country.EnsureSuccessStatusCode();
        Assert.Equal("uk", target.LoadedCountryId);

        var favorites = await client.GetFromJsonAsync<JsonElement>("favorites");
        Assert.Equal(0, favorites.GetProperty("count").GetInt32());
        var countries = await client.GetFromJsonAsync<JsonElement>("countries");
        Assert.Equal(1, countries.GetProperty("count").GetInt32());

        var search = await client.PostAsJsonAsync("search", new { query = "bbc", limit = 1 });
        search.EnsureSuccessStatusCode();
        var payload = JsonDocument.Parse(await search.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("search", payload.GetProperty("action").GetString());
        Assert.Equal(1, payload.GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task TwoIndependentInstancesCanControlTheirOwnPlaybackThroughTheProtocol()
    {
        var firstPort = GetFreePort();
        var secondPort = GetFreePort();
        while (secondPort == firstPort) secondPort = GetFreePort();
        var firstTarget = new FakeTarget();
        var secondTarget = new FakeTarget();
        await using var firstServer = new LocalApiServer();
        await using var secondServer = new LocalApiServer();
        await firstServer.StartAsync(firstTarget, firstPort, "first-key");
        await secondServer.StartAsync(secondTarget, secondPort, "second-key");

        using var firstClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{firstPort}/") };
        using var secondClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{secondPort}/") };
        firstClient.DefaultRequestHeaders.Add("X-Glass-Radio-Key", "first-key");
        secondClient.DefaultRequestHeaders.Add("X-Glass-Radio-Key", "second-key");

        var firstResponse = await firstClient.PostAsJsonAsync("play", new { station_id = "one" });
        var secondResponse = await secondClient.PostAsJsonAsync("play", new { station_id = "two" });

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();
        Assert.Equal("one", firstTarget.PlayedStationId);
        Assert.Equal("two", secondTarget.PlayedStationId);
    }

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed class FakeTarget : ILocalApiTarget
    {
        public AppSettings Settings { get; private set; } = new();
        public string? PlayedStationId { get; private set; }
        public string? LoadedCountryId { get; private set; }
        public PlaybackSnapshot Playback { get; } = new(PlaybackState.Stopped, null, null, 0.72, null, false, 0.75);
        public IReadOnlyList<RadioStation> Stations { get; } = [
            new RadioStation("one", "One", "UK", "https://one.example", null, null, null, 1, 1, null, "pop", null, 128),
            new RadioStation("two", "Two", "UK", "https://two.example", null, null, null, 2, 2, null, "rock", null, 192)
        ];
        public IReadOnlyList<RadioStation> AllStations => Stations;
        public IReadOnlyList<RadioStation> TopStations => Stations.Take(1).ToArray();
        public IReadOnlyList<RadioStation> Favorites { get; } = Array.Empty<RadioStation>();
        public IReadOnlyList<CountryPreset> Countries { get; } = [new CountryPreset("uk", "United Kingdom", "United Kingdom", ["BBC"] )];

        public Task PlayAsync(string? stationId, CancellationToken cancellationToken) { PlayedStationId = stationId; return Task.CompletedTask; }
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ToggleAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetVolumeAsync(double volume, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<RadioStation>> SearchAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RadioStation>>(Stations);
        public Task LoadCountryAsync(string countryId, CancellationToken cancellationToken) { LoadedCountryId = countryId; return Task.CompletedTask; }
        public Task ApplyBrowseAsync(string? filter, string? sort, string? genre, CancellationToken cancellationToken) => Task.CompletedTask;
        public IReadOnlyList<RadioStation> GetStations(string? scope, string? genre) => Stations;
        public Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken) { Settings = settings; return Task.CompletedTask; }
    }
}
