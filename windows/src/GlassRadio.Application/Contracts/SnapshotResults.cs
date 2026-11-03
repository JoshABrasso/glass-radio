namespace GlassRadio.Application.Contracts;

public enum SnapshotLoadStatus
{
    NotAvailable,
    Missing,
    Loaded,
    RecoveredFromBackup,
    Corrupt
}

public sealed record SnapshotLoadResult(CountrySnapshot? Snapshot, SnapshotLoadStatus Status)
{
    public bool HasStations => Snapshot is { Stations.Count: > 0 };
}
