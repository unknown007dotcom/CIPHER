using CIPHER.Core;
using Xunit;

namespace CIPHER.Tests;

public sealed class WakeWordDetectorTests
{
    [Theory]
    [InlineData("Hey CIPHER open Notepad", true, "open Notepad")]
    [InlineData("hey cipher, open Notepad", true, "open Notepad")]
    [InlineData("open Notepad", false, "open Notepad")]
    public void Process_DetectsConfiguredPhrase(string input, bool expectedDetected, string expectedCommand)
    {
        var detector = new WakeWordDetector();

        var result = detector.Process(input);

        Assert.Equal(expectedDetected, result.Detected);
        Assert.Equal(expectedCommand, result.CommandText);
    }
}
