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
    public void ClickingDangerousElement_RequiresConfirmation()
    {
        var command = new ParsedCommand(
            "click Delete button",
            "click Delete button",
            IntentKind.ClickUiElement,
            Target: "Delete button",
            SecondaryTarget: "Notepad");

        var decision = _policy.Evaluate(command, _context);

        Assert.True(decision.IsAllowed);
        Assert.True(decision.RequiresConfirmation);
        Assert.Equal(RiskLevel.High, decision.Risk);
    }

    [Fact]
    public void ClickingOrdinaryElement_DoesNotRequireConfirmation()
    {
        var command = new ParsedCommand(
            "click Save button",
            "click Save button",
            IntentKind.ClickUiElement,
            Target: "Save button",
            SecondaryTarget: "Notepad");

        var decision = _policy.Evaluate(command, _context);

        Assert.True(decision.IsAllowed);
        Assert.False(decision.RequiresConfirmation);
        Assert.Equal(RiskLevel.Medium, decision.Risk);
    }

    [Fact]
    public void TypingIntoCredentialField_IsBlocked()
    {
        var command = new ParsedCommand(
            "type into password field",
            "type into password field",
            IntentKind.TypeIntoUiElement,
            Target: "Password field",
            Argument: "hunter2");

        var decision = _policy.Evaluate(command, _context);

        Assert.False(decision.IsAllowed);
        Assert.Equal(RiskLevel.Critical, decision.Risk);
    }

    [Fact]
    public void OpeningFtpWebsite_IsBlocked()
    {
        var command = new ParsedCommand(
            "open website ftp://example.com",
            "open website ftp://example.com",
            IntentKind.OpenWebsite,
            Target: "ftp://example.com");

        var decision = _policy.Evaluate(command, _context);

        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void OpeningWebsiteWithEmbeddedCredentials_IsBlocked()
    {
        var command = new ParsedCommand(
            "open website",
            "open website",
            IntentKind.OpenWebsite,
            Target: "https://user:secret@example.com");

        var decision = _policy.Evaluate(command, _context);

        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void FillingBrowserPasswordField_IsBlocked()
    {
        var command = new ParsedCommand(
            "browser fill Password",
            "browser fill Password",
            IntentKind.BrowserFillField,
            Target: "Password",
            Argument: "hunter2");

        var decision = _policy.Evaluate(command, _context);

        Assert.False(decision.IsAllowed);
        Assert.Equal(RiskLevel.Critical, decision.Risk);
    }

    [Fact]
    public void ClickingDangerousBrowserElement_RequiresConfirmation()
    {
        var command = new ParsedCommand(
            "browser click Pay now",
            "browser click Pay now",
            IntentKind.BrowserClickElement,
            Target: "Pay now");

        var decision = _policy.Evaluate(command, _context);

        Assert.True(decision.IsAllowed);
        Assert.True(decision.RequiresConfirmation);
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

    [Fact]
    public void OpeningUnknownApplication_RequiresConfirmation()
    {
        var command = new ParsedCommand(
            "open some unlisted tool",
            "open some unlisted tool",
            IntentKind.OpenApplication,
            Target: "Some Unlisted Tool");

        var decision = _policy.Evaluate(command, _context);

        Assert.True(decision.IsAllowed);
        Assert.True(decision.RequiresConfirmation);
    }
}
