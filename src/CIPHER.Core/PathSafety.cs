namespace CIPHER.Core;

/// <summary>Lexical protection for destructive and privileged file-system operations.</summary>
public static class PathSafety
{
    public static string NormalizePath(string path, string? workingDirectory = null)
    {
        var expanded = Environment.ExpandEnvironmentVariables(Unquote(path.Trim()));
        return Path.GetFullPath(expanded, workingDirectory ?? Environment.CurrentDirectory);
    }

    public static string Unquote(string value)
    {
        var result = value.Trim();
        return result.Length >= 2 &&
               ((result[0] == '"' && result[^1] == '"') ||
                (result[0] == '\'' && result[^1] == '\''))
            ? result[1..^1].Trim()
            : result;
    }

    /// <summary>
    /// Returns true for operating-system locations that CIPHER must never delete, overwrite,
    /// or use as a destructive-operation destination.
    /// </summary>
    public static bool IsProtectedPath(string? path, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "No path was supplied.";
            return true;
        }

        string fullPath;
        try
        {
            fullPath = NormalizePath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = $"The path is invalid: {ex.Message}";
            return true;
        }

        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(TrimSeparator(fullPath), TrimSeparator(root), StringComparison.OrdinalIgnoreCase))
        {
            reason = "A drive root cannot be used for this operation.";
            return true;
        }

        var protectedDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var directory in protectedDirectories.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            if (IsSameOrDescendant(fullPath, directory))
            {
                reason = $"'{fullPath}' is inside a protected Windows directory.";
                return true;
            }
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile) &&
            string.Equals(TrimSeparator(fullPath), TrimSeparator(userProfile), StringComparison.OrdinalIgnoreCase))
        {
            reason = "The user profile root cannot be deleted or overwritten.";
            return true;
        }

        return false;
    }

    public static bool IsSameOrDescendant(string candidate, string directory)
    {
        var normalizedCandidate = TrimSeparator(Path.GetFullPath(candidate));
        var normalizedDirectory = TrimSeparator(Path.GetFullPath(directory));

        return string.Equals(normalizedCandidate, normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(
                   normalizedDirectory + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(
                   normalizedDirectory + Path.AltDirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimSeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
