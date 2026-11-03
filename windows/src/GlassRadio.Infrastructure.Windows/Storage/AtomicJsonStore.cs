using System.Text.Json;

namespace GlassRadio.Infrastructure.Windows.Storage;

internal sealed class AtomicJsonStore<T>(JsonSerializerOptions? options = null)
{
    private readonly JsonSerializerOptions _options = options ?? new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<T?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return default;
        try
        {
            return await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException) when (File.Exists(path + ".bak"))
        {
            return await ReadFileAsync(path + ".bak", cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (File.Exists(path + ".bak"))
        {
            return await ReadFileAsync(path + ".bak", cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task WriteAsync(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The target path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, _options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task<T?> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken).ConfigureAwait(false);
    }
}
