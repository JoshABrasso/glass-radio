using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;
using GlassRadio.Domain;

namespace GlassRadio.Application.Services;

public sealed class RadioCatalogService(
    IRadioBrowserClient client,
    ISnapshotStore snapshots)
{
    // Explore is global rather than tied to the active country, so it needs its own durable
    // snapshot key. It deliberately uses the existing atomic/recoverable snapshot store so a
    // malformed refresh cannot blank the last usable discovery surface.
    private const string ExploreSnapshotId = "_explore";
    private const int SuspiciousRefreshMinimumCachedCount = 20;
    private const int SuspiciousRefreshMinimumFreshCount = 8;

    public async Task<SnapshotLoadResult> LoadCachedCountryAsync(CountryPreset country, CancellationToken cancellationToken)
    {
        var result = await snapshots.LoadWithRecoveryAsync(country.Id, cancellationToken).ConfigureAwait(false);
        return NormalizeSnapshotResult(result);
    }

    public async Task<ServiceResult<IReadOnlyList<RadioStation>>> LoadCountryAsync(
        CountryPreset country,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<RadioStation>, Task>? publishPreviewAsync = null)
    {
        CountrySnapshot? cached = null;
        IReadOnlyList<RadioStation>? preview = null;
        try
        {
            var cachedResult = await snapshots.LoadWithRecoveryAsync(country.Id, cancellationToken).ConfigureAwait(false);
            cached = NormalizeSnapshotResult(cachedResult).Snapshot;

            try
            {
                var previewStations = await client.GetCountryStationsPreviewAsync(country, cancellationToken).ConfigureAwait(false);
                var cleanPreview = StationRules.DeduplicateAndSort(previewStations);
                if (IsPlausibleRefresh(cleanPreview, cached?.Stations))
                {
                    preview = cleanPreview;
                    if (publishPreviewAsync is not null)
                        await publishPreviewAsync(ConsolidateForDisplay(cleanPreview)).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A preview is an acceleration path. A failed preview must not
                // prevent the authoritative expanded country request.
            }

            var stations = await client.GetCountryStationsAsync(country, cancellationToken).ConfigureAwait(false);
            var clean = StationRules.DeduplicateAndSort(stations);
            EnsureRefreshIsPlausible(clean, cached?.Stations);
            // Persist the UUID-deduped raw list, not the name-consolidated one: RadioStation.Variants
            // is [JsonIgnore], so saving an already-consolidated list would permanently discard every
            // non-primary entry's URL the moment the snapshot round-trips through disk. Consolidation
            // is applied fresh on every read instead (see ConsolidateForDisplay), so a fresh fetch and
            // a cache load always produce the same fallback candidates.
            await snapshots.SaveAsync(new CountrySnapshot(CountrySnapshot.CurrentSchemaVersion, country.Id, DateTimeOffset.UtcNow, clean), cancellationToken).ConfigureAwait(false);
            return ServiceResult<IReadOnlyList<RadioStation>>.Success(ConsolidateForDisplay(clean));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (cached is not null && cached.Stations.Count > 0)
                return ServiceResult<IReadOnlyList<RadioStation>>.Success(ConsolidateForDisplay(cached.Stations), isStaleFallback: true);
            if (preview is { Count: > 0 })
                return ServiceResult<IReadOnlyList<RadioStation>>.Success(ConsolidateForDisplay(preview), isStaleFallback: true);
            return ServiceResult<IReadOnlyList<RadioStation>>.Failure("catalogue.load_failed", "The station catalogue could not be loaded.", ex);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<RadioStation>>> RefreshCountryAsync(CountryPreset country, CancellationToken cancellationToken)
    {
        CountrySnapshot? cached = null;
        try
        {
            var cachedResult = await snapshots.LoadWithRecoveryAsync(country.Id, cancellationToken).ConfigureAwait(false);
            cached = NormalizeSnapshotResult(cachedResult).Snapshot;

            var stations = await client.GetCountryStationsAsync(country, cancellationToken).ConfigureAwait(false);
            var clean = StationRules.DeduplicateAndSort(stations);
            EnsureRefreshIsPlausible(clean, cached?.Stations);
            await snapshots.SaveAsync(new CountrySnapshot(CountrySnapshot.CurrentSchemaVersion, country.Id, DateTimeOffset.UtcNow, clean), cancellationToken).ConfigureAwait(false);
            return ServiceResult<IReadOnlyList<RadioStation>>.Success(ConsolidateForDisplay(clean));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (cached is { Stations.Count: > 0 })
                return ServiceResult<IReadOnlyList<RadioStation>>.Success(ConsolidateForDisplay(cached.Stations), isStaleFallback: true);
            return ServiceResult<IReadOnlyList<RadioStation>>.Failure("catalogue.refresh_failed", "The station catalogue could not be refreshed.", ex);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<RadioStation>>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return ServiceResult<IReadOnlyList<RadioStation>>.Success(Array.Empty<RadioStation>());
        try
        {
            var stations = await client.SearchAsync(query.Trim(), cancellationToken).ConfigureAwait(false);
            return ServiceResult<IReadOnlyList<RadioStation>>.Success(ConsolidateForDisplay(StationRules.DeduplicateAndSort(stations)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<RadioStation>>.Failure("catalogue.search_failed", "The station search could not be completed.", ex);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<RadioStation>>> DiscoverAsync(CancellationToken cancellationToken)
        => await DiscoverAsync(Array.Empty<string>(), cancellationToken).ConfigureAwait(false);

    public async Task<ServiceResult<IReadOnlyList<RadioStation>>> DiscoverAsync(
        IReadOnlyList<string> preferredLanguages,
        CancellationToken cancellationToken)
    {
        CountrySnapshot? cached = null;
        try
        {
            cached = NormalizeSnapshotResult(await snapshots.LoadWithRecoveryAsync(ExploreSnapshotId, cancellationToken).ConfigureAwait(false)).Snapshot;
            var stations = await client.GetDiscoverStationsAsync(preferredLanguages, cancellationToken).ConfigureAwait(false);
            var clean = StationRules.DeduplicateAndSort(stations);
            EnsureRefreshIsPlausible(clean, cached?.Stations);
            await snapshots.SaveAsync(new CountrySnapshot(CountrySnapshot.CurrentSchemaVersion, ExploreSnapshotId, DateTimeOffset.UtcNow, clean), cancellationToken).ConfigureAwait(false);
            return ServiceResult<IReadOnlyList<RadioStation>>.Success(ConsolidateForDisplay(clean));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (cached is { Stations.Count: > 0 })
                return ServiceResult<IReadOnlyList<RadioStation>>.Success(ConsolidateForDisplay(cached.Stations), isStaleFallback: true);
            return ServiceResult<IReadOnlyList<RadioStation>>.Failure("catalogue.discover_failed", "Discover stations could not be loaded.", ex);
        }
    }

    public async Task<SnapshotLoadResult> LoadCachedDiscoverAsync(CancellationToken cancellationToken)
    {
        var result = await snapshots.LoadWithRecoveryAsync(ExploreSnapshotId, cancellationToken).ConfigureAwait(false);
        return NormalizeSnapshotResult(result);
    }

    private static SnapshotLoadResult NormalizeSnapshotResult(SnapshotLoadResult result)
    {
        if (result.Snapshot is null || result.Snapshot.Stations.Count == 0) return result;

        var clean = StationRules.DeduplicateAndSort(result.Snapshot.Stations);
        return clean.Count == 0
            ? result with { Snapshot = null, Status = SnapshotLoadStatus.Corrupt }
            : result with { Snapshot = result.Snapshot with { Stations = ConsolidateForDisplay(clean) } };
    }

    // See the long comment on StationRules.ConsolidateVariants - this is the single choke point
    // every station list passes through before it is ever shown to the user or handed to playback,
    // so a station's fallback candidates (AllStreamCandidates) are always as rich as the catalogue
    // actually has, regardless of which of the several load/search/discover/cache paths above
    // produced the list.
    private static IReadOnlyList<RadioStation> ConsolidateForDisplay(IReadOnlyList<RadioStation> stations) =>
        StationRules.ConsolidateVariants(stations);

    private static void EnsureRefreshIsPlausible(
        IReadOnlyList<RadioStation> fresh,
        IReadOnlyList<RadioStation>? cached)
    {
        if (!IsPlausibleRefresh(fresh, cached))
        {
            var cachedCount = cached?.Count ?? 0;
            throw new InvalidDataException(cachedCount == 0
                ? "Radio Browser returned no stations."
                : $"Radio Browser returned an incomplete catalogue ({fresh.Count} of {cachedCount} saved stations).");
        }
    }

    private static bool IsPlausibleRefresh(
        IReadOnlyList<RadioStation> fresh,
        IReadOnlyList<RadioStation>? cached)
    {
        if (fresh.Count == 0) return false;
        if (cached is null || cached.Count < SuspiciousRefreshMinimumCachedCount) return true;

        // A successful HTTP response can still be a partial catalogue when an
        // upstream endpoint truncates or silently drops its pagination. Never
        // replace a known-good country snapshot with that response. The active
        // feed will use the stale snapshot and the next explicit/background
        // refresh can try the endpoint again.
        var minimumExpected = Math.Max(SuspiciousRefreshMinimumFreshCount, cached.Count / 4);
        return fresh.Count >= minimumExpected;
    }

}
