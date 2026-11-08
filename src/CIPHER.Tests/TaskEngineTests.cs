using CIPHER.Core;
using Xunit;

namespace CIPHER.Tests;

public sealed class TaskEngineTests
{
    private readonly CommandContext _context = new(
        "C:\\Users\\Public",
        "TestUser",
        "TEST-PC",
        new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task Execute_Help_ReturnsVerifiedSuccess()
    {
        var engine = CreateEngine(new BuiltInActionExecutor());

        var result = await engine.ExecuteTextAsync("help", _context);

        Assert.Equal(TaskStatus.Succeeded, result.Status);
        Assert.NotNull(result.ActionResult);
        Assert.True(result.ActionResult!.Verified);
    }

    [Fact]
    public async Task Execute_UnknownCommand_IsBlockedWithoutExecutorCall()
    {
        var executor = new RecordingExecutor();
        var engine = CreateEngine(executor);

        var result = await engine.ExecuteTextAsync("do something undefined", _context);

        Assert.Equal(TaskStatus.Blocked, result.Status);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task Execute_Delete_WhenConfirmationDenied_DoesNotRunExecutor()
    {
        var executor = new RecordingExecutor();
        var engine = CreateEngine(executor, new StaticConfirmationService(false));

        var result = await engine.ExecuteTextAsync("delete C:\\Users\\Public\\delete-me.txt", _context);

        Assert.Equal(TaskStatus.Cancelled, result.Status);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task Execute_Delete_WhenConfirmed_RunsExecutor()
    {
        var executor = new RecordingExecutor();
        var engine = CreateEngine(executor, new StaticConfirmationService(true));

        var result = await engine.ExecuteTextAsync("delete C:\\Users\\Public\\delete-me.txt", _context);

        Assert.Equal(TaskStatus.Succeeded, result.Status);
        Assert.Equal(1, executor.CallCount);
    }

    private TaskEngine CreateEngine(IActionExecutor executor, IConfirmationService? confirmationService = null)
    {
        return new TaskEngine(
            new DeterministicCommandParser(),
            new DefaultActionPolicy(),
            new[] { executor },
            confirmationService);
    }

    private sealed class RecordingExecutor : IActionExecutor
    {
        public int CallCount { get; private set; }

        public bool CanExecute(ParsedCommand command)
        {
            return command.Intent != IntentKind.Unknown;
        }

        public Task<ActionResult> ExecuteAsync(
            ParsedCommand command,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(ActionResult.Ok("Recorded.", verified: true));
        }
    }

    private sealed class StaticConfirmationService : IConfirmationService
    {
        private readonly bool _approved;

        public StaticConfirmationService(bool approved)
        {
            _approved = approved;
        }

        public Task<bool> ConfirmAsync(
            ParsedCommand command,
            PolicyDecision decision,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_approved);
        }
    }
}
