namespace GlassRadio;

/// <summary>
/// A logical broadcaster/brand result for Explore. The primary record is what is shown in the
/// card; variants remain available for the detail journey and playback fallback discovery.
/// </summary>
public sealed class ExploreBrandClusterViewModel
{
    public ExploreBrandClusterViewModel(string name, IReadOnlyList<StationItemViewModel> variants)
    {
        Name = name;
        Variants = variants;
        Primary = variants[0];
    }

    public string Name { get; }
    public StationItemViewModel Primary { get; }
    public IReadOnlyList<StationItemViewModel> Variants { get; }
    public string VariantLabel => Variants.Count == 1 ? "One station" : $"{Variants.Count} stations and streams";
    public string Location => Primary.Location;
}
