using System.Security.Cryptography;
using System.Text;

namespace GlassRadio.Infrastructure.Windows.Storage;

/// <summary>Stores one per-user Local API secret protected by Windows DPAPI.</summary>
public sealed class LocalApiKeyStore(AppPaths paths)
{
    public string GetOrCreate()
    {
        try
        {
            if (File.Exists(paths.LocalApiKey))
            {
                var existing = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    File.ReadAllBytes(paths.LocalApiKey),
                    optionalEntropy: null,
                    DataProtectionScope.CurrentUser)).Trim();
                if (!string.IsNullOrWhiteSpace(existing)) return existing;
            }
        }
        catch (CryptographicException) { }
        catch (IOException) { }

        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var key = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(key),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(paths.Root);
        var temporary = paths.LocalApiKey + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, paths.LocalApiKey, overwrite: true);
        return key;
    }
}
