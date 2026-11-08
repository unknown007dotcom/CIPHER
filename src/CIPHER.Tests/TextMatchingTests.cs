using CIPHER.Core;
using Xunit;

namespace CIPHER.Tests;

public sealed class TextMatchingTests
{
    [Fact]
    public void ContainsText_IgnoresCaseAndWhitespaceDifferences()
    {
        var editorText = "Hello   from\r\nCIPHER.\r\nSecond line.";

        Assert.True(TextMatching.ContainsText(editorText, "hello from CIPHER."));
        Assert.True(TextMatching.ContainsText(editorText, "CIPHER. Second"));
    }

    [Fact]
    public void ContainsText_ReturnsFalseForMissingOrEmptyNeedle()
    {
        Assert.False(TextMatching.ContainsText("some text", "missing"));
        Assert.False(TextMatching.ContainsText("some text", ""));
        Assert.False(TextMatching.ContainsText(null, "text"));
        Assert.False(TextMatching.ContainsText("some text", null));
    }

    [Fact]
    public void CountOccurrences_CountsNormalizedMatches()
    {
        Assert.Equal(2, TextMatching.CountOccurrences("test and TEST again", "test"));
        Assert.Equal(0, TextMatching.CountOccurrences("nothing here", "test"));
        Assert.Equal(0, TextMatching.CountOccurrences("anything", ""));
    }
}
