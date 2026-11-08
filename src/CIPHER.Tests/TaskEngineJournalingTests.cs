using CIPHER.Core;
using Xunit;

namespace CIPHER.Tests;

public sealed class TaskEngineJournalingTests
{
    private readonly CommandContext _context = new(
        "C:\\Users\\Public",
        "TestUser",
        "TEST-PC",
        new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task SuccessfulCommandIsJournaledAndAddedToHistory()
    {
        var store = new FakeMemoryStore();
        var engine = CreateEngine(store);

        var result = await engine.ExecuteTextAsync("help", _context);

        Assert.Equal(CipherTaskStatus.Succeeded, result.Status);
        var task = Assert.Single(store.Tasks);
        Assert.Equal("help", task.InputText);
        Assert.Equal(nameof(CipherTaskStatus.Succeeded), task.Status);
        var history = Assert.Single(store.History);
        Assert.Equal("help", history.InputText);
        Assert.Equal(nameof(CipherTaskStatus.Succeeded), history.Status);
        Assert.Empty(await store.GetIncompleteTasksAsync());
    }

    [Fact]
    public async Task BlockedCommandIsJournaledAsBlocked()
    {
        var store = new FakeMemoryStore();
        var engine = CreateEngine(store);

        var result = await engine.ExecuteTextAsync("do something undefined", _context);

        Assert.Equal(CipherTaskStatus.Blocked, result.Status);
        var task = Assert.Single(store.Tasks);
        Assert.Equal(nameof(CipherTaskStatus.Blocked), task.Status);
    }

    [Fact]
    public async Task MemoryFailureDoesNotBreakExecution()
    {
        var store = new FakeMemoryStore { ThrowOnBegin = true };
        var engine = CreateEngine(store);

        var result = await engine.ExecuteTextAsync("help", _context);

        Assert.Equal(CipherTaskStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task UnfinishedTaskIsVisibleAsIncompleteUntilCompleted()
    {
        var store = new FakeMemoryStore();
        var record = await store.BeginTaskAsync("open notepad");

        var incomplete = await store.GetIncompleteTasksAsync();
        var interrupted = Assert.Single(incomplete);
        Assert.Equal(record.Id, interrupted.Id);

        await store.CompleteTaskAsync(record.Id, TaskRecordStatus.Interrupted, "Session ended.");
        Assert.Empty(await store.GetIncompleteTasksAsync());
    }

    private TaskEngine CreateEngine(IMemoryStore store)
    {
        return new TaskEngine(
            new DeterministicCommandParser(),
            new DefaultActionPolicy(),
            new IActionExecutor[] { new BuiltInActionExecutor() },
            confirmationService: null,
            memoryStore: store);
    }
}
