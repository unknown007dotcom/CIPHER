using CIPHER.Core;

namespace CIPHER.Tests;

public sealed class VoiceSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static VoiceSession CreateSession() => new(new WakeWordDetector("Hey CIPHER"));

    [Fact]
    public void WakePhraseWithCommandBecomesCommandImmediately()
    {
        var result = CreateSession().HandleFinalTranscript("Hey CIPHER open Notepad", Now);

        Assert.Equal(VoiceTranscriptOutcome.CommandReady, result.Outcome);
        Assert.Equal("open Notepad", result.CommandText);
    }

    [Fact]
    public void WakePhraseAloneArmsFollowUpWindow()
    {
        var session = CreateSession();

        var wake = session.HandleFinalTranscript("hey cipher", Now);
        var command = session.HandleFinalTranscript("open Notepad", Now.AddSeconds(5));

        Assert.Equal(VoiceTranscriptOutcome.WakePhraseHeard, wake.Outcome);
        Assert.Equal(VoiceTranscriptOutcome.CommandReady, command.Outcome);
        Assert.Equal("open Notepad", command.CommandText);
    }

    [Fact]
    public void TranscriptWithoutWakePhraseIsIgnored()
    {
        var result = CreateSession().HandleFinalTranscript("open Notepad", Now);

        Assert.Equal(VoiceTranscriptOutcome.Ignored, result.Outcome);
    }

    [Fact]
    public void FollowUpAfterWindowExpiresIsIgnored()
    {
        var session = CreateSession();
        session.HandleFinalTranscript("hey cipher", Now);

        var result = session.HandleFinalTranscript("open Notepad", Now.AddSeconds(30));

        Assert.Equal(VoiceTranscriptOutcome.Ignored, result.Outcome);
    }

    [Fact]
    public void PrimedSessionTreatsNextUtteranceAsCommand()
    {
        var session = CreateSession();
        session.Prime(Now);

        var result = session.HandleFinalTranscript("what time is it", Now.AddSeconds(2));

        Assert.Equal(VoiceTranscriptOutcome.CommandReady, result.Outcome);
        Assert.Equal("what time is it", result.CommandText);
    }

    [Fact]
    public void EmptyTranscriptIsIgnored()
    {
        var result = CreateSession().HandleFinalTranscript("   ", Now);

        Assert.Equal(VoiceTranscriptOutcome.Ignored, result.Outcome);
    }
}

public sealed class VoskResultParserTests
{
    [Fact]
    public void ExtractsFinalText()
    {
        var text = VoskResultParser.GetText("{\"result\": [], \"text\": \"hey cipher open notepad\"}");

        Assert.Equal("hey cipher open notepad", text);
    }

    [Fact]
    public void ExtractsPartialText()
    {
        var text = VoskResultParser.GetPartialText("{\"partial\": \"hey cipher\"}");

        Assert.Equal("hey cipher", text);
    }

    [Fact]
    public void ReturnsNullForMalformedJson()
    {
        Assert.Null(VoskResultParser.GetText("not json"));
        Assert.Null(VoskResultParser.GetText(null));
        Assert.Null(VoskResultParser.GetText("{\"text\": \"\"}"));
        Assert.Null(VoskResultParser.GetPartialText("{\"text\": \"wrong property\"}"));
    }
}
