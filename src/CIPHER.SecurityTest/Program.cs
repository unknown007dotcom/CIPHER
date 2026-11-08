using CIPHER.Core;

namespace CIPHER.SecurityTest;

/// <summary>
/// Stage 9 audit-chain harness, run as two separate processes against one log file:
///
///   --write  --log &lt;path&gt;  appends three audit events (one deliberately carrying
///                            credential-shaped text) and exits.
///   --verify --log &lt;path&gt;  a fresh process must verify the hash chain, confirm the
///                            secrets never reached the file (redaction), and detect a
///                            tampered copy at the exact modified line.
///
/// Exit codes: 0 = ok, 1 = assertion failure, 2 = bad arguments.
/// </summary>
internal static class Program
{
    private const int ExpectedEntries = 3;

    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : string.Empty;
        var logPath = GetOption(args, "--log");
        if (string.IsNullOrWhiteSpace(logPath) || (mode is not ("--write" or "--verify")))
        {
            Console.WriteLine("::error title=Stage 9 audit harness::Usage: CIPHER.SecurityTest --write|--verify --log <path>");
            return 2;
        }

        try
        {
            return mode == "--write" ? WritePhase(logPath) : VerifyPhase(logPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"::error title=Stage 9 audit harness::STAGE9-FAIL {mode} phase threw: {ex.Message}");
            return 1;
        }
    }

    private static int WritePhase(string logPath)
    {
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        var log = new AuditLog(logPath);
        log.Append("app.started", "CIPHER security harness session");
        log.Append("task.blocked", "RunCommand attempt blocked by policy");
        log.Append("test.secret", "operator pasted api_key=sk-stage9-probe-123456 and password: probe-pass-789");

        Console.WriteLine($"STAGE9-WRITE-OK {ExpectedEntries} audit entries appended to {logPath}.");
        return 0;
    }

    private static int VerifyPhase(string logPath)
    {
        var verification = AuditLog.Verify(logPath);
        Require(verification.IsValid, $"Audit chain did not verify: {verification.Message}");
        Require(verification.EntryCount == ExpectedEntries,
            $"Expected {ExpectedEntries} entries, found {verification.EntryCount}.");

        var fileText = File.ReadAllText(logPath);
        Require(!fileText.Contains("sk-stage9-probe-123456", StringComparison.Ordinal),
            "An API-key-shaped secret reached the audit log file.");
        Require(!fileText.Contains("probe-pass-789", StringComparison.Ordinal),
            "A password-shaped secret reached the audit log file.");

        // Tamper evidence: modify one entry in a copy; verification must pinpoint it.
        var lines = File.ReadAllLines(logPath);
        Require(lines.Length >= 2, "Audit log has fewer lines than expected.");
        lines[1] = lines[1].Replace("RunCommand", "ForgedCmd");
        var tamperedPath = logPath + ".tampered";
        File.WriteAllLines(tamperedPath, lines);
        var tampered = AuditLog.Verify(tamperedPath);
        Require(!tampered.IsValid, "Tampered audit copy was not detected.");
        Require(tampered.FirstInvalidLine == 2,
            $"Tamper was not pinpointed at line 2 (reported: {tampered.FirstInvalidLine}).");

        var missing = AuditLog.Verify(logPath + ".missing");
        Require(missing.IsValid && missing.EntryCount == 0, "Missing-file verification should be valid and empty.");

        Console.WriteLine("::notice title=Stage 9 audit harness::STAGE9-PASS hash chain verified across processes; secrets redacted before writing; tampering detected at the modified line.");
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
