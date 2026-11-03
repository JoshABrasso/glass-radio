using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;
using GlassRadio.Domain;

namespace GlassRadio.Infrastructure.Windows.Playback;

// The engine the rest of the app actually depends on (constructed once in App.xaml.cs). It is a thin
// facade, not real playback logic of its own: it keeps BOTH LibVlcPlaybackEngine (the primary engine
// - see that file for why) and WinRtPlaybackEngine alive simultaneously, rather than choosing one at
// construction time and discarding the other. Per the user's explicit direction ("have the two
// engines active with the libvlc being the main but the app is smart enough not to depend on it and
// if it does go down the app can revert to the native paths"):
//  - If libVLC fails to construct at all (native binaries missing/corrupt on this machine), every
//    PlayAsync call goes straight to WinRT - the same outcome as the earlier construction-time-only
//    fallback, just expressed through the same per-call path as the case below instead of a separate
//    code path.
//  - If libVLC constructs fine but fails to open a *particular* station (every candidate exhausted -
//    the exact class of failure this engine replaced libVLC to fix, e.g. the WinRT
//    AdaptiveMediaSource/HLS limitation, still applies in reverse: some station could plausibly play
//    better on WinRT than libVLC), that same PlayAsync call is retried against WinRtPlaybackEngine
//    before surfacing a failure to the user. This is deliberately per-call, not a persistent
//    "libVLC is down, switch everything over" mode switch - if libVLC is behaving fine for 99% of
//    stations and only fails on one, only that one call pays the extra retry cost, and the next
//    PlayAsync still tries libVLC first. If libVLC is systematically broken, every single call falls
//    back the same way, which amounts to full reversion without needing separate state-tracking logic
//    to detect "libVLC is down" as a distinct condition.
// What this does NOT cover: a station that opens fine on libVLC and then fails deep into an already-
// playing session (libVLC's own internal candidate-recovery loop, inside LibVlcPlaybackEngine, keeps
// retrying candidates on that same engine indefinitely rather than ever handing back control here) -
// mid-session engine-switching is a further possible enhancement, not implemented.
public sealed class WindowsPlaybackEngine : IPlaybackEngine
{
    private readonly LibVlcPlaybackEngine? _libVlc;
    private readonly WinRtPlaybackEngine _winRt;
    private IPlaybackEngine _nowPlayingOn;
    private bool _hasPlayAttempt;

    // TEMP DIAGNOSTIC (2026-10-03): static mirror of EngineName/FallbackReason, so the Local API can
    // report which engine actually served the last PlayAsync call without threading it through
    // ILocalApiTarget. Only one WindowsPlaybackEngine is ever constructed per process, so "static" is
    // safe here. Remove once the libVLC path is confirmed stable and this is no longer needed for
    // live debugging.
    public static string? LastEngineName { get; private set; }
    public static string? LastFallbackReason { get; private set; }

    public WindowsPlaybackEngine()
    {
        _winRt = new WinRtPlaybackEngine();
        _nowPlayingOn = _winRt;

        try
        {
            _libVlc = new LibVlcPlaybackEngine();
        }
        catch (Exception ex)
        {
            // Swallowing this is deliberate: a machine where libVLC's native binaries are
            // missing/corrupt/blocked should still get a working radio app via WinRT, not a crash on
            // launch. The reason is kept on FallbackReason for callers that want to surface or log
            // it, rather than discarded.
            FallbackReason = $"{ex.GetType().Name}: {ex.Message}";
        }

        LastFallbackReason = FallbackReason;
        UpdateEngineName();

        // Only forward snapshots from whichever engine is actually serving the current station - the
        // other one sits idle (never given a Source/Media) and should never be allowed to overwrite
        // the UI with a stray Stopped snapshot of its own.
        if (_libVlc is not null)
        {
            var libVlc = _libVlc;
            libVlc.SnapshotChanged += (_, snapshot) => { if (ReferenceEquals(_nowPlayingOn, libVlc)) SnapshotChanged?.Invoke(this, snapshot); };
        }
        _winRt.SnapshotChanged += (_, snapshot) => { if (ReferenceEquals(_nowPlayingOn, _winRt)) SnapshotChanged?.Invoke(this, snapshot); };
    }

    /// Which engine actually served the most recent PlayAsync call - "libVLC" normally, "WinRT
    /// (libVLC unavailable)" if libVLC never constructed on this machine, "WinRT (libVLC failed to
    /// open this station)" if libVLC is working in general but this specific station fell back. An
    /// idle instance is reported as "Idle (...)" so diagnostics do not imply a failed station before
    /// the first PlayAsync call. Not part of IPlaybackEngine; purely for diagnostics (e.g. surfacing
    /// via the Local API or a log line).
    public string EngineName { get; private set; } = "Idle";

    /// Set only when libVLC failed to construct at all and every call is going to WinRT - the
    /// exception that caused that, so it isn't silently lost. Does not get set for the per-station
    /// fallback case (that is expected, routine behaviour, not an error worth surfacing the same way).
    public string? FallbackReason { get; }

    public PlaybackSnapshot Snapshot => _nowPlayingOn.Snapshot;
    public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

    public void ApplySettings(AppSettings settings)
    {
        _libVlc?.ApplySettings(settings);
        _winRt.ApplySettings(settings);
    }

    public async Task PlayAsync(RadioStation station, double volume, CancellationToken cancellationToken)
    {
        if (_libVlc is not null)
        {
            _hasPlayAttempt = true;
            // Select the candidate engine before awaiting it. LibVLC publishes Loading and Playing
            // from inside PlayAsync, and the facade must forward those snapshots to the UI while the
            // stream is opening. The previous ordering switched _nowPlayingOn only after the await,
            // so every startup/recovery snapshot was discarded as if it came from the idle engine.
            _nowPlayingOn = _libVlc;
            try
            {
                await _libVlc.PlayAsync(station, volume, cancellationToken).ConfigureAwait(false);
                UpdateEngineName();
                // Make sure the idle engine isn't left holding a stale station from a previous call.
                await _winRt.StopAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // libVLC exhausted every candidate for this station - select WinRT before cleaning up
                // so the failed engine's terminal Stopped snapshot cannot overwrite the fallback UI.
                // The old ordering left libVLC alive until *after* WinRT had started, which could
                // leave a failed MediaPlayer emitting stale events or briefly overlap the fallback
                // output while the second engine opened the same station.
                _nowPlayingOn = _winRt;
                UpdateEngineName();
                try
                {
                    await _libVlc.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Fallback remains the recovery path even if native cleanup itself reports an
                    // error; the next WinRT PlayAsync call is still the authoritative attempt.
                }
            }
        }

        if (_libVlc is null) _hasPlayAttempt = true;

        // Select WinRT before awaiting it for the same reason: its Loading/Playing snapshots are
        // part of the startup contract and must not be filtered as idle-engine events.
        _nowPlayingOn = _winRt;
        try
        {
            await _winRt.PlayAsync(station, volume, cancellationToken).ConfigureAwait(false);
            UpdateEngineName();
        }
        catch
        {
            // Keep the diagnostic endpoint truthful when both engines reject a station. The
            // snapshot carries the actual failure; this name identifies the final path used.
            UpdateEngineName();
            throw;
        }
        if (_libVlc is not null) await _libVlc.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public Task PauseAsync(CancellationToken cancellationToken) => _nowPlayingOn.PauseAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop both, not just the one believed active - cheap, and guarantees no stale state lingers
        // in the idle engine regardless of how _nowPlayingOn got here.
        if (_libVlc is not null) await _libVlc.StopAsync(cancellationToken).ConfigureAwait(false);
        await _winRt.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        // Keep both engines in sync.  The facade starts with WinRT selected, but LibVLC
        // still owns the volume it will use when the first station is opened.  Updating only
        // _nowPlayingOn meant the saved slider value was visible in the UI but was not applied
        // to the primary engine until the user touched the slider or playback started.
        if (_libVlc is not null)
        {
            await _libVlc.SetVolumeAsync(volume, cancellationToken).ConfigureAwait(false);
        }

        await _winRt.SetVolumeAsync(volume, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_libVlc is not null) await _libVlc.DisposeAsync().ConfigureAwait(false);
        await _winRt.DisposeAsync().ConfigureAwait(false);
    }

    private void UpdateEngineName()
    {
        EngineName = !_hasPlayAttempt
            ? _libVlc is null ? "Idle (libVLC unavailable)" : "Idle (libVLC ready)"
            : ReferenceEquals(_nowPlayingOn, _libVlc)
                ? "libVLC"
                : _libVlc is null ? "WinRT (libVLC unavailable)" : "WinRT (libVLC failed to open this station)";
        LastEngineName = EngineName;
    }
}
