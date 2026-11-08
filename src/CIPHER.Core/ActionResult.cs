namespace CIPHER.Core;

/// <summary>Result returned by an action executor.</summary>
/// <param name="Success">Whether the requested operation completed without an executor error.</param>
/// <param name="Message">Human-readable result suitable for display and optional speech output.</param>
/// <param name="Verified">
/// Whether the executor independently checked the resulting system state. A successful but
/// unverified action must never be presented as fully verified.
/// </param>
/// <param name="ErrorCode">Stable machine-readable error code for failures.</param>
/// <param name="Data">Optional structured values returned by the action.</param>
/// <param name="Duration">Measured execution duration.</param>
public sealed record ActionResult(
    bool Success,
    string Message,
    bool Verified = false,
    string? ErrorCode = null,
    IReadOnlyDictionary<string, string>? Data = null,
    TimeSpan Duration = default)
{
    public static ActionResult Ok(
        string message,
        bool verified = false,
        IReadOnlyDictionary<string, string>? data = null)
    {
        return new ActionResult(true, message, verified, null, data);
    }

    public static ActionResult Fail(string message, string errorCode = "ACTION_FAILED")
    {
        return new ActionResult(false, message, false, errorCode);
    }
}
