using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GlassRadio.Application.Abstractions;
using GlassRadio.Domain;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GlassRadio.Infrastructure.Windows.Artwork;

public sealed class ArtworkService(HttpClient httpClient, Storage.AppPaths paths) : IArtworkService
{
    private const long MaxImageBytes = 4 * 1024 * 1024;
    // v7 changed selection from raw resolution to card-fit quality. v8 also invalidates
    // entries produced before image payload sniffing, where a valid broadcaster image with a
    // missing or inaccurate MIME type could be discarded before the quality selector saw it.
    private const string ArtworkCacheVersion = "artwork-v8";
    // Large broadcaster homepages (notably the Bauer/KISS pages) can carry
    // embedded application data around the metadata. Keep the page bounded,
    // but do not reject a valid og:image page merely because it is larger than
    // the old 512 KiB ceiling.
    private const int MaxPageBytes = 2 * 1024 * 1024;
    private const int MaxCandidateCount = 12;
    private const uint MinimumPixelDimension = 96;
    private const uint PreferredPixelDimension = 768;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan WeakCacheRefreshWindow = TimeSpan.FromHours(12);
    private readonly SemaphoreSlim _gate = new(4, 4);

    public async Task<Uri?> GetArtworkAsync(RadioStation station, CancellationToken cancellationToken)
    {
        var candidates = BuildCandidates(station);
        if (candidates.Count == 0) return null;

        paths.EnsureDirectories();
        // v3 deliberately invalidates the old first-valid-favicon cache. Older
        // entries could make a 96px icon permanent for 30 days.
        // v5 invalidated earlier entries created before homepage candidates were
        // allowed through a full provider list. Without a namespace bump,
        // weak 96/196/300px artwork can remain trusted for the refresh window
        // even after the resolver has learned how to reach the broadcaster's
        // larger image. v6 also invalidates the earlier 512px early-stop result.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{ArtworkCacheVersion}:{station.Id}"))).ToLowerInvariant();
        var target = Path.Combine(paths.Artwork, key + ".img");
        var cached = await ReadCachedArtworkAsync(target, cancellationToken).ConfigureAwait(false);
        if (IsFreshCacheUsable(target, cached)) return new Uri(target, UriKind.Absolute);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = await ReadCachedArtworkAsync(target, cancellationToken).ConfigureAwait(false);
            if (IsFreshCacheUsable(target, cached)) return new Uri(target, UriKind.Absolute);

            DownloadedArtwork? best = null;
            var homepage = Uri.TryCreate(station.Homepage, UriKind.Absolute, out var homepageUri)
                && IsHttpUri(homepageUri)
                ? homepageUri
                : null;

            for (var index = 0; index < candidates.Count && index < MaxCandidateCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = candidates[index];
                var payload = await DownloadAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (payload is null) continue;

                if (payload.Value.IsImage)
                {
                    var dimensions = await ReadImageDimensionsAsync(payload.Value.Data, cancellationToken).ConfigureAwait(false);
                    if (dimensions is null) continue;

                    var downloaded = new DownloadedArtwork(payload.Value.Data, dimensions.Value.Width, dimensions.Value.Height);
                    if (best is null || IsBetter(downloaded, best.Value)) best = downloaded;
                    continue;
                }

                // Broadcaster pages often expose a proper square/share logo even
                // when the Radio Browser favicon is only 32/96px. Discover those
                // URLs instead of treating the HTML document as artwork.
                if (homepage is not null && candidate.Equals(homepage) && payload.Value.Data.Length <= MaxPageBytes)
                {
                    var discovered = ExtractArtworkUrls(payload.Value.Data, homepage)
                        .Where(uri => !candidates.Contains(uri))
                        // The list already contains the normal favicon/provider
                        // fallbacks, so its total count is not the number of
                        // slots consumed before this page was visited. Reserve
                        // the remaining request budget from the current index;
                        // otherwise stations with several host fallbacks lose
                        // their broadcaster og:image candidates entirely.
                        .Take(Math.Max(0, MaxCandidateCount - index - 1))
                        .ToArray();
                    if (discovered.Length > 0) candidates.InsertRange(index + 1, discovered);
                }
            }

            if (best is null)
            {
                // Preserve valid visible art during a transient outage.
                return cached is not null ? new Uri(target, UriKind.Absolute) : null;
            }

            await WriteAtomicallyAsync(target, best.Value.Data, cancellationToken).ConfigureAwait(false);
            return new Uri(target, UriKind.Absolute);
        }
        finally { _gate.Release(); }
    }

    private static bool IsFreshCacheUsable(string path, ImageDimensions? cached)
    {
        if (cached is null || !File.Exists(path)) return false;
        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
        return age <= MaxAge && (cached.Value.MinDimension >= PreferredPixelDimension || age <= WeakCacheRefreshWindow);
    }

    private async Task<DownloadedPayload?> DownloadAsync(Uri candidate, CancellationToken cancellationToken)
    {
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestLifetime.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
            request.Headers.UserAgent.ParseAdd("GlassRadio/2.0");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestLifetime.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var isImage = mediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
            var maxBytes = isImage ? MaxImageBytes : MaxPageBytes;
            if (response.Content.Headers.ContentLength is > MaxImageBytes) return null;

            await using var input = await response.Content.ReadAsStreamAsync(requestLifetime.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[32 * 1024];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(), requestLifetime.Token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > maxBytes) return null;
                await output.WriteAsync(buffer.AsMemory(0, read), requestLifetime.Token).ConfigureAwait(false);
            }

            var data = output.ToArray();
            // A number of broadcaster/CDN endpoints return a perfectly valid image with no
            // Content-Type (or with text/plain/application/octet-stream). Do not make the
            // server header the authority for image identity: verify the common file signatures
            // before falling back to the HTML metadata path. HTML remains non-image because it
            // has no recognised image signature and is still handled only for the homepage URL.
            var detectedImage = isImage || LooksLikeImage(data);
            return data.Length > 512 ? new DownloadedPayload(data, detectedImage) : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static List<Uri> BuildCandidates(RadioStation station)
    {
        var candidates = CuratedArtworkUrls(station).ToList();
        if (TryHttpUri(station.Favicon, out var favicon))
        {
            candidates.Add(favicon);
            if (favicon.Scheme == Uri.UriSchemeHttp && TryHttpUri(favicon.AbsoluteUri.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase), out var httpsFavicon))
                candidates.Add(httpsFavicon);
        }
        if (TryHttpUri(station.Homepage, out var homepage)) candidates.Add(homepage);

        var hosts = new[] { station.Homepage, station.UrlResolved, station.Url }
            .Select(value => TryHttpUri(value, out var uri) ? uri.Host : null)
            .Where(static host => !string.IsNullOrWhiteSpace(host))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var host in hosts)
        {
            candidates.Add(new Uri($"https://icons.duckduckgo.com/ip3/{host}.ico"));
            candidates.Add(new Uri($"https://www.google.com/s2/favicons?domain={host}&sz=512"));
            candidates.Add(new Uri($"https://icon.horse/icon/{host}"));
            candidates.Add(new Uri($"https://logo.clearbit.com/{host}?size=512"));
        }

        return candidates.Distinct().ToList();
    }

    private static IEnumerable<Uri> ExtractArtworkUrls(byte[] htmlBytes, Uri baseUri)
    {
        var html = Encoding.UTF8.GetString(htmlBytes);
        foreach (Match tagMatch in Regex.Matches(html, "<meta\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var tag = tagMatch.Value;
            var property = AttributeValue(tag, "property") ?? AttributeValue(tag, "name");
            if (property is null || (!property.Equals("og:image", StringComparison.OrdinalIgnoreCase) &&
                                     !property.Equals("twitter:image", StringComparison.OrdinalIgnoreCase) &&
                                     !property.Equals("twitter:image:src", StringComparison.OrdinalIgnoreCase))) continue;
            if (TryResolveHttpUri(AttributeValue(tag, "content"), baseUri, out var image)) yield return image;
        }

        foreach (Match tagMatch in Regex.Matches(html, "<link\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var tag = tagMatch.Value;
            var rel = AttributeValue(tag, "rel") ?? string.Empty;
            if (!rel.Contains("icon", StringComparison.OrdinalIgnoreCase)) continue;
            if (TryResolveHttpUri(AttributeValue(tag, "href"), baseUri, out var image)) yield return image;
        }
    }

    private static string? AttributeValue(string tag, string name)
    {
        var match = Regex.Match(tag, $"\\b{name}\\s*=\\s*(['\\\"])(.*?)\\1", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[2].Value.Trim()) : null;
    }

    private static bool TryResolveHttpUri(string? value, Uri baseUri, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri!) && IsHttpUri(uri)) return true;
        if (Uri.TryCreate(baseUri, value, out uri!) && IsHttpUri(uri)) return true;
        uri = null!;
        return false;
    }

    private static bool TryHttpUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && IsHttpUri(parsed))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool IsHttpUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private static bool LooksLikeImage(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) return true;
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;
        if (data.Length >= 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' &&
            data[3] == (byte)'8' && (data[4] == (byte)'7' || data[4] == (byte)'9') && data[5] == (byte)'a') return true;
        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M') return true;
        if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' &&
            data[3] == (byte)'F' && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P') return true;
        if (data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0) return true;
        return false;
    }

    private static bool IsBetter(DownloadedArtwork candidate, DownloadedArtwork current)
    {
        // Station artwork is presented in square tiles throughout Home and Explore. A
        // 1600x400 hero banner has more pixels than a 768x768 logo, but only 400px of
        // useful square crop resolution and usually loses the broadcaster's identity
        // when centre-cropped. Score the usable square resolution first, then retain
        // the old resolution/bytes tie-breakers for equally suitable candidates.
        var candidateFit = candidate.SquareFitScore;
        var currentFit = current.SquareFitScore;
        return candidateFit > currentFit + 0.01 ||
               Math.Abs(candidateFit - currentFit) <= 0.01 &&
               (candidate.MinDimension > current.MinDimension ||
                candidate.PixelArea > current.PixelArea ||
                candidate.Data.Length > current.Data.Length);
    }

    private static async Task<ImageDimensions?> ReadCachedArtworkAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var dimensions = await ReadImageDimensionsAsync(data, cancellationToken).ConfigureAwait(false);
            if (dimensions is not null) return dimensions;
            try { File.Delete(path); } catch { }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { }
        return null;
    }

    private static async Task<ImageDimensions?> ReadImageDimensionsAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (data.Length <= 512) return null;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(data.AsBuffer()).AsTask(cancellationToken).ConfigureAwait(false);
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            if (decoder.PixelWidth < MinimumPixelDimension || decoder.PixelHeight < MinimumPixelDimension) return null;
            return new ImageDimensions(decoder.PixelWidth, decoder.PixelHeight);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static async Task WriteAtomicallyAsync(string target, byte[] data, CancellationToken cancellationToken)
    {
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, data, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static IEnumerable<Uri> CuratedArtworkUrls(RadioStation station)
    {
        if (!station.Name.Contains("smash hits", StringComparison.OrdinalIgnoreCase)) yield break;

        yield return new Uri("https://images.radio.co/station_logos/s3cee08f23.20200321084448.jpg");
        yield return new Uri("https://isteam.wsimg.com/ip/d1c7a489-7904-4a8e-ae12-76dce3ced6be/Smash%20Hits%20logo.jpg/:/rs=w:512,h:512,m");
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        foreach (var file in Directory.EnumerateFiles(paths.Artwork))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file);
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private readonly record struct DownloadedPayload(byte[] Data, bool IsImage);
    private readonly record struct DownloadedArtwork(byte[] Data, uint Width, uint Height)
    {
        public uint MinDimension => Math.Min(Width, Height);
        public ulong PixelArea => (ulong)Width * Height;
        public double SquareFitScore
        {
            get
            {
                var shortSide = Math.Min(Width, Height);
                var longSide = Math.Max(Width, Height);
                var aspectPenalty = Math.Sqrt(longSide / (double)shortSide);
                return shortSide / aspectPenalty;
            }
        }
    }
    private readonly record struct ImageDimensions(uint Width, uint Height)
    {
        public uint MinDimension => Math.Min(Width, Height);
    }
}
