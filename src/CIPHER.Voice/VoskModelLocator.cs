namespace CIPHER.Voice;

/// <summary>
/// Locates an offline Vosk speech-model directory. A directory counts as a model when it
/// contains the <c>conf/model.conf</c> file every Vosk model ships. Search order: the
/// configured path, a <c>models</c> folder next to the application (installer layout), then
/// <c>%LocalAppData%\CIPHER\models</c> (user-installed layout).
/// </summary>
public static class VoskModelLocator
{
    public const string SmallEnglishModelName = "vosk-model-small-en-us-0.15";

    public static bool IsModelDirectory(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               Directory.Exists(path) &&
               File.Exists(Path.Combine(path, "conf", "model.conf"));
    }

    public static string? Resolve(string? configuredPath)
    {
        if (IsModelDirectory(configuredPath))
        {
            return configuredPath;
        }

        var candidates = new List<string>();

        // The installer stages the model under "models" (Stage 10 layout); the raw
        // dependency bundle calls the folder "vosk". Probe both so either layout —
        // and either model folder name — resolves without configuration.
        foreach (var folderName in new[] { "models", "vosk" })
        {
            var applicationModels = Path.Combine(AppContext.BaseDirectory, folderName);
            candidates.Add(Path.Combine(applicationModels, SmallEnglishModelName));
            candidates.Add(Path.Combine(applicationModels, "model-en-us-small"));
            AddSubdirectories(candidates, applicationModels);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var userModels = Path.Combine(localAppData, "CIPHER", "models");
            candidates.Add(Path.Combine(userModels, SmallEnglishModelName));
            candidates.Add(Path.Combine(userModels, "model-en-us-small"));
            AddSubdirectories(candidates, userModels);
        }

        return candidates.FirstOrDefault(IsModelDirectory);
    }

    private static void AddSubdirectories(List<string> candidates, string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                candidates.AddRange(Directory.GetDirectories(directory));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable models folder simply means "no model found there".
        }
    }
}
