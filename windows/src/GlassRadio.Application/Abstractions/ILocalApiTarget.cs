using GlassRadio.Domain;
using GlassRadio.Application.Contracts;

namespace GlassRadio.Application.Abstractions;

/// <summary>
/// The narrow application surface exposed to the explicitly enabled local control API.
/// It contains playback/catalogue state only; it is not an analytics or telemetry sink.
/// </summary>
public interface ILocalApiTarget
{
    AppSettings Settings { get; }
    PlaybackSnapshot Playback { get; }
    IReadOnlyList<RadioStation> Stations { get; }
    IReadOnlyList<RadioStation> AllStations { get; }
    IReadOnlyList<RadioStation> TopStations { get; }
    IReadOnlyList<RadioStation> Favorites { get; }
    IReadOnlyList<CountryPreset> Countries { get; }

    Task PlayAsync(string? stationId, CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task ToggleAsync(CancellationToken cancellationToken);
    Task NextAsync(CancellationToken cancellationToken);
    Task PreviousAsync(CancellationToken cancellationToken);
    Task SetVolumeAsync(double volume, CancellationToken cancellationToken);
    Task<IReadOnlyList<RadioStation>> SearchAsync(string query, CancellationToken cancellationToken);
    Task LoadCountryAsync(string countryId, CancellationToken cancellationToken);
    Task ApplyBrowseAsync(string? filter, string? sort, string? genre, CancellationToken cancellationToken);
    IReadOnlyList<RadioStation> GetStations(string? scope, string? genre);
    Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken);
}
