using CIPHER.Core;
using Xunit;

namespace CIPHER.Tests;

public sealed class DefaultActionPolicyTests
{
    private readonly DefaultActionPolicy _policy = new();
    private readonly CommandContext _context = new(
        "C:\\Users\\Public",
        "TestUser",
        "TEST-PC",
        DateTimeOffset.Now);

    [Fact]
    public void Delete_RequiresConfirmation()
    {
        var command = new ParsedCommand(
            "delete file",
            "delete file",
            IntentKind.DeletePath,
            Target: "C:\\Users\\Public\\delete-me.txt",
            Risk: RiskLevel.Critical);

        var decision = _policy.Evaluate(command, _context);

        Assert.True(decision.IsAllowed);
        Assert.True(decision.RequiresConfirmation);
        Assert.Equal(RiskLevel.Critical, decision.Risk);
    }

    [Fact]
    public void Delete_WindowsDirectory_IsBlocked()
    {
        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var command = new ParsedCommand(
            "delete windows",
            "delete windows",
            IntentKind.DeletePath,
            Target: windowsPath,
            Risk: RiskLevel.Critical);

        var decision = _policy.Evaluate(command, _context);

        Assert.False(decision.IsAllowed);
        Assert.Equal(RiskLevel.Critical, decision.Risk);
    }

    [Fact]
    public void ArbitraryShellCommand_IsBlocked()
    {
        var command = new ParsedCommand(
            "run command",
            "run command",
            IntentKind.RunCommand,
            Target: "format C:",
            Risk: RiskLevel.Critical);

        var decision = _policy.Evaluate(command, _context);

        Assert.False(decision.IsAllowed);
        Assert.False(decision.RequiresConfirmation);
    }

    [Fact]
    public void OpeningSensitiveApplication_RequiresConfirmation()
    {
        var command = new ParsedCommand(
            "open PowerShell",
            "open PowerShell",
            IntentKind.OpenApplication,
            Target: "PowerShell");

        var decision = _policy.Evaluate(command, _context);

        Assert.True(decision.IsAllowed);
        Assert.True(decision.RequiresConfirmation);
        Assert.Equal(RiskLevel.High, decision.Risk);
    }

    [Theory]
    [InlineData(IntentKind.ReadWindowText)]
    [InlineData(IntentKind.VerifyWindowText)]
    public void ReadingWindowText_IsLowRiskAndDoesNotRequireConfirmation(IntentKind intent)
    {
        var command = new ParsedCommand(
            "read window text",
            "read window text",
            intent,
            Target: "Notepad",
            Argument: "expected text");

        var decision = _policy.Evaluate(command, _context);

        Assert.True(decision.IsAllowed);
        Assert.False(decision.RequiresConfirmation);
        Assert.Equal(RiskLevel.Low, decision.Risk);
    }

    [Fact]
    public void OpeningOrdinaryApplication_DoesNotRequireConfirmation()
    {
        var command = new ParsedCommand(
            "open Notepad",
            "open Notepad",
            IntentKind.OpenApplication,
            Target: "Notepad");

        var decision = _policy.Evaluate(command, _context);

        Assert.True(decision.IsAllowed);
        Assert.False(decision.RequiresConfirmation);
    }
}
