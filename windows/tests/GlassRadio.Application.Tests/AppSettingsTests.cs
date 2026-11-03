using GlassRadio.Application.Contracts;
using Xunit;

namespace GlassRadio.Application.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void SettingsNormalizeToProductRanges()
    {
        var normalized = new AppSettings(2, 99, " ").Normalize();
        Assert.Equal(1, normalized.Volume);
        Assert.Equal(30, normalized.ConnectionTimeoutSeconds);
        Assert.Equal("uk", normalized.DefaultCountryId);
    }

    [Fact]
    public void RecentCountriesNormalizeToEightUniqueIds()
    {
        var ids = Enumerable.Range(0, 10).Select(index => index == 9 ? " UK " : $"c{index}").Concat(["c1"]).ToArray();
        var normalized = new AppSettings(0.5, 12, "uk", RecentCountryIds: ids).Normalize();

        Assert.Equal(8, normalized.RecentCountryIds!.Count);
        Assert.Equal("c0", normalized.RecentCountryIds[0]);
        Assert.Equal(7373, normalized.LocalApiPort);
        Assert.DoesNotContain("c1", normalized.RecentCountryIds.Skip(8));
    }

    [Fact]
    public void LocalApiIsDisabledAndLanAccessIsDisabledByDefault()
    {
        var settings = new AppSettings();

        Assert.False(settings.LocalApiEnabled);
        Assert.False(settings.LocalApiAllowLanAccess);
        Assert.Equal(7373, settings.LocalApiPort);
    }

    [Fact]
    public void ReligiousAndSportsContentAreDisabledByDefault()
    {
        var settings = new AppSettings();

        Assert.False(settings.AllowReligiousContent);
        Assert.False(settings.AllowSportsContent);
        Assert.True(settings.AllowNewsContent);
    }

    [Fact]
    public void OnboardingIsRequiredByDefaultAndPersistsAsCompleted()
    {
        Assert.False(new AppSettings().OnboardingCompleted);
        Assert.True(new AppSettings(OnboardingCompleted: true).Normalize().OnboardingCompleted);
    }

    [Fact]
    public void PaneWidthsAreClampedToSafeSplitterBounds()
    {
        var normalized = new AppSettings(LeftPaneWidth: 1, RightPaneWidth: 999).Normalize();

        Assert.Equal(180, normalized.LeftPaneWidth);
        Assert.Equal(380, normalized.RightPaneWidth);
    }

    [Fact]
    public void HomeLayoutSectionsAreVisibleByDefaultWithListView()
    {
        var settings = new AppSettings();

        Assert.True(settings.ShowTopStations);
        Assert.True(settings.ShowFavoritesNav);
        Assert.True(settings.ShowCountriesPanel);
        Assert.True(settings.ShowGenreFilter);
        Assert.Equal(AllStationsViewStyle.List, settings.AllStationsViewStyle);
    }

    [Fact]
    public void NormalizeCoercesAnUndefinedViewStyleToList()
    {
        var normalized = new AppSettings(AllStationsViewStyle: (AllStationsViewStyle)99).Normalize();

        Assert.Equal(AllStationsViewStyle.List, normalized.AllStationsViewStyle);
    }

    [Fact]
    public void PreferredLanguagesNormalizeWithoutOverwritingMultipleChoices()
    {
        var normalized = new AppSettings(PreferredLanguages: [" EN ", "es", "en", " ", "ur"]).Normalize();

        Assert.Equal(["en", "es", "ur"], normalized.PreferredLanguages);
    }

    [Theory]
    [InlineData("Explore", "Explore")]
    [InlineData("Search", "Explore")]
    [InlineData("Discover", "Explore")]
    [InlineData("unknown", "Home")]
    [InlineData("", "Home")]
    public void LastActiveSectionNormalizesToTheUnifiedShellRoutes(string value, string expected)
    {
        Assert.Equal(expected, new AppSettings(LastActiveSection: value).Normalize().LastActiveSection);
    }
}
