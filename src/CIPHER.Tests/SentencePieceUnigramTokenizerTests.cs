using CIPHER.Brain;
using Xunit;

namespace CIPHER.Tests;

/// <summary>
/// Tokenizer tests run against a hand-built miniature tokenizer.json with the same
/// shape as the real Flan-T5 one (Metaspace + Unigram), so the segmentation and
/// decode logic are verified without the 100 MB model bundle.
/// </summary>
public sealed class SentencePieceUnigramTokenizerTests
{
    private const string MiniTokenizerJson = """
        {
          "version": "1.0",
          "added_tokens": [
            { "id": 0, "content": "<pad>", "special": true },
            { "id": 1, "content": "</s>", "special": true },
            { "id": 2, "content": "<unk>", "special": true }
          ],
          "normalizer": { "type": "Precompiled", "precompiled_charsmap": "" },
          "pre_tokenizer": {
            "type": "Metaspace",
            "replacement": "▁",
            "prepend_scheme": "always",
            "split": true
          },
          "model": {
            "type": "Unigram",
            "unk_id": 2,
            "byte_fallback": false,
            "vocab": [
              ["<pad>", 0.0],
              ["</s>", 0.0],
              ["<unk>", 0.0],
              ["▁", -2.0],
              ["▁hello", -1.0],
              ["▁world", -1.2],
              ["hello", -3.0],
              ["world", -3.0],
              ["▁open", -1.1],
              ["▁notepad", -1.3],
              ["open", -3.2],
              ["notepad", -3.4]
            ]
          }
        }
        """;

    private readonly SentencePieceUnigramTokenizer _tokenizer =
        SentencePieceUnigramTokenizer.FromJson(MiniTokenizerJson);

    [Fact]
    public void SpecialIds_ComeFromAddedTokens()
    {
        Assert.Equal(0, _tokenizer.PadId);
        Assert.Equal(1, _tokenizer.EosId);
        Assert.Equal(2, _tokenizer.UnkId);
        Assert.Equal(12, _tokenizer.VocabSize);
    }

    [Fact]
    public void Encode_PrefersLongestHighScoringPieces_AndAppendsEos()
    {
        // " open notepad" (after the SentencePiece dummy prefix) segments into the
        // two multi-char pieces, not ▁+open / ▁+notepad, because they score better.
        var ids = _tokenizer.Encode("open notepad");

        Assert.Equal(new[] { 8, 9, 1 }, ids);
    }

    [Fact]
    public void Decode_RoundTripsEncodedText()
    {
        var ids = _tokenizer.Encode("hello world");

        Assert.Equal("hello world", _tokenizer.Decode(ids));
    }

    [Fact]
    public void Encode_UnknownCharacters_FallBackToUnk_AndDecodeSkipsThem()
    {
        var ids = _tokenizer.Encode("hello ~world");

        Assert.Contains(_tokenizer.UnkId, ids);
        Assert.Equal(_tokenizer.EosId, ids[^1]);
        Assert.Equal("hello world", _tokenizer.Decode(ids));
    }

    [Fact]
    public void Encode_EmptyText_ProducesOnlyEos()
    {
        Assert.Equal(new[] { _tokenizer.EosId }, _tokenizer.Encode(string.Empty));
    }
}
