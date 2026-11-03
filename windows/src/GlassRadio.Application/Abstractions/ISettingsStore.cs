using GlassRadio.Application.Contracts;

namespace GlassRadio.Application.Abstractions;

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
