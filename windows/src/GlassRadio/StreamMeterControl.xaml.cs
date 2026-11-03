using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using GlassRadio.Application.Abstractions;
using Windows.UI.ViewManagement;

namespace GlassRadio;

public sealed partial class StreamMeterControl : UserControl
{
    // Deliberate design choice, not simulated telemetry: a classic multi-band VU-meter look that
    // tells the user "this stream is actively playing," with independently jittering bars per
    // tick. It does not claim to represent measured audio levels (nothing in this app's playback
    // pipeline taps real sample data - confirmed by searching the whole solution before building
    // this) - only PlaybackState drives whether it animates at all, same honesty boundary as
    // before, just a livelier visual than a single static frame.
    private static readonly Random Random = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private readonly UISettings _uiSettings = new();

    private const double MinBarHeight = 3;
    private const double MaxBarHeight = 17;

    public StreamMeterControl()
    {
        InitializeComponent();
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(140);
        _timer.Tick += Timer_Tick;
        UpdateVisuals();
    }

    private void Root_Loaded(object sender, RoutedEventArgs e) => StartOrStopAnimation();

    private void Root_Unloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    public PlaybackState State
    {
        get => (PlaybackState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(PlaybackState), typeof(StreamMeterControl),
        new PropertyMetadata(PlaybackState.Stopped, static (d, _) => ((StreamMeterControl)d).OnStateChanged()));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(StreamMeterControl),
        new PropertyMetadata(0d, static (d, _) => ((StreamMeterControl)d).UpdateVisuals()));

    public bool HasProgress
    {
        get => (bool)GetValue(HasProgressProperty);
        set => SetValue(HasProgressProperty, value);
    }

    public static readonly DependencyProperty HasProgressProperty = DependencyProperty.Register(
        nameof(HasProgress), typeof(bool), typeof(StreamMeterControl),
        new PropertyMetadata(false, static (d, _) => ((StreamMeterControl)d).UpdateVisuals()));

    private void OnStateChanged()
    {
        UpdateVisuals();
        StartOrStopAnimation();
    }

    // Animate only while there is actually something to indicate (playing/loading), and only
    // when the control is live and the user hasn't disabled animations - same reduced-motion
    // gate this codebase already uses for MiniPongControl.
    private void StartOrStopAnimation()
    {
        var shouldAnimate = State is PlaybackState.Playing or PlaybackState.Loading;
        if (!shouldAnimate || XamlRoot is null || !_uiSettings.AnimationsEnabled)
        {
            _timer.Stop();
            if (!shouldAnimate) UpdateVisuals();
            return;
        }

        _timer.Start();
    }

    private void Timer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (XamlRoot is null || !_uiSettings.AnimationsEnabled || State is not (PlaybackState.Playing or PlaybackState.Loading))
        {
            _timer.Stop();
            return;
        }

        SetHeights(
            Random.NextDouble() * (MaxBarHeight - MinBarHeight) + MinBarHeight,
            Random.NextDouble() * (MaxBarHeight - MinBarHeight) + MinBarHeight,
            Random.NextDouble() * (MaxBarHeight - MinBarHeight) + MinBarHeight,
            Random.NextDouble() * (MaxBarHeight - MinBarHeight) + MinBarHeight);
    }

    private void UpdateVisuals()
    {
        var resourceKey = State switch
        {
            PlaybackState.Playing => "GlassMeterPlayingBrush",
            PlaybackState.Loading => "GlassMeterLoadingBrush",
            PlaybackState.Paused => "GlassMeterPausedBrush",
            _ => "GlassMeterStoppedBrush"
        };
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var brush = resources[resourceKey] as Brush
            ?? resources["GlassPanelBorderBrush"] as Brush;
        if (brush is null) return;
        BarOne.Background = brush;
        BarTwo.Background = brush;
        BarThree.Background = brush;
        BarFour.Background = brush;
        Opacity = State is PlaybackState.Playing or PlaybackState.Loading ? 1 : 0.6;

        // The animation timer (while playing/loading) owns bar heights frame-to-frame; this is
        // only the resting frame for paused/stopped, or the first frame before the timer's first
        // tick lands. Real buffer health still drives the resting profile when available - it is
        // never overwritten by the random animation's honesty boundary, just by its cadence.
        if (HasProgress)
        {
            var progress = Math.Clamp(Progress, 0, 1);
            SetHeights(
                4 + progress * 6,
                5 + progress * 10,
                6 + progress * 12,
                4 + progress * 8);
            return;
        }

        switch (State)
        {
            case PlaybackState.Playing:
                SetHeights(7, 12, 16, 10);
                break;
            case PlaybackState.Loading:
                SetHeights(5, 10, 14, 8);
                break;
            case PlaybackState.Paused:
                SetHeights(4, 7, 10, 6);
                break;
            default:
                SetHeights(3, 5, 7, 4);
                break;
        }
    }

    private void SetHeights(double one, double two, double three, double four)
    {
        BarOne.Height = one;
        BarTwo.Height = two;
        BarThree.Height = three;
        BarFour.Height = four;
    }
}
