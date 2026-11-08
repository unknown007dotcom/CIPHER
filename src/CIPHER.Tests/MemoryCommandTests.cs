using CIPHER.Core;
using Xunit;

namespace CIPHER.Tests;

public sealed class MemoryCommandTextTests
{
    [Theory]
    [InlineData("alias morning = open notepad", "morning", "open notepad")]
    [InlineData("create alias focus time for open calculator", "focus time", "open calculator")]
    [InlineData("remember alias notes for open notepad", "notes", "open notepad")]
    public void ParsesDefineAliasForms(string input, string expectedName, string expectedExpansion)
    {
        var parsed = MemoryCommandText.TryParse(input, out var command);

        Assert.True(parsed);
        Assert.Equal(MemoryCommandKind.DefineAlias, command.Kind);
        Assert.Equal(expectedName, command.Name);
        Assert.Equal(expectedExpansion, command.Value);
    }

    [Theory]
    [InlineData("remember app my editor at C:\\Tools\\editor.exe", "my editor", "C:\\Tools\\editor.exe")]
    [InlineData("teach application notes at \"C:\\Program Files\\Notes\\notes.exe\"", "notes", "C:\\Program Files\\Notes\\notes.exe")]
    public void ParsesDefineAppPathForms(string input, string expectedName, string expectedPath)
    {
        var parsed = MemoryCommandText.TryParse(input, out var command);

        Assert.True(parsed);
        Assert.Equal(MemoryCommandKind.DefineAppPath, command.Kind);
        Assert.Equal(expectedName, command.Name);
        Assert.Equal(expectedPath, command.Value);
    }

    [Theory]
    [InlineData("delete alias morning", MemoryCommandKind.DeleteAlias, "morning")]
    [InlineData("forget app my editor", MemoryCommandKind.DeleteAppPath, "my editor")]
    [InlineData("list aliases", MemoryCommandKind.ListAliases, "")]
    [InlineData("show apps", MemoryCommandKind.ListAppPaths, "")]
    [InlineData("list learned apps", MemoryCommandKind.ListAppPaths, "")]
    public void ParsesOtherMemoryCommands(string input, MemoryCommandKind expectedKind, string expectedName)
    {
        var parsed = MemoryCommandText.TryParse(input, out var command);

        Assert.True(parsed);
        Assert.Equal(expectedKind, command.Kind);
        Assert.Equal(expectedName, command.Name);
    }

    [Theory]
    [InlineData("open notepad")]
    [InlineData("what time is it")]
    [InlineData("")]
    public void OrdinaryCommandsAreNotMemoryCommands(string input)
    {
        Assert.False(MemoryCommandText.TryParse(input, out _));
    }
}

public sealed class MemoryCommandHandlerTests
{
    [Fact]
    public async Task DefineThenExpandThenDeleteAlias()
    {
        var store = new FakeMemoryStore();
        var handler = new MemoryCommandHandler(store);

        var define = await handler.TryHandleAsync("alias morning = open notepad");
        Assert.Equal(MemoryHandlingKind.Responded, define.Kind);
        Assert.Contains("Alias saved", define.Response);

        var expand = await handler.TryHandleAsync("  MORNING ");
        Assert.Equal(MemoryHandlingKind.ExpandAlias, expand.Kind);
        Assert.Equal("open notepad", expand.Expansion);

        var delete = await handler.TryHandleAsync("forget alias morning");
        Assert.Equal(MemoryHandlingKind.Responded, delete.Kind);

        var afterDelete = await handler.TryHandleAsync("morning");
        Assert.Equal(MemoryHandlingKind.NotHandled, afterDelete.Kind);
    }

    [Fact]
    public async Task LearnedAppPathCommandsRoundTrip()
    {
        var store = new FakeMemoryStore();
        var handler = new MemoryCommandHandler(store);

        var define = await handler.TryHandleAsync("remember app my editor at C:\\Tools\\editor.exe");
        Assert.Equal(MemoryHandlingKind.Responded, define.Kind);
        Assert.Equal("C:\\Tools\\editor.exe", await store.GetAppPathAsync("My Editor"));

        var list = await handler.TryHandleAsync("list apps");
        Assert.Contains("my editor", list.Response, StringComparison.OrdinalIgnoreCase);

        var forget = await handler.TryHandleAsync("forget app my editor");
        Assert.Equal(MemoryHandlingKind.Responded, forget.Kind);
        Assert.Null(await store.GetAppPathAsync("my editor"));
    }

    [Fact]
    public async Task OrdinaryCommandIsNotHandled()
    {
        var handler = new MemoryCommandHandler(new FakeMemoryStore());

        var result = await handler.TryHandleAsync("open notepad");

        Assert.Equal(MemoryHandlingKind.NotHandled, result.Kind);
    }

    [Fact]
    public async Task SelfReferentialAliasDoesNotExpand()
    {
        var store = new FakeMemoryStore();
        var handler = new MemoryCommandHandler(store);
        await handler.TryHandleAsync("alias loop = loop");

        var result = await handler.TryHandleAsync("loop");

        Assert.Equal(MemoryHandlingKind.NotHandled, result.Kind);
    }
}
