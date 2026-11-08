using System.Security.Cryptography;
using System.Text;

namespace CIPHER.Delegation;

/// <summary>
/// Stores the external-AI API key encrypted with Windows DPAPI (current-user scope) in a
/// small binary file. The key is never written to settings JSON, the SQLite memory, logs,
/// or command history — and this app never accepts a key through the command line, because
/// typed commands are journaled. Keys are entered only through the settings password box.
/// </summary>
public sealed class DpapiApiKeyStore
{
    private readonly string _filePath;

    public DpapiApiKeyStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A key file path is required.", nameof(filePath));
        }

        _filePath = filePath;
    }

    public string FilePath => _filePath;

    public bool HasKey => File.Exists(_filePath);

    public static DpapiApiKeyStore CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : Path.Combine(localAppData, "CIPHER");

        return new DpapiApiKeyStore(Path.Combine(root, "secrets", "ai-api-key.bin"));
    }

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("The API key must not be empty.", nameof(apiKey));
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var plainBytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        try
        {
            var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_filePath, protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public string? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var protectedBytes = File.ReadAllBytes(_filePath);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        try
        {
            var key = Encoding.UTF8.GetString(plainBytes).Trim();
            return key.Length == 0 ? null : key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public void Delete()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }
}
