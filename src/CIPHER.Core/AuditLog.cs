using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CIPHER.Core;

/// <summary>Outcome of <see cref="AuditLog.Verify"/>.</summary>
public sealed record AuditVerificationResult(
    bool IsValid,
    int EntryCount,
    int? FirstInvalidLine,
    string Message);

/// <summary>
/// Append-only, hash-chained local audit log (one JSON object per line). Every entry
/// carries the SHA-256 of its predecessor, so editing or deleting history breaks the
/// chain and is detectable with <see cref="Verify"/> (`verify audit log`). Details are
/// passed through <see cref="SensitiveDataRedactor"/> before anything is written.
///
/// Retention: when the active file grows past the rotation threshold it is renamed to
/// <c>&lt;file&gt;.1</c> (single archive generation, replaced on the next rotation) and a
/// fresh chain begins. This is lightweight tamper-evidence for a local assistant, not a
/// remote attestation system — an attacker with full local write access can regenerate
/// a chain, which the threat model states explicitly.
/// </summary>
public sealed class AuditLog
{
    private const string GenesisHash = "GENESIS";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly long _rotationThresholdBytes;
    private readonly object _gate = new();
    private string? _lastHash;

    public AuditLog(string filePath, long rotationThresholdBytes = 1_000_000)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("An audit log path is required.", nameof(filePath));
        }

        _filePath = filePath;
        _rotationThresholdBytes = Math.Max(1024, rotationThresholdBytes);
    }

    public string FilePath => _filePath;

    public static string CreateDefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : Path.Combine(localAppData, "CIPHER");

        return Path.Combine(root, "logs", "cipher-audit.log");
    }

    /// <summary>Appends one event. Thread-safe; details are redacted before hashing/writing.</summary>
    public void Append(string eventType, string details)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("An audit event type is required.", nameof(eventType));
        }

        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RotateIfNeeded();

            var previousHash = _lastHash ?? ReadLastHash() ?? GenesisHash;
            var timestamp = DateTimeOffset.UtcNow.ToString("O");
            var safeDetails = SensitiveDataRedactor.Redact(details);
            var hash = ComputeHash(previousHash, timestamp, eventType.Trim(), safeDetails);
            var entry = new AuditEntry(timestamp, eventType.Trim(), safeDetails, previousHash, hash);
            File.AppendAllText(_filePath, JsonSerializer.Serialize(entry, JsonOptions) + "\n");
            _lastHash = hash;
        }
    }

    /// <summary>Recomputes the whole chain; reports the first line that fails, if any.</summary>
    public static AuditVerificationResult Verify(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new AuditVerificationResult(true, 0, null, "No audit log file exists yet; nothing to verify.");
        }

        var lines = File.ReadAllLines(filePath);
        var expectedPrevious = GenesisHash;
        var count = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            count++;
            AuditEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<AuditEntry>(line, JsonOptions);
            }
            catch (JsonException)
            {
                return new AuditVerificationResult(false, count, index + 1, $"Line {index + 1} is not valid audit JSON.");
            }

            if (entry is null)
            {
                return new AuditVerificationResult(false, count, index + 1, $"Line {index + 1} could not be read.");
            }

            if (!string.Equals(entry.Prev, expectedPrevious, StringComparison.Ordinal))
            {
                return new AuditVerificationResult(
                    false, count, index + 1,
                    $"Line {index + 1} does not chain to the previous entry (history may have been removed or reordered).");
            }

            var recomputed = ComputeHash(entry.Prev, entry.Ts, entry.Type, entry.Details);
            if (!string.Equals(entry.Hash, recomputed, StringComparison.Ordinal))
            {
                return new AuditVerificationResult(
                    false, count, index + 1,
                    $"Line {index + 1} fails its integrity hash (the entry was modified after being written).");
            }

            expectedPrevious = entry.Hash;
        }

        return new AuditVerificationResult(true, count, null, $"Audit chain verified: {count} entr{(count == 1 ? "y" : "ies")}, all hashes intact.");
    }

    private void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(_filePath);
            if (info.Exists && info.Length >= _rotationThresholdBytes)
            {
                File.Move(_filePath, _filePath + ".1", overwrite: true);
                _lastHash = null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Rotation is best-effort; appending continues on the current file.
        }
    }

    private string? ReadLastHash()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        string? lastHash = null;
        foreach (var line in File.ReadLines(_filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<AuditEntry>(line, JsonOptions);
                if (entry is not null)
                {
                    lastHash = entry.Hash;
                }
            }
            catch (JsonException)
            {
                // A damaged tail is a verification problem, not an append blocker.
            }
        }

        return lastHash;
    }

    private static string ComputeHash(string previousHash, string timestamp, string eventType, string details)
    {
        var payload = string.Join('|', previousHash, timestamp, eventType, details);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private sealed record AuditEntry(string Ts, string Type, string Details, string Prev, string Hash);
}
