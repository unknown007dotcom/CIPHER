using System.Text.Json;

namespace CIPHER.Actions;

/// <summary>One item held in CIPHER's reversible-delete quarantine.</summary>
public sealed record QuarantinedItem(
    string QuarantinePath,
    string DisplayName,
    string? OriginalPath,
    DateTimeOffset? QuarantinedUtc,
    bool IsDirectory);

/// <summary>
/// Manages the quarantine folder that deletes move items into. Each quarantined item can
/// carry a small JSON sidecar recording where it came from, which is what makes
/// `restore &lt;name&gt;` able to put it back exactly. Items quarantined before sidecars
/// existed still list fine; they restore into the Recovered folder instead.
/// </summary>
public sealed class QuarantineService
{
    private const string MetadataSuffix = ".cipher-quarantine.json";

    public QuarantineService(string? root = null)
    {
        Root = string.IsNullOrWhiteSpace(root) ? DefaultRoot() : root;
    }

    public string Root { get; }

    public static string DefaultRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var basePath = string.IsNullOrWhiteSpace(localAppData) ? Path.GetTempPath() : localAppData;
        return Path.Combine(basePath, "CIPHER", "Quarantine");
    }

    public static string RecoveredRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var basePath = string.IsNullOrWhiteSpace(localAppData) ? Path.GetTempPath() : localAppData;
        return Path.Combine(basePath, "CIPHER", "Recovered");
    }

    public int Count() => List().Count;

    public IReadOnlyList<QuarantinedItem> List()
    {
        if (!Directory.Exists(Root))
        {
            return Array.Empty<QuarantinedItem>();
        }

        var items = new List<QuarantinedItem>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(Root))
        {
            if (entry.EndsWith(MetadataSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var metadata = ReadMetadata(entry);
            items.Add(new QuarantinedItem(
                entry,
                StripTimestampPrefix(Path.GetFileName(entry)),
                metadata?.OriginalPath,
                metadata?.QuarantinedUtc,
                Directory.Exists(entry)));
        }

        return items
            .OrderByDescending(item => item.QuarantinedUtc ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>Finds items by original display name or exact quarantined file name.</summary>
    public IReadOnlyList<QuarantinedItem> FindMatches(string name)
    {
        var wanted = name.Trim().Trim('"');
        if (wanted.Length == 0)
        {
            return Array.Empty<QuarantinedItem>();
        }

        return List()
            .Where(item =>
                string.Equals(item.DisplayName, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(item.QuarantinePath), wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void WriteMetadata(string quarantinePath, string originalPath, bool isDirectory)
    {
        var metadata = new QuarantineMetadata(originalPath, DateTimeOffset.UtcNow, isDirectory);
        File.WriteAllText(MetadataPathFor(quarantinePath), JsonSerializer.Serialize(metadata));
    }

    public void DeleteMetadata(string quarantinePath)
    {
        var metadataPath = MetadataPathFor(quarantinePath);
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }
    }

    public static string MetadataPathFor(string quarantinePath) => quarantinePath + MetadataSuffix;

    private static QuarantineMetadata? ReadMetadata(string quarantinePath)
    {
        try
        {
            var metadataPath = MetadataPathFor(quarantinePath);
            return File.Exists(metadataPath)
                ? JsonSerializer.Deserialize<QuarantineMetadata>(File.ReadAllText(metadataPath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string StripTimestampPrefix(string fileName)
    {
        // Quarantined names look like "20261108-143022123-report.txt" (yyyyMMdd-HHmmssfff-).
        return fileName.Length > 19 && char.IsDigit(fileName[0]) && fileName[8] == '-'
            ? fileName[19..]
            : fileName;
    }

    private sealed record QuarantineMetadata(string OriginalPath, DateTimeOffset QuarantinedUtc, bool IsDirectory);
}
