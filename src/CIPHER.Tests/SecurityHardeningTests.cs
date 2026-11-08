using CIPHER.Core;
using Xunit;

namespace CIPHER.Tests;

public sealed class SensitiveDataRedactorTests
{
    [Theory]
    [InlineData("the api_key=sk-abcdefgh12345678 ok", "api_key=***")]
    [InlineData("password: hunter2", "password: ***")]
    [InlineData("Authorization: Bearer abc.def-12345", "Bearer ***")]
    [InlineData("fetch https://user:pass@example.com/x", "https://***@example.com/x")]
    [InlineData("key sk-abcdefgh12345678 here", "sk-***")]
    public void RedactsCredentialShapedSecrets(string input, string expectedFragment)
    {
        var redacted = SensitiveDataRedactor.Redact(input);

        Assert.Contains(expectedFragment, redacted);
        Assert.DoesNotContain("hunter2", redacted);
        Assert.DoesNotContain("sk-abcdefgh12345678", redacted);
        Assert.DoesNotContain("abc.def-12345", redacted);
    }

    [Fact]
    public void OrdinaryTextPassesThrough()
    {
        Assert.Equal("open notepad at 5 please", SensitiveDataRedactor.Redact("open notepad at 5 please"));
        Assert.Equal(string.Empty, SensitiveDataRedactor.Redact(null));
        Assert.Equal(string.Empty, SensitiveDataRedactor.Redact(string.Empty));
    }
}

public sealed class AuditLogTests : IDisposable
{
    private readonly string _directory;
    private readonly string _logPath;

    public AuditLogTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"cipher-audit-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _logPath = Path.Combine(_directory, "cipher-audit.log");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temporary test data.
        }
    }

    [Fact]
    public void AppendedEntriesVerifyAsIntactChain()
    {
        var log = new AuditLog(_logPath);
        log.Append("task.blocked", "RunCommand blocked by policy");
        log.Append("task.cancelled", "DeletePath denied by user");
        log.Append("delegation.completed", "endpoint=api.example.com artifact=C:\\results\\a.txt");

        var verification = AuditLog.Verify(_logPath);

        Assert.True(verification.IsValid, verification.Message);
        Assert.Equal(3, verification.EntryCount);
    }

    [Fact]
    public void NewInstanceContinuesExistingChain()
    {
        new AuditLog(_logPath).Append("one", "first");
        new AuditLog(_logPath).Append("two", "second");

        var verification = AuditLog.Verify(_logPath);
        Assert.True(verification.IsValid, verification.Message);
        Assert.Equal(2, verification.EntryCount);
    }

    [Fact]
    public void ModifiedEntryIsDetected()
    {
        var log = new AuditLog(_logPath);
        log.Append("one", "first");
        log.Append("two", "second entry");
        log.Append("three", "third");

        var lines = File.ReadAllLines(_logPath);
        lines[1] = lines[1].Replace("second entry", "forged entry");
        File.WriteAllLines(_logPath, lines);

        var verification = AuditLog.Verify(_logPath);
        Assert.False(verification.IsValid);
        Assert.Equal(2, verification.FirstInvalidLine);
    }

    [Fact]
    public void RemovedEntryBreaksTheChain()
    {
        var log = new AuditLog(_logPath);
        log.Append("one", "first");
        log.Append("two", "second");
        log.Append("three", "third");

        var lines = File.ReadAllLines(_logPath).ToList();
        lines.RemoveAt(1);
        File.WriteAllLines(_logPath, lines);

        var verification = AuditLog.Verify(_logPath);
        Assert.False(verification.IsValid);
    }

    [Fact]
    public void SecretsAreRedactedBeforeWriting()
    {
        var log = new AuditLog(_logPath);
        log.Append("test", "the api_key=sk-probe-123456 and password: hunter2 leaked");

        var fileText = File.ReadAllText(_logPath);
        Assert.DoesNotContain("sk-probe-123456", fileText);
        Assert.DoesNotContain("hunter2", fileText);
        Assert.True(AuditLog.Verify(_logPath).IsValid);
    }

    [Fact]
    public void RotationKeepsCurrentChainVerifiable()
    {
        var log = new AuditLog(_logPath, rotationThresholdBytes: 1100);
        for (var i = 0; i < 40; i++)
        {
            log.Append("bulk", $"entry number {i} with some padding text to grow the file");
        }

        Assert.True(File.Exists(_logPath + ".1"), "Expected a rotated archive file.");
        var verification = AuditLog.Verify(_logPath);
        Assert.True(verification.IsValid, verification.Message);
        Assert.True(verification.EntryCount > 0);
    }

    [Fact]
    public void MissingFileVerifiesAsEmpty()
    {
        var verification = AuditLog.Verify(Path.Combine(_directory, "missing.log"));

        Assert.True(verification.IsValid);
        Assert.Equal(0, verification.EntryCount);
    }
}

public sealed class Stage9CommandTests
{
    private readonly CommandContext _context = new(
        "C:\\Users\\Public",
        "TestUser",
        "TEST-PC",
        new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero));

    [Fact]
    public void ParsesAuditAndQuarantineCommands()
    {
        var parser = new DeterministicCommandParser();

        Assert.Equal(IntentKind.VerifyAuditLog, parser.Parse("verify audit log", _context).Intent);
        Assert.Equal(IntentKind.ListQuarantine, parser.Parse("list quarantine", _context).Intent);
        var restore = parser.Parse("restore report.txt", _context);
        Assert.Equal(IntentKind.RestoreQuarantineItem, restore.Intent);
        Assert.Equal("report.txt", restore.Target);
    }

    [Fact]
    public void QuarantineListAndAuditVerifyAreLowRiskReadOnly()
    {
        var parser = new DeterministicCommandParser();
        var policy = new DefaultActionPolicy();

        var list = policy.Evaluate(parser.Parse("list quarantine", _context), _context);
        var audit = policy.Evaluate(parser.Parse("verify audit log", _context), _context);

        Assert.True(list.IsAllowed && !list.RequiresConfirmation && list.Risk == RiskLevel.Low);
        Assert.True(audit.IsAllowed && !audit.RequiresConfirmation && audit.Risk == RiskLevel.Low);
    }

    [Fact]
    public void RestoreRequiresConfirmation()
    {
        var parser = new DeterministicCommandParser();

        var decision = new DefaultActionPolicy().Evaluate(parser.Parse("restore report.txt", _context), _context);

        Assert.True(decision.IsAllowed);
        Assert.True(decision.RequiresConfirmation);
    }
}

/// <summary>
/// Stage 9 exit-gate evidence at the engine layer: blocked, cancelled, and failed
/// actions must not reach an executor (so they cannot mutate anything), and whatever
/// gets journaled is redacted first.
/// </summary>
public sealed class SecurityGateTests
{
    private readonly CommandContext _context = new(
        "C:\\Users\\Public",
        "TestUser",
        "TEST-PC",
        new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task BlockedCommandNeverReachesExecutor()
    {
        var executor = new RecordingExecutor();
        var store = new FakeMemoryStore();
        var engine = CreateEngine(executor, confirmation: null, store);

        var result = await engine.ExecuteTextAsync("do something undefined", _context);

        Assert.Equal(CipherTaskStatus.Blocked, result.Status);
        Assert.Equal(0, executor.CallCount);
        Assert.Equal(nameof(CipherTaskStatus.Blocked), Assert.Single(store.Tasks).Status);
    }

    [Fact]
    public async Task DeniedConfirmationNeverReachesExecutor()
    {
        var executor = new RecordingExecutor();
        var store = new FakeMemoryStore();
        var engine = CreateEngine(executor, new StaticConfirmationService(false), store);

        var result = await engine.ExecuteTextAsync("delete C:\\Users\\Public\\delete-me.txt", _context);

        Assert.Equal(CipherTaskStatus.Cancelled, result.Status);
        Assert.Equal(0, executor.CallCount);
        Assert.Equal(nameof(CipherTaskStatus.Cancelled), Assert.Single(store.Tasks).Status);
    }

    [Fact]
    public async Task FailedExecutorIsJournaledAsFailedNotVerified()
    {
        var store = new FakeMemoryStore();
        var engine = CreateEngine(new ThrowingExecutor(), confirmation: null, store);

        var result = await engine.ExecuteTextAsync("what time is it", _context);

        Assert.Equal(CipherTaskStatus.Failed, result.Status);
        Assert.Equal(nameof(CipherTaskStatus.Failed), Assert.Single(store.Tasks).Status);
        Assert.Equal(nameof(CipherTaskStatus.Failed), Assert.Single(store.History).Status);
    }

    [Fact]
    public async Task JournaledTextIsRedacted()
    {
        var store = new FakeMemoryStore();
        var engine = CreateEngine(new RecordingExecutor(), confirmation: null, store);

        await engine.ExecuteTextAsync("leak check api_key=sk-abcdefgh12345678 please", _context);

        var history = Assert.Single(store.History);
        Assert.DoesNotContain("sk-abcdefgh12345678", history.InputText);
        Assert.Contains("***", history.InputText);
        var task = Assert.Single(store.Tasks);
        Assert.DoesNotContain("sk-abcdefgh12345678", task.InputText);
    }

    [Fact]
    public async Task AliasToDeleteStillRequiresConfirmation()
    {
        var store = new FakeMemoryStore();
        var handler = new MemoryCommandHandler(store);
        await handler.TryHandleAsync("alias wipe = delete C:\\Users\\Public\\delete-me.txt");
        var expansion = await handler.TryHandleAsync("wipe");
        Assert.Equal(MemoryHandlingKind.ExpandAlias, expansion.Kind);

        var executor = new RecordingExecutor();
        var engine = CreateEngine(executor, new StaticConfirmationService(false), store);
        var result = await engine.ExecuteTextAsync(expansion.Expansion!, _context);

        Assert.Equal(CipherTaskStatus.Cancelled, result.Status);
        Assert.Equal(0, executor.CallCount);
    }

    private TaskEngine CreateEngine(IActionExecutor executor, IConfirmationService? confirmation, IMemoryStore store)
    {
        return new TaskEngine(
            new DeterministicCommandParser(),
            new DefaultActionPolicy(),
            new[] { executor },
            confirmation,
            store);
    }

    private sealed class RecordingExecutor : IActionExecutor
    {
        public int CallCount { get; private set; }

        public bool CanExecute(ParsedCommand command) => command.Intent != IntentKind.Unknown;

        public Task<ActionResult> ExecuteAsync(ParsedCommand command, CommandContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(ActionResult.Ok("Recorded.", verified: true));
        }
    }

    private sealed class ThrowingExecutor : IActionExecutor
    {
        public bool CanExecute(ParsedCommand command) => command.Intent != IntentKind.Unknown;

        public Task<ActionResult> ExecuteAsync(ParsedCommand command, CommandContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated executor failure.");
        }
    }

    private sealed class StaticConfirmationService : IConfirmationService
    {
        private readonly bool _approved;

        public StaticConfirmationService(bool approved) => _approved = approved;

        public Task<bool> ConfirmAsync(ParsedCommand command, PolicyDecision decision, CancellationToken cancellationToken)
        {
            return Task.FromResult(_approved);
        }
    }
}
