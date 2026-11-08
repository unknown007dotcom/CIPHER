using CIPHER.Core;
using CIPHER.Memory;
using Xunit;

namespace CIPHER.Tests;

/// <summary>
/// Real-SQLite integration tests on temporary database files: CRUD, recovery journaling,
/// persistence across instances (the restart case), and corruption quarantine.
/// </summary>
public sealed class SqliteMemoryStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;

    public SqliteMemoryStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"cipher-memory-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "test-memory.db");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temporary test data; a locked file must not fail the suite.
        }
    }

    [Fact]
    public async Task AliasesRoundTripCaseInsensitively()
    {
        using var store = new SqliteMemoryStore(_databasePath);
        await store.SetAliasAsync("Morning", "open notepad");

        Assert.Equal("open notepad", await store.GetAliasExpansionAsync("MORNING"));
        var aliases = await store.ListAliasesAsync();
        var alias = Assert.Single(aliases);
        Assert.Equal("Morning", alias.Name);

        Assert.True(await store.DeleteAliasAsync("morning"));
        Assert.Null(await store.GetAliasExpansionAsync("morning"));
        Assert.False(await store.DeleteAliasAsync("morning"));
    }

    [Fact]
    public async Task AppPathsRoundTrip()
    {
        using var store = new SqliteMemoryStore(_databasePath);
        await store.SetAppPathAsync("my editor", "C:\\Tools\\editor.exe");

        Assert.Equal("C:\\Tools\\editor.exe", await store.GetAppPathAsync("MY EDITOR"));
        Assert.Single(await store.ListAppPathsAsync());
        Assert.True(await store.DeleteAppPathAsync("My Editor"));
        Assert.Null(await store.GetAppPathAsync("my editor"));
    }

    [Fact]
    public async Task HistoryReturnsMostRecentFirstWithLimit()
    {
        using var store = new SqliteMemoryStore(_databasePath);
        await store.AddHistoryAsync("first", "Succeeded", "ok");
        await store.AddHistoryAsync("second", "Failed", "bad");
        await store.AddHistoryAsync("third", "Succeeded", "ok");

        var recent = await store.GetRecentHistoryAsync(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("third", recent[0].InputText);
        Assert.Equal("second", recent[1].InputText);
    }

    [Fact]
    public async Task TasksPersistAcrossStoreInstances()
    {
        Guid taskId;
        using (var first = new SqliteMemoryStore(_databasePath))
        {
            var record = await first.BeginTaskAsync("open notepad");
            taskId = record.Id;
            await first.SetAliasAsync("morning", "open notepad");
            await first.AddHistoryAsync("help", "Succeeded", "ok");
        }

        // A brand-new instance on the same file simulates a process restart.
        using var second = new SqliteMemoryStore(_databasePath);
        var incomplete = await second.GetIncompleteTasksAsync();
        var recovered = Assert.Single(incomplete);
        Assert.Equal(taskId, recovered.Id);
        Assert.Equal("open notepad", recovered.InputText);
        Assert.Equal("open notepad", await second.GetAliasExpansionAsync("morning"));
        Assert.Single(await second.GetRecentHistoryAsync());

        await second.CompleteTaskAsync(taskId, TaskRecordStatus.Interrupted, "Recovered at startup.");
        Assert.Empty(await second.GetIncompleteTasksAsync());
    }

    [Fact]
    public async Task CorruptDatabaseIsQuarantinedAndRecreated()
    {
        await File.WriteAllTextAsync(_databasePath, "this is not a sqlite database at all");

        using var store = new SqliteMemoryStore(_databasePath);
        await store.InitializeAsync();

        Assert.True(store.RecoveredFromCorruption);
        Assert.NotEmpty(Directory.GetFiles(_directory, "*.corrupt-*"));
        await store.SetAliasAsync("morning", "open notepad");
        Assert.Equal("open notepad", await store.GetAliasExpansionAsync("morning"));
    }

    [Fact]
    public async Task CompletedTaskIsNotIncomplete()
    {
        using var store = new SqliteMemoryStore(_databasePath);
        var record = await store.BeginTaskAsync("help");
        await store.CompleteTaskAsync(record.Id, "Succeeded", "done");

        Assert.Empty(await store.GetIncompleteTasksAsync());
    }
}
