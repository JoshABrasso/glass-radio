using CommunityToolkit.Mvvm.ComponentModel;
using GlassRadio.Domain;

namespace GlassRadio;

public sealed partial class OnboardingCountryItemViewModel(CountryPreset country) : ObservableObject
{
    public CountryPreset Country { get; } = country;
    public string DisplayName => Country.DisplayName;
    public string FlagEmoji => Country.FlagEmoji;
    public string FlagImagePath => Country.FlagImagePath;
    public string CountryCode => Country.CountryCode;

    [ObservableProperty]
    private bool isSelected;

    public string SelectionAutomationName => IsSelected
        ? $"Remove {DisplayName} from the full library selection"
        : $"Add {DisplayName} to the full library selection";
    public string SelectionGlyph => IsSelected ? "\uE73E" : "\uE739";

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectionAutomationName));
        OnPropertyChanged(nameof(SelectionGlyph));
    }
}
