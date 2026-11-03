using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;
using GlassRadio.Application.Services;
using GlassRadio.Domain;
using Microsoft.UI.Dispatching;
using System.Globalization;

namespace GlassRadio;

public enum MainSection
{
    Home,
    Explore
}

public enum OnboardingStep
{
    Welcome,
    Selections
}

public partial class MainViewModel : ObservableObject, IAsyncDisposable, ILocalApiTarget
{
    private static readonly string[] MacGenreCategories =
    [
        "80s", "90s", "Adult Contemporary", "Alternative", "Ambient", "Blues",
        "Classical", "Classic Rock", "Country", "Dance", "Disco", "Drum & Bass",
        "EDM", "Electronic", "Folk", "Funk", "Gospel", "Hip-Hop", "House", "Indie",
        "Jazz", "Latin", "Lounge", "Metal", "News", "Oldies", "Pop", "R&B", "Reggae",
        "Rock", "Soul", "Sports", "Talk", "Top 40", "Urban"
    ];

    private const int CuratedTopStationLimit = 50;

    private readonly RadioCatalogService _catalog;
    private readonly ISettingsStore _settingsStore;
    private readonly IFavoritesStore _favoritesStore;
    private readonly ILastPlayedStore _lastPlayedStore;
    private readonly IArtworkService _artworkService;
    private readonly IPlaybackEngine _playback;
    private readonly DispatcherQueue? _uiQueue;
    private CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private CancellationTokenSource? _quickSearchOperation;
    private CancellationTokenSource? _browseOperation;
    private CancellationTokenSource? _artworkOperation;
    private CancellationTokenSource? _backgroundRefreshCancellation;
    private Task? _backgroundRefreshTask;
    private bool _rebuildingFavoriteRows;
    private bool _startupHasBegun;
    private bool _settingsLoaded;
    private readonly TaskCompletionSource<bool> _startupReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AppSettings _settings = new();
    private IReadOnlyList<RadioStation> _queue = Array.Empty<RadioStation>();
    private IReadOnlyList<RadioStation> _availableStations = Array.Empty<RadioStation>();
    private CountryPreset? _loadedCountry;
    private readonly Dictionary<string, StationItemViewModel> _rowCache = new(StringComparer.OrdinalIgnoreCase);
    private int _queueIndex = -1;
    private CatalogOperation _lastCatalogOperation;
    private static readonly TimeSpan BackgroundRefreshInterval = TimeSpan.FromMinutes(30);

    public MainViewModel(RadioCatalogService catalog, ISettingsStore settingsStore, IFavoritesStore favoritesStore, ILastPlayedStore lastPlayedStore, IArtworkService artworkService, IPlaybackEngine playback)
    {
        _catalog = catalog;
        _settingsStore = settingsStore;
        _favoritesStore = favoritesStore;
        _lastPlayedStore = lastPlayedStore;
        _artworkService = artworkService;
        _playback = playback;
        _uiQueue = DispatcherQueue.GetForCurrentThread();
        _playback.SnapshotChanged += OnPlaybackChanged;
        Countries = new(CountryPreset.All);
        RecentCountries = [];
        OnboardingCountryItems = new(Countries.Select(static country => new OnboardingCountryItemViewModel(country)));
        OnboardingVisibleCountryItems = new(OnboardingCountryItems);
        foreach (var option in LanguagePreferenceCatalog.Options)
            OnboardingLanguageOptions.Add(new LanguageOptionViewModel(option.Code, option.DisplayName));
        SetAllOnboardingCountries(true);
        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        LoadCountryCommand = new AsyncRelayCommand<CountryPreset>(LoadCountryAsync);
        LoadCountryAndNavigateCommand = new AsyncRelayCommand<CountryPreset>(LoadCountryAndNavigateAsync);
        RefreshCountryCommand = new AsyncRelayCommand(RefreshCountryAsync);
        SearchCommand = new AsyncRelayCommand(SearchAsync);
        SearchSuggestionCommand = new AsyncRelayCommand<string>(SearchSuggestionAsync);
        ExploreGenreCommand = new AsyncRelayCommand<string>(ExploreGenreAsync);
        SelectGenreCommand = new RelayCommand<string>(SelectGenre);
        RunQuickSearchCommand = new AsyncRelayCommand(RunQuickSearchAsync);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        DiscoverCommand = new AsyncRelayCommand(DiscoverAsync);
        PlayCommand = new AsyncRelayCommand<RadioStation>(PlayAsync);
        PauseCommand = new RelayCommand(Pause);
        TogglePlaybackCommand = new RelayCommand(TogglePlayback);
        StopCommand = new RelayCommand(Stop);
        PreviousCommand = new AsyncRelayCommand(PreviousAsync);
        NextCommand = new AsyncRelayCommand(NextAsync);
        ToggleFavoriteCommand = new RelayCommand<RadioStation>(ToggleFavorite);
        FavoriteRows.CollectionChanged += FavoriteRows_CollectionChanged;
        Genres.CollectionChanged += Genres_CollectionChanged;
        RebuildExploreGenreLayout();
        ToggleOnboardingCountryCommand = new RelayCommand<OnboardingCountryItemViewModel>(ToggleOnboardingCountry);
        ToggleAllOnboardingCountriesCommand = new RelayCommand(() => SetAllOnboardingCountries(!AreAllOnboardingCountriesSelected));
        CompleteOnboardingCommand = new AsyncRelayCommand(CompleteOnboardingAsync);
        ToggleMiniGameCommand = new RelayCommand(() => IsMiniGamePresented = !IsMiniGamePresented);
        NextOnboardingStepCommand = new RelayCommand(() => OnboardingStep = OnboardingStep.Selections);
        BackOnboardingStepCommand = new RelayCommand(() => OnboardingStep = OnboardingStep.Welcome);
        RetryCommand = new AsyncRelayCommand(RetryAsync);
        NavigateHomeCommand = new AsyncRelayCommand(NavigateHomeAsync);
        NavigateExploreCommand = new AsyncRelayCommand(NavigateExploreAsync);
        CloseExploreDetailsCommand = new RelayCommand(() => IsExploreDetailsOpen = false);
        NavigateSearchCommand = new RelayCommand(() => _ = NavigateExploreAsync());
        NavigateDiscoverCommand = new RelayCommand(() => _ = NavigateExploreAsync());
        SelectAllStationsViewStyleCommand = new RelayCommand<string>(styleName =>
            AllStationsViewStyle = Enum.TryParse<AllStationsViewStyle>(styleName, out var style) ? style : AllStationsViewStyle.List);
    }

    public ObservableCollection<CountryPreset> Countries { get; }
    public ObservableCollection<CountryPreset> RecentCountries { get; }
    public ObservableCollection<OnboardingCountryItemViewModel> OnboardingCountryItems { get; }
    public ObservableCollection<OnboardingCountryItemViewModel> OnboardingVisibleCountryItems { get; }
    public ObservableCollection<LanguageOptionViewModel> OnboardingLanguageOptions { get; } = [];
    public ObservableCollection<StationItemViewModel> Stations { get; } = [];
    public ObservableCollection<StationItemViewModel> SearchResults { get; } = [];
    public ObservableCollection<StationItemViewModel> DiscoverResults { get; } = [];
    public ObservableCollection<StationItemViewModel> ExploreResults { get; } = [];
    public ObservableCollection<StationItemViewModel> ExploreChannels { get; } = [];
    public ObservableCollection<StationItemViewModel> ExploreMajorBrands { get; } = [];
    public ObservableCollection<StationItemViewModel> ExploreWithGenres { get; } = [];
    public ObservableCollection<StationItemViewModel> ExplorePresets { get; } = [];
    public ObservableCollection<StationItemViewModel> ExploreWorldwide { get; } = [];
    public ObservableCollection<ExploreBrandClusterViewModel> ExploreBrandClusters { get; } = [];
    public ObservableCollection<string> ExploreGenres { get; } = [];
    public ObservableCollection<StationItemViewModel> TopStations { get; } = [];
    public ObservableCollection<ObservableCollection<StationItemViewModel>> FeaturedRows { get; } = [];
    public ObservableCollection<RadioStation> Favorites { get; } = [];
    public ObservableCollection<StationItemViewModel> FavoriteRows { get; } = [];
    public ObservableCollection<RadioStation> QuickSearchSuggestions { get; } = [];
    public ObservableCollection<string> SearchSuggestions { get; } = ["BBC Radio", "Classic Rock", "Paris", "90s", "Lo-Fi"];
    public ObservableCollection<string> Genres { get; } = ["All"];
    public event EventHandler<AppSettings>? SettingsChanged;
    public event EventHandler? LayoutSettingsLoaded;

    [ObservableProperty] private CountryPreset? selectedCountry;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string quickSearchText = string.Empty;
    [ObservableProperty] private string statusText = "Choose a country to begin.";
    [ObservableProperty] private string recoveryStatus = string.Empty;
    [ObservableProperty] private RadioStation? nowPlaying;
    [ObservableProperty] private Uri? nowPlayingArtworkUri;
    [ObservableProperty] private string nowPlayingArtworkInitials = "GR";
    [ObservableProperty] private bool hasNowPlayingArtwork;
    [ObservableProperty] private RadioStation? lastPlayed;
    [ObservableProperty] private PlaybackState playbackState = PlaybackState.Stopped;
    [ObservableProperty] private double volume = 0.72;
    [ObservableProperty] private bool isMuted;
    [ObservableProperty] private string telemetryLabel = "No audio telemetry";
    [ObservableProperty] private string telemetryDisplayLabel = "-- kbps";
    [ObservableProperty] private double bufferingProgress;
    [ObservableProperty] private bool hasBufferingProgress;
    [ObservableProperty] private bool isBusy;
    // Null is the deliberate closed-control state: the Home toolbar shows its
    // descriptive anchor until the user chooses a filter or sort mode. Browse
    // evaluation still uses the Mac defaults below when the value is null.
    [ObservableProperty] private StationFilterMode? selectedFilter;
    [ObservableProperty] private StationSortMode? selectedSort;
    [ObservableProperty] private string selectedGenre = "All";
    [ObservableProperty] private string? selectedExploreGenre = "All";
    [ObservableProperty] private bool isOnboarding;
    [ObservableProperty] private OnboardingStep onboardingStep = OnboardingStep.Welcome;
    [ObservableProperty] private CountryPreset? onboardingCountry;
    [ObservableProperty] private string onboardingSearchText = string.Empty;
    [ObservableProperty] private bool isStartingOnboarding;
    [ObservableProperty] private bool onboardingAllowReligious;
    [ObservableProperty] private bool onboardingAllowSports;
    [ObservableProperty] private bool onboardingAllowNews = true;
    [ObservableProperty] private bool onboardingCacheAllCountries = true;
    [ObservableProperty] private bool isInitialLoading;
    [ObservableProperty] private double initialLoadingProgress;
    [ObservableProperty] private string initialLoadingMessage = string.Empty;
    [ObservableProperty] private bool isMiniGamePresented;
    [ObservableProperty] private MainSection activeSection = MainSection.Home;
    [ObservableProperty] private StationItemViewModel? exploreSelectedRow;
    [ObservableProperty] private bool isExploreDetailsOpen;
    [ObservableProperty] private bool isExploreQueryActive;
    [ObservableProperty] private double listCardWidth = 360;
    [ObservableProperty] private double topCardWidth = 184;
    [ObservableProperty] private int featuredGridColumns = 6;
    [ObservableProperty] private int stationGridColumns = 3;
    [ObservableProperty] private int exploreCategoryColumns = 6;
    [ObservableProperty] private int exploreCategoryRows = 6;
    [ObservableProperty] private double exploreCategoryGridWidth = 1048;
    [ObservableProperty] private string stationCountLabel = "0 shown of 0";
    [ObservableProperty] private bool showTopStations = true;
    [ObservableProperty] private bool showFavoritesNav = true;
    [ObservableProperty] private bool showCountriesPanel = true;
    [ObservableProperty] private bool showGenreFilter = true;
    [ObservableProperty] private AllStationsViewStyle allStationsViewStyle = AllStationsViewStyle.List;

    // Fluent UI System Icons (Filled) codepoints - see Assets/Fonts/FluentSystemIcons-Filled.ttf
    // and THIRD-PARTY-NOTICES.md. Solid/filled glyphs, not the outline Segoe MDL2/Fluent system
    // ones, per direct user feedback referencing the native Windows Media Player app's own look.
    public string PlaybackGlyph => PlaybackState is PlaybackState.Playing or PlaybackState.Loading ? "\uF5AB" : "\uF60F";
    public string VolumeGlyph => IsMuted ? "\uF70F" : "\uF704";
    public string VolumeAutomationName => IsMuted ? "Unmute playback" : "Mute playback";
    // A country selection can briefly be ahead of its retained catalogue while
    // the new preview/full fetch is in flight. Keep the heading tied to the
    // catalogue actually rendered so a failed switch cannot label UK stations
    // as France (or any other country).
    public string TopStationsHeading => (_loadedCountry ?? SelectedCountry) is { } country ? $"Top Stations in {country.DisplayName}" : "Top Stations";
    public string TopStationsCountLabel => $"{TopStations.Count:N0} curated";
    public string ExploreFeatureTitle => IsExploreQueryActive ? "Station channels" : "Featured radio";
    public bool ShowExploreNetworkRail => !IsExploreQueryActive && ExploreMajorBrands.Count > 0;
    public RadioStation? ExploreSelectedStation => ExploreSelectedRow?.Station;
    public string ExploreSelectedStreamLabel => ExploreSelectedStation is { } station
        ? string.Join(" · ", new[]
        {
            string.IsNullOrWhiteSpace(station.Codec) ? null : station.Codec,
            station.Bitrate is > 0 ? $"{station.Bitrate} kbps" : null
        }.Where(static value => !string.IsNullOrWhiteSpace(value)))
        : string.Empty;
    public string ExploreSelectedLocationLabel => ExploreSelectedStation is { } station
        ? string.Join(" · ", new[] { station.City, station.State, station.Country }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        : string.Empty;
    public string ExploreSelectedGenresLabel => ExploreSelectedStation is { Genres.Count: > 0 } station
        ? string.Join(" · ", station.Genres.Take(8))
        : "No genre metadata supplied";
    public string ExploreSelectedLanguageLabel => !string.IsNullOrWhiteSpace(ExploreSelectedStation?.Language)
        ? ExploreSelectedStation!.Language!
        : "Language not supplied";
    public string ExploreSelectedPopularityLabel => ExploreSelectedStation is { } station
        ? $"{station.Votes ?? 0:N0} votes · {station.ClickCount ?? 0:N0} listeners"
        : string.Empty;
    public string ExploreSelectedAvailabilityLabel => ExploreSelectedStation?.LastCheckOk == 1
        ? "Stream verified by Radio Browser"
        : "Stream verification unavailable";
    public bool HasExploreSelectedHomepage => !string.IsNullOrWhiteSpace(ExploreSelectedStation?.Homepage);
    public string NowPlayingFavoriteGlyph => NowPlaying is not null && Favorites.Any(favorite => favorite.Id.Equals(NowPlaying.Id, StringComparison.OrdinalIgnoreCase)) ? "\uE735" : "\uE734";
    public string NowPlayingFavoriteAutomationName => NowPlaying is not null && Favorites.Any(favorite => favorite.Id.Equals(NowPlaying.Id, StringComparison.OrdinalIgnoreCase)) ? "Remove current station from favourites" : "Add current station to favourites";
    public int OnboardingSelectedCount => OnboardingCountryItems.Count(static item => item.IsSelected);
    public bool AreAllOnboardingCountriesSelected => OnboardingSelectedCount == OnboardingCountryItems.Count;
    public string OnboardingSelectionLabel => OnboardingSelectedCount == 0
        ? "No countries selected"
        : $"{OnboardingSelectedCount} of {OnboardingCountryItems.Count} countries selected for full caching";
    public string OnboardingSelectionToggleLabel => AreAllOnboardingCountriesSelected ? "Deselect all" : "Select all";
    public string OnboardingActionLabel => IsStartingOnboarding ? "Setting up…" : "Get Started";
    public bool IsOnboardingWelcomeStep => OnboardingStep == OnboardingStep.Welcome;
    public bool IsOnboardingSelectionsStep => OnboardingStep == OnboardingStep.Selections;
    public string OnboardingCountryScopeSummary => OnboardingCacheAllCountries
        ? $"All {OnboardingCountryItems.Count} countries will be prepared. This takes longer but your whole library is ready immediately."
        : "Choose exactly which countries to prepare below. Others stay reachable from Home, just not pre-cached.";
    public bool HasRecoveryStatus => !string.IsNullOrWhiteSpace(RecoveryStatus);
    public bool IsAllStationsGridStyle => AllStationsViewStyle == AllStationsViewStyle.Grid;
    public bool IsAllStationsListStyle => AllStationsViewStyle == AllStationsViewStyle.List;
    public double LeftPaneWidth => _settings.LeftPaneWidth;
    public double RightPaneWidth => _settings.RightPaneWidth;

    public IReadOnlyList<StationFilterMode> FilterOptions { get; } =
    [
        StationFilterMode.All,
        StationFilterMode.FavoritesOnly,
        StationFilterMode.Popular,
        StationFilterMode.Reliable,
        StationFilterMode.MajorBrands,
        StationFilterMode.WithGenres
    ];
    public IReadOnlyList<StationSortMode> SortOptions { get; } =
    [
        StationSortMode.Name,
        StationSortMode.Popularity,
        StationSortMode.Clicks,
        StationSortMode.BitrateHigh,
        StationSortMode.BitrateLow,
        StationSortMode.FavoritesFirst,
        StationSortMode.NetworksFirst
    ];

    public IAsyncRelayCommand InitializeCommand { get; }
    public IAsyncRelayCommand<CountryPreset> LoadCountryCommand { get; }
    public IAsyncRelayCommand<CountryPreset> LoadCountryAndNavigateCommand { get; }
    public IAsyncRelayCommand RefreshCountryCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand<string> SearchSuggestionCommand { get; }
    public IAsyncRelayCommand<string> ExploreGenreCommand { get; }
    public IRelayCommand<string> SelectGenreCommand { get; }
    public IAsyncRelayCommand RunQuickSearchCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }
    public IAsyncRelayCommand DiscoverCommand { get; }
    public IAsyncRelayCommand<RadioStation> PlayCommand { get; }
    public IRelayCommand PauseCommand { get; }
    public IRelayCommand TogglePlaybackCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IAsyncRelayCommand PreviousCommand { get; }
    public IAsyncRelayCommand NextCommand { get; }
    public IRelayCommand ToggleFavoriteCommand { get; }
    public IRelayCommand<OnboardingCountryItemViewModel> ToggleOnboardingCountryCommand { get; }
    public IRelayCommand ToggleAllOnboardingCountriesCommand { get; }
    public IAsyncRelayCommand CompleteOnboardingCommand { get; }
    public IRelayCommand ToggleMiniGameCommand { get; }
    public IRelayCommand NextOnboardingStepCommand { get; }
    public IRelayCommand BackOnboardingStepCommand { get; }
    public IAsyncRelayCommand RetryCommand { get; }
    public IAsyncRelayCommand NavigateHomeCommand { get; }
    public IAsyncRelayCommand NavigateExploreCommand { get; }
    public IRelayCommand CloseExploreDetailsCommand { get; }
    public IRelayCommand NavigateSearchCommand { get; }
    public IRelayCommand NavigateDiscoverCommand { get; }
    // Takes the style name as a string, not the enum directly: a plain XAML CommandParameter
    // attribute (e.g. CommandParameter="Card") is parsed as a string, not converted to an enum,
    // the same reason SelectGenreCommand below takes a string rather than a typed value.
    public IRelayCommand<string> SelectAllStationsViewStyleCommand { get; }

    public void UpdateQuickSearchSuggestions(string? text)
    {
        QuickSearchSuggestions.Clear();
        var query = text?.Trim() ?? string.Empty;
        if (query.Length < 2) return;

        foreach (var station in _availableStations
                     .Where(station => StationRules.MatchesSearch(station, query))
                     .Take(8))
        {
            QuickSearchSuggestions.Add(station);
        }
    }

    public void OpenExploreStationDetails(RadioStation? station)
    {
        if (station is null) return;
        if (_rowCache.TryGetValue(station.Id, out var row)) ExploreSelectedRow = row;
        else
        {
            ExploreSelectedRow = CreateStationRow(station);
            _rowCache[station.Id] = ExploreSelectedRow;
        }

        IsExploreDetailsOpen = true;
    }

    private void ApplyPreferredLanguages(IReadOnlyList<string>? selected)
    {
        var normalized = LanguagePreferenceCatalog.Normalize(selected);
        foreach (var option in OnboardingLanguageOptions)
            option.IsSelected = normalized.Contains(option.Code, StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> SelectedPreferredLanguages()
    {
        var selected = OnboardingLanguageOptions
            .Where(static option => option.IsSelected)
            .Select(static option => option.Code)
            .ToArray();
        return LanguagePreferenceCatalog.Normalize(selected);
    }

    partial void OnSearchTextChanged(string value)
    {
        var hasQuery = !string.IsNullOrWhiteSpace(value);
        if (IsExploreQueryActive != hasQuery) IsExploreQueryActive = hasQuery;
        if (hasQuery && !string.Equals(value.Trim(), SelectedExploreGenre, StringComparison.OrdinalIgnoreCase))
            SelectedExploreGenre = null;
    }

    IReadOnlyList<RadioStation> ILocalApiTarget.Stations => ReadOnUi(() => Stations.Select(static row => row.Station).ToArray());
    IReadOnlyList<RadioStation> ILocalApiTarget.AllStations => ReadOnUi(() => _availableStations.ToArray());
    IReadOnlyList<RadioStation> ILocalApiTarget.TopStations => ReadOnUi(() => TopStations.Select(static row => row.Station).ToArray());
    IReadOnlyList<RadioStation> ILocalApiTarget.Favorites => ReadOnUi(() => Favorites.ToArray());
    IReadOnlyList<CountryPreset> ILocalApiTarget.Countries => ReadOnUi(() => Countries.ToArray());
    PlaybackSnapshot ILocalApiTarget.Playback => _playback.Snapshot;
    AppSettings ILocalApiTarget.Settings => ReadOnUi(() => _settings.Normalize());

    // Lazily created on first use so no CancellationTokenSource is allocated for sessions where the
    // user never touches the volume slider.
    private CancellationTokenSource? _volumePersistDebounce;
    private CancellationTokenSource? _volumeEngineDebounce;

    partial void OnVolumeChanged(double value)
    {
        // Dragging the slider implies the user wants to hear the result - matches standard media
        // player behaviour (Windows' own volume flyout does the same) and avoids the confusing case
        // of moving the slider while muted and hearing nothing happen.
        if (IsMuted) { IsMuted = false; return; }

        // This fires on every 0.01 StepFrequency tick - dozens of times across a single drag.
        // LibVlcPlaybackEngine.SetVolumeAsync is genuinely synchronous (a direct native
        // MediaPlayer.Volume property set under a lock, no real await anywhere in its call
        // chain), so calling it inline here ran that native call, synchronously, on the UI
        // thread, once per tick - blocking the dispatcher long enough on each one to visibly
        // stutter the slider's own rendering and the next pointer-move. Throttling to the
        // latest value every ~30ms (imperceptible as audio lag, well under one video frame)
        // and applying it off the UI thread via ConfigureAwait(false) keeps the slider fluid
        // without changing what value ultimately reaches the engine.
        _volumeEngineDebounce?.Cancel();
        _volumeEngineDebounce?.Dispose();
        var engineDebounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _volumeEngineDebounce = engineDebounce;
        _ = ApplyVolumeToEngineAfterDelayAsync(value, engineDebounce.Token);

        // The live player Volume and the persisted AppSettings.Volume used to be two completely
        // disconnected values: this handler only ever pushed the new volume to the playback engine,
        // never to _settings/disk. AppSettings.Volume therefore stayed frozen at whatever it was on
        // the last explicit Settings-window Save (or the installer default) for the entire rest of
        // the session, no matter how many times the slider moved. The Settings window loads its own
        // Volume field from that same stale on-disk value whenever it's opened
        // (SettingsViewModel.LoadAsync), and MainViewModel.ApplySettings - called after ANY Settings
        // Save, for literally any reason, not just a volume change - unconditionally does
        // `Volume = _settings.Volume`, silently snapping the live, just-chosen slider position back
        // to that stale value. This was the real bug behind "its just broken" - not a reconnect-only
        // edge case, but something that could fire on every single use of the Settings dialog,
        // confirmed by tracing the full call chain (SettingsViewModel.LoadAsync/SaveAsync ->
        // App.xaml.cs wiring settingsApplied to MainViewModel.ApplySettings ->
        // `Volume = _settings.Volume`), not guessed. Persisting Volume into _settings here, the same
        // way every other live preference already does via PersistSettings(), closes the loop: once
        // this is saved, a later ApplySettings() call reapplies the SAME value instead of an old one.
        // Debounced (unlike the other PersistSettings() callers, which only fire once per discrete
        // click) because a single slider drag can raise this dozens of times a second via
        // StepFrequency - without debouncing that's dozens of overlapping disk writes per drag.
        _volumePersistDebounce?.Cancel();
        _volumePersistDebounce?.Dispose();
        var debounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _volumePersistDebounce = debounce;
        _ = PersistVolumeAfterDelayAsync(debounce.Token);
    }

    private async Task PersistVolumeAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        if (cancellationToken.IsCancellationRequested) return;
        await RunOnUiAsync(() => { PersistSettings(); return Task.CompletedTask; }).ConfigureAwait(false);
    }

    private async Task ApplyVolumeToEngineAfterDelayAsync(double value, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(30), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        if (cancellationToken.IsCancellationRequested) return;
        await _playback.SetVolumeAsync(value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-applies the persisted session volume after the window and its bindings have been
    /// materialized.  The playback engine also receives the value during startup and on every
    /// PlayAsync call; this explicit boundary closes the remaining WinUI first-render race where
    /// the slider could show the saved value before the native audio output had received it.
    /// </summary>
    public Task ReapplyVolumeAsync() =>
        _playback.SetVolumeAsync(IsMuted ? 0 : Volume, _lifetime.Token);

    partial void OnIsMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(VolumeGlyph));
        OnPropertyChanged(nameof(VolumeAutomationName));
        _ = _playback.SetVolumeAsync(value ? 0 : Volume, _lifetime.Token);
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    partial void OnPlaybackStateChanged(PlaybackState value) => OnPropertyChanged(nameof(PlaybackGlyph));
    partial void OnIsStartingOnboardingChanged(bool value) => OnPropertyChanged(nameof(OnboardingActionLabel));
    partial void OnNowPlayingChanged(RadioStation? value)
    {
        OnPropertyChanged(nameof(NowPlayingFavoriteGlyph));
        OnPropertyChanged(nameof(NowPlayingFavoriteAutomationName));
        var row = value is not null && _rowCache.TryGetValue(value.Id, out var cachedRow) ? cachedRow : null;
        NowPlayingArtworkUri = row?.ArtworkUri;
        NowPlayingArtworkInitials = row?.ArtworkInitials ?? (value is null ? "GR" : StationItemViewModel.BuildInitials(value.Name));
    }

    partial void OnNowPlayingArtworkUriChanged(Uri? value) => HasNowPlayingArtwork = value is not null;

    partial void OnExploreSelectedRowChanged(StationItemViewModel? value)
    {
        OnPropertyChanged(nameof(ExploreSelectedStation));
        OnPropertyChanged(nameof(ExploreSelectedStreamLabel));
        OnPropertyChanged(nameof(ExploreSelectedLocationLabel));
        OnPropertyChanged(nameof(ExploreSelectedGenresLabel));
        OnPropertyChanged(nameof(ExploreSelectedLanguageLabel));
        OnPropertyChanged(nameof(ExploreSelectedPopularityLabel));
        OnPropertyChanged(nameof(ExploreSelectedAvailabilityLabel));
        OnPropertyChanged(nameof(HasExploreSelectedHomepage));
    }

    partial void OnIsExploreQueryActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ExploreFeatureTitle));
        OnPropertyChanged(nameof(ShowExploreNetworkRail));
    }

    partial void OnSelectedCountryChanged(CountryPreset? value)
    {
        OnPropertyChanged(nameof(TopStationsHeading));
    }

    partial void OnRecoveryStatusChanged(string value) => OnPropertyChanged(nameof(HasRecoveryStatus));

    partial void OnSelectedFilterChanged(StationFilterMode? value) => _ = RebuildBrowseAsync(_lifetime.Token);
    partial void OnSelectedSortChanged(StationSortMode? value) => _ = RebuildBrowseAsync(_lifetime.Token);
    partial void OnSelectedGenreChanged(string value)
    {
        if (Genres.Contains(value, StringComparer.OrdinalIgnoreCase)) SelectedExploreGenre = value;
        _ = RebuildBrowseAsync(_lifetime.Token);
    }
    partial void OnFeaturedGridColumnsChanged(int value) => RebuildFeaturedRows();
    partial void OnOnboardingSearchTextChanged(string value) => FilterOnboardingCountries(value);
    partial void OnActiveSectionChanged(MainSection value)
    {
        // Avoid writing the constructor default while the settings record is still loading.
        if (_settingsLoaded) PersistSettings();
    }

    // Switching to "cache every country" pre-selects the full list so the checklist
    // (shown only in "choose specific countries" mode) and the eventual caching pass
    // agree on what "all" means. Switching back to "specific" leaves the user's last
    // explicit selection alone rather than clearing it.
    partial void OnOnboardingCacheAllCountriesChanged(bool value)
    {
        // "All countries" pre-selects everything (nothing for the user to do). Switching to
        // "specific countries" must not silently inherit that pre-selected state - it starts
        // from nothing so the checked list always reflects a deliberate choice, not a
        // leftover default.
        SetAllOnboardingCountries(value);
        OnPropertyChanged(nameof(OnboardingCountryScopeSummary));
    }

    partial void OnOnboardingStepChanged(OnboardingStep value)
    {
        OnPropertyChanged(nameof(IsOnboardingWelcomeStep));
        OnPropertyChanged(nameof(IsOnboardingSelectionsStep));
    }

    partial void OnShowTopStationsChanged(bool value) => PersistSettings();
    partial void OnShowFavoritesNavChanged(bool value) => PersistSettings();
    partial void OnShowCountriesPanelChanged(bool value) => PersistSettings();
    partial void OnShowGenreFilterChanged(bool value) => PersistSettings();

    partial void OnAllStationsViewStyleChanged(AllStationsViewStyle value)
    {
        OnPropertyChanged(nameof(IsAllStationsGridStyle));
        OnPropertyChanged(nameof(IsAllStationsListStyle));
        PersistSettings();
    }

    // Shared tail for every Home-layout preference (and now Volume - see OnVolumeChanged) - fold
    // the latest value into _settings and fire-and-forget a save, matching SavePaneWidths' own
    // save pattern below.
    private void PersistSettings()
    {
        _settings = (_settings with
        {
            Volume = Volume,
            ShowTopStations = ShowTopStations,
            ShowFavoritesNav = ShowFavoritesNav,
            ShowCountriesPanel = ShowCountriesPanel,
            ShowGenreFilter = ShowGenreFilter,
            AllStationsViewStyle = AllStationsViewStyle,
            LastActiveSection = ActiveSection is MainSection.Explore ? "Explore" : "Home"
        }).Normalize();
        _ = _settingsStore.SaveAsync(_settings, _lifetime.Token);
    }

    private void FilterOnboardingCountries(string? query)
    {
        var normalized = query?.Trim() ?? string.Empty;
        OnboardingVisibleCountryItems.Clear();
        foreach (var item in OnboardingCountryItems.Where(item => normalized.Length == 0 || item.DisplayName.Contains(normalized, StringComparison.CurrentCultureIgnoreCase)))
            OnboardingVisibleCountryItems.Add(item);
    }

    private void ToggleOnboardingCountry(OnboardingCountryItemViewModel? item)
    {
        if (item is null) return;
        item.IsSelected = !item.IsSelected;
        OnPropertyChanged(nameof(OnboardingSelectedCount));
        OnPropertyChanged(nameof(AreAllOnboardingCountriesSelected));
        OnPropertyChanged(nameof(OnboardingSelectionLabel));
        OnPropertyChanged(nameof(OnboardingSelectionToggleLabel));
    }

    private void SetAllOnboardingCountries(bool selected)
    {
        foreach (var item in OnboardingCountryItems) item.IsSelected = selected;
        OnPropertyChanged(nameof(OnboardingSelectedCount));
        OnPropertyChanged(nameof(AreAllOnboardingCountriesSelected));
        OnPropertyChanged(nameof(OnboardingSelectionLabel));
        OnPropertyChanged(nameof(OnboardingSelectionToggleLabel));
    }

    private async Task InitializeAsync()
    {
        _startupHasBegun = true;
        try
        {
            _settings = await _settingsStore.LoadAsync(_lifetime.Token);
            _settingsLoaded = true;
            ApplyPreferredLanguages(_settings.PreferredLanguages);
            // Seeding through the public properties (not the backing fields - CommunityToolkit's
            // generator forbids referencing those directly, MVVMTK0034) fires each property's
            // partial On...Changed hook, which re-persists the exact value just loaded. That's a
            // harmless, functionally-no-op extra save on launch for a user with a non-default
            // preference, not a correctness issue. This must still happen before
            // LayoutSettingsLoaded fires below, since MainWindow's handler re-derives pane
            // visibility from these properties - firing the event first would make it see the
            // field initializers' defaults instead of whatever was actually loaded.
            ShowTopStations = _settings.ShowTopStations;
            ShowFavoritesNav = _settings.ShowFavoritesNav;
            ShowCountriesPanel = _settings.ShowCountriesPanel;
            ShowGenreFilter = _settings.ShowGenreFilter;
            AllStationsViewStyle = _settings.AllStationsViewStyle;
            LayoutSettingsLoaded?.Invoke(this, EventArgs.Empty);
            _playback.ApplySettings(_settings);
            Volume = _settings.Volume;
            // Volume is loaded before a station is played.  Apply it explicitly so the
            // primary engine honours the saved level immediately, rather than only after the
            // slider is touched.
            await _playback.SetVolumeAsync(_settings.Volume, _lifetime.Token);
            foreach (var favorite in await _favoritesStore.LoadAsync(_lifetime.Token)) Favorites.Add(favorite);
            LastPlayed = await _lastPlayedStore.LoadAsync(_lifetime.Token);
            foreach (var countryId in _settings.RecentCountryIds ?? Array.Empty<string>())
            {
                var country = Countries.FirstOrDefault(candidate => candidate.Id == countryId);
                if (country is not null) RecentCountries.Add(country);
            }
            SelectedCountry = ResolveStartupCountry(_settings);
            OnboardingCountry = SelectedCountry;
            // Search/Explore can be requested while the initial country catalogue is still loading.
            // Publish the startup contract before starting that shared operation so a user action can
            // deliberately supersede the country load instead of being cancelled by it.
            _startupReady.TrySetResult(true);
            if (!_settings.OnboardingCompleted)
            {
                OnboardingAllowReligious = _settings.AllowReligiousContent;
                OnboardingAllowSports = _settings.AllowSportsContent;
                OnboardingAllowNews = _settings.AllowNewsContent;
                OnboardingStep = OnboardingStep.Welcome;
                IsOnboarding = true;
                StatusText = "Choose your opening country to begin.";
                return;
            }
            await LoadCountryAsync(SelectedCountry);
            StartBackgroundRefresh();

            // The catalogue snapshot is persisted independently, but Explore's transient
            // collections were previously rebuilt only after the user clicked Explore. Restore
            // the last destination and paint the cached snapshot immediately on restart.
            if (string.Equals(_settings.LastActiveSection, "Explore", StringComparison.OrdinalIgnoreCase))
            {
                ActiveSection = MainSection.Explore;
                await DiscoverAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText = $"Startup could not load local data: {ex.Message}"; }
        finally { _startupReady.TrySetResult(true); }
    }

    private Task WaitForStartupReadyAsync()
    {
        if (!_startupHasBegun || _startupReady.Task.IsCompleted) return Task.CompletedTask;
        return _startupReady.Task.WaitAsync(_lifetime.Token);
    }

    private CountryPreset? ResolveStartupCountry(AppSettings settings)
    {
        var savedCountry = Countries.FirstOrDefault(country =>
            country.Id.Equals(settings.DefaultCountryId, StringComparison.OrdinalIgnoreCase));

        // Match the Mac first-run contract: suggest the machine's locale before
        // falling back to the product's safe default. Once onboarding has been
        // completed, or a recent-country history exists, the user's persisted
        // selection remains authoritative.
        var hasPersistedChoice = settings.OnboardingCompleted || settings.RecentCountryIds is { Count: > 0 };
        if (hasPersistedChoice) return savedCountry ?? Countries.FirstOrDefault();

        var localeRegion = TryGetLocaleRegion();
        return Countries.FirstOrDefault(country =>
                   country.Id.Equals(localeRegion, StringComparison.OrdinalIgnoreCase) ||
                   country.CountryCode.Equals(localeRegion, StringComparison.OrdinalIgnoreCase))
               ?? savedCountry
               ?? Countries.FirstOrDefault(country => country.Id.Equals("uk", StringComparison.OrdinalIgnoreCase))
               ?? Countries.FirstOrDefault();
    }

    private static string TryGetLocaleRegion()
    {
        try
        {
            var cultureName = CultureInfo.CurrentCulture.Name;
            if (!string.IsNullOrWhiteSpace(cultureName))
                return new RegionInfo(cultureName).TwoLetterISORegionName.ToLowerInvariant();
        }
        catch (CultureNotFoundException) { }
        catch (ArgumentException) { }

        return string.Empty;
    }

    private async Task LoadCountryAsync(CountryPreset? country)
    {
        if (country is null) return;
        // Every country entry point funnels through this method (Home cards, Explore country
        // cards, startup, and the local API). Keep the selected-country identity in sync with the
        // catalogue operation before any cached or preview data is painted; otherwise a successful
        // tile load can leave refresh/recent-country state targeting the previous country.
        SelectedCountry = country;
        _lastCatalogOperation = CatalogOperation.Country;
        CancelOperation();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        if (_settingsLoaded)
            await RememberCountrySelectionAsync(country, operation.Token);
        IsBusy = true;
        RecoveryStatus = string.Empty;
        StatusText = $"Loading {country.DisplayName}…";
        try
        {
            var cachedResult = await _catalog.LoadCachedCountryAsync(country, operation.Token);
            var cached = cachedResult.Snapshot?.Stations;
            if (cached is { Count: > 0 } && IsCurrentOperation(operation))
            {
                await ReplaceStationsAsync(cached, operation.Token, country);
                if (!IsCurrentOperation(operation)) return;
                StatusText = cachedResult.Status == SnapshotLoadStatus.RecoveredFromBackup
                    ? $"Recovered {Stations.Count} saved stations in {country.DisplayName}; refreshing…"
                    : $"Showing {Stations.Count} saved stations in {country.DisplayName}; refreshing…";
            }
            if (cachedResult.Status == SnapshotLoadStatus.RecoveredFromBackup)
                RecoveryStatus = "The saved station cache was recovered from its last known-good copy.";
            else if (cachedResult.Status == SnapshotLoadStatus.Corrupt)
                RecoveryStatus = "The saved station cache was invalid and was moved aside before rebuilding.";

            var result = await _catalog.LoadCountryAsync(country, operation.Token, async preview =>
            {
                await RunOnUiAsync(async () =>
                {
                    if (!IsCurrentOperation(operation)) return;
                        await ReplaceStationsAsync(preview, operation.Token, country);
                    if (IsCurrentOperation(operation))
                        StatusText = $"Showing {Stations.Count} stations in {country.DisplayName}; refreshing...";
                });
            });
            if (!IsCurrentOperation(operation)) return;
            if (!result.IsSuccess) { StatusText = result.Error!.Message; return; }
            await ReplaceStationsAsync(result.Value!, operation.Token, country);
            if (!IsCurrentOperation(operation)) return;
            StatusText = result.IsStaleFallback
                ? $"{Stations.Count} saved stations in {country.DisplayName}; refresh unavailable."
                : $"{Stations.Count} stations in {country.DisplayName}.";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) { if (IsCurrentOperation(operation)) StatusText = $"Could not load stations: {ex.Message}"; }
        finally { CompleteOperation(operation); }
    }

    private async Task RememberCountrySelectionAsync(CountryPreset country, CancellationToken cancellationToken)
    {
        var recentIds = new[] { country.Id }
            .Concat(_settings.RecentCountryIds ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        _settings = (_settings with
        {
            DefaultCountryId = country.Id,
            RecentCountryIds = recentIds
        }).Normalize();
        RecentCountries.Clear();
        foreach (var countryId in recentIds)
        {
            var recent = Countries.FirstOrDefault(candidate => candidate.Id.Equals(countryId, StringComparison.OrdinalIgnoreCase));
            if (recent is not null) RecentCountries.Add(recent);
        }
        await _settingsStore.SaveAsync(_settings, cancellationToken);
    }

    private async Task LoadCountryAndNavigateAsync(CountryPreset? country)
    {
        if (country is null) return;
        ActiveSection = MainSection.Home;
        await LoadCountryAsync(country);
    }

    private async Task CompleteOnboardingAsync()
    {
        if (IsStartingOnboarding) return;
        // "All countries" always means the live full list, not whatever the per-item
        // checkboxes happen to hold — the checklist is only meaningful, and only shown,
        // in "choose specific countries" mode.
        var selectedCountries = (OnboardingCacheAllCountries
                ? Countries.AsEnumerable()
                : OnboardingCountryItems.Where(static item => item.IsSelected).Select(static item => item.Country))
            .ToList();
        var country = OnboardingCountry ?? selectedCountries.FirstOrDefault() ?? Countries.FirstOrDefault();
        if (country is null) return;
        IsStartingOnboarding = true;
        try
        {
            selectedCountries.Remove(country);
            selectedCountries.Insert(0, country);
            var recentIds = selectedCountries
                .Select(static item => item.Id)
                .Concat(_settings.RecentCountryIds ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();
            // Persist the user's choice before the network work, but deliberately keep
            // onboarding incomplete. If Windows or the app stops during the first load,
            // the next launch must return to this screen instead of opening an empty Home.
            _settings = (_settings with
            {
                DefaultCountryId = country.Id,
                OnboardingCompleted = false,
                RecentCountryIds = recentIds,
                AllowReligiousContent = OnboardingAllowReligious,
                AllowSportsContent = OnboardingAllowSports,
                AllowNewsContent = OnboardingAllowNews,
                PreferredLanguages = SelectedPreferredLanguages()
            }).Normalize();
            await _settingsStore.SaveAsync(_settings, _lifetime.Token);
            SelectedCountry = country;
            IsOnboarding = false;
            IsInitialLoading = true;
            InitialLoadingProgress = 0.05;
            InitialLoadingMessage = $"Loading {country.DisplayName}…";
            await LoadCountryAsync(country);

            if (Stations.Count == 0 || !string.Equals(_loadedCountry?.Id, country.Id, StringComparison.OrdinalIgnoreCase))
            {
                IsInitialLoading = false;
                IsMiniGamePresented = false;
                IsOnboarding = true;
                OnboardingStep = OnboardingStep.Selections;
                StatusText = $"Could not prepare {country.DisplayName}. Check the connection and try again.";
                return;
            }

            _settings = (_settings with { OnboardingCompleted = true }).Normalize();
            await _settingsStore.SaveAsync(_settings, _lifetime.Token);

            await CacheSelectedCountriesWithProgressAsync(selectedCountries.Skip(1).ToArray(), _lifetime.Token);

            InitialLoadingMessage = "All set — enjoy Glass Radio.";
            InitialLoadingProgress = 1.0;
            await Task.Delay(TimeSpan.FromMilliseconds(700), _lifetime.Token);
            IsInitialLoading = false;
            IsMiniGamePresented = false;
            StartBackgroundRefresh();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            IsInitialLoading = false;
            IsMiniGamePresented = false;
            IsOnboarding = true;
            OnboardingStep = OnboardingStep.Selections;
            StatusText = $"Onboarding could not be completed: {ex.Message}";
        }
        finally { IsStartingOnboarding = false; }
    }

    private async Task CacheSelectedCountriesWithProgressAsync(IReadOnlyList<CountryPreset> countries, CancellationToken cancellationToken)
    {
        var remaining = countries
            .DistinctBy(static country => country.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (remaining.Length == 0) return;

        try
        {
            for (var i = 0; i < remaining.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var country = remaining[i];
                InitialLoadingMessage = $"Caching {country.DisplayName} ({i + 1} of {remaining.Length})…";
                // Reserve the first 10% for the primary country load that already happened
                // and the last moment for the closing "all set" message.
                InitialLoadingProgress = 0.1 + 0.85 * (i + 1) / remaining.Length;
                await _catalog.RefreshCountryAsync(country, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            // A selected secondary country failing must not take down the usable primary
            // library. It remains retryable through Settings and the country selector.
        }
    }

    private void StartBackgroundRefresh()
    {
        _backgroundRefreshCancellation?.Cancel();
        _backgroundRefreshCancellation?.Dispose();
        _backgroundRefreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _backgroundRefreshTask = RefreshRecentCountriesPeriodicallyAsync(_backgroundRefreshCancellation.Token);
    }

    private async Task RefreshRecentCountriesPeriodicallyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(BackgroundRefreshInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await RefreshRecentCountriesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task RefreshRecentCountriesAsync(CancellationToken cancellationToken)
    {
        var countryIds = ReadOnUi(() => new[] { SelectedCountry?.Id }
            .Concat(_settings.RecentCountryIds ?? Array.Empty<string>())
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray());

        var countries = ReadOnUi(() => countryIds
            .Select(id => Countries.FirstOrDefault(candidate => candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(static country => country is not null)
            .Cast<CountryPreset>()
            .ToArray());

        foreach (var country in countries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _catalog.RefreshCountryAsync(country, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess || result.IsStaleFallback || result.Value is null) continue;

            var shouldUpdateActiveFeed = ReadOnUi(() =>
                !IsBusy && SelectedCountry?.Id.Equals(country.Id, StringComparison.OrdinalIgnoreCase) == true);
            if (!shouldUpdateActiveFeed) continue;

            await RunOnUiAsync(async () =>
            {
                if (IsBusy || SelectedCountry?.Id.Equals(country.Id, StringComparison.OrdinalIgnoreCase) != true) return;
                await ReplaceStationsAsync(result.Value, cancellationToken, country);
                if (!IsBusy) StatusText = $"{Stations.Count} stations in {country.DisplayName}.";
            }).ConfigureAwait(false);
        }
    }


    private Task RetryAsync() => _lastCatalogOperation switch
    {
        // Reparse is deliberately a fresh active-country request. It must not
        // re-enter the cached-load path that can immediately serve the same
        // malformed/stale snapshot the user is trying to recover from.
        CatalogOperation.Country => RefreshCountryAsync(),
        CatalogOperation.Refresh => RefreshCountryAsync(),
        CatalogOperation.Search => SearchAsync(),
        CatalogOperation.Discover => DiscoverAsync(),
        _ => Task.CompletedTask
    };

    private async Task SearchAsync()
    {
        await WaitForStartupReadyAsync();
        _lastCatalogOperation = CatalogOperation.Search;
        CancelOperation();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        var query = SearchText.Trim();
        IsExploreQueryActive = query.Length > 0;
        IsBusy = true;
        try
        {
            var result = await _catalog.SearchAsync(query, operation.Token);
            if (!IsCurrentOperation(operation)) return;
            if (!result.IsSuccess) { StatusText = result.Error!.Message; return; }
            // Radio Browser's public search endpoint indexes station names and state,
            // but city metadata is not consistently queryable server-side. Keep city
            // search functional by merging matches from the loaded catalogue with
            // the remote result instead of silently dropping that dimension.
            var localMatches = _availableStations.Where(station => StationRules.MatchesSearch(station, query));
            var merged = result.Value!
                .Concat(localMatches)
                .GroupBy(static station => station.Id, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToArray();
            await ReplaceTransientStationsAsync(RankExploreStations(FilterStations(merged)), operation.Token, TransientSurface.Search);
            if (!IsCurrentOperation(operation)) return;
            StatusText = $"{SearchResults.Count} results for “{query}”.";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) { if (IsCurrentOperation(operation)) StatusText = $"Search failed: {ex.Message}"; }
        finally { CompleteOperation(operation); }
    }

    private async Task SearchSuggestionAsync(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion)) return;
        SearchText = suggestion.Trim();
        ActiveSection = MainSection.Explore;
        await SearchAsync();
    }

    private async Task ExploreGenreAsync(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre)) return;
        var value = genre.Trim();
        SelectedExploreGenre = value;
        if (value.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            SearchText = string.Empty;
            SelectedGenre = "All";
            IsExploreQueryActive = false;
            ActiveSection = MainSection.Explore;
            await DiscoverAsync();
            return;
        }

        SearchText = value;
        SelectedGenre = value;
        ActiveSection = MainSection.Explore;
        await SearchAsync();
    }

    private void SelectGenre(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre)) return;
        SelectedGenre = genre.Trim();
        ActiveSection = MainSection.Home;
    }

    private void ClearSearch()
    {
        SearchText = string.Empty;
        SelectedExploreGenre = "All";
        SearchResults.Clear();
        ExploreResults.Clear();
        ExploreChannels.Clear();
        ExploreMajorBrands.Clear();
        ExploreWithGenres.Clear();
        ExplorePresets.Clear();
        ExploreWorldwide.Clear();
        ExploreBrandClusters.Clear();
        OnPropertyChanged(nameof(ShowExploreNetworkRail));
        IsExploreQueryActive = false;
        StationCountLabel = "0 results";
        StatusText = "Explore stations, genres, countries, and live radio.";
        if (ActiveSection == MainSection.Explore)
            _ = DiscoverAsync();
    }

    private async Task RunQuickSearchAsync()
    {
        await WaitForStartupReadyAsync();
        var query = QuickSearchText.Trim();
        if (query.Length == 0) return;

        CancelQuickSearchOperation();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _quickSearchOperation = operation;
        try
        {
            var result = await _catalog.SearchAsync(query, operation.Token);
            if (!IsCurrentQuickSearchOperation(operation) || !result.IsSuccess) return;

            var localMatches = _availableStations.Where(station => StationRules.MatchesSearch(station, query));
            var clean = StationRules.DeduplicateAndSort(result.Value!
                .Concat(localMatches)
                .Where(station => StationRules.IsEligibleForContent(
                    station,
                    _settings.AllowReligiousContent,
                    _settings.AllowSportsContent,
                    _settings.AllowNewsContent)))
                .Take(40)
                .ToArray();

            QuickSearchSuggestions.Clear();
            foreach (var station in clean)
            {
                if (_rowCache.TryGetValue(station.Id, out var row)) row.UpdateStation(station);
                else _rowCache[station.Id] = CreateStationRow(station);
                QuickSearchSuggestions.Add(station);
            }

            StartArtworkLoad(clean
                .Select(station => _rowCache[station.Id])
                .Where(static row => row.ArtworkUri is null));
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        finally { CompleteQuickSearchOperation(operation); }
    }

    private async Task NavigateExploreAsync()
    {
        ActiveSection = MainSection.Explore;
        if (DiscoverResults.Count == 0 && !IsExploreQueryActive)
            await DiscoverAsync();
    }

    private async Task NavigateHomeAsync()
    {
        ActiveSection = MainSection.Home;
        await RebuildBrowseAsync(_lifetime.Token);
    }

    private async Task RefreshCountryAsync()
    {
        var country = SelectedCountry;
        if (country is null) return;
        _lastCatalogOperation = CatalogOperation.Refresh;
        CancelOperation();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        IsBusy = true;
        RecoveryStatus = string.Empty;
        StatusText = $"Refreshing {country.DisplayName}...";
        try
        {
            var result = await _catalog.RefreshCountryAsync(country, operation.Token);
            if (!IsCurrentOperation(operation)) return;
            if (!result.IsSuccess) { StatusText = result.Error!.Message; return; }
            await ReplaceStationsAsync(result.Value!, operation.Token, country);
            if (!IsCurrentOperation(operation)) return;
            StatusText = result.IsStaleFallback
                ? $"{Stations.Count} saved stations in {country.DisplayName}; refresh unavailable."
                : $"{Stations.Count} stations in {country.DisplayName}.";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) { if (IsCurrentOperation(operation)) StatusText = $"Could not refresh stations: {ex.Message}"; }
        finally { CompleteOperation(operation); }
    }

    private async Task DiscoverAsync()
    {
        await WaitForStartupReadyAsync();
        _lastCatalogOperation = CatalogOperation.Discover;
        CancelOperation();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        IsBusy = true;
        IsExploreQueryActive = false;
        StatusText = "Loading discover stations...";
        try
        {
            // Paint the last valid Explore snapshot immediately while the network refresh runs.
            // This keeps the discovery surface useful across offline starts and prevents a failed
            // parse/request from replacing a known-good feed with an empty page.
            var cached = await _catalog.LoadCachedDiscoverAsync(operation.Token);
            if (cached.Snapshot is { Stations.Count: > 0 } && IsCurrentOperation(operation))
            {
                await ReplaceTransientStationsAsync(
                    RankExploreStations(FilterStations(cached.Snapshot.Stations)),
                    operation.Token,
                    TransientSurface.Discover);
                StatusText = $"Showing saved discoveries from {cached.Snapshot.RetrievedAt.LocalDateTime:g}; refreshing...";
            }

            var result = await _catalog.DiscoverAsync(SelectedPreferredLanguages(), operation.Token);
            if (!IsCurrentOperation(operation)) return;
            if (!result.IsSuccess) { StatusText = result.Error!.Message; return; }
            await ReplaceTransientStationsAsync(RankExploreStations(FilterStations(result.Value!)), operation.Token, TransientSurface.Discover);
            if (!IsCurrentOperation(operation)) return;
            StatusText = result.IsStaleFallback
                ? $"{DiscoverResults.Count} saved discover stations; refresh unavailable."
                : $"{DiscoverResults.Count} discover stations.";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) { if (IsCurrentOperation(operation)) StatusText = $"Discover failed: {ex.Message}"; }
        finally { CompleteOperation(operation); }
    }

    private async Task PlayAsync(RadioStation? station)
    {
        if (station is null) return;
        try
        {
            await _playback.PlayAsync(station, Volume, _lifetime.Token);
            _queueIndex = IndexOf(_queue, station.Id);
            NowPlaying = station;
            LastPlayed = station;
            await _lastPlayedStore.SaveAsync(station, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    private void Pause() => _ = _playback.PauseAsync(_lifetime.Token);

    private void TogglePlayback()
    {
        if (_playback.Snapshot.State == PlaybackState.Playing)
        {
            Pause();
            return;
        }

        var station = NowPlaying ?? LastPlayed ?? _queue.FirstOrDefault();
        if (station is not null) _ = PlayAsync(station);
    }

    private void Stop() => _ = _playback.StopAsync(_lifetime.Token);

    async Task ILocalApiTarget.PlayAsync(string? stationId, CancellationToken cancellationToken)
    {
        await RunOnUiAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var station = FindStation(stationId) ?? NowPlaying ?? LastPlayed;
            if (station is not null) await PlayAsync(station);
        }).ConfigureAwait(false);
    }

    Task ILocalApiTarget.PauseAsync(CancellationToken cancellationToken)
    {
        return RunOnUiAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _playback.PauseAsync(_lifetime.Token);
        });
    }

    async Task ILocalApiTarget.ToggleAsync(CancellationToken cancellationToken)
    {
        await RunOnUiAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_playback.Snapshot.State == PlaybackState.Playing) await _playback.PauseAsync(_lifetime.Token);
            else
            {
                var station = NowPlaying ?? LastPlayed ?? _queue.FirstOrDefault();
                if (station is not null) await PlayAsync(station);
            }
        }).ConfigureAwait(false);
    }

    Task ILocalApiTarget.NextAsync(CancellationToken cancellationToken)
    {
        return RunOnUiAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NavigateQueueAsync(1);
        });
    }

    Task ILocalApiTarget.PreviousAsync(CancellationToken cancellationToken)
    {
        return RunOnUiAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NavigateQueueAsync(-1);
        });
    }

    Task ILocalApiTarget.SetVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        return RunOnUiAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Volume = Math.Clamp(volume, 0, 1);
            return _playback.SetVolumeAsync(Volume, _lifetime.Token);
        });
    }

    async Task<IReadOnlyList<RadioStation>> ILocalApiTarget.SearchAsync(string query, CancellationToken cancellationToken)
    {
        var result = await RunOnUiAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchText = query;
            await SearchAsync();
            return SearchResults.Select(static row => row.Station).ToArray();
        }).ConfigureAwait(false);
        return result;
    }

    async Task ILocalApiTarget.LoadCountryAsync(string countryId, CancellationToken cancellationToken)
    {
        await RunOnUiAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var country = Countries.FirstOrDefault(candidate => candidate.Id.Equals(countryId, StringComparison.OrdinalIgnoreCase));
            if (country is null) throw new InvalidOperationException("The country is not supported.");
            SelectedCountry = country;
            await LoadCountryAsync(country);
        }).ConfigureAwait(false);
    }

    async Task ILocalApiTarget.ApplyBrowseAsync(string? filter, string? sort, string? genre, CancellationToken cancellationToken)
    {
        await RunOnUiAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            SelectedFilter = ParseFilter(filter);
            SelectedSort = ParseSort(sort);
            SelectedGenre = string.IsNullOrWhiteSpace(genre) ? "All" : genre.Trim();
            await RebuildBrowseAsync(cancellationToken);
        }).ConfigureAwait(false);
    }

    IReadOnlyList<RadioStation> ILocalApiTarget.GetStations(string? scope, string? genre) => ReadOnUi(() => GetStationsOnUi(scope, genre));

    private IReadOnlyList<RadioStation> GetStationsOnUi(string? scope, string? genre)
    {
        var normalizedScope = scope?.Trim().ToLowerInvariant();
        return normalizedScope switch
        {
            "top" => TopStations.Select(static row => row.Station).ToArray(),
            "all" => _availableStations.ToArray(),
            "genre" => StationBrowseRules.Apply(_availableStations, _loadedCountry, StationFilterMode.All, SelectedSort ?? StationSortMode.Popularity, genre ?? SelectedGenre, Favorites.Select(static station => station.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)),
            "favorites" or "favourites" => Favorites.ToArray(),
            "search" => SearchResults.Select(static row => row.Station).ToArray(),
            "discover" => DiscoverResults.Select(static row => row.Station).ToArray(),
            _ => Stations.Select(static row => row.Station).ToArray()
        };
    }

    async Task ILocalApiTarget.ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await RunOnUiAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = settings.Normalize();
            await _settingsStore.SaveAsync(normalized, cancellationToken);
            ApplySettings(normalized);
        }).ConfigureAwait(false);
    }

    private Task PreviousAsync() => NavigateQueueAsync(-1);

    private Task NextAsync() => NavigateQueueAsync(1);

    private async Task NavigateQueueAsync(int direction)
    {
        var nextIndex = _queueIndex + direction;
        if (nextIndex < 0 || nextIndex >= _queue.Count)
        {
            StatusText = direction < 0 ? "Already at the first station." : "Already at the last station.";
            return;
        }

        await PlayAsync(_queue[nextIndex]);
    }

    private void ToggleFavorite(RadioStation? station)
    {
        if (station is null) return;
        var existing = Favorites.FirstOrDefault(f => f.Id.Equals(station.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) Favorites.Remove(existing); else Favorites.Add(station);
        if (_rowCache.TryGetValue(station.Id, out var row)) row.IsFavorite = existing is null;
        RebuildFavoriteRows();
        OnPropertyChanged(nameof(NowPlayingFavoriteGlyph));
        OnPropertyChanged(nameof(NowPlayingFavoriteAutomationName));
        _ = _favoritesStore.SaveAsync(Favorites.ToArray(), _lifetime.Token);
        if (SelectedFilter == StationFilterMode.FavoritesOnly) _ = RebuildBrowseAsync(_lifetime.Token);
    }

    public void ReorderFavorites(IReadOnlyList<string> orderedIds)
    {
        if (orderedIds.Count == 0 || Favorites.Count < 2) return;

        var currentById = Favorites.ToDictionary(static station => station.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<RadioStation>(Favorites.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in orderedIds)
        {
            if (seen.Add(id) && currentById.TryGetValue(id, out var station))
                ordered.Add(station);
        }

        // Preserve any item omitted by a virtualized ListView event rather than
        // losing a preset during a drag.
        foreach (var station in Favorites)
        {
            if (seen.Add(station.Id)) ordered.Add(station);
        }

        if (ordered.Count != Favorites.Count) return;
        var changed = ordered.Where((station, index) => !station.Id.Equals(Favorites[index].Id, StringComparison.OrdinalIgnoreCase)).Any();
        if (!changed) return;

        Favorites.Clear();
        foreach (var station in ordered) Favorites.Add(station);
        RebuildFavoriteRows();
        _ = _favoritesStore.SaveAsync(Favorites.ToArray(), _lifetime.Token);
    }

    private async Task ReplaceStationsAsync(IReadOnlyList<RadioStation> stations, CancellationToken cancellationToken, CountryPreset? country = null)
    {
        _availableStations = stations;
        _loadedCountry = country;
        OnPropertyChanged(nameof(TopStationsHeading));
        foreach (var station in stations)
        {
            if (_rowCache.TryGetValue(station.Id, out var row)) row.UpdateStation(station);
            else _rowCache[station.Id] = CreateStationRow(station);
        }
        RebuildFavoriteRows();
        Genres.Clear();
        Genres.Add("All");
        var eligibleStations = FilterStations(stations);
        var genreCandidates = MacGenreCategories
            .Concat(CountrySpecificGenres(_loadedCountry))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var genre in genreCandidates
            .Where(genre => eligibleStations.Any(station => StationBrowseRules.MatchesGenre(station, genre)))
            .OrderBy(static genre => genre, StringComparer.CurrentCultureIgnoreCase))
            Genres.Add(genre);
        if (!Genres.Contains(SelectedGenre, StringComparer.OrdinalIgnoreCase)) SelectedGenre = "All";
        await RebuildBrowseAsync(cancellationToken);
    }

    private static IReadOnlyList<string> CountrySpecificGenres(CountryPreset? country) => country?.Id switch
    {
        "uk" => ["Grime", "Garage", "Drum & Bass"],
        "us" => ["Americana", "Bluegrass", "Gospel", "Country"],
        "es" => ["Urban", "Latin", "Flamenco"],
        "fr" => ["Chanson"],
        "de" => ["Schlager"],
        "it" => ["Italo Disco"],
        "jp" => ["J-Pop", "City Pop"],
        "kr" => ["K-Pop"],
        "in" => ["Bollywood"],
        "br" => ["Sertanejo", "Samba", "Bossa Nova"],
        "mx" => ["Regional", "Banda", "Norteño"],
        "ar" => ["Tango"],
        "ng" => ["Afrobeats"],
        "za" => ["Amapiano"],
        "ie" => ["Celtic"],
        "au" => ["Aussie Rock"],
        _ => []
    };

    private Task ReplaceTransientStationsAsync(IReadOnlyList<RadioStation> stations, CancellationToken cancellationToken, TransientSurface surface)
    {
        var clean = StationRules.DeduplicateAndSort(stations);
        foreach (var station in clean)
        {
            if (_rowCache.TryGetValue(station.Id, out var row)) row.UpdateStation(station);
            else _rowCache[station.Id] = CreateStationRow(station);
        }

        var target = surface == TransientSurface.Search ? SearchResults : DiscoverResults;
        target.Clear();
        ExploreResults.Clear();
        ExploreChannels.Clear();
        ExploreMajorBrands.Clear();
        ExploreWithGenres.Clear();
        ExplorePresets.Clear();
        ExploreWorldwide.Clear();
        ExploreBrandClusters.Clear();
        OnPropertyChanged(nameof(ShowExploreNetworkRail));
        foreach (var station in clean) ExploreResults.Add(_rowCache[station.Id]);

        // These are the same distinct discovery rails used by the Mac Search page. Each rail is
        // shaped from the API result set independently; the page must not turn one flattened
        // slice into five differently-labelled copies of the same stations.
        var favoriteIds = Favorites.Select(static station => station.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var station in BuildExploreBrandRail(clean).Take(10))
            ExploreMajorBrands.Add(_rowCache[station.Id]);
        foreach (var station in BuildExploreGenreRail(clean).Take(10))
            ExploreWithGenres.Add(_rowCache[station.Id]);
        foreach (var station in clean.Where(station => favoriteIds.Contains(station.Id)).Take(10))
            ExplorePresets.Add(_rowCache[station.Id]);
        foreach (var station in BuildExploreWorldwideRail(clean).Take(12)) ExploreWorldwide.Add(_rowCache[station.Id]);
        if (surface == TransientSurface.Search && IsExploreQueryActive)
        {
            foreach (var group in clean
                         .GroupBy(ExploreBrandKey, StringComparer.Ordinal)
                         .Where(static group => group.Key.Length > 0)
                         .OrderByDescending(static group => group.Max(station => station.ClickCount ?? 0))
                         .Take(8))
            {
                var variants = group
                    .OrderByDescending(static station => station.Votes ?? 0)
                    .ThenByDescending(static station => station.ClickCount ?? 0)
                    .Select(station => _rowCache[station.Id])
                    .ToArray();
                if (variants.Length > 0) ExploreBrandClusters.Add(new ExploreBrandClusterViewModel(group.Key, variants));
            }
        }
        OnPropertyChanged(nameof(ShowExploreNetworkRail));
        _queue = clean;
        _queueIndex = NowPlaying is null ? -1 : IndexOf(clean, NowPlaying.Id);
        foreach (var station in clean) target.Add(_rowCache[station.Id]);

        StartArtworkLoad(target.Where(static row => row.ArtworkUri is null));
        return Task.CompletedTask;
    }

    private Task RebuildBrowseAsync(CancellationToken cancellationToken)
    {
        _browseOperation?.Cancel();
        _browseOperation?.Dispose();
        _browseOperation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _browseOperation.Token;

        if (_availableStations.Count == 0)
        {
            Stations.Clear();
            TopStations.Clear();
            OnPropertyChanged(nameof(TopStationsCountLabel));
            _queue = Array.Empty<RadioStation>();
            _queueIndex = -1;
            StationCountLabel = "0 shown of 0";
            StartArtworkLoad(Array.Empty<StationItemViewModel>());
            return Task.CompletedTask;
        }

        var eligibleStations = FilterStations(_availableStations);
        var favoriteIds = Favorites.Select(static station => station.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visible = StationBrowseRules.Apply(eligibleStations, _loadedCountry, SelectedFilter ?? StationFilterMode.All, SelectedSort ?? StationSortMode.Popularity, SelectedGenre, favoriteIds);

        Stations.Clear();
        _queue = visible;
        _queueIndex = NowPlaying is null ? -1 : IndexOf(visible, NowPlaying.Id);
        foreach (var station in visible) Stations.Add(_rowCache[station.Id]);
        StationCountLabel = $"{Stations.Count:N0} shown of {eligibleStations.Count:N0}";

        TopStations.Clear();
        FeaturedRows.Clear();
        if (_loadedCountry is not null)
        {
            foreach (var station in BuildTopStations(eligibleStations, _loadedCountry)) TopStations.Add(_rowCache[station.Id]);
            RebuildFeaturedRows();
        }
        OnPropertyChanged(nameof(TopStationsCountLabel));

        // Do not hold the catalogue operation open while remote broadcaster artwork is fetched.
        // The Mac view loads artwork lazily; Windows keeps the same user-visible behaviour by
        // making artwork an owned, cancellable background operation with the top rows first.
        StartArtworkLoad(TopStations.Concat(Stations).Distinct().Where(static row => row.ArtworkUri is null));
        return Task.CompletedTask;
    }

    private void StartArtworkLoad(IEnumerable<StationItemViewModel> source)
    {
        // Each navigation/search/country-change calls this. A new batch must never cancel an
        // older one that is still fetching rows the user can still see (e.g. an Explore brand
        // cluster row that isn't also in whatever surface triggered this call) - that previously
        // left cards permanently stuck on their initials fallback because the fetch that would
        // have filled them in was killed before it finished. The token here is shared across every
        // batch for app lifetime; only ClearArtwork() and shutdown cancel it.
        _artworkOperation ??= CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var operation = _artworkOperation;
        // The Mac view loads artwork as cards enter the visible presentation. Starting a
        // request for every station in a large country catalogue makes the Windows UI
        // compete with hundreds of remote favicon/logo requests while the user is trying
        // to search or play. Prioritise every curated card, then bound the catalogue
        // prefetch; rows without artwork keep their deterministic initials fallback.
        var rows = source.Distinct().Where(static row => row.ArtworkUri is null).Take(240).ToArray();
        _ = LoadArtworkAsync(rows, operation);
    }

    private async Task LoadArtworkAsync(IReadOnlyList<StationItemViewModel> rows, CancellationTokenSource operation)
    {
        try
        {
            await Parallel.ForEachAsync(rows, new ParallelOptions
            {
                CancellationToken = operation.Token,
                MaxDegreeOfParallelism = 4
            }, async (row, cancellationToken) =>
            {
                var artwork = await _artworkService.GetArtworkAsync(row.Station, cancellationToken).ConfigureAwait(false);
                if (artwork is null || cancellationToken.IsCancellationRequested) return;
                await RunOnUiAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested) return Task.CompletedTask;
                    row.ArtworkUri = artwork;
                    if (NowPlaying?.Id.Equals(row.Station.Id, StringComparison.OrdinalIgnoreCase) == true)
                        NowPlayingArtworkUri = artwork;
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch { /* Artwork is optional; the deterministic fallback remains visible. */ }
    }

    private static IReadOnlyList<RadioStation> BuildTopStations(IReadOnlyList<RadioStation> stations, CountryPreset country) =>
        CuratedStationRules.Build(stations, country, CuratedTopStationLimit);

    private void RebuildFeaturedRows()
    {
        FeaturedRows.Clear();
        var rowSize = Math.Max(1, FeaturedGridColumns);
        for (var index = 0; index < TopStations.Count; index += rowSize)
        {
            var row = new ObservableCollection<StationItemViewModel>();
            foreach (var station in TopStations.Skip(index).Take(rowSize)) row.Add(station);
            FeaturedRows.Add(row);
        }
    }

    private void OnPlaybackChanged(object? sender, PlaybackSnapshot snapshot)
    {
        if (_uiQueue is not null && !_uiQueue.HasThreadAccess)
        {
            _uiQueue.TryEnqueue(() => ApplyPlaybackSnapshot(snapshot));
            return;
        }

        ApplyPlaybackSnapshot(snapshot);
    }

    private void ApplyPlaybackSnapshot(PlaybackSnapshot snapshot)
    {
        PlaybackState = snapshot.State;
        NowPlaying = snapshot.Station;
        HasBufferingProgress = snapshot.BufferingProgress is { };
        BufferingProgress = Math.Clamp(snapshot.BufferingProgress ?? 0, 0, 1);
        TelemetryLabel = snapshot.BufferingProgress is { } buffer
            ? $"Buffer health {buffer:P0}; throughput not measured"
            : snapshot.Bitrate is { } bitrate
                ? $"Catalogue bitrate: {bitrate} kbps (not measured)"
                : snapshot.State == PlaybackState.Playing ? "Audio active (throughput not measured)" : "No audio telemetry";
        TelemetryDisplayLabel = snapshot.BufferingProgress is { } displayBuffer
            ? $"Buffer {displayBuffer:P0}"
            : snapshot.Bitrate is int displayBitrate && displayBitrate > 0 ? $"{displayBitrate} kbps" : "-- kbps";
        foreach (var row in _rowCache.Values)
        {
            row.IsPlaying = snapshot.Station is not null &&
                            snapshot.Station.Id.Equals(row.Station.Id, StringComparison.OrdinalIgnoreCase) &&
                            snapshot.State is PlaybackState.Playing or PlaybackState.Loading;
        }
        if (snapshot.Station is not null && _rowCache.TryGetValue(snapshot.Station.Id, out var currentRow))
            NowPlayingArtworkUri = currentRow.ArtworkUri;
        if (snapshot.Error is not null) StatusText = snapshot.Error;
    }

    private StationItemViewModel CreateStationRow(RadioStation station)
    {
        return new StationItemViewModel(station)
        {
            ListCardWidth = ListCardWidth,
            TopCardWidth = TopCardWidth,
            IsFavorite = Favorites.Any(favorite => favorite.Id.Equals(station.Id, StringComparison.OrdinalIgnoreCase)),
            IsPlaying = NowPlaying?.Id.Equals(station.Id, StringComparison.OrdinalIgnoreCase) == true &&
                        PlaybackState is PlaybackState.Playing or PlaybackState.Loading
        };
    }

    public void ApplyLayoutWidths()
    {
        foreach (var row in _rowCache.Values)
        {
            row.ListCardWidth = ListCardWidth;
            row.TopCardWidth = TopCardWidth;
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        var previous = _settings;
        _settings = settings.Normalize();
        ApplyPreferredLanguages(_settings.PreferredLanguages);
        _playback.ApplySettings(_settings);
        Volume = _settings.Volume;
        _ = _playback.SetVolumeAsync(_settings.Volume, _lifetime.Token);
        SettingsChanged?.Invoke(this, _settings);
        if (SelectedCountry?.Id != _settings.DefaultCountryId)
        {
            SelectedCountry = Countries.FirstOrDefault(country => country.Id == _settings.DefaultCountryId) ?? SelectedCountry;
            if (SelectedCountry is not null && !IsBusy) _ = LoadCountryCommand.ExecuteAsync(SelectedCountry);
        }
        else if (previous.AllowReligiousContent != _settings.AllowReligiousContent ||
                 previous.AllowSportsContent != _settings.AllowSportsContent ||
                 previous.AllowNewsContent != _settings.AllowNewsContent ||
                 !LanguagePreferenceCatalog.Normalize(previous.PreferredLanguages).SequenceEqual(
                     LanguagePreferenceCatalog.Normalize(_settings.PreferredLanguages), StringComparer.OrdinalIgnoreCase))
        {
            RebuildGenresForCurrentSettings();
            _ = RebuildBrowseAsync(_lifetime.Token);
            if (ActiveSection == MainSection.Explore)
                _ = IsExploreQueryActive ? SearchAsync() : DiscoverAsync();
        }
    }

    private void RebuildGenresForCurrentSettings()
    {
        Genres.Clear();
        Genres.Add("All");
        var eligibleStations = FilterStations(_availableStations);
        var genreCandidates = MacGenreCategories
            .Concat(CountrySpecificGenres(_loadedCountry))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var genre in genreCandidates
            .Where(genre => eligibleStations.Any(station => StationBrowseRules.MatchesGenre(station, genre)))
            .OrderBy(static genre => genre, StringComparer.CurrentCultureIgnoreCase))
            Genres.Add(genre);
        if (!Genres.Contains(SelectedGenre, StringComparer.OrdinalIgnoreCase)) SelectedGenre = "All";
    }

    public void SavePaneWidths(double leftWidth, double rightWidth)
    {
        var normalizedLeft = Math.Clamp(leftWidth, 180, 340);
        var normalizedRight = Math.Clamp(rightWidth, 220, 380);
        if (Math.Abs(_settings.LeftPaneWidth - normalizedLeft) < 0.5 &&
            Math.Abs(_settings.RightPaneWidth - normalizedRight) < 0.5) return;

        _settings = (_settings with
        {
            LeftPaneWidth = normalizedLeft,
            RightPaneWidth = normalizedRight
        }).Normalize();
        _ = _settingsStore.SaveAsync(_settings, _lifetime.Token);
    }

    public void ClearFavorites()
    {
        Favorites.Clear();
        FavoriteRows.Clear();
        foreach (var row in _rowCache.Values) row.IsFavorite = false;
        OnPropertyChanged(nameof(NowPlayingFavoriteGlyph));
        OnPropertyChanged(nameof(NowPlayingFavoriteAutomationName));
        _ = _favoritesStore.SaveAsync(Favorites.ToArray(), _lifetime.Token);
        if (SelectedFilter == StationFilterMode.FavoritesOnly) _ = RebuildBrowseAsync(_lifetime.Token);
    }

    public void ClearLastPlayed() => LastPlayed = null;

    public void ClearArtwork()
    {
        // _artworkOperation is a long-lived, shared token now (see StartArtworkLoad) - once
        // cancelled it can never un-cancel, so it must be disposed and nulled here, not just
        // cancelled, or every artwork load after this point would silently no-op forever.
        _artworkOperation?.Cancel();
        _artworkOperation?.Dispose();
        _artworkOperation = null;
        foreach (var row in _rowCache.Values) row.ArtworkUri = null;
        NowPlayingArtworkUri = null;
        // Clearing the persistent cache is an explicit user refresh action,
        // not a request to leave every visible card on its initials fallback.
        // Requeue the visible/priority rows immediately so the UI repopulates
        // from the same cancellable artwork pipeline used after a country load.
        StartArtworkLoad(FavoriteRows.Concat(TopStations).Concat(Stations).Distinct());
    }

    private void RebuildFavoriteRows()
    {
        _rebuildingFavoriteRows = true;
        try
        {
            FavoriteRows.Clear();
            foreach (var favorite in Favorites)
            {
                if (!_rowCache.TryGetValue(favorite.Id, out var row))
                {
                    row = CreateStationRow(favorite);
                    _rowCache[favorite.Id] = row;
                }
                FavoriteRows.Add(row);
            }
        }
        finally { _rebuildingFavoriteRows = false; }
        StartArtworkLoad(FavoriteRows.Concat(TopStations).Concat(Stations).Distinct().Where(static row => row.ArtworkUri is null));
    }

    private void FavoriteRows_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildingFavoriteRows || FavoriteRows.Count != Favorites.Count) return;

        for (var targetIndex = 0; targetIndex < FavoriteRows.Count; targetIndex++)
        {
            var targetId = FavoriteRows[targetIndex].Station.Id;
            var currentIndex = -1;
            for (var index = 0; index < Favorites.Count; index++)
            {
                if (Favorites[index].Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = index;
                    break;
                }
            }

            if (currentIndex == targetIndex) continue;
            if (currentIndex < 0) return;
            Favorites.Move(currentIndex, targetIndex);
        }

        _ = _favoritesStore.SaveAsync(Favorites.ToArray(), _lifetime.Token);
    }

    private void Genres_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ExploreCategoryRows = Math.Max(1, (int)Math.Ceiling(Math.Max(1, Genres.Count) / (double)Math.Max(1, ExploreCategoryColumns)));
        RebuildExploreGenreLayout();
    }

    public void RebuildExploreGenreLayout()
    {
        var source = Genres.ToArray();
        var columns = Math.Max(1, ExploreCategoryColumns);
        var rows = Math.Max(1, (int)Math.Ceiling(Math.Max(1, source.Length) / (double)columns));
        var arranged = new List<string>(source.Length);

        // The panel fills vertically to guarantee complete columns. Reorder the backing
        // sequence so the visual result still reads left-to-right across each row.
        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                var index = row * columns + column;
                if (index < source.Length) arranged.Add(source[index]);
            }
        }

        if (ExploreGenres.SequenceEqual(arranged, StringComparer.Ordinal)) return;
        ExploreGenres.Clear();
        foreach (var genre in arranged) ExploreGenres.Add(genre);
    }

    private IReadOnlyList<RadioStation> FilterStations(IReadOnlyList<RadioStation> stations) =>
        stations.Where(station => StationRules.IsEligibleForContent(
            station,
            _settings.AllowReligiousContent,
            _settings.AllowSportsContent,
            _settings.AllowNewsContent)).ToArray();

    private IReadOnlyList<RadioStation> RankExploreStations(IReadOnlyList<RadioStation> stations)
    {
        var preferred = LanguagePreferenceCatalog.Normalize(_settings.PreferredLanguages);
        return stations
            .Select(station => (Station: station, Score: LanguagePreferenceScore(station, preferred)))
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Station.ClickCount ?? 0)
            .ThenBy(static item => item.Station.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Station)
            .ToArray();
    }

    private IReadOnlyList<RadioStation> BuildExploreBrandRail(IReadOnlyList<RadioStation> stations)
    {
        return stations
            .Select(station => (Station: station, Key: ExploreBrandKey(station)))
            .Where(static item => item.Key.Length > 0)
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .Select(static item => item.Station)
                .OrderByDescending(ExploreRank)
                .ThenBy(static station => station.Name, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(ExploreRank)
            .ThenBy(static station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<RadioStation> BuildExploreGenreRail(IReadOnlyList<RadioStation> stations) =>
        stations
            .Where(static station => station.Genres.Count > 0)
            .OrderByDescending(ExploreRank)
            .ThenBy(static station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IReadOnlyList<RadioStation> BuildExploreWorldwideRail(IReadOnlyList<RadioStation> stations)
    {
        // The Mac Explore page shows one representative per country. Keeping that constraint here
        // prevents the worldwide rail from becoming another arbitrary popularity slice dominated by
        // one broadcaster or one country.
        return stations
            .Select(station => (Station: station, Key: ExploreCountryKey(station)))
            .Where(static item => item.Key.Length > 0)
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .Select(static item => item.Station)
                .OrderByDescending(ExploreRank)
                .ThenBy(static station => station.Name, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(ExploreRank)
            .ThenBy(static station => station.Country, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private int ExploreRank(RadioStation station) =>
        LanguagePreferenceScore(station, LanguagePreferenceCatalog.Normalize(_settings.PreferredLanguages)) * 100_000
        + (station.ClickCount ?? 0)
        + (station.Votes ?? 0) * 3;

    private static string ExploreCountryKey(RadioStation station)
    {
        if (!string.IsNullOrWhiteSpace(station.CountryCode))
            return station.CountryCode.Trim().ToUpperInvariant();
        return StationRules.Normalize(station.Country);
    }

    private string ExploreBrandKey(RadioStation station)
    {
        // Canonicalize recognized parent networks using the active country's metadata. This keeps
        // a query for Capital from presenting Capital London/Manchester/Glasgow as unrelated
        // brands, while still allowing a real sub-brand such as Kisstory through when it is an
        // actual catalogue result. The top-brand metadata identifies families; it does not choose
        // the Explore result set or hard-code the API's ranking.
        var country = Countries.FirstOrDefault(candidate =>
            (!string.IsNullOrWhiteSpace(station.CountryCode) && candidate.CountryCode.Equals(station.CountryCode, StringComparison.OrdinalIgnoreCase)) ||
            StationRules.Normalize(candidate.ApiName).Equals(StationRules.Normalize(station.Country), StringComparison.Ordinal) ||
            StationRules.Normalize(candidate.DisplayName).Equals(StationRules.Normalize(station.Country), StringComparison.Ordinal));
        // Never use the currently loaded Home country as a fallback for a global Explore result.
        // Doing so incorrectly grouped stations from other countries under UK brand metadata.
        var brands = country?.TopBrands;
        if (brands is not null)
        {
            var brandIndex = StationRules.BestBrandIndex(station, brands);
            if (brandIndex >= 0) return brands[brandIndex];
        }

        var normalized = StationRules.Normalize(station.Name);
        if (normalized.Length == 0) return string.Empty;
        var withoutQuality = System.Text.RegularExpressions.Regex.Replace(normalized, @"\b\d{2,3}\s?(kb|kbps|kbit)\b", string.Empty);
        withoutQuality = System.Text.RegularExpressions.Regex.Replace(withoutQuality, @"\b(aac\+?|mp3|ogg|stream|live|hq|lq)\b", string.Empty);
        return withoutQuality.Trim();
    }

    private static int LanguagePreferenceScore(RadioStation station, IReadOnlyList<string> preferred)
    {
        if (preferred.Count == 0 || string.IsNullOrWhiteSpace(station.Language)) return 0;
        var language = station.Language.ToLowerInvariant();
        for (var index = 0; index < preferred.Count; index++)
        {
            var code = preferred[index];
            var displayName = LanguagePreferenceCatalog.Options
                .FirstOrDefault(option => option.Code.Equals(code, StringComparison.OrdinalIgnoreCase)).DisplayName;
            if (language.Contains(code, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(displayName) && language.Contains(displayName, StringComparison.OrdinalIgnoreCase)))
                return preferred.Count - index;
        }

        return 0;
    }

    private static int IndexOf(IReadOnlyList<RadioStation> stations, string stationId)
    {
        for (var index = 0; index < stations.Count; index++)
        {
            if (stations[index].Id.Equals(stationId, StringComparison.OrdinalIgnoreCase)) return index;
        }
        return -1;
    }

    private static StationFilterMode ParseFilter(string? value)
    {
        var normalized = NormalizeBrowseValue(value);
        return normalized switch
        {
            "favorites" or "favourites" or "presetsonly" => StationFilterMode.FavoritesOnly,
            "popular" or "popularpicks" => StationFilterMode.Popular,
            "reliable" or "stablestreams" => StationFilterMode.Reliable,
            "majorbrands" or "bignetworks" or "top" => StationFilterMode.MajorBrands,
            "withgenres" or "genre" => StationFilterMode.WithGenres,
            _ => StationFilterMode.All
        };
    }

    private static StationSortMode ParseSort(string? value)
    {
        var normalized = NormalizeBrowseValue(value);
        return normalized switch
        {
            "name" or "alphabetical" => StationSortMode.Name,
            "clicks" or "mostclicked" or "trending" or "mostlistened" => StationSortMode.Clicks,
            "popularity" or "mostpopular" => StationSortMode.Popularity,
            "bitratedescending" or "highestbitrate" => StationSortMode.BitrateHigh,
            "bitrateascending" or "lowestbitrate" => StationSortMode.BitrateLow,
            "favoritesfirst" => StationSortMode.FavoritesFirst,
            "networksfirst" => StationSortMode.NetworksFirst,
            _ => StationSortMode.Popularity
        };
    }

    private static string NormalizeBrowseValue(string? value) => new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private enum CatalogOperation
    {
        None,
        Country,
        Refresh,
        Search,
        Discover
    }

    private enum TransientSurface
    {
        Search,
        Discover
    }

    private RadioStation? FindStation(string? stationId)
    {
        if (string.IsNullOrWhiteSpace(stationId)) return null;
        return ((ILocalApiTarget)this).Stations
            .Concat(((ILocalApiTarget)this).Favorites)
            .FirstOrDefault(station => station.Id.Equals(stationId, StringComparison.OrdinalIgnoreCase));
    }

    private void CancelOperation()
    {
        _operation?.Cancel();
        _operation?.Dispose();
        _operation = null;
    }

    private void CancelQuickSearchOperation()
    {
        _quickSearchOperation?.Cancel();
        _quickSearchOperation?.Dispose();
        _quickSearchOperation = null;
    }

    private bool IsCurrentOperation(CancellationTokenSource operation) =>
        ReferenceEquals(_operation, operation) && !_lifetime.IsCancellationRequested;

    private bool IsCurrentQuickSearchOperation(CancellationTokenSource operation) =>
        ReferenceEquals(_quickSearchOperation, operation) && !_lifetime.IsCancellationRequested;

    private void CompleteOperation(CancellationTokenSource operation)
    {
        if (!ReferenceEquals(_operation, operation)) return;
        _operation = null;
        IsBusy = false;
    }

    private void CompleteQuickSearchOperation(CancellationTokenSource operation)
    {
        if (ReferenceEquals(_quickSearchOperation, operation)) _quickSearchOperation = null;
    }

    private Task RunOnUiAsync(Func<Task> operation)
    {
        if (_uiQueue is null || _uiQueue.HasThreadAccess) return operation();
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_uiQueue.TryEnqueue(async () =>
        {
            try { await operation(); completion.SetResult(null); }
            catch (Exception ex) { completion.SetException(ex); }
        })) completion.SetException(new InvalidOperationException("The application UI dispatcher is unavailable."));
        return completion.Task;
    }

    private Task<T> RunOnUiAsync<T>(Func<Task<T>> operation)
    {
        if (_uiQueue is null || _uiQueue.HasThreadAccess) return operation();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_uiQueue.TryEnqueue(async () =>
        {
            try { completion.SetResult(await operation()); }
            catch (Exception ex) { completion.SetException(ex); }
        })) completion.SetException(new InvalidOperationException("The application UI dispatcher is unavailable."));
        return completion.Task;
    }

    private T ReadOnUi<T>(Func<T> read)
    {
        if (_uiQueue is null || _uiQueue.HasThreadAccess) return read();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_uiQueue.TryEnqueue(() =>
        {
            try { completion.SetResult(read()); }
            catch (Exception ex) { completion.SetException(ex); }
        })) completion.SetException(new InvalidOperationException("The application UI dispatcher is unavailable."));
        return completion.Task.GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _backgroundRefreshCancellation?.Cancel();
        CancelOperation();
        CancelQuickSearchOperation();
        _browseOperation?.Cancel();
        _browseOperation?.Dispose();
        _artworkOperation?.Cancel();
        _artworkOperation?.Dispose();
        if (_backgroundRefreshTask is not null)
        {
            try { await _backgroundRefreshTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _backgroundRefreshCancellation?.Dispose();
        _playback.SnapshotChanged -= OnPlaybackChanged;
        _lifetime.Dispose();
    }
}
