using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;
using GlassRadio.Domain;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace GlassRadio.Infrastructure.Windows.Playback;

// Built on WinRT's Windows.Media.Playback.MediaPlayer. This was the app's only playback engine until
// 2026-10-03, when it was kept on as a fallback (see WindowsPlaybackEngine.cs) behind
// LibVlcPlaybackEngine, which replaced it as the primary engine after a thorough investigation found
// Windows Media Foundation's AdaptiveMediaSource cannot open single-rendition live audio HLS streams
// - what most BBC stations serve (full diagnostic trail: truth/windows-build-plan.md's 2026-10-02/03
// entries). This engine still works correctly for everything except that specific HLS case, and
// remains here in case libVLC's native binaries fail to load on a given machine.
public sealed class WinRtPlaybackEngine : IPlaybackEngine
{
    private static readonly TimeSpan RecoveryBackoff = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan MaximumRecoveryBackoff = TimeSpan.FromSeconds(10);
    private readonly object _gate = new();
    private MediaPlayer? _player;
    // Keep the requested gain even while no MediaPlayer exists.  Startup applies the
    // persisted value before a station is opened; the next real player must inherit it
    // instead of waiting for the slider to be touched.
    private double _requestedVolume = 1;
    private CancellationTokenSource? _sessionCancellation;
    private long _sessionId;
    private TimeSpan _connectionTimeout = TimeSpan.FromSeconds(12);
    private bool _preferLowerBitrate;
    private bool _userPaused;
    private PlaybackSnapshot _snapshot = new(PlaybackState.Stopped, null, null, 0.72, null, false);
    // Read the Mac source (AudioPlayerManager.swift) before changing this again: it has no
    // configurable stall grace period at all - it reacts to AVFoundation's own
    // AVPlayerItemPlaybackStalled notification immediately and abandons that URL for the next
    // fallback candidate, no waiting. This port's 30-90s grace period (3x the connect timeout) was
    // far too forgiving by comparison - a real network hiccup is over in a few seconds either way,
    // so anything still buffering well past that point is "this candidate is bad right now," not
    // "give it more time." Shortened to a flat, short window so a stuck candidate gets abandoned for
    // a fallback URL (now far more likely to exist - see StationRules.ConsolidateVariants) quickly
    // instead of leaving the user staring at "Loading" for the better part of a minute or two.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(8);

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
        var candidates = BuildStreamCandidates(station);
        if (candidates.Count == 0) throw new InvalidOperationException("The station has no valid stream URL.");

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var session = Interlocked.Increment(ref _sessionId);
        Interlocked.Exchange(ref _sessionCancellation, linked);
        Publish(new PlaybackSnapshot(PlaybackState.Loading, station, null, Math.Clamp(volume, 0, 1), station.Bitrate, false));

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

            // Surface the last candidate's real failure reason rather than a bare "could not be
            // started" - this was previously discarded entirely, making every failure-to-start
            // report indistinguishable regardless of cause (bad codec, timeout, HLS/manifest
            // error, etc.), which made real diagnosis impossible without re-instrumenting.
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
        MediaPlayer? player;
        lock (_gate)
        {
            _userPaused = true;
            player = _player;
        }
        // WinRT can raise playback-state callbacks from native pause. Keep those callbacks out of a
        // state-lock critical section, matching the LibVLC engine's teardown discipline.
        player?.Pause();
        if (Snapshot.State is PlaybackState.Playing or PlaybackState.Loading)
            Publish(Snapshot with { State = PlaybackState.Paused });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _sessionId);
        CancelCurrentSession();
        MediaPlayer? playerToDispose;
        lock (_gate)
        {
            _userPaused = false;
            playerToDispose = _player;
            _player = null;
        }
        if (playerToDispose is not null)
        {
            playerToDispose.SystemMediaTransportControls.ButtonPressed -= OnTransportButtonPressed;
            // Native teardown can raise state callbacks; _player is already detached and no lock is
            // held, so those callbacks are safely ignored by IsCurrent instead of deadlocking here.
            playerToDispose.Pause();
            playerToDispose.Source = null;
            playerToDispose.Dispose();
        }
        Publish(new PlaybackSnapshot(PlaybackState.Stopped, null, null, Snapshot.Volume, null, false));
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clamped = Math.Clamp(volume, 0, 1);
        lock (_gate)
        {
            _requestedVolume = clamped;
            if (_player is not null) _player.Volume = clamped;
        }
        Publish(Snapshot with { Volume = clamped });
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
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

    // A plain Uri MediaSource treats a live HLS manifest (.m3u8) as a one-shot download: it plays
    // the segments listed at open time and then stops once they run out, instead of re-polling the
    // live playlist for newly added segments the way a real HLS client must - confirmed as the cause
    // of real "plays ~15s then stops, and the restart does the same thing" reports against actual
    // BBC stations (e.g. BBC Radio 1 Anthems).
    //
    // `AdaptiveMediaSource` is WinRT's purpose-built HLS/DASH API and was tried THREE times this
    // session (2026-10-02), each with real diagnostics via AdaptiveMediaSource's own
    // DownloadRequested/DownloadCompleted/DownloadFailed events - not guesses, a real investigation
    // each time, and each one eliminated a specific plausible cause rather than just failing again:
    //  1. Raw `MediaSource.CreateFromAdaptiveMediaSource(...)` handed directly to `player.Source`:
    //     `AdaptiveMediaSource.CreateFromUriAsync` reports `Status=Success` and the adaptive source
    //     genuinely works at the network level - real segments download, and the live manifest is
    //     correctly re-polled for new ones (confirmed: re-fetched ~6s and ~9s after the initial
    //     batch, a textbook live-HLS refresh cadence). `MediaOpened`/`MediaFailed` never fired.
    //  2. Wrapped in `MediaPlaybackItem` instead (matches Microsoft's own official live-streaming
    //     sample exactly - Windows-universal-samples, Samples/AdaptiveStreaming/cs/
    //     Scenario7_LiveSeekableRange.xaml.cs - fetched and compared line by line). No change.
    //     `AvailableBitrates` for these BBC streams is `[0]` (a single-rendition HLS *media*
    //     playlist, not a multi-bitrate *master* playlist - normal for audio-only radio HLS, and
    //     confirmed this is NOT what breaks it: explicitly setting `InitialBitrate` made no
    //     difference).
    //  3. Explicit `DesiredLiveOffset = TimeSpan.FromSeconds(6)` plus a 90-second wait (not the
    //     normal 12-30s budget - `AppSettings.ConnectionTimeoutSeconds`'s clamp was temporarily
    //     raised to 120 to allow this one test, then restored to 30) to rule out "the player is just
    //     waiting to build a bigger live buffer than we gave it time for." Stuck at `Loading` for
    //     the full 90 seconds, then the same "did not open in time" timeout. Not a patience problem.
    // Checked for a known/filed bug: `microsoft/WindowsAppSDK#4095` documents a related but distinct
    // AdaptiveMediaSource/HLS failure (multi-track video, fails after a few seconds with a decoding
    // error, closed "not planned", no fix) - confirms AdaptiveMediaSource has real rough edges with
    // real-world HLS content, but is not the same symptom (ours never opens at all) and offers no
    // applicable workaround.
    // Conclusion: this is very likely a genuine WinRT/AdaptiveMediaSource limitation with
    // single-rendition live audio HLS specifically, not a fixable usage error in this codebase - three
    // independently-reasoned attempts (wrapper type, bitrate, live-offset timing) all eliminated,
    // each with real evidence, not reverted on a guess. Deliberately not used - shipping it would turn
    // "plays ~15s then stops" into "never plays at all" for every HLS station, a regression. A real
    // fix most likely needs a hand-rolled live-HLS client (periodically re-fetch the manifest, track
    // newly-added segments, feed them through a lower-level API such as `MediaStreamSource` that
    // doesn't go through AdaptiveMediaSource at all) rather than more tuning of this WinRT API -
    // a genuinely large undertaking, not attempted this session given everything else investigated.
    private static MediaSource CreateMediaSource(string url) => MediaSource.CreateFromUri(new Uri(url));

    // Deliberately takes no `volume` parameter - see the identical fix and full explanation in
    // LibVlcPlaybackEngine.StartCandidateAsync. The same bug existed here too (this engine
    // constructs a brand new MediaPlayer per candidate/recovery attempt, and was reapplying
    // whatever volume was captured when PlayAsync was first called on every single one of them) -
    // `Snapshot.Volume` is always current, so reading it fresh here means every reconnect reapplies
    // whatever the user most recently chose.
    private async Task StartCandidateAsync(IReadOnlyList<string> candidates, int candidateIndex, RadioStation station, long session, CancellationToken cancellationToken)
    {
        var url = candidates[candidateIndex];
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanedUp = false;
        var hasOpened = false;
        var recoveryStarted = 0;
        CancellationTokenSource? stallCancellation = null;
        SystemMediaTransportControls? transport = null;
        MediaPlaybackSession? playbackSession = null;
        MediaPlayer player = new();
        player.MediaOpened += OnMediaOpened;
        player.MediaFailed += OnMediaFailed;
        player.MediaEnded += OnMediaEnded;
        player.BufferingStarted += OnBufferingStarted;
        player.BufferingEnded += OnBufferingEnded;
        transport = player.SystemMediaTransportControls;
        transport.IsEnabled = true;
        transport.IsPlayEnabled = true;
        transport.IsPauseEnabled = true;
        transport.IsStopEnabled = true;
        transport.ButtonPressed += OnTransportButtonPressed;
        // This is an audio-only radio player. Explicitly selecting the media audio
        // category prevents Windows from treating the stream as a communications or
        // background sound session with platform-dependent routing/ducking behaviour.
        player.AudioCategory = MediaPlayerAudioCategory.Media;
        player.RealTimePlayback = false;
        player.IsMuted = false;
        lock (_gate) player.Volume = _requestedVolume;
        playbackSession = player.PlaybackSession;
        if (playbackSession is not null)
        {
            playbackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            playbackSession.BufferingProgressChanged += OnBufferingProgressChanged;
        }
        lock (_gate) _player = player;
        player.Source = CreateMediaSource(url);
        player.Play();

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
            CleanupPlayer();
            throw;
        }

        void OnMediaOpened(MediaPlayer openedPlayer, object __)
        {
            if (!IsCurrent(openedPlayer, session)) return;
            // MediaPlayer may materialise its audio output during Source/Play. Apply the retained
            // startup value once more at MediaOpened so the visible slider and audible gain agree
            // before any user interaction.
            lock (_gate) openedPlayer.Volume = _requestedVolume;
            hasOpened = true;
            var state = IsUserPaused() ? PlaybackState.Paused : PlaybackState.Playing;
            Publish(new PlaybackSnapshot(state, station, null, Snapshot.Volume, station.Bitrate, false, ReadBufferingProgress()));
            opened.TrySetResult(true);
        }

        void OnBufferingProgressChanged(MediaPlaybackSession changedSession, object __)
        {
            if (!IsCurrent(player, session)) return;
            Publish(Snapshot with { BufferingProgress = ReadBufferingProgress(changedSession) });
        }

        void OnPlaybackStateChanged(MediaPlaybackSession changedSession, object __)
        {
            if (!hasOpened || !IsCurrent(player, session) || IsUserPaused()) return;
            if (changedSession.PlaybackState != MediaPlaybackState.None) return;

            // Some live sources stop producing media without raising MediaFailed or
            // MediaEnded. Treat the terminal session state as a recoverable stream
            // failure, but never interrupt an explicit user pause.
            _ = RecoverAfterOpenFailureAsync("The station stream stopped unexpectedly.");
        }

        void OnBufferingStarted(MediaPlayer bufferingPlayer, object __)
        {
            if (!hasOpened || !IsCurrent(bufferingPlayer, session)) return;
            if (!IsUserPaused())
                Publish(Snapshot with { State = PlaybackState.Loading, BufferingProgress = ReadBufferingProgress() });
            stallCancellation?.Cancel();
            stallCancellation?.Dispose();
            stallCancellation = new CancellationTokenSource();
            _ = FailAfterSustainedBufferingAsync(bufferingPlayer, stallCancellation.Token);
        }

        void OnBufferingEnded(MediaPlayer bufferingPlayer, object __)
        {
            if (!IsCurrent(bufferingPlayer, session)) return;
            stallCancellation?.Cancel();
            if (!IsUserPaused() && Snapshot.State == PlaybackState.Loading)
                Publish(Snapshot with { State = PlaybackState.Playing, BufferingProgress = ReadBufferingProgress() });
        }

        async Task FailAfterSustainedBufferingAsync(MediaPlayer bufferingPlayer, CancellationToken stallToken)
        {
            try
            {
                await Task.Delay(StallTimeout, stallToken).ConfigureAwait(false);
                if (stallToken.IsCancellationRequested || !IsCurrent(bufferingPlayer, session) || bufferingPlayer.CurrentState != MediaPlayerState.Buffering)
                    return;

                await RecoverAfterOpenFailureAsync("The station stream stalled.").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stallToken.IsCancellationRequested) { }
        }

        void OnMediaFailed(MediaPlayer failedPlayer, MediaPlayerFailedEventArgs args)
        {
            if (!IsCurrent(failedPlayer, session)) return;

            var error = new InvalidOperationException(string.IsNullOrWhiteSpace(args.ErrorMessage)
                ? "The station stream failed."
                : args.ErrorMessage);
            if (!opened.TrySetException(error))
            {
                _ = RecoverAfterOpenFailureAsync(error.Message);
            }
        }

        void OnMediaEnded(MediaPlayer endedPlayer, object __)
        {
            if (!IsCurrent(endedPlayer, session)) return;
            if (hasOpened)
            {
                // A live station ending after it opened is an unexpected stream
                // termination, not a user stop. Reuse the same bounded candidate
                // recovery path as post-open failure and sustained buffering.
                _ = RecoverAfterOpenFailureAsync("The station stream ended unexpectedly.");
                return;
            }
            opened.TrySetException(new InvalidOperationException("The station stream ended before opening."));
            CleanupPlayer();
        }

        void CleanupPlayer()
        {
            if (cleanedUp) return;
            cleanedUp = true;
            lock (_gate)
            {
                if (ReferenceEquals(_player, player)) _player = null;
            }
            player.MediaOpened -= OnMediaOpened;
            player.MediaFailed -= OnMediaFailed;
            player.MediaEnded -= OnMediaEnded;
            player.BufferingStarted -= OnBufferingStarted;
            player.BufferingEnded -= OnBufferingEnded;
            if (playbackSession is not null)
            {
                playbackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
                playbackSession.BufferingProgressChanged -= OnBufferingProgressChanged;
            }
            if (transport is not null) transport.ButtonPressed -= OnTransportButtonPressed;
            stallCancellation?.Cancel();
            stallCancellation?.Dispose();
            stallCancellation = null;
            player.Pause();
            player.Source = null;
            player.Dispose();
        }

        double? ReadBufferingProgress(MediaPlaybackSession? source = null)
        {
            var progress = source?.BufferingProgress ?? playbackSession?.BufferingProgress;
            return progress is { } value && !double.IsNaN(value)
                ? Math.Clamp(value, 0, 1)
                : null;
        }

        async Task RecoverAfterOpenFailureAsync(string reason)
        {
            if (Interlocked.Exchange(ref recoveryStarted, 1) != 0 || cancellationToken.IsCancellationRequested || !IsCurrent(player, session)) return;

            CleanupPlayer();
            Publish(new PlaybackSnapshot(PlaybackState.Loading, station, reason, Snapshot.Volume, station.Bitrate, false));

            try
            {
                // Give the platform a short opportunity to release the failed
                // media source before creating the next one. This avoids treating
                // a transient source teardown as a permanent station failure.
                await Task.Delay(RecoveryBackoff, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }

            // A live station can fail after opening even when the URL is still
            // valid. Cycle every retained candidate and keep retrying while the
            // user session remains active. A bounded retry count made otherwise
            // recoverable stations permanently fail after roughly a minute.
            var recoveryAttempt = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                for (var offset = 1; offset <= candidates.Count; offset++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var nextIndex = (candidateIndex + offset) % candidates.Count;
                    try
                    {
                        await StartCandidateAsync(candidates, nextIndex, station, session, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                    catch { /* Try the next retained candidate. */ }
                }

                recoveryAttempt++;
                var backoffMilliseconds = Math.Min(
                    MaximumRecoveryBackoff.TotalMilliseconds,
                    RecoveryBackoff.TotalMilliseconds * Math.Pow(2, Math.Min(recoveryAttempt - 1, 5)));
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(backoffMilliseconds), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }

                Publish(new PlaybackSnapshot(PlaybackState.Loading, station,
                    "The station stream is reconnecting…", Snapshot.Volume, station.Bitrate, false));
            }
        }

    }

    private bool IsCurrent(MediaPlayer player, long session)
    {
        lock (_gate)
        {
            return ReferenceEquals(_player, player) && session == Interlocked.Read(ref _sessionId);
        }
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

    private bool IsUserPaused()
    {
        lock (_gate) return _userPaused;
    }

    private void OnTransportButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        MediaPlayer? player;
        lock (_gate) player = _player;
        if (player is null) return;

        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                lock (_gate) _userPaused = false;
                player.Play();
                Publish(Snapshot with { State = PlaybackState.Playing });
                break;
            case SystemMediaTransportControlsButton.Pause:
                lock (_gate) _userPaused = true;
                player.Pause();
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
}
