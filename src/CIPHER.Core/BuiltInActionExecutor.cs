namespace CIPHER.Core;

/// <summary>Executes offline informational commands that do not touch the desktop.</summary>
public sealed class BuiltInActionExecutor : IActionExecutor
{
    public bool CanExecute(ParsedCommand command)
    {
        return command.Intent is IntentKind.Help or IntentKind.GetTime or IntentKind.GetDate or IntentKind.GetSystemInfo;
    }

    public Task<ActionResult> ExecuteAsync(
        ParsedCommand command,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = command.Intent switch
        {
            IntentKind.Help => ActionResult.Ok(
                "Supported commands: open <app>, close <app>, switch to <window>, type <text>, " +
                "press <keys>, create folder <path>, create file <path>, list files in <path>, " +
                "copy <source> to <destination>, move <source> to <destination>, " +
                "rename <source> to <destination>, delete <path>, time, date, and system info.",
                verified: true,
                data: new Dictionary<string, string>
                {
                    ["commands"] = "open, close, switch, type, press, create, list, copy, move, rename, delete, time, date, system info"
                }),
            IntentKind.GetTime => ActionResult.Ok(
                $"The current time is {context.Timestamp:hh:mm:ss tt}.",
                verified: true,
                data: new Dictionary<string, string> { ["time"] = context.Timestamp.ToString("O") }),
            IntentKind.GetDate => ActionResult.Ok(
                $"Today is {context.Timestamp:dddd, MMMM dd, yyyy}.",
                verified: true,
                data: new Dictionary<string, string> { ["date"] = context.Timestamp.ToString("yyyy-MM-dd") }),
            IntentKind.GetSystemInfo => ActionResult.Ok(
                $"{context.MachineName} is running {Environment.OSVersion}; " +
                $"{Environment.ProcessorCount} logical processor(s) are available.",
                verified: true,
                data: new Dictionary<string, string>
                {
                    ["machineName"] = context.MachineName,
                    ["userName"] = context.UserName,
                    ["operatingSystem"] = Environment.OSVersion.ToString(),
                    ["processorCount"] = Environment.ProcessorCount.ToString(),
                    ["dotnetVersion"] = Environment.Version.ToString(),
                    ["workingDirectory"] = context.WorkingDirectory
                }),
            _ => ActionResult.Fail($"The built-in executor does not handle '{command.Intent}'.", "UNSUPPORTED_INTENT")
        };

        return Task.FromResult(result);
    }
}
