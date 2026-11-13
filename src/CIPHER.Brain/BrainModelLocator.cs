namespace CIPHER.Brain;

/// <summary>
/// Locates the bundled on-device brain model directory (Stage 11). A directory counts
/// as a brain when it contains the encoder, the merged decoder (both int8-quantized
/// ONNX), and the SentencePiece tokenizer description. Search order mirrors
/// <c>VoskModelLocator</c>: the configured path, a <c>models\brain</c> folder next to
/// the application (the Stage 10/11 installer layout), then
/// <c>%LocalAppData%\CIPHER\models\brain</c> (user-installed layout).
/// </summary>
public static class BrainModelLocator
{
    /// <summary>Human-readable identity of the bundled model.</summary>
    public const string ModelDisplayName = "Flan-T5-small (80M parameters, int8 ONNX)";

    public const string EncoderFileName = "encoder_model_quantized.onnx";
    public const string DecoderFileName = "decoder_model_merged_quantized.onnx";
    public const string TokenizerFileName = "tokenizer.json";

    public static bool IsBrainDirectory(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               Directory.Exists(path) &&
               File.Exists(Path.Combine(path, EncoderFileName)) &&
               File.Exists(Path.Combine(path, DecoderFileName)) &&
               File.Exists(Path.Combine(path, TokenizerFileName));
    }

    public static string? Resolve(string? configuredPath)
    {
        if (IsBrainDirectory(configuredPath))
        {
            return configuredPath;
        }

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "models", "brain"),
            Path.Combine(AppContext.BaseDirectory, "brain"),
            Path.Combine(Environment.CurrentDirectory, "models", "brain")
        };

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "CIPHER", "models", "brain"));
        }

        return candidates.FirstOrDefault(IsBrainDirectory);
    }
}
