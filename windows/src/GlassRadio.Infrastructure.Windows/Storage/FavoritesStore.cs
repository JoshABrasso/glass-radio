using System.Text.Json;
using GlassRadio.Application.Abstractions;
using GlassRadio.Domain;

namespace GlassRadio.Infrastructure.Windows.Storage;

public sealed class FavoritesStore(AppPaths paths) : IFavoritesStore
{
    private readonly AtomicJsonStore<IReadOnlyList<RadioStation>> _store = new();

    public async Task<IReadOnlyList<RadioStation>> LoadAsync(CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        try { return await _store.ReadAsync(paths.Favorites, cancellationToken).ConfigureAwait(false) ?? Array.Empty<RadioStation>(); }
        catch (JsonException) { return Array.Empty<RadioStation>(); }
        catch (IOException) { return Array.Empty<RadioStation>(); }
    }

    public Task SaveAsync(IReadOnlyList<RadioStation> stations, CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        return _store.WriteAsync(paths.Favorites, stations.ToArray(), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteAtomicFiles(paths.Favorites);
        return Task.CompletedTask;
    }

    private static void DeleteAtomicFiles(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
    }
}
