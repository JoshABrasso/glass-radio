using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;
using GlassRadio.Domain;
using LibVLCSharp.Shared;
using Windows.Media;

namespace GlassRadio.Infrastructure.Windows.Playback;

// Built on libVLC (LibVLCSharp), not WinRT's Windows.Media.Playback.MediaPlayer. This is the primary
// playback engine as of 2026-10-03, replacing WinRtPlaybackEngine (kept on as a fallback - see
// WindowsPlaybackEngine.cs) after an extensive, evidence-based investigation (see
// truth/windows-build-plan.md's 2026-10-02/03 entries) found that Windows Media Foundation's
// AdaptiveMediaSource cannot open single-rendition live audio HLS streams - which is what most BBC
// stations serve - and that this is very likely a genuine WinRT platform limitation, not a fixable
// usage error. VLC itself solves exactly this problem with its own self-contained, cross-platform
// HLS demuxer that never depends on the OS media stack at all; libVLC is that same engine, used here
// via its official .NET bindings instead of reimplementing a worse version of it from scratch.
public sealed class LibVlcPlaybackEngine : IPlaybackEngine
{
    private static readonly TimeSpan RecoveryBackoff = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan MaximumRecoveryBackoff = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(5);
    // 8s was right for the old WinRT engine, which had no self-healing of its own - any stall
    // genuinely meant "this candidate is dead, move on." libVLC is different: it has its own
    // internal reconnect/resume handling for exactly the kind of transient network jitter that
    // causes a brief buffering episode, and that's most of what a live Icecast/HLS stream produces
    // day to day. A real listening session confirmed 8s was too impatient here - this engine's own
    // candidate-switch recovery kept firing mid-stall, cutting off libVLC's own recovery of the SAME
    // connection and jumping to a different stream URL instead (audible as "switching feeds, out of
    // sync"). 25s gives libVLC real room to recover the current candidate on its own before this
    // engine steps in as a last resort.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(25);

    private readonly object _gate = new();
    private readonly LibVLC _libVlc;
    // A single MediaPlayer, reused for the app's lifetime via Media swaps rather than recreated per
    // attempt. The previous WinRT engine constructed and disposed a new MediaPlayer/
    // SystemMediaTransportControls on every single candidate/recovery attempt - flagged in this
    // project's own truth log as a plausible source of instability in its own right, and something
    // the Mac source (AudioPlayerManager.swift) deliberately avoids by keeping one AVPlayer for the
    // app's lifetime. libVLC's MediaPlayer is designed to be reused this way, so this rewrite adopts
    // that pattern - which also means event handlers are wired ONCE here, not per attempt; every
    // handler below reads the current attempt's state from `_attempt` rather than closing over
    // per-call locals, so nothing needs subscribing/unsubscribing as stations change.
    private readonly MediaPlayer _player;
    // Volume is a session setting, not a property that only exists after VLC has
    // created its audio output.  Retain it so startup settings are honoured by the
    // first media object and every later reconnect.
    private double _requestedVolume = 1;
    private readonly SystemMediaTransportControls _transport;
    // A real WinRT MediaPlayer, never given a Source and never played - kept alive purely so its
    // .SystemMediaTransportControls property can be harvested below. This is a documented, known
    // WinUI3 limitation, not a guess: SystemMediaTransportControls.GetForCurrentView() throws
    // COMException 0x80070578 ("Invalid window handle") in every WinUI3 desktop app, packaged or
    // not - confirmed both empirically here and against a filed, still-open Microsoft issue
    // (microsoft/WindowsAppSDK#6740). The only working way to obtain a real SystemMediaTransportControls
    // in WinUI3 is via an actual MediaPlayer instance's own property, which is how the WinRT fallback
    // engine (WinRtPlaybackEngine) has reliably gotten one all along.
    private readonly global::Windows.Media.Playback.MediaPlayer _smtcAnchor;
    private Media? _currentMedia;
    private string? _lastErrorLogLine;
    private AttemptState? _attempt;
    private CancellationTokenSource? _sessionCancellation;
    private long _sessionId;
    private TimeSpan _connectionTimeout = TimeSpan.FromSeconds(12);
    private bool _preferLowerBitrate;
    private bool _userPaused;
    private bool _intentionalStop;
    private PlaybackSnapshot _snapshot = new(PlaybackState.Stopped, null, null, 0.72, null, false);

    // TEMP DIAGNOSTIC (2026-10-03): ring buffer of recent libVLC log lines (any level, not just
    // Error), for live debugging of the first real playback attempt. Remove once confirmed working.
    public static readonly System.Collections.Concurrent.ConcurrentQueue<string> DiagnosticLog = new();

    public LibVlcPlaybackEngine()
    {
        Core.Initialize();
        // Keep the last known stable libVLC configuration while the BBC Radio 1 Anthems regression
        // is isolated. The later 15-second/live/clock-jitter experiment did not stop the observed
        // PCR resyncs and is not allowed to remain the default without a controlled soak result.
        _libVlc = new LibVLC(enableDebugLogs: true, "--network-caching=8000");
        _libVlc.Log += OnLibVlcLog;

        _player = new MediaPlayer(_libVlc);
        _player.Playing += OnPlaying;
        _player.Buffering += OnBuffering;
        _player.TimeChanged += OnTimeChanged;
        _player.EncounteredError += OnEncounteredError;
        _player.EndReached += OnEndReached;
        _player.Stopped += OnStopped;

        _smtcAnchor = new global::Windows.Media.Playback.MediaPlayer();
        _transport = _smtcAnchor.SystemMediaTransportControls;
        _transport.IsEnabled = true;
        _transport.IsPlayEnabled = true;
        _transport.IsPauseEnabled = true;
        _transport.IsStopEnabled = true;
        _transport.ButtonPressed += OnTransportButtonPressed;
    }

    public PlaybackSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        _connectionTimeout = TimeSpan.FromSeconds(normalized.ConnectionTimeoutSeconds);
        _preferLowerBitrate = normalized.PreferLowerBitrate;
    }

    public async Task PlayAsync(RadioStation station, double volume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The ViewModel passes the authoritative session volume into every play request.  Keep it
        // in the engine before opening the first Media object as well as when the slider changes:
        // startup can race catalogue/window initialisation, and relying only on the earlier
        // SetVolumeAsync call left the visible slider at the saved value while the first audio
        // output still used LibVLC's default gain until the user touched the control.
        var requestedVolume = Math.Clamp(volume, 0, 1);
        lock (_gate)
        {
            _requestedVolume = requestedVolume;
            _player.Volume = ToLibVlcGain(requestedVolume);
            LastAppliedGain = ToLibVlcGain(requestedVolume);
        }
        var candidates = BuildStreamCandidates(station);
        if (candidates.Count == 0) throw new InvalidOperationException("The station has no valid stream URL.");

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var session = Interlocked.Increment(ref _sessionId);
        Interlocked.Exchange(ref _sessionCancellation, linked);
        Publish(new PlaybackSnapshot(PlaybackState.Loading, station, null, requestedVolume, station.Bitrate, false));

        Exception? lastCandidateError = null;
        try
        {
            for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                linked.Token.ThrowIfCancellationRequested();
                try
                {
                    await StartCandidateAsync(candidates, candidateIndex, station, session, linked.Token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch (Exception candidateError) { lastCandidateError = candidateError; /* Try the next known-good candidate. */ }
            }

            var detail = lastCandidateError?.Message;
            var message = string.IsNullOrWhiteSpace(detail)
                ? "The station could not be started."
                : $"The station could not be started: {detail}";
            Publish(new PlaybackSnapshot(PlaybackState.Failed, station, message, Snapshot.Volume, station.Bitrate, false));
            DisposeSessionIfCurrent(linked);
            throw new InvalidOperationException("All station stream candidates failed.");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            var isCurrent = session == Interlocked.Read(ref _sessionId) && ReferenceEquals(_sessionCancellation, linked);
            if (isCurrent)
            {
                DisposeSessionIfCurrent(linked);
                Publish(new PlaybackSnapshot(PlaybackState.Stopped, null, null, Snapshot.Volume, null, false));
            }
            throw;
        }
    }

    public Task PauseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaPlayer player;
        lock (_gate)
        {
            _userPaused = true;
            player = _player;
        }
        // Native pause may synchronously raise a state event; do not hold _gate across it because
        // the event handlers read the same state.
        player.Pause();
        if (Snapshot.State is PlaybackState.Playing or PlaybackState.Loading)
            Publish(Snapshot with { State = PlaybackState.Paused });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _sessionId);
        CancelCurrentSession();
        AttemptState? attemptToCancel;
        Media? mediaToDispose;
        lock (_gate)
        {
            _userPaused = false;
            _intentionalStop = true;
            attemptToCancel = _attempt;
            _attempt = null;
            mediaToDispose = _currentMedia;
            _currentMedia = null;
        }
        CancelStallWatchdog(attemptToCancel);
        CancelHealthWatchdog(attemptToCancel);
        // Stop outside the state lock: libVLC may synchronously raise Stopped while this call is
        // returning, and OnStopped also reads the same lock. Holding it across native teardown can
        // deadlock the playback/recovery path.
        _player.Stop();
        mediaToDispose?.Dispose();
        Publish(new PlaybackSnapshot(PlaybackState.Stopped, null, null, Snapshot.Volume, null, false));
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clamped = Math.Clamp(volume, 0, 1);
        var gain = ToLibVlcGain(clamped);
        lock (_gate)
        {
            _requestedVolume = clamped;
            _player.Volume = gain;
        }
        LastAppliedGain = gain;
        Publish(Snapshot with { Volume = clamped });
        return Task.CompletedTask;
    }

    // Plain linear mapping - slider 0.0-1.0 straight onto libVLC's 0-100 gain range, no curve.
    //
    // History, kept here rather than deleted so the next person doesn't retry the same thing: two
    // different perceptual tapers were tried this session (cubic, then a -30dB-floor dB taper) to
    // compensate for libVLC's MediaPlayer.Volume being raw linear digital gain with no OS-level
    // curve on top of it (unlike WinRtPlaybackEngine's Windows.Media.Playback.MediaPlayer.Volume,
    // which Windows' own session mixer renders through its own curve regardless of the linear input
    // value). Both tapers made real, measured parts of the slider's range noticeably quieter than
    // before any taper existed - at points so quiet the user reported genuine silence at a slider
    // position that should have been loud - without the user confirming either one actually sounded
    // better overall. Per explicit instruction ("you messed with the curve and never put it back"),
    // reverted to plain linear rather than attempting a third guessed curve. If a real perceptual
    // mismatch between the two engines is confirmed again later, fix it with a curve whose shape is
    // validated against the user's own ears at specific slider positions before shipping it, not
    // picked from theory alone.
    private static int ToLibVlcGain(double linearVolume) => (int)Math.Round(Math.Clamp(linearVolume, 0, 1) * 100);

    // TEMP DIAGNOSTIC (2026-10-03): static mirror (same pattern/reasoning as LastEngineName above -
    // exactly one LibVlcPlaybackEngine per process) of the actual int 0-100 gain last applied to
    // libVLC, exposed via /diag/volume. Useful for confirming the gain genuinely tracks the slider
    // 1:1 (it should, now that ToLibVlcGain is plain linear) and for catching any future regression
    // of the stale-volume-on-reconnect bug class documented on StartCandidateAsync below. Remove once
    // this project's volume handling has gone a good while without needing live diagnosis.
    public static int LastAppliedGain { get; private set; }

    public ValueTask DisposeAsync()
    {
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _player.Playing -= OnPlaying;
        _player.Buffering -= OnBuffering;
        _player.TimeChanged -= OnTimeChanged;
        _player.EncounteredError -= OnEncounteredError;
        _player.EndReached -= OnEndReached;
        _player.Stopped -= OnStopped;
        _transport.ButtonPressed -= OnTransportButtonPressed;
        _libVlc.Log -= OnLibVlcLog;
        _player.Dispose();
        _libVlc.Dispose();
        _smtcAnchor.Dispose();
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<string> BuildStreamCandidates(RadioStation station)
    {
        if (!_preferLowerBitrate || station.Variants.Count < 2) return station.AllStreamCandidates;

        return station.Variants
            .OrderBy(static variant => variant.Bitrate is > 0 ? variant.Bitrate.Value : int.MaxValue)
            .SelectMany(static variant => variant.StreamCandidates)
            .Concat(station.StreamCandidates)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void OnLibVlcLog(object? sender, LogEventArgs e)
    {
        // libVLC's public API rarely surfaces a structured error object (unlike WinRT's
        // MediaPlayerFailedEventArgs.ErrorMessage) - its native logs are the only place a real
        // failure reason shows up. Keep the last error-level line so a failure can be reported with
        // an actual reason instead of a bare "the stream failed."
        if (e.Level == LogLevel.Error) _lastErrorLogLine = e.Message;

        DiagnosticLog.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {e.Level} [{e.Module}] {e.Message}");
        while (DiagnosticLog.Count > 3000 && DiagnosticLog.TryDequeue(out _)) { }
    }

    private void CancelCurrentSession()
    {
        var cancellation = Interlocked.Exchange(ref _sessionCancellation, null);
        if (cancellation is null) return;
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        cancellation.Dispose();
    }

    private bool DisposeSessionIfCurrent(CancellationTokenSource session)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _sessionCancellation, null, session), session))
            return false;
        session.Dispose();
        return true;
    }

    // Deliberately takes no `volume` parameter - a prior version threaded the volume the user had
    // selected at the moment PlayAsync was first called all the way through every later candidate
    // switch and stall-recovery retry via AttemptState.Volume, an immutable value captured once and
    // never updated. Every single reconnect (and these streams reconnect often - see the PCR/clock-
    // drift entry above) silently reapplied that stale value to the player, invisibly overwriting
    // any volume change the user had made since the station started playing: the slider still showed
    // the new position (the ViewModel never reads volume back out of a snapshot), but the actual
    // audible gain would snap back to whatever it was minutes earlier - a real, confirmed bug,
    // reported directly by the user as "a real regressed mess" and traced here by reading every
    // volume-related call site end to end, not guessed. `Snapshot.Volume` is the one place that's
    // always current (every SetVolumeAsync call updates it immediately) - reading it fresh here
    // means every reconnect reapplies whatever the user most recently chose, not a stale snapshot.
    private async Task StartCandidateAsync(IReadOnlyList<string> candidates, int candidateIndex, RadioStation station, long session, CancellationToken cancellationToken)
    {
        var url = candidates[candidateIndex];
        var attempt = new AttemptState(station, candidates, candidateIndex, session, cancellationToken)
        {
            Opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        _lastErrorLogLine = null;
        var media = new Media(_libVlc, new Uri(url));
        lock (_gate)
        {
            _intentionalStop = false;
            _currentMedia?.Dispose();
            _currentMedia = media;
            _attempt = attempt;
            var initialGain = ToLibVlcGain(_requestedVolume);
            _player.Volume = initialGain;
            LastAppliedGain = initialGain;
        }

        _player.Play(media);

        var opened = attempt.Opened;
        using var registration = cancellationToken.Register(() => opened.TrySetCanceled(cancellationToken));
        using var timeout = new CancellationTokenSource(_connectionTimeout);
        using var timeoutRegistration = timeout.Token.Register(() => opened.TrySetException(new TimeoutException("The stream did not open in time.")));
        try
        {
            await opened.Task.ConfigureAwait(false);
            if (session != Interlocked.Read(ref _sessionId)) throw new OperationCanceledException(cancellationToken);
        }
        catch
        {
            StopAttemptMedia(attempt);
            CancelStallWatchdog(attempt);
            CancelHealthWatchdog(attempt);
            throw;
        }
    }

    // Everything below reacts to the shared, reused MediaPlayer's events by reading `_attempt` - the
    // currently in-flight or currently-playing candidate's state - rather than per-call closures.
    // `_attempt` is set at the start of StartCandidateAsync and cleared whenever that attempt ends
    // (success handoff to steady-state playback keeps it set so Buffering/EncounteredError/Stopped
    // during ongoing playback still resolve to the right candidate for recovery; StopAsync and a
    // terminal failure clear it).

    private void OnPlaying(object? sender, EventArgs e)
    {
        var attempt = CurrentAttempt();
        if (attempt is null) return;
        // LibVLC can initialise the audio output when Play() transitions to Playing and
        // overwrite the value that was set while the Media object was being prepared. Reapply
        // the retained request at the point the output is real, otherwise the slider can show the
        // saved level while the first audible frames still use LibVLC's default gain until the
        // user touches the control.
        lock (_gate)
        {
            var gain = ToLibVlcGain(_requestedVolume);
            _player.Volume = gain;
            LastAppliedGain = gain;
        }
        attempt.HasOpened = true;
        CancelStallWatchdog(attempt);
        StartHealthWatchdog(attempt);
        var state = IsUserPaused() ? PlaybackState.Paused : PlaybackState.Playing;
        Publish(new PlaybackSnapshot(state, attempt.Station, null, Snapshot.Volume, attempt.Station.Bitrate, false));
        attempt.Opened?.TrySetResult(true);
    }

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs e)
    {
        var attempt = CurrentAttempt();
        if (attempt is null) return;
        var progress = Math.Clamp(e.Cache / 100.0, 0, 1);

        if (e.Cache >= 100)
        {
            // libVLC has no distinct "buffering ended, resumed" event the way WinRT's
            // BufferingEnded did - a full cache is the closest equivalent signal that this
            // buffering episode is over and real playback has resumed. Without this, every prior
            // version of this handler left the published state stuck on Loading indefinitely
            // after the first buffering tick, even while audio was genuinely still playing fine
            // underneath (confirmed: a real listening session stayed in Loading for a full 2
            // minutes of actually-fine playback).
            CancelStallWatchdog(attempt);
            if (!IsUserPaused() && attempt.HasOpened)
                Publish(Snapshot with { State = PlaybackState.Playing, BufferingProgress = progress });
            return;
        }

        if (!IsUserPaused())
            Publish(Snapshot with { State = PlaybackState.Loading, BufferingProgress = progress });

        // Only arm a stall watchdog if one isn't already running for this buffering episode.
        // libVLC reports buffering progress repeatedly while filling (0%, 12%, 45%, ...) - the
        // previous version reset an 8s countdown on every single one of those updates, which did
        // two things wrong at once: a stream that was continuously-but-slowly buffering (normal
        // live-stream network jitter, which libVLC's own network-caching buffer is specifically
        // meant to absorb) never actually tripped the watchdog, while any buffering happening at
        // all kept re-arming it, overlapping with libVLC's own internal reconnect/resume logic
        // for the SAME candidate. A real listening session confirmed the result: audible
        // "switching feeds, out of sync" - this engine's own candidate-switch recovery was firing
        // mid-stall, before libVLC had a real chance to quietly recover the same connection on its
        // own, so playback jumped to a different stream URL (a genuinely different live position)
        // instead of just continuing. One watchdog per genuine stall episode, not per tick, fixes
        // both: it actually catches a stream that's frozen with zero progress, and it stops
        // pre-empting libVLC's own recovery for one that's merely catching up.
        if (attempt.StallCancellation is null)
        {
            var stallCts = new CancellationTokenSource();
            attempt.StallCancellation = stallCts;
            _ = FailAfterSustainedBufferingAsync(attempt, stallCts.Token);
        }
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        var attempt = CurrentAttempt();
        if (attempt is null || !attempt.HasOpened) return;

        // TimeChanged is the strongest signal available from libVLC that the live media is
        // actually advancing. Do not use it until it has changed at least once: some valid
        // live streams expose a permanent zero time, and those streams must continue using
        // the existing buffering/error/stop recovery paths rather than being falsely reset.
        if (!attempt.HasTimeProgress || attempt.LastMediaTime != e.Time)
        {
            attempt.HasTimeProgress = true;
            attempt.LastMediaTime = e.Time;
            attempt.LastTimeProgressAt = DateTimeOffset.UtcNow;
        }
    }

    private void StartHealthWatchdog(AttemptState attempt)
    {
        if (attempt.HealthCancellation is not null) return;
        var cancellation = new CancellationTokenSource();
        attempt.HealthCancellation = cancellation;
        _ = MonitorPlaybackHealthAsync(attempt, cancellation);
    }

    private void CancelHealthWatchdog(AttemptState? attempt)
    {
        if (attempt is null) return;
        var cancellation = attempt.HealthCancellation;
        attempt.HealthCancellation = null;
        CancelAndDispose(cancellation);
    }

    private async Task MonitorPlaybackHealthAsync(AttemptState attempt, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(HealthCheckInterval, cancellation.Token).ConfigureAwait(false);
                if (cancellation.IsCancellationRequested || !ReferenceEquals(CurrentAttempt(), attempt)) return;
                if (IsUserPaused()) continue;

                if (attempt.HasTimeProgress && DateTimeOffset.UtcNow - attempt.LastTimeProgressAt >= StallTimeout)
                {
                    await RecoverAfterOpenFailureAsync(attempt, "The station stream stopped advancing.").ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(attempt.HealthCancellation, cancellation))
                attempt.HealthCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelStallWatchdog(AttemptState? attempt)
    {
        if (attempt is null) return;
        var cancellation = attempt.StallCancellation;
        attempt.StallCancellation = null;
        CancelAndDispose(cancellation);
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return;
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        cancellation.Dispose();
    }

    private async Task FailAfterSustainedBufferingAsync(AttemptState attempt, CancellationToken stallToken)
    {
        try
        {
            await Task.Delay(StallTimeout, stallToken).ConfigureAwait(false);
            if (stallToken.IsCancellationRequested || !ReferenceEquals(CurrentAttempt(), attempt)) return;
            await RecoverAfterOpenFailureAsync(attempt, "The station stream stalled.").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stallToken.IsCancellationRequested) { }
        finally
        {
            // Only clear it if it's still THIS watchdog's token - a newer buffering episode may
            // already have armed a different one by the time this runs.
            if (attempt.StallCancellation?.Token == stallToken) attempt.StallCancellation = null;
        }
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        var attempt = CurrentAttempt();
        if (attempt is null) return;
        var error = new InvalidOperationException(string.IsNullOrWhiteSpace(_lastErrorLogLine)
            ? "The station stream failed."
            : _lastErrorLogLine);
        if (attempt.Opened is { } opened && opened.TrySetException(error)) return;
        _ = RecoverAfterOpenFailureAsync(attempt, error.Message);
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        var attempt = CurrentAttempt();
        if (attempt is null) return;
        if (attempt.HasOpened)
        {
            // A live station reaching "end" after it opened is an unexpected stream termination,
            // not a real end-of-media - treat it the same as a post-open failure.
            _ = RecoverAfterOpenFailureAsync(attempt, "The station stream ended unexpectedly.");
            return;
        }
        attempt.Opened?.TrySetException(new InvalidOperationException("The station stream ended before opening."));
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        lock (_gate) { if (_intentionalStop) return; }
        var attempt = CurrentAttempt();
        // libVLC can stop on its own for some dropped connections without EncounteredError or
        // EndReached firing first. Treat it the same as the old WinRT engine's "stream stopped
        // unexpectedly" case rather than silently going quiet.
        if (attempt is null || !attempt.HasOpened) return;
        _ = RecoverAfterOpenFailureAsync(attempt, "The station stream stopped unexpectedly.");
    }

    private async Task RecoverAfterOpenFailureAsync(AttemptState attempt, string reason)
    {
        if (Interlocked.Exchange(ref attempt.RecoveryStarted, 1) != 0
            || attempt.CancellationToken.IsCancellationRequested
            || attempt.Session != Interlocked.Read(ref _sessionId))
            return;

        StopAttemptMedia(attempt);
        CancelStallWatchdog(attempt);
        // The health watchdog can finish and dispose its CTS concurrently with a native
        // EncounteredError/EndReached/Stopped callback. Use the same disposal-safe boundary as
        // explicit Stop; a direct Cancel here could throw ObjectDisposedException out of this
        // fire-and-forget recovery task and leave the station silent with no retry loop.
        CancelHealthWatchdog(attempt);
        Publish(new PlaybackSnapshot(PlaybackState.Loading, attempt.Station, reason, Snapshot.Volume, attempt.Station.Bitrate, false));

        try
        {
            // Give libVLC a short opportunity to release the failed media before creating the next
            // one. This avoids treating a transient source teardown as a permanent failure.
            await Task.Delay(RecoveryBackoff, attempt.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (attempt.CancellationToken.IsCancellationRequested) { return; }

        // A live station can fail after opening even when the URL is still valid. Cycle every
        // retained candidate and keep retrying while the user session remains active.
        var recoveryAttempt = 0;
        while (!attempt.CancellationToken.IsCancellationRequested)
        {
            for (var offset = 1; offset <= attempt.Candidates.Count; offset++)
            {
                attempt.CancellationToken.ThrowIfCancellationRequested();
                var nextIndex = (attempt.CandidateIndex + offset) % attempt.Candidates.Count;
                try
                {
                    await StartCandidateAsync(attempt.Candidates, nextIndex, attempt.Station, attempt.Session, attempt.CancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (attempt.CancellationToken.IsCancellationRequested) { return; }
                catch { /* Try the next retained candidate. */ }
            }

            recoveryAttempt++;
            var backoffMilliseconds = Math.Min(
                MaximumRecoveryBackoff.TotalMilliseconds,
                RecoveryBackoff.TotalMilliseconds * Math.Pow(2, Math.Min(recoveryAttempt - 1, 5)));
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(backoffMilliseconds), attempt.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (attempt.CancellationToken.IsCancellationRequested) { return; }

            Publish(new PlaybackSnapshot(PlaybackState.Loading, attempt.Station,
                "The station stream is reconnecting…", Snapshot.Volume, attempt.Station.Bitrate, false));
        }
    }

    private AttemptState? CurrentAttempt()
    {
        lock (_gate) return _attempt;
    }

    private void StopAttemptMedia(AttemptState attempt)
    {
        Media? mediaToDispose = null;
        var shouldStop = false;
        lock (_gate)
        {
            if (!ReferenceEquals(_attempt, attempt)) return;
            _attempt = null;
            _intentionalStop = true;
            mediaToDispose = _currentMedia;
            _currentMedia = null;
            shouldStop = true;
        }

        if (shouldStop) _player.Stop();
        mediaToDispose?.Dispose();
    }

    private bool IsUserPaused()
    {
        lock (_gate) return _userPaused;
    }

    private void OnTransportButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                lock (_gate) _userPaused = false;
                _player.Play();
                Publish(Snapshot with { State = PlaybackState.Playing });
                break;
            case SystemMediaTransportControlsButton.Pause:
                lock (_gate) _userPaused = true;
                _player.Pause();
                Publish(Snapshot with { State = PlaybackState.Paused });
                break;
            case SystemMediaTransportControlsButton.Stop:
                _ = StopAsync(CancellationToken.None);
                break;
        }
    }

    private void Publish(PlaybackSnapshot snapshot)
    {
        lock (_gate) _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    // Mutable state for one candidate-URL attempt, shared between StartCandidateAsync and the
    // class-level event handlers that react to it while it is the current `_attempt`.
    private sealed class AttemptState(RadioStation station, IReadOnlyList<string> candidates, int candidateIndex, long session, CancellationToken cancellationToken)
    {
        public RadioStation Station { get; } = station;
        public IReadOnlyList<string> Candidates { get; } = candidates;
        public int CandidateIndex { get; } = candidateIndex;
        public long Session { get; } = session;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public bool HasOpened { get; set; }
        public bool HasTimeProgress { get; set; }
        public long LastMediaTime { get; set; }
        public DateTimeOffset LastTimeProgressAt { get; set; } = DateTimeOffset.UtcNow;
        public int RecoveryStarted;
        public CancellationTokenSource? StallCancellation { get; set; }
        public CancellationTokenSource? HealthCancellation { get; set; }
        public TaskCompletionSource<bool>? Opened { get; set; }
    }
}
