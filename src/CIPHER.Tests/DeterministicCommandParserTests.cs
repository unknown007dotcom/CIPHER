using CIPHER.Core;
using Xunit;

namespace CIPHER.Tests;

public sealed class DeterministicCommandParserTests
{
    private readonly DeterministicCommandParser _parser = new();
    private readonly CommandContext _context = new(
        "C:\\Users\\Public",
        "TestUser",
        "TEST-PC",
        new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero));

    [Fact]
    public void Parse_RemovesWakePhrase_AndRecognizesOpenApplication()
    {
        var command = _parser.Parse("Hey CIPHER, open Notepad", _context);

        Assert.Equal(IntentKind.OpenApplication, command.Intent);
        Assert.Equal("Notepad", command.Target);
        Assert.True(command.Confidence >= 0.9);
    }

    [Fact]
    public void Parse_TypePreservesText()
    {
        var command = _parser.Parse("type Hello, CIPHER! This is a test.", _context);

        Assert.Equal(IntentKind.TypeText, command.Intent);
        Assert.Equal("Hello, CIPHER! This is a test.", command.Argument);
    }

    [Fact]
    public void Parse_PressKeys()
    {
        var command = _parser.Parse("press Ctrl+S", _context);

        Assert.Equal(IntentKind.PressKeys, command.Intent);
        Assert.Equal("Ctrl+S", command.Argument);
    }

    [Fact]
    public void Parse_ReadWindowText()
    {
        var command = _parser.Parse("read text in Notepad", _context);

        Assert.Equal(IntentKind.ReadWindowText, command.Intent);
        Assert.Equal("Notepad", command.Target);
    }

    [Fact]
    public void Parse_VerifyWindowText()
    {
        var command = _parser.Parse("verify Notepad contains CIPHER milestone test", _context);

        Assert.Equal(IntentKind.VerifyWindowText, command.Intent);
        Assert.Equal("Notepad", command.Target);
        Assert.Equal("CIPHER milestone test", command.Argument);
    }

    [Fact]
    public void Parse_VerifyWindowText_WithIfPhrasing()
    {
        var command = _parser.Parse("check if Notepad still contains hello world", _context);

        Assert.Equal(IntentKind.VerifyWindowText, command.Intent);
        Assert.Equal("Notepad", command.Target);
        Assert.Equal("hello world", command.Argument);
    }

    [Fact]
    public void Parse_ClickElementInWindow()
    {
        var command = _parser.Parse("click Save button in Notepad", _context);

        Assert.Equal(IntentKind.ClickUiElement, command.Intent);
        Assert.Equal("Save button", command.Target);
        Assert.Equal("Notepad", command.SecondaryTarget);
    }

    [Fact]
    public void Parse_ClickElementWithoutWindow()
    {
        var command = _parser.Parse("click the OK button", _context);

        Assert.Equal(IntentKind.ClickUiElement, command.Intent);
        Assert.Equal("OK button", command.Target);
        Assert.Null(command.SecondaryTarget);
    }

    [Fact]
    public void Parse_ClickElementNameContainingIn()
    {
        var command = _parser.Parse("click Sign in button in Calculator", _context);

        Assert.Equal(IntentKind.ClickUiElement, command.Intent);
        Assert.Equal("Sign in button", command.Target);
        Assert.Equal("Calculator", command.SecondaryTarget);
    }

    [Fact]
    public void Parse_TypeIntoFieldInWindow()
    {
        var command = _parser.Parse("type hello world into Search field in Notepad", _context);

        Assert.Equal(IntentKind.TypeIntoUiElement, command.Intent);
        Assert.Equal("Search field", command.Target);
        Assert.Equal("hello world", command.Argument);
        Assert.Equal("Notepad", command.SecondaryTarget);
    }

    [Fact]
    public void Parse_TypeIntoFieldWithoutWindow()
    {
        var command = _parser.Parse("enter CIPHER test into command input", _context);

        Assert.Equal(IntentKind.TypeIntoUiElement, command.Intent);
        Assert.Equal("command input", command.Target);
        Assert.Equal("CIPHER test", command.Argument);
        Assert.Null(command.SecondaryTarget);
    }

    [Theory]
    [InlineData("what time is it", IntentKind.GetTime)]
    [InlineData("what is the date", IntentKind.GetDate)]
    [InlineData("system info", IntentKind.GetSystemInfo)]
    [InlineData("help", IntentKind.Help)]
    public void Parse_InformationalCommands(string input, IntentKind expected)
    {
        var command = _parser.Parse(input, _context);
        Assert.Equal(expected, command.Intent);
    }

    [Fact]
    public void Parse_CreateFolderInLocationWithName()
    {
        var command = _parser.Parse("create folder in C:\\Temp called CIPHER-Test", _context);

        Assert.Equal(IntentKind.CreateFolder, command.Intent);
        Assert.Equal("C:\\Temp", command.Target);
        Assert.Equal("CIPHER-Test", command.Argument);
    }

    [Fact]
    public void Parse_MoveCommand()
    {
        var command = _parser.Parse("move C:\\Temp\\a.txt to C:\\Temp\\b.txt", _context);

        Assert.Equal(IntentKind.MovePath, command.Intent);
        Assert.Equal("C:\\Temp\\a.txt", command.Target);
        Assert.Equal("C:\\Temp\\b.txt", command.SecondaryTarget);
        Assert.Equal(RiskLevel.High, command.Risk);
    }

    [Fact]
    public void Parse_UnknownCommandReturnsSuggestions()
    {
        var command = _parser.Parse("make me a sandwich", _context);

        Assert.Equal(IntentKind.Unknown, command.Intent);
        Assert.NotEmpty(command.Suggestions);
    }
}
