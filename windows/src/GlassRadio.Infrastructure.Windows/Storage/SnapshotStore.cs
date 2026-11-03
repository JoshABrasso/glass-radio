using System.Text.Json;
using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;

namespace GlassRadio.Infrastructure.Windows.Storage;

public sealed class SnapshotStore(AppPaths paths) : ISnapshotStore
{
    private readonly AtomicJsonStore<CountrySnapshot> _store = new();
    private readonly JsonSerializerOptions _readOptions = new(JsonSerializerDefaults.Web);

    public async Task<SnapshotLoadResult> LoadWithRecoveryAsync(string countryId, CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        var path = Path.Combine(paths.Snapshots, countryId + ".json");
        var backupPath = path + ".bak";
        var primaryExists = File.Exists(path);
        var backupExists = File.Exists(backupPath);
        if (!primaryExists && !backupExists) return new(null, SnapshotLoadStatus.Missing);

        var primary = await TryReadValidAsync(path, countryId, cancellationToken).ConfigureAwait(false);
        if (primary is not null) return new(primary, SnapshotLoadStatus.Loaded);

        var backup = await TryReadValidAsync(backupPath, countryId, cancellationToken).ConfigureAwait(false);
        if (backup is not null)
        {
            if (primaryExists) Quarantine(path, countryId);
            try { File.Copy(backupPath, path, true); } catch (IOException) { }
            return new(backup, SnapshotLoadStatus.RecoveredFromBackup);
        }

        if (primaryExists) Quarantine(path, countryId);
        if (backupExists) Quarantine(backupPath, countryId);
        return new(null, SnapshotLoadStatus.Corrupt);
    }

    public async Task<CountrySnapshot?> LoadAsync(string countryId, CancellationToken cancellationToken)
    {
        return (await LoadWithRecoveryAsync(countryId, cancellationToken).ConfigureAwait(false)).Snapshot;
    }

    public Task SaveAsync(CountrySnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.SchemaVersion != CountrySnapshot.CurrentSchemaVersion) throw new InvalidDataException("Unsupported snapshot schema.");
        paths.EnsureDirectories();
        return _store.WriteAsync(Path.Combine(paths.Snapshots, snapshot.CountryId + ".json"), snapshot, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        // Clear the whole snapshot directory, not only primary JSON files. Atomic
        // writes leave .bak files and interrupted writes can leave .tmp-* files;
        // retaining either would let a later recovery resurrect a library the user
        // explicitly reset.
        foreach (var file in Directory.EnumerateFiles(paths.Snapshots))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file);
        }
        return Task.CompletedTask;
    }

    private async Task<CountrySnapshot?> TryReadValidAsync(string path, string countryId, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<CountrySnapshot>(stream, _readOptions, cancellationToken).ConfigureAwait(false);
            return snapshot is { SchemaVersion: CountrySnapshot.CurrentSchemaVersion, Stations.Count: > 0 } &&
                   string.Equals(snapshot.CountryId, countryId, StringComparison.OrdinalIgnoreCase)
                ? snapshot
                : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (InvalidDataException) { return null; }
    }

    private void Quarantine(string path, string countryId)
    {
        if (!File.Exists(path)) return;
        var quarantine = Path.Combine(paths.Snapshots, $"{countryId}.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");
        try { File.Move(path, quarantine); } catch (IOException) { }
    }
}
