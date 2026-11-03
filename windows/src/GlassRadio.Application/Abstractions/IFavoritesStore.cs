using GlassRadio.Domain;

namespace GlassRadio.Application.Abstractions;

public interface IFavoritesStore
{
    Task<IReadOnlyList<RadioStation>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<RadioStation> stations, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
