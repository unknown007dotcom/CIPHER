namespace CIPHER.Brain;

/// <summary>
/// A text-generation model that runs entirely on this PC. Implementations must be
/// safe to call from any thread; calls are serialized by the implementation.
/// </summary>
public interface ILocalBrain
{
    /// <summary>True when the model files were found and can be loaded.</summary>
    bool IsAvailable { get; }

    /// <summary>Short human-readable description of the underlying model.</summary>
    string ModelDescription { get; }

    /// <summary>
    /// Generates up to <paramref name="maxNewTokens"/> tokens of text continuing the
    /// given instruction/prompt, using greedy decoding (deterministic output).
    /// </summary>
    string Generate(string prompt, int maxNewTokens, CancellationToken cancellationToken = default);
}
