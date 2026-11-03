using System.Text.Json;
using GlassRadio.Application.Abstractions;
using GlassRadio.Domain;

namespace GlassRadio.Infrastructure.Windows.Storage;

public sealed class LastPlayedStore(AppPaths paths) : ILastPlayedStore
{
    private readonly AtomicJsonStore<RadioStation> _store = new();

    public async Task<RadioStation?> LoadAsync(CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        try { return await _store.ReadAsync(paths.LastPlayed, cancellationToken).ConfigureAwait(false); }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    public Task SaveAsync(RadioStation station, CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        return _store.WriteAsync(paths.LastPlayed, station, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(paths.LastPlayed)) File.Delete(paths.LastPlayed);
        if (File.Exists(paths.LastPlayed + ".bak")) File.Delete(paths.LastPlayed + ".bak");
        return Task.CompletedTask;
    }
}
