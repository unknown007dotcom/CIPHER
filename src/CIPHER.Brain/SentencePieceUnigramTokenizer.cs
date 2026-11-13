using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CIPHER.Brain;

/// <summary>
/// Minimal SentencePiece unigram tokenizer driven by a Hugging Face <c>tokenizer.json</c>
/// file (the format shipped with <c>Xenova/flan-t5-small</c>). It implements the pieces
/// CIPHER needs and nothing more:
///
///   * literal <c>Replace</c> normalizers with string patterns (the T5 precompiled
///     normalizer is a no-op for the plain ASCII text voice commands produce);
///   * the Metaspace pre-tokenizer (spaces become U+2581 "▁", with the dummy prefix
///     SentencePiece adds at the start of the text);
///   * unigram Viterbi segmentation over the vocab scores, with &lt;unk&gt; fallback;
///   * EOS (&lt;/s&gt;) appended on encode, special tokens skipped on decode.
///
/// Instances are immutable and thread-safe after construction.
/// </summary>
public sealed class SentencePieceUnigramTokenizer
{
    private const string DefaultSpaceReplacement = "▁";
    private const double UnkPenalty = -20.0;

    private readonly string[] _piecesById;
    private readonly Dictionary<string, int> _idByPiece;
    private readonly double[] _scoresById;
    private readonly int _maxPieceLength;
    private readonly string _spaceReplacement;
    private readonly bool _prependSpace;
    private readonly IReadOnlyList<(string Pattern, string Content)> _literalReplacements;

    private SentencePieceUnigramTokenizer(
        string[] piecesById,
        double[] scoresById,
        int padId,
        int eosId,
        int unkId,
        string spaceReplacement,
        bool prependSpace,
        IReadOnlyList<(string Pattern, string Content)> literalReplacements)
    {
        _piecesById = piecesById;
        _scoresById = scoresById;
        _idByPiece = new Dictionary<string, int>(StringComparer.Ordinal);
        var maxLength = 1;
        for (var id = 0; id < piecesById.Length; id++)
        {
            _idByPiece[piecesById[id]] = id;
            maxLength = Math.Max(maxLength, piecesById[id].Length);
        }

        _maxPieceLength = Math.Min(maxLength, 64);
        PadId = padId;
        EosId = eosId;
        UnkId = unkId;
        _spaceReplacement = spaceReplacement;
        _prependSpace = prependSpace;
        _literalReplacements = literalReplacements;
    }

    public int PadId { get; }

    public int EosId { get; }

    public int UnkId { get; }

    public int VocabSize => _piecesById.Length;

    public static SentencePieceUnigramTokenizer FromFile(string path)
    {
        return FromJson(File.ReadAllText(path));
    }

    public static SentencePieceUnigramTokenizer FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Special token ids come from added_tokens when present; the T5 defaults
        // (<pad>=0, </s>=1, <unk>=2) are the fallback.
        var specialIds = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.TryGetProperty("added_tokens", out var addedTokens) &&
            addedTokens.ValueKind == JsonValueKind.Array)
        {
            foreach (var added in addedTokens.EnumerateArray())
            {
                if (added.TryGetProperty("content", out var content) &&
                    added.TryGetProperty("id", out var idElement) &&
                    idElement.TryGetInt32(out var id))
                {
                    specialIds[content.GetString() ?? string.Empty] = id;
                }
            }
        }

        if (!root.TryGetProperty("model", out var model) ||
            !model.TryGetProperty("vocab", out var vocab) ||
            vocab.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("tokenizer.json does not contain a unigram model vocab.");
        }

        var pieces = new List<string>();
        var scores = new List<double>();
        foreach (var entry in vocab.EnumerateArray())
        {
            // Unigram vocab entries serialize as [piece, score] pairs.
            if (entry.ValueKind == JsonValueKind.Array && entry.GetArrayLength() >= 2)
            {
                pieces.Add(entry[0].GetString() ?? string.Empty);
                scores.Add(entry[1].GetDouble());
            }
            else if (entry.ValueKind == JsonValueKind.Object &&
                     entry.TryGetProperty("piece", out var pieceElement))
            {
                pieces.Add(pieceElement.GetString() ?? string.Empty);
                scores.Add(entry.TryGetProperty("score", out var scoreElement) ? scoreElement.GetDouble() : 0.0);
            }
        }

        if (pieces.Count == 0)
        {
            throw new InvalidDataException("tokenizer.json unigram vocab is empty.");
        }

        var unkId = specialIds.TryGetValue("<unk>", out var addedUnk)
            ? addedUnk
            : model.TryGetProperty("unk_id", out var unkElement) && unkElement.TryGetInt32(out var modelUnk)
                ? modelUnk
                : 2;
        var padId = specialIds.TryGetValue("<pad>", out var addedPad) ? addedPad : 0;
        var eosId = specialIds.TryGetValue("</s>", out var addedEos) ? addedEos : 1;

        var (spaceReplacement, prependSpace) = ReadMetaspace(root);
        var replacements = new List<(string Pattern, string Content)>();
        if (root.TryGetProperty("normalizer", out var normalizer))
        {
            CollectLiteralReplacements(normalizer, replacements);
        }

        return new SentencePieceUnigramTokenizer(
            pieces.ToArray(),
            scores.ToArray(),
            padId,
            eosId,
            unkId,
            spaceReplacement,
            prependSpace,
            replacements);
    }

    /// <summary>Encodes text to token ids, appending EOS (T5 has no BOS).</summary>
    public IReadOnlyList<int> Encode(string? text)
    {
        var normalized = Normalize(text);
        var ids = Segment(normalized);
        ids.Add(EosId);
        return ids;
    }

    /// <summary>Decodes token ids back to text, skipping special tokens.</summary>
    public string Decode(IEnumerable<int> tokenIds)
    {
        ArgumentNullException.ThrowIfNull(tokenIds);

        var builder = new StringBuilder();
        foreach (var id in tokenIds)
        {
            if (id == PadId || id == EosId || id == UnkId)
            {
                continue;
            }

            if (id >= 0 && id < _piecesById.Length)
            {
                builder.Append(_piecesById[id]);
            }
        }

        var text = builder.ToString().Replace(_spaceReplacement, " ", StringComparison.Ordinal);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private string Normalize(string? text)
    {
        var value = text ?? string.Empty;
        foreach (var (pattern, content) in _literalReplacements)
        {
            value = value.Replace(pattern, content, StringComparison.Ordinal);
        }

        value = value.Replace(" ", _spaceReplacement, StringComparison.Ordinal);
        if (_prependSpace && value.Length > 0)
        {
            value = _spaceReplacement + value;
        }

        return value;
    }

    private List<int> Segment(string normalized)
    {
        var ids = new List<int>();
        if (normalized.Length == 0)
        {
            return ids;
        }

        var length = normalized.Length;
        var bestScore = new double[length + 1];
        var bestFrom = new int[length + 1];
        var bestToken = new int[length + 1];
        for (var i = 1; i <= length; i++)
        {
            bestScore[i] = double.NegativeInfinity;
            bestFrom[i] = -1;
            bestToken[i] = UnkId;
        }

        for (var start = 0; start < length; start++)
        {
            if (double.IsNegativeInfinity(bestScore[start]))
            {
                continue;
            }

            // Unknown single character: always available so a path always exists.
            var unkScore = bestScore[start] + UnkPenalty;
            if (unkScore > bestScore[start + 1])
            {
                bestScore[start + 1] = unkScore;
                bestFrom[start + 1] = start;
                bestToken[start + 1] = UnkId;
            }

            var maxLength = Math.Min(_maxPieceLength, length - start);
            for (var pieceLength = 1; pieceLength <= maxLength; pieceLength++)
            {
                var piece = normalized.Substring(start, pieceLength);
                if (!_idByPiece.TryGetValue(piece, out var id))
                {
                    continue;
                }

                var candidate = bestScore[start] + _scoresById[id];
                if (candidate > bestScore[start + pieceLength])
                {
                    bestScore[start + pieceLength] = candidate;
                    bestFrom[start + pieceLength] = start;
                    bestToken[start + pieceLength] = id;
                }
            }
        }

        var reversed = new List<int>();
        var position = length;
        while (position > 0)
        {
            reversed.Add(bestToken[position]);
            var from = bestFrom[position];
            position = from < 0 ? 0 : from;
        }

        reversed.Reverse();
        return reversed;
    }

    private static (string Replacement, bool Prepend) ReadMetaspace(JsonElement root)
    {
        var replacement = DefaultSpaceReplacement;
        var prepend = true;

        if (root.TryGetProperty("pre_tokenizer", out var preTokenizer))
        {
            if (TryFindMetaspace(preTokenizer, ref replacement, ref prepend))
            {
                return (replacement, prepend);
            }
        }

        return (replacement, prepend);
    }

    private static bool TryFindMetaspace(JsonElement element, ref string replacement, ref bool prepend)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var typeElement) &&
                string.Equals(typeElement.GetString(), "Metaspace", StringComparison.Ordinal) &&
                element.TryGetProperty("replacement", out var replacementElement))
            {
                replacement = replacementElement.GetString() ?? DefaultSpaceReplacement;
                if (element.TryGetProperty("prepend_scheme", out var schemeElement))
                {
                    prepend = !string.Equals(schemeElement.GetString(), "never", StringComparison.OrdinalIgnoreCase);
                }
                else if (element.TryGetProperty("add_prefix_space", out var prefixElement))
                {
                    prepend = prefixElement.ValueKind == JsonValueKind.True;
                }

                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindMetaspace(property.Value, ref replacement, ref prepend))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindMetaspace(item, ref replacement, ref prepend))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void CollectLiteralReplacements(
        JsonElement element,
        List<(string Pattern, string Content)> replacements)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var typeElement) &&
                string.Equals(typeElement.GetString(), "Replace", StringComparison.Ordinal) &&
                element.TryGetProperty("pattern", out var pattern) &&
                pattern.ValueKind == JsonValueKind.Object &&
                pattern.TryGetProperty("String", out var literal) &&
                element.TryGetProperty("content", out var content))
            {
                var patternText = literal.GetString();
                var contentText = content.GetString();
                if (!string.IsNullOrEmpty(patternText) && contentText is not null)
                {
                    replacements.Add((patternText, contentText));
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                CollectLiteralReplacements(property.Value, replacements);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectLiteralReplacements(item, replacements);
            }
        }
    }
}
