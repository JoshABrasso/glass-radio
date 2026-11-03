using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.ViewManagement;

namespace GlassRadio;

// A faithful, small-scope port of the Mac app's MiniPongView: the same normalized (0..1)
// playfield, paddle/ball sizing ratios, bounce-angle-from-offset physics, and CPU miss-chance
// AI. Kept as self-contained animation/rendering state in code-behind (not business logic) to
// match how the Mac source itself keeps this as local view state, not app view-model state.
public sealed partial class MiniPongControl : UserControl
{
    private readonly Random _random = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private readonly UISettings _uiSettings = new();

    private double _paddleX = 0.5;
    private double _cpuX = 0.5;
    private double _ballX = 0.5;
    private double _ballY = 0.3;
    private double _velocityX = 0.32;
    private double _velocityY = 0.42;
    private int _playerScore;
    private int _cpuScore;
    private const double CpuMissChance = 0.12;
    private DateTime _lastTick;

    public MiniPongControl()
    {
        InitializeComponent();
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += Timer_Tick;
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        _lastTick = DateTime.UtcNow;
        ResetBall(towardsPlayer: false);
        Focus(FocusState.Programmatic);
        StartIfAllowed();
    }

    private void Root_Unloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    // Mirrors the Mac/earlier-Windows reduced-motion gate: only animate while this control is
    // actually loaded, has a live XamlRoot, and the user has not disabled animations.
    private void StartIfAllowed()
    {
        if (XamlRoot is null) return;
        if (!_uiSettings.AnimationsEnabled) return;
        _timer.Start();
    }

    private void GameCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Render();

    private void Timer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (XamlRoot is null || !_uiSettings.AnimationsEnabled)
        {
            _timer.Stop();
            return;
        }

        var now = DateTime.UtcNow;
        var delta = Math.Min((now - _lastTick).TotalSeconds, 0.05) * 1.2;
        _lastTick = now;
        Step(delta);
        Render();
    }

    private void Step(double delta)
    {
        var newX = _ballX + _velocityX * delta;
        var newY = _ballY + _velocityY * delta;

        if (newX < 0.05 || newX > 0.95)
        {
            _velocityX *= -1;
            newX = Math.Clamp(newX, 0.05, 0.95);
        }

        if (newY < 0.08)
        {
            if (Math.Abs(newX - _cpuX) <= 0.12)
            {
                _velocityY *= -1;
                var offset = (newX - _cpuX) * 0.9;
                _velocityX = Math.Clamp(_velocityX + offset, -0.6, 0.6);
                newY = 0.1;
            }
            else
            {
                _playerScore++;
                UpdateScoreText();
                ResetBall(towardsPlayer: false);
                UpdateCpuPaddle();
                return;
            }
        }

        if (newY > 0.9)
        {
            if (Math.Abs(newX - _paddleX) <= 0.12)
            {
                _velocityY *= -1;
                var offset = (newX - _paddleX) * 0.9;
                _velocityX = Math.Clamp(_velocityX + offset, -0.6, 0.6);
                newY = 0.88;
            }
            else
            {
                _cpuScore++;
                UpdateScoreText();
                ResetBall(towardsPlayer: true);
                UpdateCpuPaddle();
                return;
            }
        }

        _ballX = newX;
        _ballY = newY;
        UpdateCpuPaddle();
    }

    private void ResetBall(bool towardsPlayer)
    {
        _ballX = 0.5;
        _ballY = 0.5;
        _velocityX = _random.NextDouble() * 0.7 - 0.35;
        _velocityY = towardsPlayer ? 0.4 : -0.4;
    }

    private void UpdateCpuPaddle()
    {
        var miss = _random.NextDouble() < CpuMissChance;
        var jitter = miss ? _random.NextDouble() * 0.36 - 0.18 : 0;
        var desired = Math.Clamp(_ballX + jitter, 0.1, 0.9);
        _cpuX += (desired - _cpuX) * 0.12;
    }

    private void UpdateScoreText()
    {
        PlayerScoreText.Text = $"You {_playerScore}";
        CpuScoreText.Text = $"CPU {_cpuScore}";
    }

    private void Render()
    {
        var width = GameCanvas.ActualWidth;
        var height = GameCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        Canvas.SetLeft(CpuPaddle, Math.Clamp(_cpuX, 0.1, 0.9) * width - CpuPaddle.Width / 2);
        Canvas.SetTop(CpuPaddle, 16);

        Canvas.SetLeft(PlayerPaddle, Math.Clamp(_paddleX, 0.1, 0.9) * width - PlayerPaddle.Width / 2);
        Canvas.SetTop(PlayerPaddle, height - 24);

        Canvas.SetLeft(Ball, _ballX * width - Ball.Width / 2);
        Canvas.SetTop(Ball, _ballY * height - Ball.Height / 2);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left:
                _paddleX = Math.Max(0.1, _paddleX - 0.05);
                Render();
                e.Handled = true;
                break;
            case VirtualKey.Right:
                _paddleX = Math.Min(0.9, _paddleX + 0.05);
                Render();
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                RequestClose();
                e.Handled = true;
                break;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => RequestClose();

    public event EventHandler? CloseRequested;

    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
