using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;
using GlassRadio.Application.Services;
using GlassRadio.Domain;
using Xunit;

namespace GlassRadio.Application.Tests;

public sealed class RadioCatalogServiceTests
{
    [Fact]
    public async Task NormalCountryLoadRefreshesAStoredSnapshot()
    {
        var snapshots = new FakeSnapshots(new CountrySnapshot(
            CountrySnapshot.CurrentSchemaVersion,
            "uk",
            DateTimeOffset.UtcNow.AddDays(-2),
            [Station("cached")]));
        var client = new FakeClient([Station("fresh")]);
        var service = new RadioCatalogService(client, snapshots);

        var result = await service.LoadCountryAsync(Uk, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsStaleFallback);
        Assert.Equal("fresh", Assert.Single(result.Value!).Name);
        Assert.Equal("fresh", Assert.Single(snapshots.Saved!.Stations).Name);
    }

    [Fact]
    public async Task NormalCountryLoadUsesTheStoredSnapshotOnlyWhenRefreshFails()
    {
        var snapshots = new FakeSnapshots(new CountrySnapshot(
            CountrySnapshot.CurrentSchemaVersion,
            "uk",
            DateTimeOffset.UtcNow.AddDays(-2),
            [Station("cached")]));
        var service = new RadioCatalogService(new FakeClient(null), snapshots);

        var result = await service.LoadCountryAsync(Uk, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsStaleFallback);
        Assert.Equal("cached", Assert.Single(result.Value!).Name);
        Assert.Null(snapshots.Saved);
    }

    [Fact]
    public async Task ExplicitRefreshUsesTheStoredSnapshotWhenRefreshFails()
    {
        var snapshots = new FakeSnapshots(new CountrySnapshot(
            CountrySnapshot.CurrentSchemaVersion,
            "uk",
            DateTimeOffset.UtcNow.AddDays(-2),
            [Station("cached")]));
        var service = new RadioCatalogService(new FakeClient(null), snapshots);

        var result = await service.RefreshCountryAsync(Uk, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsStaleFallback);
        Assert.Equal("cached", Assert.Single(result.Value!).Name);
        Assert.Null(snapshots.Saved);
    }

    [Fact]
    public async Task EmptyRefreshResponseKeepsTheLastKnownGoodSnapshot()
    {
        var snapshots = new FakeSnapshots(new CountrySnapshot(
            CountrySnapshot.CurrentSchemaVersion,
            "uk",
            DateTimeOffset.UtcNow.AddDays(-2),
            [Station("cached")]));
        var service = new RadioCatalogService(new FakeClient([]), snapshots);

        var result = await service.RefreshCountryAsync(Uk, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsStaleFallback);
        Assert.Equal("cached", Assert.Single(result.Value!).Name);
        Assert.Null(snapshots.Saved);
    }

    [Fact]
    public async Task CachedStationsAreNormalizedBeforeAStaleFallbackIsReturned()
    {
        var cached = Station("cached");
        var snapshots = new FakeSnapshots(new CountrySnapshot(
            CountrySnapshot.CurrentSchemaVersion,
            "uk",
            DateTimeOffset.UtcNow.AddDays(-2),
            [cached, cached with { Name = "duplicate cached name" }, Station("other") with { StationUuid = "" }]));
        var service = new RadioCatalogService(new FakeClient(null), snapshots);

        var result = await service.RefreshCountryAsync(Uk, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsStaleFallback);
        Assert.Equal(["cached"], result.Value!.Select(station => station.Name));
    }

    [Fact]
    public async Task SuspiciouslySmallRefreshKeepsTheLastKnownGoodSnapshot()
    {
        var cachedStations = Enumerable.Range(1, 40).Select(index => Station($"cached-{index}")).ToArray();
        var snapshots = new FakeSnapshots(new CountrySnapshot(
            CountrySnapshot.CurrentSchemaVersion,
            "uk",
            DateTimeOffset.UtcNow.AddDays(-2),
            cachedStations));
        var service = new RadioCatalogService(new FakeClient([Station("partial")]), snapshots);

        var result = await service.RefreshCountryAsync(Uk, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsStaleFallback);
        Assert.Equal(40, result.Value!.Count);
        Assert.Null(snapshots.Saved);
    }

    [Fact]
    public async Task CountryLoadPublishesAUsablePreviewBeforeTheExpandedResult()
    {
        var snapshots = new FakeSnapshots(null);
        var client = new FakeClient([Station("expanded")], [Station("preview")]);
        var service = new RadioCatalogService(client, snapshots);
        IReadOnlyList<RadioStation>? published = null;

        var result = await service.LoadCountryAsync(
            Uk,
            CancellationToken.None,
            stations =>
            {
                published = stations;
                return Task.CompletedTask;
            });

        Assert.True(result.IsSuccess);
        Assert.Equal("preview", Assert.Single(published!).Name);
        Assert.Equal("expanded", Assert.Single(result.Value!).Name);
        Assert.Equal("expanded", Assert.Single(snapshots.Saved!.Stations).Name);
    }

    [Fact]
    public async Task CountryLoadDoesNotPublishASuspiciouslySmallPreviewOverTheCachedFeed()
    {
        var cachedStations = Enumerable.Range(1, 40).Select(index => Station($"cached-{index}")).ToArray();
        var snapshots = new FakeSnapshots(new CountrySnapshot(
            CountrySnapshot.CurrentSchemaVersion,
            "uk",
            DateTimeOffset.UtcNow.AddDays(-2),
            cachedStations));
        var service = new RadioCatalogService(
            new FakeClient(cachedStations, [Station("partial-preview")]),
            snapshots);
        IReadOnlyList<RadioStation>? published = null;

        var result = await service.LoadCountryAsync(
            Uk,
            CancellationToken.None,
            stations =>
            {
                published = stations;
                return Task.CompletedTask;
            });

        Assert.True(result.IsSuccess);
        Assert.Null(published);
        Assert.Equal(40, result.Value!.Count);
    }

    [Fact]
    public async Task DiscoverPersistsAValidGlobalSnapshot()
    {
        var snapshots = new FakeSnapshots(null);
        var service = new RadioCatalogService(new FakeClient(null, null, [Station("fresh-discover")]), snapshots);

        var result = await service.DiscoverAsync([], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsStaleFallback);
        Assert.Equal("fresh-discover", Assert.Single(result.Value!).Name);
        Assert.Equal("_explore", snapshots.Saved!.CountryId);
        Assert.Equal("fresh-discover", Assert.Single(snapshots.Saved.Stations).Name);
    }

    [Fact]
    public async Task DiscoverKeepsTheLastValidSnapshotWhenRefreshFails()
    {
        var snapshots = new FakeSnapshots(new CountrySnapshot(
            CountrySnapshot.CurrentSchemaVersion,
            "_explore",
            DateTimeOffset.UtcNow.AddHours(-3),
            [Station("saved-discover")]));
        var service = new RadioCatalogService(new FakeClient(null, null, null), snapshots);

        var result = await service.DiscoverAsync([], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsStaleFallback);
        Assert.Equal("saved-discover", Assert.Single(result.Value!).Name);
        Assert.Null(snapshots.Saved);
    }

    [Fact]
    public async Task CachedDiscoverLoadsThroughTheSameRecoveryBoundaryAsCountrySnapshots()
    {
        var snapshots = new FakeSnapshots(new CountrySnapshot(
            CountrySnapshot.CurrentSchemaVersion,
            "_explore",
            DateTimeOffset.UtcNow.AddMinutes(-20),
            [Station("saved-discover")]));
        var service = new RadioCatalogService(new FakeClient(null), snapshots);

        var result = await service.LoadCachedDiscoverAsync(CancellationToken.None);

        Assert.True(result.HasStations);
        Assert.Equal("_explore", result.Snapshot!.CountryId);
        Assert.Equal("saved-discover", Assert.Single(result.Snapshot.Stations).Name);
    }

    private static CountryPreset Uk => CountryPreset.All.Single(country => country.Id == "uk");

    private static RadioStation Station(string name) => new(
        "id-" + name,
        name,
        "United Kingdom",
        "https://example.test/stream",
        null,
        null,
        null,
        1,
        1,
        null,
        null,
        "MP3",
        128,
        1);

    private sealed class FakeClient(IReadOnlyList<RadioStation>? stations, IReadOnlyList<RadioStation>? previewStations = null, IReadOnlyList<RadioStation>? discoverStations = null) : IRadioBrowserClient
    {
        private readonly IReadOnlyList<RadioStation>? _previewStations = previewStations ?? stations;
        private readonly IReadOnlyList<RadioStation>? _discoverStations = discoverStations;

        public Task<IReadOnlyList<RadioStation>> GetCountryStationsPreviewAsync(CountryPreset country, CancellationToken cancellationToken) =>
            _previewStations is null ? Task.FromException<IReadOnlyList<RadioStation>>(new HttpRequestException("offline")) : Task.FromResult(_previewStations);

        public Task<IReadOnlyList<RadioStation>> GetCountryStationsAsync(CountryPreset country, CancellationToken cancellationToken) =>
            stations is null ? Task.FromException<IReadOnlyList<RadioStation>>(new HttpRequestException("offline")) : Task.FromResult(stations);

        public Task<IReadOnlyList<RadioStation>> SearchAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RadioStation>>([]);

        public Task<IReadOnlyList<RadioStation>> GetDiscoverStationsAsync(CancellationToken cancellationToken) =>
            _discoverStations is null
                ? Task.FromException<IReadOnlyList<RadioStation>>(new HttpRequestException("offline"))
                : Task.FromResult(_discoverStations);
    }

    private sealed class FakeSnapshots(CountrySnapshot? stored) : ISnapshotStore
    {
        public CountrySnapshot? Saved { get; private set; }

        public Task<SnapshotLoadResult> LoadWithRecoveryAsync(string countryId, CancellationToken cancellationToken) =>
            Task.FromResult(new SnapshotLoadResult(stored, stored is null ? SnapshotLoadStatus.Missing : SnapshotLoadStatus.Loaded));

        public Task<CountrySnapshot?> LoadAsync(string countryId, CancellationToken cancellationToken) => Task.FromResult(stored);

        public Task SaveAsync(CountrySnapshot snapshot, CancellationToken cancellationToken)
        {
            Saved = snapshot;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
