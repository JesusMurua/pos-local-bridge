using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PosLocalBridge.Contracts.Security;

namespace PosLocalBridge.Security;

/// <summary>
/// File-based token store. By default persists the device token under
/// <c>%ProgramData%\Fino\.fino-auth</c>, encrypted via Windows DPAPI with
/// <see cref="DataProtectionScope.LocalMachine"/> scope. Tests can override
/// the storage path via the <c>overridePath</c> constructor parameter to
/// avoid colliding with the global production location.
///
/// Read failures (missing file, corrupted ciphertext, DPAPI errors) are
/// logged and surface as a null token so the caller can trigger re-pairing.
/// </summary>
public sealed class FileTokenStore : ITokenStore
{
    private const string FolderName = "Fino";
    private const string FileName = ".fino-auth";

    private readonly ILogger<FileTokenStore> _logger;
    private readonly string _path;

    public FileTokenStore(ILogger<FileTokenStore> logger, string? overridePath = null)
    {
        _logger = logger;
        _path = overridePath ?? ComputeDefaultPath();

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task<string?> GetTokenAsync()
    {
        if (!File.Exists(_path)) return null;

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_path).ConfigureAwait(false);
            var decrypted = ProtectedData.Unprotect(
                encrypted,
                optionalEntropy: null,
                DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read or decrypt token store. {ExceptionType}",
                ex.GetType().Name);
            return null;
        }
    }

    public async Task SaveTokenAsync(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var encrypted = ProtectedData.Protect(
            bytes,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);
        await File.WriteAllBytesAsync(_path, encrypted).ConfigureAwait(false);
    }

    public Task ClearTokenAsync()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
        return Task.CompletedTask;
    }

    private static string ComputeDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            FolderName,
            FileName);
}
