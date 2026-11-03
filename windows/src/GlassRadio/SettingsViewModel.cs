using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;
using GlassRadio.Domain;

namespace GlassRadio;

public enum SettingsSection
{
    Playback,
    Library,
    Countries,
    Api,
    Content,
    About
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly ISnapshotStore _snapshots;
    private readonly IArtworkService _artwork;
    private readonly Action<AppSettings>? _settingsApplied;
    private readonly IFavoritesStore? _favoritesStore;
    private readonly ILastPlayedStore? _lastPlayedStore;
    private readonly Action? _favoritesCleared;
    private readonly Action? _lastPlayedCleared;
    private readonly Action? _artworkCleared;
    private readonly Func<AppSettings, Task>? _localApiApplied;
    private readonly Func<string?>? _localApiKeyProvider;
    private readonly Func<Task>? _refreshLibrary;

    public SettingsViewModel(ISettingsStore settingsStore, ISnapshotStore snapshots, IArtworkService artwork, Action<AppSettings>? settingsApplied = null, IFavoritesStore? favoritesStore = null, ILastPlayedStore? lastPlayedStore = null, Action? favoritesCleared = null, Action? lastPlayedCleared = null, Action? artworkCleared = null, Func<AppSettings, Task>? localApiApplied = null, Func<string?>? localApiKeyProvider = null, Func<Task>? refreshLibrary = null)
    {
        _settingsStore = settingsStore;
        _snapshots = snapshots;
        _artwork = artwork;
        _settingsApplied = settingsApplied;
        _favoritesStore = favoritesStore;
        _lastPlayedStore = lastPlayedStore;
        _favoritesCleared = favoritesCleared;
        _lastPlayedCleared = lastPlayedCleared;
        _artworkCleared = artworkCleared;
        _localApiApplied = localApiApplied;
        _localApiKeyProvider = localApiKeyProvider;
        _refreshLibrary = refreshLibrary;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        ClearArtworkCommand = new AsyncRelayCommand(ClearArtworkAsync);
        ResetLibraryCommand = new AsyncRelayCommand(ResetLibraryAsync);
        RefreshLibraryCommand = new AsyncRelayCommand(RefreshLibraryAsync);
        ResetFavoritesCommand = new AsyncRelayCommand(ResetFavoritesAsync);
        ClearLastPlayedCommand = new AsyncRelayCommand(ClearLastPlayedAsync);
        foreach (var option in LanguagePreferenceCatalog.Options)
            LanguageOptions.Add(new LanguageOptionViewModel(option.Code, option.DisplayName));
    }

    [ObservableProperty] private double volume = 0.72;
    [ObservableProperty] private int connectionTimeoutSeconds = 12;
    [ObservableProperty] private bool preferLowerBitrate;
    [ObservableProperty] private CountryPreset? defaultCountry;
    [ObservableProperty] private bool allowReligiousContent;
    [ObservableProperty] private bool allowSportsContent;
    [ObservableProperty] private bool allowNewsContent;
    [ObservableProperty] private bool localApiEnabled;
    [ObservableProperty] private int localApiPort = 7373;
    [ObservableProperty] private bool localApiAllowLanAccess;
    [ObservableProperty] private bool onboardingCompleted;
    [ObservableProperty] private string localApiKey = "Not running";
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private SettingsSection activeSection = SettingsSection.Playback;
    public ObservableCollection<LanguageOptionViewModel> LanguageOptions { get; } = [];

    public IReadOnlyList<CountryPreset> Countries => CountryPreset.All;
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand ClearArtworkCommand { get; }
    public IAsyncRelayCommand ResetLibraryCommand { get; }
    public IAsyncRelayCommand RefreshLibraryCommand { get; }
    public IAsyncRelayCommand ResetFavoritesCommand { get; }
    public IAsyncRelayCommand ClearLastPlayedCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        Volume = settings.Volume;
        ConnectionTimeoutSeconds = settings.ConnectionTimeoutSeconds;
        PreferLowerBitrate = settings.PreferLowerBitrate;
        DefaultCountry = Countries.FirstOrDefault(c => c.Id == settings.DefaultCountryId) ?? Countries.FirstOrDefault();
        AllowReligiousContent = settings.AllowReligiousContent;
        AllowSportsContent = settings.AllowSportsContent;
        AllowNewsContent = settings.AllowNewsContent;
        LocalApiEnabled = settings.LocalApiEnabled;
        LocalApiPort = settings.LocalApiPort;
        LocalApiAllowLanAccess = settings.LocalApiAllowLanAccess;
        OnboardingCompleted = settings.OnboardingCompleted;
        var selectedLanguages = LanguagePreferenceCatalog.Normalize(settings.PreferredLanguages);
        foreach (var option in LanguageOptions) option.IsSelected = selectedLanguages.Contains(option.Code, StringComparer.OrdinalIgnoreCase);
        LocalApiKey = _localApiKeyProvider?.Invoke() ?? "Not running";
    }

    private async Task SaveAsync()
    {
        var current = await _settingsStore.LoadAsync(CancellationToken.None);
        var settings = current with
        {
            Volume = Volume,
            ConnectionTimeoutSeconds = ConnectionTimeoutSeconds,
            PreferLowerBitrate = PreferLowerBitrate,
            DefaultCountryId = DefaultCountry?.Id ?? "uk",
            AllowReligiousContent = AllowReligiousContent,
            AllowSportsContent = AllowSportsContent,
            AllowNewsContent = AllowNewsContent,
            LocalApiEnabled = LocalApiEnabled,
            LocalApiPort = LocalApiPort,
            LocalApiAllowLanAccess = LocalApiAllowLanAccess,
            OnboardingCompleted = OnboardingCompleted,
            PreferredLanguages = LanguagePreferenceCatalog.Normalize(
                LanguageOptions.Where(static option => option.IsSelected).Select(static option => option.Code))
        };
        settings = settings.Normalize();
        await _settingsStore.SaveAsync(settings, CancellationToken.None);
        _settingsApplied?.Invoke(settings);
        if (_localApiApplied is not null) await _localApiApplied(settings);
        StatusText = "Settings saved.";
    }

    private async Task ClearArtworkAsync()
    {
        await _artwork.ClearAsync(CancellationToken.None);
        _artworkCleared?.Invoke();
        StatusText = "Artwork cache cleared.";
    }

    private async Task ResetLibraryAsync()
    {
        await _snapshots.ClearAsync(CancellationToken.None);
        StatusText = "Station snapshots cleared.";
    }

    private async Task RefreshLibraryAsync()
    {
        if (_refreshLibrary is null)
        {
            StatusText = "Station refresh is unavailable.";
            return;
        }

        StatusText = "Refreshing station library…";
        await _refreshLibrary();
        StatusText = "Station library refresh requested.";
    }

    private async Task ResetFavoritesAsync()
    {
        if (_favoritesStore is not null) await _favoritesStore.ClearAsync(CancellationToken.None);
        _favoritesCleared?.Invoke();
        StatusText = "Favourites cleared.";
    }

    private async Task ClearLastPlayedAsync()
    {
        if (_lastPlayedStore is not null) await _lastPlayedStore.ClearAsync(CancellationToken.None);
        _lastPlayedCleared?.Invoke();
        StatusText = "Last-played station cleared.";
    }
}
