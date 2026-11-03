namespace GlassRadio.Infrastructure.Windows.Storage;

public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassRadio");
        Snapshots = Path.Combine(Root, "snapshots");
        Artwork = Path.Combine(Root, "artwork");
        Settings = Path.Combine(Root, "settings.json");
        Favorites = Path.Combine(Root, "favorites.json");
        LastPlayed = Path.Combine(Root, "last-played.json");
        LocalApiKey = Path.Combine(Root, "local-api-key.bin");
    }

    public string Root { get; }
    public string Snapshots { get; }
    public string Artwork { get; }
    public string Settings { get; }
    public string Favorites { get; }
    public string LastPlayed { get; }
    public string LocalApiKey { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Snapshots);
        Directory.CreateDirectory(Artwork);
    }
}
