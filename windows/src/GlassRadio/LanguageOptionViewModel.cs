using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GlassRadio;

public sealed partial class LanguageOptionViewModel(string code, string displayName) : ObservableObject
{
    public string Code { get; } = code;
    public string DisplayName { get; } = displayName;

    [ObservableProperty]
    private bool isSelected;
}

public static class LanguagePreferenceCatalog
{
    public static IReadOnlyList<(string Code, string DisplayName)> Options { get; } =
    [
        ("en", "English"), ("es", "Spanish"), ("ur", "Urdu"), ("hi", "Hindi"),
        ("pa", "Punjabi"), ("fr", "French"), ("de", "German"), ("it", "Italian"),
        ("pt", "Portuguese"), ("ar", "Arabic"), ("bn", "Bengali"), ("nl", "Dutch"),
        ("pl", "Polish"), ("tr", "Turkish"), ("ru", "Russian"), ("zh", "Chinese"),
        ("ja", "Japanese"), ("ko", "Korean"), ("sw", "Swahili"), ("ro", "Romanian")
    ];

    public static string DetectDefaultLanguage()
    {
        var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return Options.Any(option => option.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ? code : "en";
    }

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? values)
    {
        var selected = (values ?? Array.Empty<string>())
            .Select(static value => value.Trim().ToLowerInvariant())
            .Where(value => Options.Any(option => option.Code.Equals(value, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return selected.Length > 0 ? selected : [DetectDefaultLanguage()];
    }
}
