using CIPHER.Core;
using Xunit;

namespace CIPHER.Tests;

/// <summary>
/// Stage 11 deterministic grammar + policy: dictation, volume, lock/power, web
/// search, and brain questions. These stay rule-based on purpose — the local model
/// only classifies phrasing the grammar cannot pin down (see BrainIntentMapperTests).
/// </summary>
public sealed class Stage11CommandTests
{
    private readonly DeterministicCommandParser _parser = new();
    private readonly DefaultActionPolicy _policy = new();
    private readonly CommandContext _context = new(
        "C:\\Users\\Public",
        "TestUser",
        "TEST-PC",
        new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("search for cats", "cats")]
    [InlineData("search the web for local news", "local news")]
    [InlineData("google pizza recipes", "pizza recipes")]
    [InlineData("look up the eiffel tower", "eiffel tower")]
    public void Parse_WebSearch_ExtractsQuery(string input, string expectedQuery)
    {
        var command = _parser.Parse(input, _context);

        Assert.Equal(IntentKind.WebSearch, command.Intent);
        Assert.Equal(expectedQuery, command.Target);
    }

    [Theory]
    [InlineData("start dictation", IntentKind.StartDictation)]
    [InlineData("begin dictation", IntentKind.StartDictation)]
    [InlineData("stop dictation", IntentKind.StopDictation)]
    [InlineData("end dictation", IntentKind.StopDictation)]
    public void Parse_DictationCommands(string input, IntentKind expected)
    {
        Assert.Equal(expected, _parser.Parse(input, _context).Intent);
    }

    [Theory]
    [InlineData("volume up", "up")]
    [InlineData("turn the volume down", "down")]
    [InlineData("mute", "mute")]
    [InlineData("unmute", "unmute")]
    public void Parse_VolumeCommands(string input, string expectedDirection)
    {
        var command = _parser.Parse(input, _context);

        Assert.Equal(IntentKind.SetVolume, command.Intent);
        Assert.Equal(expectedDirection, command.Argument);
    }

    [Fact]
    public void Parse_LockTheComputer()
    {
        Assert.Equal(IntentKind.LockWorkstation, _parser.Parse("lock the computer", _context).Intent);
    }

    [Theory]
    [InlineData("shut down the computer", IntentKind.ShutdownComputer)]
    [InlineData("shutdown", IntentKind.ShutdownComputer)]
    [InlineData("restart the computer", IntentKind.RestartComputer)]
    [InlineData("reboot", IntentKind.RestartComputer)]
    public void Parse_PowerCommands(string input, IntentKind expected)
    {
        Assert.Equal(expected, _parser.Parse(input, _context).Intent);
    }

    [Theory]
    [InlineData("what is the capital of France")]
    [InlineData("who wrote Romeo and Juliet")]
    [InlineData("tell me about black holes")]
    [InlineData("explain photosynthesis")]
    public void Parse_Questions_GoToLocalBrain(string input)
    {
        var command = _parser.Parse(input, _context);

        Assert.Equal(IntentKind.AskLocalBrain, command.Intent);
        Assert.Equal(input, command.Target);
    }

    [Fact]
    public void Parse_AskPrefix_GoesToLocalBrain()
    {
        var command = _parser.Parse("ask what is two plus two", _context);

        Assert.Equal(IntentKind.AskLocalBrain, command.Intent);
        Assert.Equal("what is two plus two", command.Target);
    }

    [Fact]
    public void Parse_OpenDownloads_ResolvesKnownFolder()
    {
        var command = _parser.Parse("open downloads", _context);

        Assert.Equal(IntentKind.OpenPath, command.Intent);
        Assert.EndsWith("Downloads", command.Target);
    }

    [Fact]
    public void Policy_PowerCommands_RequireConfirmation()
    {
        var shutdown = _policy.Evaluate(_parser.Parse("shut down the computer", _context), _context);
        var restart = _policy.Evaluate(_parser.Parse("restart the computer", _context), _context);

        Assert.True(shutdown.IsAllowed && shutdown.RequiresConfirmation);
        Assert.True(restart.IsAllowed && restart.RequiresConfirmation);
    }

    [Theory]
    [InlineData(IntentKind.WebSearch)]
    [InlineData(IntentKind.AskLocalBrain)]
    [InlineData(IntentKind.StartDictation)]
    [InlineData(IntentKind.StopDictation)]
    [InlineData(IntentKind.SetVolume)]
    [InlineData(IntentKind.LockWorkstation)]
    public void Policy_JarvisIntents_AreAllowedWithoutConfirmation(IntentKind intent)
    {
        var command = new ParsedCommand("test", "test", intent, Target: "x", Argument: "up");
        var decision = _policy.Evaluate(command, _context);

        Assert.True(decision.IsAllowed);
        Assert.False(decision.RequiresConfirmation);
    }
}
