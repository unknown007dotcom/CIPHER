using CIPHER.Core;
using Xunit;

namespace CIPHER.Tests;

public sealed class ExternalAiCommandTests
{
    private readonly CommandContext _context = new(
        "C:\\Users\\Public",
        "TestUser",
        "TEST-PC",
        new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("ask ai what is the capital of France", "what is the capital of France")]
    [InlineData("ask the ai to write a haiku about rain", "write a haiku about rain")]
    [InlineData("ask ai: summarize this topic", "summarize this topic")]
    [InlineData("delegate draft a polite follow-up email", "draft a polite follow-up email")]
    [InlineData("delegate to ai plan my study schedule", "plan my study schedule")]
    public void ParsesDelegationCommands(string input, string expectedPrompt)
    {
        var command = new DeterministicCommandParser().Parse(input, _context);

        Assert.Equal(IntentKind.AskExternalAi, command.Intent);
        Assert.Equal(expectedPrompt, command.Target);
        Assert.Equal(RiskLevel.High, command.Risk);
    }

    [Fact]
    public void DelegationAlwaysRequiresConfirmation()
    {
        var command = new DeterministicCommandParser().Parse("ask ai hello there", _context);

        var decision = new DefaultActionPolicy().Evaluate(command, _context);

        Assert.True(decision.IsAllowed);
        Assert.True(decision.RequiresConfirmation);
        Assert.Equal(RiskLevel.High, decision.Risk);
        Assert.Contains("external AI", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
