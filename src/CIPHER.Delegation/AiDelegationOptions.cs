namespace CIPHER.Delegation;

/// <summary>
/// Runtime configuration for one delegated request, composed by the app from settings and
/// the DPAPI-protected key store. The API key travels only inside this object on its way
/// to the HTTP Authorization header — it is never logged, journaled, or persisted here.
/// </summary>
public sealed record AiDelegationOptions(
    bool Enabled,
    string BaseUrl,
    string Model,
    string? ApiKey,
    string OutputDirectory)
{
    /// <summary>Loopback endpoints (local model servers) legitimately need no API key.</summary>
    public bool IsLoopbackEndpoint => IsLoopback(BaseUrl);

    public static bool IsLoopback(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.IsLoopback ||
               string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>A delegation request failed in a way the user can act on.</summary>
public sealed class AiDelegationException : Exception
{
    public AiDelegationException(string message)
        : base(message)
    {
    }

    public AiDelegationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
