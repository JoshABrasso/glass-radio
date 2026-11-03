using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GlassRadio.Application.Abstractions;
using GlassRadio.Application.Contracts;

namespace GlassRadio.Infrastructure.Windows.LocalApi;

/// Explicitly owned, authenticated control API. It is disabled unless started by the caller.
/// LAN binding is a separate explicit opt-in and uses a user-mode TCP listener.
public sealed class LocalApiServer : IAsyncDisposable
{
    private const int MaxBodyBytes = 64 * 1024;
    private const int RequestsPerMinute = 180;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _requests = new();
    private readonly IPAddress? _bindAddressOverride;
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private ILocalApiTarget? _target;
    public bool IsRunning => _listener is not null;
    public int Port { get; private set; }
    public bool AllowLanAccess { get; private set; }
    public string? ApiKey { get; private set; }

    public LocalApiServer() { }

    // Test-only construction keeps LAN-mode contract tests on loopback. The real
    // application continues to bind Any only when the user explicitly opts in.
    internal LocalApiServer(IPAddress bindAddressOverride) => _bindAddressOverride = bindAddressOverride;

    public Task StartAsync(ILocalApiTarget target, int port, string apiKey, bool allowLanAccess = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("A local API key is required.", nameof(apiKey));
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        cancellationToken.ThrowIfCancellationRequested();
        if (IsRunning) throw new InvalidOperationException("The local API is already running.");
        var listener = new TcpListener(_bindAddressOverride ?? (allowLanAccess ? IPAddress.Any : IPAddress.Loopback), port);
        listener.Start();
        _target = target; _listener = listener; _lifetime = new CancellationTokenSource();
        Port = port; AllowLanAccess = allowLanAccess; ApiKey = apiKey;
        _ = AcceptLoopAsync(listener, _lifetime.Token);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _lifetime?.Cancel(); _listener?.Stop(); _listener = null; _target = null;
        ApiKey = null; Port = 0; AllowLanAccess = false; _lifetime?.Dispose(); _lifetime = null;
        return ValueTask.CompletedTask;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { _ = HandleClientAsync(await listener.AcceptTcpClientAsync(token).ConfigureAwait(false), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
            catch (SocketException) when (token.IsCancellationRequested) { break; }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using var owned = client;
        using var stream = client.GetStream();
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        requestLifetime.CancelAfter(RequestTimeout);
        var requestToken = requestLifetime.Token;
        try
        {
            var request = await ReadRequestAsync(stream, requestToken).ConfigureAwait(false);
            if (request.Method == "OPTIONS") { await WriteAsync(stream, HttpStatusCode.NoContent, new { ok = true }, requestToken); return; }
            if (!AllowRequest()) { await WriteAsync(stream, HttpStatusCode.TooManyRequests, new { error = "rate_limited" }, requestToken); return; }
            if (!Authorized(request.Headers)) { await WriteAsync(stream, HttpStatusCode.Unauthorized, new { error = "unauthorized" }, requestToken); return; }
            var result = await RouteAsync(request, requestToken).ConfigureAwait(false);
            await WriteAsync(stream, result.Status, result.Payload, requestToken).ConfigureAwait(false);
        }
        catch (JsonException) { await WriteAsync(stream, HttpStatusCode.BadRequest, new { error = "invalid_json" }, token); }
        catch (ArgumentException ex) { await WriteAsync(stream, HttpStatusCode.BadRequest, new { error = ex.Message }, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (OperationCanceledException) { await WriteAsync(stream, HttpStatusCode.RequestTimeout, new { error = "request_timeout" }, CancellationToken.None); }
        catch { await WriteAsync(stream, HttpStatusCode.InternalServerError, new { error = "request_failed" }, CancellationToken.None); }
    }

    private async Task<(HttpStatusCode Status, object Payload)> RouteAsync(RequestData request, CancellationToken token)
    {
        var target = _target ?? throw new InvalidOperationException("The local API target is unavailable.");
        return (request.Method, request.Path) switch
        {
            ("GET", "/status") => (HttpStatusCode.OK, new { app = "Glass Radio", api_version = "1.0", state = target.Playback.State.ToString().ToLowerInvariant(), volume = target.Playback.Volume, station = target.Playback.Station, bitrate_kbps = target.Playback.Bitrate, bitrate_measured = target.Playback.IsMeasuredTelemetry, buffering_progress = target.Playback.BufferingProgress }),
            ("GET", "/metrics") => (HttpStatusCode.OK, Metrics(target)),
            ("GET", "/preferences") => (HttpStatusCode.OK, Preferences(target.Settings)),
            ("POST", "/preferences") => await UpdatePreferencesAsync(target, request.Body, token),
            ("GET", "/favorites") => (HttpStatusCode.OK, new { ok = true, favorites = target.Favorites, count = target.Favorites.Count }),
            ("GET", "/countries") => (HttpStatusCode.OK, new { ok = true, countries = target.Countries, count = target.Countries.Count }),
            ("GET", "/stations") => (HttpStatusCode.OK, Stations(target, request)),
            ("POST", "/play") => await PlayAsync(target, request.Body, token),
            ("POST", "/pause") => await CommandAsync(target.PauseAsync, token),
            ("POST", "/toggle") => await CommandAsync(target.ToggleAsync, token),
            ("POST", "/next") => await CommandAsync(target.NextAsync, token),
            ("POST", "/previous") => await CommandAsync(target.PreviousAsync, token),
            ("POST", "/volume") => await VolumeAsync(target, request.Body, token),
            ("POST", "/search") => await SearchAsync(target, request.Body, token),
            ("POST", "/country") => await CountryAsync(target, request.Body, token),
            ("POST", "/browse") => await BrowseAsync(target, request.Body, token),
            ("GET", "/diag/vlc") => (HttpStatusCode.OK, new { ok = true, log = GlassRadio.Infrastructure.Windows.Playback.LibVlcPlaybackEngine.DiagnosticLog.ToArray() }),
            ("GET", "/diag/engine") => (HttpStatusCode.OK, new { ok = true, engine = GlassRadio.Infrastructure.Windows.Playback.WindowsPlaybackEngine.LastEngineName, fallbackReason = GlassRadio.Infrastructure.Windows.Playback.WindowsPlaybackEngine.LastFallbackReason }),
            ("GET", "/diag/volume") => (HttpStatusCode.OK, new { ok = true, sliderVolume = target.Playback.Volume, libVlcGain0to100 = GlassRadio.Infrastructure.Windows.Playback.LibVlcPlaybackEngine.LastAppliedGain }),
            _ => (HttpStatusCode.NotFound, (object)new { error = "not_found" })
        };
    }

    private static async Task<(HttpStatusCode, object)> PlayAsync(ILocalApiTarget t, string b, CancellationToken c) { await t.PlayAsync(ReadOptional(b, "uuid") ?? ReadOptional(b, "station_id"), c); return (HttpStatusCode.OK, new { ok = true }); }
    private static async Task<(HttpStatusCode, object)> CommandAsync(Func<CancellationToken, Task> command, CancellationToken c) { await command(c); return (HttpStatusCode.OK, new { ok = true }); }
    private static async Task<(HttpStatusCode, object)> VolumeAsync(ILocalApiTarget t, string b, CancellationToken c) { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(b) ? "{}" : b); if (!d.RootElement.TryGetProperty("level", out var l) || !l.TryGetDouble(out var v)) throw new ArgumentException("A numeric level is required."); await t.SetVolumeAsync(v, c); return (HttpStatusCode.OK, new { ok = true, volume = Math.Clamp(v, 0, 1) }); }
    private static async Task<(HttpStatusCode, object)> SearchAsync(ILocalApiTarget t, string b, CancellationToken c)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(b) ? "{}" : b);
        var root = document.RootElement;
        var query = Text(root, "query", string.Empty);
        if (query.Length == 0) return (HttpStatusCode.OK, new { ok = true, action = "search", query, accepted = false });
        var limit = (int)Math.Clamp(Number(root, "limit", 160), 1, 500);
        var results = await t.SearchAsync(query, c).ConfigureAwait(false);
        var page = results.Take(limit).ToArray();
        return (HttpStatusCode.OK, new { ok = true, action = "search", query, limit, accepted = true, stations = page, count = page.Length, total = results.Count });
    }

    private static async Task<(HttpStatusCode, object)> CountryAsync(ILocalApiTarget t, string b, CancellationToken c)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(b) ? "{}" : b);
        var root = document.RootElement;
        var requested = Text(root, "id", Text(root, "country", Text(root, "code", string.Empty)));
        if (requested.Length == 0) throw new ArgumentException("A country is required.");
        var country = t.Countries.FirstOrDefault(candidate =>
            candidate.Id.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
            candidate.CountryCode.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
            candidate.DisplayName.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
            candidate.ApiName.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (country is null) throw new ArgumentException("country_not_found");
        await t.LoadCountryAsync(country.Id, c).ConfigureAwait(false);
        return (HttpStatusCode.OK, new { ok = true, action = "country", loading = false, country });
    }
    private static async Task<(HttpStatusCode, object)> BrowseAsync(ILocalApiTarget t, string b, CancellationToken c)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(b) ? "{}" : b);
        var root = document.RootElement;
        var filter = Text(root, "filter", "All");
        var sort = Text(root, "sort", "Popularity");
        var genre = Text(root, "genre", "All");
        await t.ApplyBrowseAsync(filter, sort, genre, c).ConfigureAwait(false);
        return (HttpStatusCode.OK, new { ok = true, action = "browse", filter, sort, genre, visible_station_count = t.Stations.Count });
    }
    private static async Task<(HttpStatusCode, object)> UpdatePreferencesAsync(ILocalApiTarget target, string body, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = document.RootElement;
        var current = target.Settings;
        var updated = (current with
        {
            Volume = Number(root, "default_volume", current.Volume),
            ConnectionTimeoutSeconds = (int)Math.Round(Number(root, "connect_timeout_seconds", current.ConnectionTimeoutSeconds)),
            PreferLowerBitrate = Boolean(root, "prefer_lower_bitrate", current.PreferLowerBitrate),
            DefaultCountryId = Text(root, "default_country_id", current.DefaultCountryId),
            AllowReligiousContent = Boolean(root, "show_religious_content", current.AllowReligiousContent),
            AllowSportsContent = Boolean(root, "show_sports_content", current.AllowSportsContent),
            AllowNewsContent = Boolean(root, "show_news_content", current.AllowNewsContent),
            LocalApiEnabled = Boolean(root, "api_enabled", current.LocalApiEnabled),
            LocalApiPort = (int)Math.Round(Number(root, "preferred_port", current.LocalApiPort)),
            LocalApiAllowLanAccess = Boolean(root, "allow_lan_access", current.LocalApiAllowLanAccess)
        }).Normalize();
        await target.ApplySettingsAsync(updated, cancellationToken).ConfigureAwait(false);
        return (HttpStatusCode.OK, new { ok = true, preferences = Preferences(updated) });
    }

    private static object Metrics(ILocalApiTarget target) => new
    {
        ok = true,
        app = "Glass Radio",
        api_version = "1.0",
        playback = target.Playback,
        catalog = new { station_count = target.Stations.Count, favorite_count = target.Favorites.Count, country_count = target.Countries.Count },
        preferences = Preferences(target.Settings)
    };

    private static object Preferences(AppSettings settings) => new
    {
        volume = settings.Volume,
        connection_timeout_seconds = settings.ConnectionTimeoutSeconds,
        prefer_lower_bitrate = settings.PreferLowerBitrate,
        default_country_id = settings.DefaultCountryId,
        show_religious_content = settings.AllowReligiousContent,
        show_sports_content = settings.AllowSportsContent,
        show_news_content = settings.AllowNewsContent,
        api_enabled = settings.LocalApiEnabled,
        preferred_port = settings.LocalApiPort,
        allow_lan_access = settings.LocalApiAllowLanAccess
    };

    private static double Number(JsonElement root, string name, double fallback) => root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : fallback;
    private static bool Boolean(JsonElement root, string name, bool fallback) => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static string Text(JsonElement root, string name, string fallback) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!.Trim() : fallback;
    private static string? ReadOptional(string body, string property) { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); return d.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null; }
    private static string Required(string body, string property) => string.IsNullOrWhiteSpace(ReadOptional(body, property)) ? throw new ArgumentException($"A {property} is required.") : ReadOptional(body, property)!.Trim();
    private static object Stations(ILocalApiTarget target, RequestData request)
    {
        var scope = request.Parameters.GetValueOrDefault("scope", "top");
        var genre = request.Parameters.GetValueOrDefault("genre");
        var source = target.GetStations(scope, genre);
        var limit = int.TryParse(request.Parameters.GetValueOrDefault("limit"), out var parsedLimit) ? Math.Clamp(parsedLimit, 1, 100) : 50;
        var offset = int.TryParse(request.Parameters.GetValueOrDefault("offset"), out var parsedOffset) ? Math.Max(parsedOffset, 0) : 0;
        var page = source.Skip(offset).Take(limit).ToArray();
        return new { ok = true, scope, offset, limit, count = page.Length, total = source.Count, stations = page };
    }

    private bool Authorized(IReadOnlyDictionary<string, string> headers)
    { headers.TryGetValue("x-glass-radio-key", out var supplied); if (string.IsNullOrWhiteSpace(supplied) && headers.TryGetValue("authorization", out var bearer) && bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) supplied = bearer[7..].Trim(); return !string.IsNullOrEmpty(supplied) && ApiKey is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(ApiKey)); }
    private bool AllowRequest() { lock (_gate) { var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1); while (_requests.Count > 0 && _requests.Peek() < cutoff) _requests.Dequeue(); if (_requests.Count >= RequestsPerMinute) return false; _requests.Enqueue(DateTimeOffset.UtcNow); return true; } }

    private static async Task<RequestData> ReadRequestAsync(NetworkStream stream, CancellationToken token)
    {
        using var data = new MemoryStream(); var chunk = new byte[4096]; var headerEnd = -1;
        while (data.Length <= MaxBodyBytes + 16384 && headerEnd < 0) { var n = await stream.ReadAsync(chunk, token); if (n == 0) throw new IOException("Request ended early."); data.Write(chunk, 0, n); var a = data.GetBuffer(); for (var i = Math.Max(0, (int)data.Length - n - 3); i < data.Length - 3; i++) if (a[i] == 13 && a[i + 1] == 10 && a[i + 2] == 13 && a[i + 3] == 10) { headerEnd = i; break; } }
        if (headerEnd < 0) throw new ArgumentException("Request headers are too large.");
        var all = data.ToArray(); var lines = Encoding.ASCII.GetString(all, 0, headerEnd).Split("\r\n"); var first = lines[0].Split(' ', 3); if (first.Length != 3) throw new ArgumentException("Invalid HTTP request.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); foreach (var line in lines.Skip(1)) { var i = line.IndexOf(':'); if (i > 0) headers[line[..i].Trim()] = line[(i + 1)..].Trim(); }
        var bodyOffset = headerEnd + 4;
        byte[] body;
        if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding) && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            body = await ReadChunkedBodyAsync(stream, all, bodyOffset, token).ConfigureAwait(false);
        }
        else
        {
            var length = headers.TryGetValue("Content-Length", out var raw) && int.TryParse(raw, out var parsed) ? parsed : 0;
            if (length > MaxBodyBytes) throw new ArgumentException("Request body is too large.");
            var have = all.Length - bodyOffset;
            while (have < length) { var n = await stream.ReadAsync(chunk, token); if (n == 0) throw new IOException("Request body ended early."); data.Write(chunk, 0, n); have += n; all = data.ToArray(); }
            body = length == 0 ? Array.Empty<byte>() : all[bodyOffset..(bodyOffset + length)];
        }
        var uri = new Uri("http://localhost" + first[1]); var parameters = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(static x => x.Split('=', 2)).Where(static x => x.Length == 2).ToDictionary(static x => Uri.UnescapeDataString(x[0]), static x => Uri.UnescapeDataString(x[1]), StringComparer.OrdinalIgnoreCase);
        return new RequestData(first[0].ToUpperInvariant(), string.IsNullOrEmpty(uri.AbsolutePath.TrimEnd('/')) ? "/" : uri.AbsolutePath.TrimEnd('/').ToLowerInvariant(), parameters, Encoding.UTF8.GetString(body), headers);
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(NetworkStream stream, byte[] initial, int offset, CancellationToken token)
    {
        var pending = new List<byte>(initial.AsSpan(offset).ToArray());
        using var body = new MemoryStream();
        while (true)
        {
            var line = await ReadChunkLineAsync(stream, pending, token).ConfigureAwait(false);
            var separator = line.IndexOf(';');
            var sizeText = (separator >= 0 ? line[..separator] : line).Trim();
            if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out var size) || size < 0) throw new ArgumentException("Invalid chunked request body.");
            if (size == 0) return body.ToArray();
            if (body.Length + size > MaxBodyBytes) throw new ArgumentException("Request body is too large.");
            await EnsurePendingAsync(stream, pending, size + 2, token).ConfigureAwait(false);
            body.Write(pending.Take(size).ToArray());
            pending.RemoveRange(0, size + 2);
        }
    }

    private static async Task<string> ReadChunkLineAsync(NetworkStream stream, List<byte> pending, CancellationToken token)
    {
        while (true)
        {
            for (var i = 0; i + 1 < pending.Count; i++)
            {
                if (pending[i] == 13 && pending[i + 1] == 10)
                {
                    var line = Encoding.ASCII.GetString(pending.Take(i).ToArray());
                    pending.RemoveRange(0, i + 2);
                    return line;
                }
            }
            await ReadMoreAsync(stream, pending, token).ConfigureAwait(false);
        }
    }

    private static async Task EnsurePendingAsync(NetworkStream stream, List<byte> pending, int required, CancellationToken token)
    {
        while (pending.Count < required) await ReadMoreAsync(stream, pending, token).ConfigureAwait(false);
        if (pending[required - 2] != 13 || pending[required - 1] != 10) throw new ArgumentException("Invalid chunked request body.");
    }

    private static async Task ReadMoreAsync(NetworkStream stream, List<byte> pending, CancellationToken token)
    {
        var buffer = new byte[4096];
        var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
        if (read == 0) throw new IOException("Request body ended early.");
        pending.AddRange(buffer.AsSpan(0, read).ToArray());
    }

    private static async Task WriteAsync(NetworkStream stream, HttpStatusCode status, object payload, CancellationToken token) { var bytes = JsonSerializer.SerializeToUtf8Bytes(payload); var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(int)status} {status}\r\nContent-Type: application/json; charset=utf-8\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type, Authorization, X-Glass-Radio-Key\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"); await stream.WriteAsync(header, token); await stream.WriteAsync(bytes, token); }
    private static async Task TryWriteAsync(TcpClient client, HttpStatusCode status, object payload, CancellationToken token) { try { await using var stream = client.GetStream(); await WriteAsync(stream, status, payload, token); } catch { } }
    private sealed record RequestData(string Method, string Path, IReadOnlyDictionary<string, string> Parameters, string Body, IReadOnlyDictionary<string, string> Headers);
}
