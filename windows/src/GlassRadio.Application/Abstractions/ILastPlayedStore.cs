using GlassRadio.Domain;

namespace GlassRadio.Application.Abstractions;

public interface ILastPlayedStore
{
    Task<RadioStation?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(RadioStation station, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
