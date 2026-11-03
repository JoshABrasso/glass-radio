using GlassRadio.Application.Contracts;

namespace GlassRadio.Application.Abstractions;

public interface ISnapshotStore
{
    Task<SnapshotLoadResult> LoadWithRecoveryAsync(string countryId, CancellationToken cancellationToken);
    Task<CountrySnapshot?> LoadAsync(string countryId, CancellationToken cancellationToken);
    Task SaveAsync(CountrySnapshot snapshot, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
