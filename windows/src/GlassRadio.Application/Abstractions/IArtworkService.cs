using GlassRadio.Domain;

namespace GlassRadio.Application.Abstractions;

public interface IArtworkService : IAsyncDisposable
{
    Task<Uri?> GetArtworkAsync(RadioStation station, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
