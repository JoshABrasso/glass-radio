using Microsoft.UI.Xaml;
using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;
using GlassRadio.Application.Services;
using GlassRadio.Infrastructure.Windows;
using GlassRadio.Infrastructure.Windows.Artwork;
using GlassRadio.Infrastructure.Windows.Playback;
using GlassRadio.Infrastructure.Windows.Storage;
using GlassRadio.Infrastructure.Windows.LocalApi;

namespace GlassRadio;

public partial class App : Microsoft.UI.Xaml.Application
{
    private static Mutex? _singleInstanceMutex;
    private static bool _secondaryInstance;
    private Window? _window;
    private readonly AppHost _host = AppHost.Create();

    public App()
    {
        _singleInstanceMutex = new Mutex(true, "Local\\GlassRadio.SingleInstance", out var createdNew);
        _secondaryInstance = !createdNew;
        if (!_secondaryInstance) InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (_secondaryInstance)
        {
            Environment.Exit(0);
            return;
        }

        var mainViewModel = _host.CreateMainViewModel();
        mainViewModel.SettingsChanged += (_, settings) => _ = _host.ApplyLocalApiSettingsAsync(mainViewModel, settings);
        _ = _host.InitializeLocalApiAsync(mainViewModel);
        _window = new MainWindow(mainViewModel, () => new SettingsWindow(_host.CreateSettingsViewModel(
            mainViewModel.ApplySettings,
            mainViewModel.ClearFavorites,
            mainViewModel.ClearLastPlayed,
            mainViewModel.ClearArtwork,
            settings => _host.ApplyLocalApiSettingsAsync(mainViewModel, settings),
            _host.GetLocalApiKey,
            () => mainViewModel.RefreshCountryCommand.ExecuteAsync(null))));
        _window.Closed += async (_, _) => await _host.DisposeAsync();
        _window.Activate();
    }
}

internal sealed class AppHost : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly WindowsPlaybackEngine _playback;
    private readonly SettingsStore _settings;
    private readonly FavoritesStore _favorites;
    private readonly LastPlayedStore _lastPlayed;
    private readonly SnapshotStore _snapshots;
    private readonly ArtworkService _artwork;
    private readonly LocalApiKeyStore _localApiKeys;
    private readonly LocalApiServer _localApi = new();
    private string? _localApiKey;
    private int _disposed;

    private AppHost(HttpClient httpClient, WindowsPlaybackEngine playback, SettingsStore settings, FavoritesStore favorites, LastPlayedStore lastPlayed, SnapshotStore snapshots, ArtworkService artwork, LocalApiKeyStore localApiKeys, RadioCatalogService catalog)
    {
        _httpClient = httpClient;
        _playback = playback;
        _settings = settings;
        _favorites = favorites;
        _lastPlayed = lastPlayed;
        _snapshots = snapshots;
        _artwork = artwork;
        _localApiKeys = localApiKeys;
        Catalog = catalog;
    }

    public RadioCatalogService Catalog { get; }

    public static AppHost Create()
    {
        var paths = new AppPaths();
        var settings = new SettingsStore(paths);
        var favorites = new FavoritesStore(paths);
        var lastPlayed = new LastPlayedStore(paths);
        var snapshots = new SnapshotStore(paths);
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GlassRadio/3.0 (personal FOSS desktop app)");
        var client = new RadioBrowserClient(http);
        var catalog = new RadioCatalogService(client, snapshots);
        var artwork = new ArtworkService(http, paths);
        return new AppHost(http, new WindowsPlaybackEngine(), settings, favorites, lastPlayed, snapshots, artwork, new LocalApiKeyStore(paths), catalog);
    }

    public MainViewModel CreateMainViewModel() => new(Catalog, _settings, _favorites, _lastPlayed, _artwork, _playback);
    public SettingsViewModel CreateSettingsViewModel(Action<AppSettings>? settingsApplied = null, Action? favoritesCleared = null, Action? lastPlayedCleared = null, Action? artworkCleared = null, Func<AppSettings, Task>? localApiApplied = null, Func<string?>? localApiKeyProvider = null, Func<Task>? refreshLibrary = null) => new(_settings, _snapshots, _artwork, settingsApplied, _favorites, _lastPlayed, favoritesCleared, lastPlayedCleared, artworkCleared, localApiApplied, localApiKeyProvider, refreshLibrary);

    public string? GetLocalApiKey() => _localApi.IsRunning ? _localApiKey : null;

    public async Task InitializeLocalApiAsync(MainViewModel mainViewModel)
    {
        var settings = await _settings.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        await ApplyLocalApiSettingsAsync(mainViewModel, settings).ConfigureAwait(false);
    }

    public async Task ApplyLocalApiSettingsAsync(MainViewModel mainViewModel, AppSettings settings)
    {
        if (!settings.Normalize().LocalApiEnabled)
        {
            await _localApi.DisposeAsync().ConfigureAwait(false);
            _localApiKey = null;
            return;
        }

        var normalized = settings.Normalize();
        if (_localApi.IsRunning && _localApi.Port == normalized.LocalApiPort && _localApi.AllowLanAccess == normalized.LocalApiAllowLanAccess) return;
        await _localApi.DisposeAsync().ConfigureAwait(false);
        _localApiKey ??= _localApiKeys.GetOrCreate();
        await _localApi.StartAsync(mainViewModel, normalized.LocalApiPort, _localApiKey, normalized.LocalApiAllowLanAccess).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _localApi.DisposeAsync();
        await _playback.DisposeAsync();
        await _artwork.DisposeAsync();
        _httpClient.Dispose();
    }

}
