using System.Text.Json;
using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;

namespace GlassRadio.Infrastructure.Windows.Storage;

public sealed class SettingsStore(AppPaths paths) : ISettingsStore
{
    private readonly AtomicJsonStore<AppSettings> _store = new();

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        try { return (await _store.ReadAsync(paths.Settings, cancellationToken).ConfigureAwait(false) ?? new AppSettings()).Normalize(); }
        catch (JsonException) { return new AppSettings(); }
        catch (IOException) { return new AppSettings(); }
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        return _store.WriteAsync(paths.Settings, settings.Normalize(), cancellationToken);
    }
}
