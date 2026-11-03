using GlassRadio.Application.Contracts;
using GlassRadio.Application.Abstractions;
using GlassRadio.Domain;
using GlassRadio.Infrastructure.Windows.Playback;
using Xunit;

namespace GlassRadio.Infrastructure.Tests;

public sealed class PlaybackEngineTests
{
    [Fact]
    public async Task RejectsStationsWithoutHttpOrHttpsCandidatesAndRemainsStopped()
    {
        await using var engine = new WindowsPlaybackEngine();
        var station = new RadioStation(
            "invalid", "Invalid", "UK", "ftp://example.test/stream", null, null, null,
            null, null, null, null, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PlayAsync(station, 0.7, CancellationToken.None));

        Assert.Equal(PlaybackState.Stopped, engine.Snapshot.State);
        Assert.Null(engine.Snapshot.Station);
    }

    [Fact]
    public async Task VolumeIsClampedAndStopIsIdempotent()
    {
        await using var engine = new WindowsPlaybackEngine();

        await engine.SetVolumeAsync(2, CancellationToken.None);
        Assert.Equal(1, engine.Snapshot.Volume);

        await engine.SetVolumeAsync(-1, CancellationToken.None);
        Assert.Equal(0, engine.Snapshot.Volume);

        await engine.StopAsync(CancellationToken.None);
        await engine.StopAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Stopped, engine.Snapshot.State);
    }

    [Fact]
    public async Task PreCancelledPlayDoesNotLeaveLoadingState()
    {
        await using var engine = new WindowsPlaybackEngine();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var station = new RadioStation(
            "cancelled", "Cancelled", "UK", "https://example.test/stream", null, null, null,
            null, null, null, null, null, null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.PlayAsync(station, 0.7, cancellation.Token));

        Assert.Equal(PlaybackState.Stopped, engine.Snapshot.State);
        Assert.Null(engine.Snapshot.Station);
    }

    [Fact]
    public async Task PauseWhileStoppedPublishesNoFalsePlayingState()
    {
        await using var engine = new WindowsPlaybackEngine();

        await engine.PauseAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Stopped, engine.Snapshot.State);
        Assert.Null(engine.Snapshot.Station);
    }

    [Fact]
    public async Task UnreachableHttpsCandidateFailsAndClearsSession()
    {
        await using var engine = new WindowsPlaybackEngine();
        engine.ApplySettings(new AppSettings(ConnectionTimeoutSeconds: 6));
        var station = new RadioStation(
            "unreachable", "Unreachable", "UK", "https://127.0.0.1:1/stream", null, null, null,
            null, null, null, null, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PlayAsync(station, 0.7, CancellationToken.None));

        Assert.Equal(PlaybackState.Failed, engine.Snapshot.State);
        Assert.Equal("unreachable", engine.Snapshot.Station?.Id);
        Assert.NotNull(engine.Snapshot.Error);
    }
}
