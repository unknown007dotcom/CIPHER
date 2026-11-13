using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CIPHER.Brain;

/// <summary>
/// On-device text generation with Google's Flan-T5-small (80M parameters) exported to
/// int8-quantized ONNX, run by ONNX Runtime on the CPU. Nothing is sent off the PC.
///
/// The graph pair is the transformers.js layout: an encoder
/// (<c>encoder_model_quantized.onnx</c>) and a merged decoder with KV cache
/// (<c>decoder_model_merged_quantized.onnx</c>). Input/output names, layer count, and
/// cache shapes are discovered from session metadata at load time rather than assumed,
/// so a re-export of the same model keeps working. Decoding is greedy, which makes
/// the brain deterministic: the same prompt always produces the same words.
///
/// Sessions load lazily on first use (a few hundred milliseconds) and are reused for
/// the process lifetime. Calls are serialized; CIPHER issues one brain request at a
/// time by design.
/// </summary>
public sealed class FlanT5Brain : ILocalBrain, IDisposable
{
    private const int DefaultNumHeads = 8;
    private const int DefaultHeadDimension = 64;
    private const int MaxInputTokens = 512;

    private readonly string _modelDirectory;
    private readonly object _gate = new();

    private SentencePieceUnigramTokenizer? _tokenizer;
    private InferenceSession? _encoder;
    private InferenceSession? _decoder;
    private DecoderLayout? _layout;
    private string? _loadError;
    private bool _disposed;

    public FlanT5Brain(string modelDirectory)
    {
        _modelDirectory = modelDirectory ?? throw new ArgumentNullException(nameof(modelDirectory));
        IsAvailable = BrainModelLocator.IsBrainDirectory(modelDirectory);
    }

    public bool IsAvailable { get; }

    public string ModelDescription => BrainModelLocator.ModelDisplayName;

    public string Generate(string prompt, int maxNewTokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                $"The local brain model was not found in '{_modelDirectory}'. " +
                $"Expected {BrainModelLocator.EncoderFileName}, {BrainModelLocator.DecoderFileName}, " +
                $"and {BrainModelLocator.TokenizerFileName}.");
        }

        if (maxNewTokens <= 0)
        {
            return string.Empty;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLoaded();
            return GenerateCore(prompt, maxNewTokens, cancellationToken);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _encoder?.Dispose();
            _decoder?.Dispose();
            _encoder = null;
            _decoder = null;
        }
    }

    private void EnsureLoaded()
    {
        if (_encoder is not null && _decoder is not null && _tokenizer is not null && _layout is not null)
        {
            return;
        }

        if (_loadError is not null)
        {
            throw new InvalidOperationException(_loadError);
        }

        try
        {
            _tokenizer = SentencePieceUnigramTokenizer.FromFile(
                Path.Combine(_modelDirectory, BrainModelLocator.TokenizerFileName));

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4)
            };

            _encoder = new InferenceSession(
                Path.Combine(_modelDirectory, BrainModelLocator.EncoderFileName), options);
            _decoder = new InferenceSession(
                Path.Combine(_modelDirectory, BrainModelLocator.DecoderFileName), options);
            _layout = DecoderLayout.Discover(_encoder, _decoder);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _loadError = $"The local brain failed to load: {ex.Message}";
            throw new InvalidOperationException(_loadError, ex);
        }
    }

    private string GenerateCore(string prompt, int maxNewTokens, CancellationToken cancellationToken)
    {
        var tokenizer = _tokenizer!;
        var layout = _layout!;

        var encoded = tokenizer.Encode(prompt);
        if (encoded.Count > MaxInputTokens)
        {
            encoded = encoded.Take(MaxInputTokens).ToList();
        }

        var inputIds = new DenseTensor<long>(encoded.Select(id => (long)id).ToArray(), new[] { 1, encoded.Count });
        var attentionMask = new DenseTensor<long>(
            Enumerable.Repeat(1L, encoded.Count).ToArray(), new[] { 1, encoded.Count });

        Tensor<float> encoderHidden;
        using (var encoderResults = _encoder!.Run(new[]
               {
                   NamedOnnxValue.CreateFromTensor(layout.EncoderInputIdsName, inputIds),
                   NamedOnnxValue.CreateFromTensor(layout.EncoderInputMaskName, attentionMask)
               }))
        {
            var hidden = encoderResults.First(result => result.Name == layout.EncoderOutputName).AsTensor<float>();
            encoderHidden = new DenseTensor<float>(hidden.ToArray(), hidden.Dimensions.ToArray());
        }

        var past = new Dictionary<string, Tensor<float>>(StringComparer.Ordinal);
        foreach (var pastName in layout.PastInputNames)
        {
            past[pastName] = new DenseTensor<float>(
                Array.Empty<float>(), new[] { 1, layout.NumHeads, 0, layout.HeadDimension });
        }

        var generated = new List<int>();
        long nextToken = tokenizer.PadId; // T5 decoder_start_token_id is the pad token.
        var firstStep = true;

        for (var step = 0; step < maxNewTokens; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(
                    layout.DecoderInputIdsName,
                    new DenseTensor<long>(new[] { nextToken }, new[] { 1, 1 })),
                NamedOnnxValue.CreateFromTensor(layout.DecoderHiddenStatesName, encoderHidden),
                NamedOnnxValue.CreateFromTensor(layout.DecoderEncoderMaskName, attentionMask)
            };

            foreach (var pastName in layout.PastInputNames)
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(pastName, past[pastName]));
            }

            if (layout.UseCacheBranchName is not null)
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(
                    layout.UseCacheBranchName,
                    new DenseTensor<bool>(new[] { !firstStep }, new[] { 1 })));
            }

            using var decoderResults = _decoder!.Run(inputs);

            var logits = decoderResults.First(result => result.Name == layout.LogitsName).AsTensor<float>();
            var vocabSize = logits.Dimensions[2];
            var lastPositionOffset = (logits.Dimensions[1] - 1) * vocabSize;
            var logitsData = logits.ToArray();
            var bestToken = 0;
            var bestScore = float.NegativeInfinity;
            for (var candidate = 0; candidate < vocabSize; candidate++)
            {
                var score = logitsData[lastPositionOffset + candidate];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestToken = candidate;
                }
            }

            // Copy the new cache out before the results (and their tensors) are disposed.
            foreach (var presentName in layout.PresentOutputNames)
            {
                var tensor = decoderResults.First(result => result.Name == presentName).AsTensor<float>();
                past[layout.PresentToPastName(presentName)] =
                    new DenseTensor<float>(tensor.ToArray(), tensor.Dimensions.ToArray());
            }

            firstStep = false;
            if (bestToken == tokenizer.EosId)
            {
                break;
            }

            generated.Add(bestToken);
            nextToken = bestToken;
        }

        return tokenizer.Decode(generated);
    }

    /// <summary>Names/shapes of the merged-decoder graph, discovered from its metadata.</summary>
    private sealed class DecoderLayout
    {
        private DecoderLayout(
            string encoderInputIdsName,
            string encoderInputMaskName,
            string encoderOutputName,
            string decoderInputIdsName,
            string decoderHiddenStatesName,
            string decoderEncoderMaskName,
            string logitsName,
            string? useCacheBranchName,
            IReadOnlyList<string> pastInputNames,
            IReadOnlyList<string> presentOutputNames,
            int numHeads,
            int headDimension)
        {
            EncoderInputIdsName = encoderInputIdsName;
            EncoderInputMaskName = encoderInputMaskName;
            EncoderOutputName = encoderOutputName;
            DecoderInputIdsName = decoderInputIdsName;
            DecoderHiddenStatesName = decoderHiddenStatesName;
            DecoderEncoderMaskName = decoderEncoderMaskName;
            LogitsName = logitsName;
            UseCacheBranchName = useCacheBranchName;
            PastInputNames = pastInputNames;
            PresentOutputNames = presentOutputNames;
            NumHeads = numHeads;
            HeadDimension = headDimension;
        }

        public string EncoderInputIdsName { get; }
        public string EncoderInputMaskName { get; }
        public string EncoderOutputName { get; }
        public string DecoderInputIdsName { get; }
        public string DecoderHiddenStatesName { get; }
        public string DecoderEncoderMaskName { get; }
        public string LogitsName { get; }
        public string? UseCacheBranchName { get; }
        public IReadOnlyList<string> PastInputNames { get; }
        public IReadOnlyList<string> PresentOutputNames { get; }
        public int NumHeads { get; }
        public int HeadDimension { get; }

        public string PresentToPastName(string presentName)
        {
            // "present.3.decoder.key" feeds back as "past_key_values.3.decoder.key".
            return presentName.StartsWith("present", StringComparison.Ordinal)
                ? "past_key_values" + presentName["present".Length..]
                : presentName;
        }

        public static DecoderLayout Discover(InferenceSession encoder, InferenceSession decoder)
        {
            var encoderInputs = encoder.InputMetadata.Keys.ToList();
            var encoderOutputs = encoder.OutputMetadata.Keys.ToList();
            var decoderInputs = decoder.InputMetadata.Keys.ToList();
            var decoderOutputs = decoder.OutputMetadata.Keys.ToList();

            var pastInputNames = decoderInputs
                .Where(name => name.StartsWith("past_key_values.", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (pastInputNames.Count == 0)
            {
                throw new InvalidDataException(
                    "The decoder graph exposes no past_key_values inputs; a merged decoder with KV cache is required.");
            }

            var presentOutputNames = decoderOutputs
                .Where(name => name.StartsWith("present", StringComparison.Ordinal) ||
                               name.StartsWith("past_key_values.", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (presentOutputNames.Count != pastInputNames.Count)
            {
                throw new InvalidDataException(
                    $"The decoder graph has {pastInputNames.Count} past inputs but {presentOutputNames.Count} present outputs; they must pair up.");
            }

            var numHeads = DefaultNumHeads;
            var headDimension = DefaultHeadDimension;
            var firstPastDimensions = decoder.InputMetadata[pastInputNames[0]].Dimensions;
            if (firstPastDimensions.Length == 4)
            {
                if (firstPastDimensions[1] > 0)
                {
                    numHeads = firstPastDimensions[1];
                }

                if (firstPastDimensions[3] > 0)
                {
                    headDimension = firstPastDimensions[3];
                }
            }

            return new DecoderLayout(
                encoderInputIdsName: FindName(encoderInputs, "input_ids"),
                encoderInputMaskName: FindName(encoderInputs, "attention_mask"),
                encoderOutputName: encoderOutputs.Contains("last_hidden_state")
                    ? "last_hidden_state"
                    : encoderOutputs[0],
                decoderInputIdsName: FindName(decoderInputs, "input_ids", "decoder_input_ids"),
                decoderHiddenStatesName: FindName(decoderInputs, "encoder_hidden_states"),
                decoderEncoderMaskName: FindName(decoderInputs, "encoder_attention_mask", "attention_mask"),
                logitsName: decoderOutputs.Contains("logits") ? "logits" : decoderOutputs[0],
                useCacheBranchName: decoderInputs.FirstOrDefault(
                    name => string.Equals(name, "use_cache_branch", StringComparison.Ordinal)),
                pastInputNames: pastInputNames,
                presentOutputNames: presentOutputNames,
                numHeads: numHeads,
                headDimension: headDimension);
        }

        private static string FindName(IReadOnlyList<string> names, params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                var exact = names.FirstOrDefault(
                    name => string.Equals(name, candidate, StringComparison.Ordinal));
                if (exact is not null)
                {
                    return exact;
                }
            }

            foreach (var candidate in candidates)
            {
                var containing = names.FirstOrDefault(
                    name => name.Contains(candidate, StringComparison.Ordinal));
                if (containing is not null)
                {
                    return containing;
                }
            }

            throw new InvalidDataException(
                $"None of [{string.Join(", ", candidates)}] was found in the model graph (inputs/outputs: {string.Join(", ", names)}).");
        }
    }
}
