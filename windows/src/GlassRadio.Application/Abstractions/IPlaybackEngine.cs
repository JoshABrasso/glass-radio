using GlassRadio.Domain;
using GlassRadio.Application.Contracts;

namespace GlassRadio.Application.Abstractions;

public enum PlaybackState { Stopped, Loading, Playing, Paused, Failed }

public sealed record PlaybackSnapshot(
    PlaybackState State,
    RadioStation? Station,
    string? Error,
    double Volume,
    int? Bitrate,
    bool IsMeasuredTelemetry,
    double? BufferingProgress = null);

public interface IPlaybackEngine : IAsyncDisposable
{
    PlaybackSnapshot Snapshot { get; }
    event EventHandler<PlaybackSnapshot>? SnapshotChanged;
    void ApplySettings(AppSettings settings);
    Task PlayAsync(RadioStation station, double volume, CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task SetVolumeAsync(double volume, CancellationToken cancellationToken);
}
