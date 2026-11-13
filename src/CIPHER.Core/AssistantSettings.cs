using System.Text.Json;

namespace CIPHER.Core;

/// <summary>Non-sensitive user settings persisted locally as JSON.</summary>
public sealed record AssistantSettings
{
    public string WakePhrase { get; init; } = "Hey CIPHER";
    public bool AlwaysOnTop { get; init; } = true;
    public string VoiceLanguage { get; init; } = "en-US";
    public string SpeechVoice { get; init; } = "Default";
    public string ResultsDirectory { get; init; } = CreateDefaultResultsDirectory();

    /// <summary>Directory containing the offline Vosk speech model (Stage 6).</summary>
    public string VoiceModelPath { get; init; } = CreateDefaultVoiceModelPath();

    /// <summary>Path to espeak-ng.exe; empty means auto-detect (app folder, Program Files, PATH).</summary>
    public string EspeakExecutablePath { get; init; } = string.Empty;

    /// <summary>
    /// Master switch for the Stage 11 on-device brain (Flan-T5-small). On by default:
    /// when the deterministic grammar cannot map a request, the local model classifies
    /// it, and questions are answered locally. Nothing is sent off the PC either way.
    /// </summary>
    public bool LocalBrainEnabled { get; init; } = true;

    /// <summary>Directory containing the on-device brain model files (Stage 11).</summary>
    public string BrainModelPath { get; init; } = CreateDefaultBrainModelPath();

    /// <summary>Speak command results aloud with eSpeak NG when available.</summary>
    public bool SpeechOutputEnabled { get; init; }

    /// <summary>
    /// Master switch for Stage 8 external AI delegation. Off by default: nothing is ever
    /// sent off this PC unless the user enables this AND approves each prompt.
    /// </summary>
    public bool AiDelegationEnabled { get; init; }

    /// <summary>OpenAI-compatible API base URL (for example a cloud endpoint or a local Ollama server).</summary>
    public string AiBaseUrl { get; init; } = "https://api.openai.com/v1";

    /// <summary>Model name sent to the delegation endpoint.</summary>
    public string AiModel { get; init; } = "gpt-4o-mini";

    public AssistantSettings Normalize()
    {
        return this with
        {
            WakePhrase = string.IsNullOrWhiteSpace(WakePhrase) ? "Hey CIPHER" : WakePhrase.Trim(),
            VoiceLanguage = string.IsNullOrWhiteSpace(VoiceLanguage) ? "en-US" : VoiceLanguage.Trim(),
            SpeechVoice = string.IsNullOrWhiteSpace(SpeechVoice) ? "Default" : SpeechVoice.Trim(),
            ResultsDirectory = string.IsNullOrWhiteSpace(ResultsDirectory)
                ? CreateDefaultResultsDirectory()
                : ResultsDirectory.Trim(),
            VoiceModelPath = string.IsNullOrWhiteSpace(VoiceModelPath)
                ? CreateDefaultVoiceModelPath()
                : VoiceModelPath.Trim(),
            BrainModelPath = string.IsNullOrWhiteSpace(BrainModelPath)
                ? CreateDefaultBrainModelPath()
                : BrainModelPath.Trim(),
            EspeakExecutablePath = EspeakExecutablePath?.Trim() ?? string.Empty,
            AiBaseUrl = string.IsNullOrWhiteSpace(AiBaseUrl) ? "https://api.openai.com/v1" : AiBaseUrl.Trim(),
            AiModel = string.IsNullOrWhiteSpace(AiModel) ? "gpt-4o-mini" : AiModel.Trim()
        };
    }

    private static string CreateDefaultBrainModelPath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.GetTempPath()
            : localApplicationData;

        return Path.Combine(root, "CIPHER", "models", "brain");
    }

    private static string CreateDefaultVoiceModelPath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.GetTempPath()
            : localApplicationData;

        return Path.Combine(root, "CIPHER", "models", "vosk-model-small-en-us-0.15");
    }

    private static string CreateDefaultResultsDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.GetTempPath()
            : localApplicationData;

        return Path.Combine(root, "CIPHER", "Results");
    }
}

public interface ISettingsStore
{
    Task<AssistantSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AssistantSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>Atomic JSON settings store. Passwords and credentials are never stored here.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public JsonSettingsStore(string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("A settings path is required.", nameof(settingsPath));
        }

        _settingsPath = settingsPath;
    }

    public static JsonSettingsStore CreateDefault()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.GetTempPath()
            : localApplicationData;

        return new JsonSettingsStore(Path.Combine(root, "CIPHER", "settings.json"));
    }

    public async Task<AssistantSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AssistantSettings();
            }

            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer
                .DeserializeAsync<AssistantSettings>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return (settings ?? new AssistantSettings()).Normalize();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged settings file must not prevent the offline assistant from starting.
            return new AssistantSettings();
        }
    }

    public async Task SaveAsync(AssistantSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer
                .SerializeAsync(stream, settings.Normalize(), JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
