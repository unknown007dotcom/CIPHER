using CIPHER.Core;

namespace CIPHER.Tests;

/// <summary>In-memory <see cref="IMemoryStore"/> for deterministic handler/engine tests.</summary>
internal sealed class FakeMemoryStore : IMemoryStore
{
    private long _nextHistoryId;

    public List<TaskRecord> Tasks { get; } = new();

    public List<CommandHistoryEntry> History { get; } = new();

    public Dictionary<string, CommandAlias> Aliases { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, LearnedAppPath> AppPaths { get; } = new(StringComparer.Ordinal);

    public bool ThrowOnBegin { get; set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<TaskRecord> BeginTaskAsync(string inputText, CancellationToken cancellationToken = default)
    {
        if (ThrowOnBegin)
        {
            throw new InvalidOperationException("Simulated memory failure.");
        }

        var now = DateTimeOffset.UtcNow;
        var record = new TaskRecord(Guid.NewGuid(), inputText, TaskRecordStatus.Running, string.Empty, now, now);
        Tasks.Add(record);
        return Task.FromResult(record);
    }

    public Task CompleteTaskAsync(Guid taskId, string status, string message, CancellationToken cancellationToken = default)
    {
        var index = Tasks.FindIndex(task => task.Id == taskId);
        if (index >= 0)
        {
            Tasks[index] = Tasks[index] with { Status = status, Message = message, UpdatedUtc = DateTimeOffset.UtcNow };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TaskRecord>> GetIncompleteTasksAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TaskRecord> results = Tasks.Where(task => task.Status == TaskRecordStatus.Running).ToList();
        return Task.FromResult(results);
    }

    public Task AddHistoryAsync(string inputText, string status, string message, CancellationToken cancellationToken = default)
    {
        History.Add(new CommandHistoryEntry(++_nextHistoryId, inputText, status, message, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CommandHistoryEntry>> GetRecentHistoryAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CommandHistoryEntry> results = History.OrderByDescending(entry => entry.Id).Take(limit).ToList();
        return Task.FromResult(results);
    }

    public Task SetAliasAsync(string name, string expansion, CancellationToken cancellationToken = default)
    {
        Aliases[MemoryCommandText.NormalizeName(name)] = new CommandAlias(name.Trim(), expansion.Trim(), DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task<string?> GetAliasExpansionAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Aliases.TryGetValue(MemoryCommandText.NormalizeName(name), out var alias) ? alias.Expansion : null);
    }

    public Task<IReadOnlyList<CommandAlias>> ListAliasesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CommandAlias> results = Aliases.Values.OrderBy(alias => alias.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return Task.FromResult(results);
    }

    public Task<bool> DeleteAliasAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Aliases.Remove(MemoryCommandText.NormalizeName(name)));
    }

    public Task SetAppPathAsync(string name, string path, CancellationToken cancellationToken = default)
    {
        AppPaths[MemoryCommandText.NormalizeName(name)] = new LearnedAppPath(name.Trim(), path.Trim(), DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task<string?> GetAppPathAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(AppPaths.TryGetValue(MemoryCommandText.NormalizeName(name), out var app) ? app.Path : null);
    }

    public Task<IReadOnlyList<LearnedAppPath>> ListAppPathsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LearnedAppPath> results = AppPaths.Values.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return Task.FromResult(results);
    }

    public Task<bool> DeleteAppPathAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(AppPaths.Remove(MemoryCommandText.NormalizeName(name)));
    }
}
