using CommunityToolkit.Mvvm.ComponentModel;
using GlassRadio.Domain;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GlassRadio;

public sealed partial class StationItemViewModel(RadioStation station) : ObservableObject
{
    public RadioStation Station { get; private set; } = station;
    public string Name => Station.Name;
    public string Country => Station.Country;
    public string CountryFlag => BuildCountryFlag(Station.CountryCode);
    public bool HasCountryFlag => CountryFlag.Length > 0;
    // WinUI does not reliably render regional-indicator emoji as flags. Reuse the
    // same real flag assets used by the country browser instead.
    public string CountryFlagImagePath => BuildCountryFlagImagePath(Station.CountryCode);
    public string Location => Station.LocationText;
    public string GenresText => string.Join(" · ", Station.Genres.Take(3));
    public bool HasGenres => Station.Genres.Count > 0;
    public string ArtworkInitials => BuildInitials(Name);
    public SolidColorBrush ArtworkBrush { get; } = CreateArtworkBrush();
    // Station cards use the same Segoe MDL2 glyphs as the last known-good
    // visual-fixes-20261003 build. The player pill has its own Fluent glyph
    // property and font; these card glyphs must not be mixed with it.
    public string PlayGlyph => IsPlaying ? "\uE769" : "\uE768";
    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
    public string PlayAutomationName => IsPlaying ? $"Pause {Name}" : $"Play {Name}";
    public string FavoriteAutomationName => IsFavorite ? $"Remove {Name} from favourites" : $"Add {Name} to favourites";

    [ObservableProperty]
    private double listCardWidth = 360;

    [ObservableProperty]
    private double topCardWidth = 184;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private bool isFavorite;

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayGlyph));
        OnPropertyChanged(nameof(PlayAutomationName));
    }

    partial void OnIsFavoriteChanged(bool value)
    {
        OnPropertyChanged(nameof(FavoriteGlyph));
        OnPropertyChanged(nameof(FavoriteAutomationName));
    }

    [ObservableProperty]
    private Uri? artworkUri;

    [ObservableProperty]
    private bool hasArtwork;

    partial void OnArtworkUriChanged(Uri? value) => HasArtwork = value is not null;

    public void UpdateStation(RadioStation station)
    {
        ArgumentNullException.ThrowIfNull(station);
        if (!Station.Id.Equals(station.Id, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The replacement station must have the same identity.", nameof(station));

        var artworkSourceChanged = !string.Equals(Station.Favicon, station.Favicon, StringComparison.OrdinalIgnoreCase) ||
                                   !string.Equals(Station.Homepage, station.Homepage, StringComparison.OrdinalIgnoreCase);
        Station = station;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Country));
        OnPropertyChanged(nameof(CountryFlag));
        OnPropertyChanged(nameof(HasCountryFlag));
        OnPropertyChanged(nameof(CountryFlagImagePath));
        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(GenresText));
        OnPropertyChanged(nameof(HasGenres));
        OnPropertyChanged(nameof(ArtworkInitials));
        OnPropertyChanged(nameof(PlayAutomationName));
        OnPropertyChanged(nameof(FavoriteAutomationName));

        // A refresh may return a better favicon or broadcaster homepage for the
        // same station UUID. Do not keep rendering the old image while the v6
        // artwork resolver is given the new metadata.
        if (artworkSourceChanged) ArtworkUri = null;
    }

    public static string BuildInitials(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "GR";
        if (words.Length == 1) return new string(words[0].Take(2).ToArray()).ToUpperInvariant();
        return string.Concat(words[0][0], words[1][0]).ToUpperInvariant();
    }

    private static string BuildCountryFlag(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return string.Empty;
        var code = countryCode.Trim().ToUpperInvariant();
        if (code.Length != 2 || code.Any(character => character is < 'A' or > 'Z')) return string.Empty;
        return string.Concat(code.Select(character => char.ConvertFromUtf32(0x1F1E6 + character - 'A')));
    }

    private static string BuildCountryFlagImagePath(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return string.Empty;
        var code = countryCode.Trim().ToLowerInvariant();
        return code.Length == 2 && code.All(character => character is >= 'a' and <= 'z')
            ? $"https://flagcdn.com/w160/{code}.png"
            : string.Empty;
    }

    private static SolidColorBrush CreateArtworkBrush()
    {
        if (Microsoft.UI.Xaml.Application.Current.Resources["GlassArtworkFallbackBrush"] is SolidColorBrush brush)
            return brush;
        return new SolidColorBrush();
    }
}
