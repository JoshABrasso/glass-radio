using GlassRadio.Application.Contracts;
using GlassRadio.Domain;
using GlassRadio.Infrastructure.Windows.Storage;
using GlassRadio.Infrastructure.Windows.Artwork;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Xunit;

namespace GlassRadio.Infrastructure.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public void LocalApiKeyStorePersistsOnePerUserSecretWithProtectedAtRestBytes()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "api-key-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var first = new LocalApiKeyStore(paths).GetOrCreate();
            var second = new LocalApiKeyStore(paths).GetOrCreate();

            Assert.Equal(first, second);
            Assert.True(File.Exists(paths.LocalApiKey));
            Assert.DoesNotContain(first, Convert.ToBase64String(File.ReadAllBytes(paths.LocalApiKey)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotStoreFallsBackToLastKnownGoodCopy()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "persistence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var store = new SnapshotStore(paths);
            var first = new CountrySnapshot(1, "uk", DateTimeOffset.UtcNow, [Station("first")]);
            var second = new CountrySnapshot(1, "uk", DateTimeOffset.UtcNow, [Station("second")]);

            await store.SaveAsync(first, CancellationToken.None);
            await store.SaveAsync(second, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(paths.Snapshots, "uk.json"), "{ not valid json");

            var recovery = await store.LoadWithRecoveryAsync("uk", CancellationToken.None);
            var recovered = recovery.Snapshot;

            Assert.NotNull(recovered);
            Assert.Equal(SnapshotLoadStatus.RecoveredFromBackup, recovery.Status);
            Assert.Equal("first", Assert.Single(recovered!.Stations).Name);
            Assert.Equal("first", Assert.Single((await store.LoadAsync("uk", CancellationToken.None))!.Stations).Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotStoreQuarantinesInvalidPrimaryAndBackup()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "persistence-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var store = new SnapshotStore(paths);
            paths.EnsureDirectories();
            await File.WriteAllTextAsync(Path.Combine(paths.Snapshots, "uk.json"), "{ not valid json");
            await File.WriteAllTextAsync(Path.Combine(paths.Snapshots, "uk.json.bak"), "{ also not valid json");

            var result = await store.LoadWithRecoveryAsync("uk", CancellationToken.None);

            Assert.Null(result.Snapshot);
            Assert.Equal(SnapshotLoadStatus.Corrupt, result.Status);
            Assert.False(File.Exists(Path.Combine(paths.Snapshots, "uk.json")));
            Assert.False(File.Exists(Path.Combine(paths.Snapshots, "uk.json.bak")));
            Assert.Equal(2, Directory.EnumerateFiles(paths.Snapshots, "uk.corrupt-*.json").Count());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotStoreClearRemovesRecoveryAndAtomicWriteFiles()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "persistence-clear-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var store = new SnapshotStore(paths);
            paths.EnsureDirectories();
            await File.WriteAllTextAsync(Path.Combine(paths.Snapshots, "uk.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(paths.Snapshots, "uk.json.bak"), "{}");
            await File.WriteAllTextAsync(Path.Combine(paths.Snapshots, "uk.corrupt-20260930-000000000.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(paths.Snapshots, "uk.json.tmp-interrupted"), "{}");

            await store.ClearAsync(CancellationToken.None);

            Assert.Empty(Directory.EnumerateFiles(paths.Snapshots));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FavoritesAndLastPlayedClearRemoveAtomicBackups()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "persistence-user-reset-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var station = Station("saved");
            var favorites = new FavoritesStore(paths);
            var lastPlayed = new LastPlayedStore(paths);

            await favorites.SaveAsync([station], CancellationToken.None);
            await favorites.SaveAsync(Array.Empty<RadioStation>(), CancellationToken.None);
            await lastPlayed.SaveAsync(station, CancellationToken.None);
            await lastPlayed.SaveAsync(Station("updated"), CancellationToken.None);

            Assert.True(File.Exists(paths.Favorites + ".bak"));
            Assert.True(File.Exists(paths.LastPlayed + ".bak"));

            await favorites.ClearAsync(CancellationToken.None);
            await lastPlayed.ClearAsync(CancellationToken.None);

            Assert.False(File.Exists(paths.Favorites));
            Assert.False(File.Exists(paths.Favorites + ".bak"));
            Assert.False(File.Exists(paths.LastPlayed));
            Assert.False(File.Exists(paths.LastPlayed + ".bak"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static RadioStation Station(string name) =>
        new("id-" + name, name, "UK", "https://example.test/stream", null, null, null, null, null, null, null, null, null);

    [Fact]
    public async Task ArtworkServiceRejectsHtmlAndUsesNextImageSource()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "artwork-" + Guid.NewGuid().ToString("N"));
        var requests = new List<Uri>();
        try
        {
            var handler = new FixtureHandler(requests);
            var httpClient = new HttpClient(handler);
            var service = new ArtworkService(httpClient, new AppPaths(root));
            var station = new RadioStation(
                StationUuid: "art",
                Name: "Artwork Station",
                Country: "UK",
                Url: "https://stream.example/stream",
                UrlResolved: null,
                Favicon: "https://bad.example/favicon",
                Homepage: "https://good.example/",
                Votes: null,
                ClickCount: null,
                Language: null,
                Tags: null,
                Codec: null,
                Bitrate: null);

            var artwork = await service.GetArtworkAsync(station, CancellationToken.None);

            Assert.True(requests.Count >= 2);
            Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(root, "artwork")));
            Assert.NotNull(artwork);
            Assert.Equal(ValidPng, await File.ReadAllBytesAsync(artwork!.LocalPath));
            await service.DisposeAsync();
            httpClient.Dispose();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ArtworkServiceRejectsHeaderlessNonImagePayload()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "artwork-headerless-" + Guid.NewGuid().ToString("N"));
        var requests = new List<Uri>();
        try
        {
            var handler = new FixtureHandler(requests);
            var service = new ArtworkService(new HttpClient(handler), new AppPaths(root));
            var station = new RadioStation(
                "headerless", "Headerless", "UK", "https://stream.example/stream", null,
                "https://unknown.example/favicon", "https://good.example/", null, null, null, null, null, null);

            var artwork = await service.GetArtworkAsync(station, CancellationToken.None);

            Assert.True(requests.Count >= 2);
            Assert.NotNull(artwork);
            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ArtworkServiceAcceptsValidImageWhenContentTypeIsMissing()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "artwork-headerless-image-" + Guid.NewGuid().ToString("N"));
        var requests = new List<Uri>();
        try
        {
            var service = new ArtworkService(new HttpClient(new FixtureHandler(requests)), new AppPaths(root));
            var station = new RadioStation(
                "headerless-image", "Headerless Image", "UK", "https://stream.example/stream", null,
                "https://headerless-image.example/logo", null, null, null, null, null, null, null, null);

            var artwork = await service.GetArtworkAsync(station, CancellationToken.None);

            Assert.NotNull(artwork);
            Assert.Contains(requests, uri => uri.Host == "headerless-image.example");
            Assert.Equal(UltraResPng, await File.ReadAllBytesAsync(artwork!.LocalPath));
            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ArtworkServicePrefersLargerHomepageArtworkOverWeakFavicon()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "artwork-quality-" + Guid.NewGuid().ToString("N"));
        var requests = new List<Uri>();
        try
        {
            var service = new ArtworkService(new HttpClient(new FixtureHandler(requests)), new AppPaths(root));
            var station = new RadioStation(
                "quality", "Quality Artwork", "UK", "https://stream.example/stream", "https://stream-two.example/stream",
                "https://low.example/favicon.png", "https://high.example/", null, null, null, null, null, null);

            var artwork = await service.GetArtworkAsync(station, CancellationToken.None);

            Assert.NotNull(artwork);
            Assert.Contains(requests, uri => uri.Host == "high.example" && uri.AbsolutePath == "/logo.png");
            Assert.Equal(HighResPng, await File.ReadAllBytesAsync(artwork!.LocalPath));
            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ArtworkServiceKeepsSearchingAfterA512PixelFavicon()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "artwork-best-candidate-" + Guid.NewGuid().ToString("N"));
        var requests = new List<Uri>();
        try
        {
            var service = new ArtworkService(new HttpClient(new FixtureHandler(requests)), new AppPaths(root));
            var station = new RadioStation(
                "best-candidate", "Best Candidate", "UK", "https://stream.example/stream", null,
                "https://mid.example/favicon.png", "https://better.example/logo.png", null, null, null, null, null, null);

            var artwork = await service.GetArtworkAsync(station, CancellationToken.None);

            Assert.NotNull(artwork);
            Assert.Contains(requests, uri => uri.Host == "better.example" && uri.AbsolutePath == "/logo.png");
            Assert.Equal(UltraResPng, await File.ReadAllBytesAsync(artwork!.LocalPath));
            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ArtworkServicePrefersSquareLogoOverHigherResolutionWideBanner()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "artwork-square-fit-" + Guid.NewGuid().ToString("N"));
        var requests = new List<Uri>();
        try
        {
            var service = new ArtworkService(new HttpClient(new FixtureHandler(requests)), new AppPaths(root));
            var station = new RadioStation(
                "square-fit", "Square Fit Station", "UK", "https://stream.example/stream", null,
                "https://wide.example/banner.png", "https://square.example/", null, null, null, null, null, null);

            var artwork = await service.GetArtworkAsync(station, CancellationToken.None);

            Assert.NotNull(artwork);
            Assert.Contains(requests, uri => uri.Host == "square.example" && uri.AbsolutePath == "/logo.png");
            Assert.Equal(SquareLogoPng, await File.ReadAllBytesAsync(artwork!.LocalPath));
            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ArtworkServiceAcceptsHttpBroadcasterArtwork()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "artwork-http-" + Guid.NewGuid().ToString("N"));
        var requests = new List<Uri>();
        try
        {
            var service = new ArtworkService(new HttpClient(new FixtureHandler(requests)), new AppPaths(root));
            var station = new RadioStation(
                "http-art", "HTTP Artwork", "UK", "https://stream.example/stream", null,
                "http://clear.example/favicon.png", null, null, null, null, null, null, null, null);

            var artwork = await service.GetArtworkAsync(station, CancellationToken.None);

            Assert.NotNull(artwork);
            Assert.Contains(requests, uri => uri.Scheme == Uri.UriSchemeHttp);
            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ArtworkServiceRepairsRecentInvalidCacheEntry()
    {
        var root = Path.Combine("C:\\GlassRadio-Build\\test-records", "artwork-repair-" + Guid.NewGuid().ToString("N"));
        var requests = new List<Uri>();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureDirectories();
            var station = new RadioStation(
                "cache-bad", "Cached Artwork Station", "UK", "https://stream.example/stream", null,
                "https://good.example/favicon.png", null, null, null, null, null, null, null, null);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"artwork-v8:{station.Id}"))).ToLowerInvariant();
            await File.WriteAllTextAsync(Path.Combine(paths.Artwork, key + ".img"), "not an image");

            var service = new ArtworkService(new HttpClient(new FixtureHandler(requests)), paths);
            var artwork = await service.GetArtworkAsync(station, CancellationToken.None);

            Assert.NotNull(artwork);
            Assert.NotEmpty(requests);
            Assert.Equal(ValidPng, await File.ReadAllBytesAsync(artwork!.LocalPath));
            await service.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FixtureHandler(List<Uri> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (request.RequestUri!.Host == "bad.example")
            {
                response.Content = new StringContent("<html>not artwork</html>");
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            }
            else if (request.RequestUri.Host == "unknown.example")
            {
                response.Content = new ByteArrayContent([1, 2, 3]);
            }
            else if (request.RequestUri.Host == "headerless-image.example")
            {
                response.Content = new ByteArrayContent(UltraResPng);
            }
            else if (request.RequestUri.Host == "high.example" && request.RequestUri.AbsolutePath == "/")
            {
                response.Content = new StringContent($"<html><head><meta property=\"og:image\" content=\"/logo.png\" /></head><body>{new string('x', 600_000)}</body></html>");
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            }
            else if (request.RequestUri.Host == "high.example" && request.RequestUri.AbsolutePath == "/logo.png")
            {
                response.Content = new ByteArrayContent(HighResPng);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            }
            else if (request.RequestUri.Host == "mid.example")
            {
                response.Content = new ByteArrayContent(HighResPng);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            }
            else if (request.RequestUri.Host == "better.example" && request.RequestUri.AbsolutePath == "/")
            {
                response.Content = new StringContent("<html><head><meta property=\"og:image\" content=\"/logo.png\" /></head></html>");
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            }
            else if (request.RequestUri.Host == "better.example" && request.RequestUri.AbsolutePath == "/logo.png")
            {
                response.Content = new ByteArrayContent(UltraResPng);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            }
            else if (request.RequestUri.Host == "square.example" && request.RequestUri.AbsolutePath == "/")
            {
                response.Content = new StringContent($"<html><head><meta property=\"og:image\" content=\"/logo.png\" /></head><body>{new string('x', 600)}</body></html>");
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            }
            else if (request.RequestUri.Host == "square.example" && request.RequestUri.AbsolutePath == "/logo.png")
            {
                response.Content = new ByteArrayContent(SquareLogoPng);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            }
            else if (request.RequestUri.Host == "wide.example")
            {
                response.Content = new ByteArrayContent(WideBannerPng);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            }
            else
            {
                response.Content = new ByteArrayContent(ValidPng);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            }
            return Task.FromResult(response);
        }
    }

    private static readonly byte[] ValidPng = CreateValidPng();
    private static readonly byte[] HighResPng = CreatePng(512, 512);
    private static readonly byte[] UltraResPng = CreatePng(768, 768);
    private static readonly byte[] SquareLogoPng = CreatePng(512, 512);
    private static readonly byte[] WideBannerPng = CreatePng(1600, 500);

    private static byte[] CreateValidPng()
        => CreatePng(96, 96);

    private static byte[] CreatePng(uint width, uint height)
    {
        var pixels = new byte[checked((int)(width * height * 4))];
        var seed = 0x13579BDFu;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            seed = seed * 1664525u + 1013904223u;
            pixels[i] = (byte)(seed >> 24);
            seed = seed * 1664525u + 1013904223u;
            pixels[i + 1] = (byte)(seed >> 24);
            seed = seed * 1664525u + 1013904223u;
            pixels[i + 2] = (byte)(seed >> 24);
            pixels[i + 3] = 255;
        }

        using var stream = new InMemoryRandomAccessStream();
        var encoder = BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream)
            .AsTask().GetAwaiter().GetResult();
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
        encoder.FlushAsync().AsTask().GetAwaiter().GetResult();
        stream.Seek(0);
        var result = new byte[checked((int)stream.Size)];
        stream.ReadAsync(result.AsBuffer(), (uint)result.Length, InputStreamOptions.None)
            .AsTask().GetAwaiter().GetResult();
        return result;
    }
}
