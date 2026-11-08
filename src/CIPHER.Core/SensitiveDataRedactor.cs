using System.Text.RegularExpressions;

namespace CIPHER.Core;

/// <summary>
/// Conservative secret scrubbing for anything CIPHER persists or logs (task journal,
/// history, audit log). It targets credential-shaped patterns — labeled secrets
/// (api_key=…, password: …), OpenAI-style keys, bearer tokens, and URL userinfo — and
/// replaces only the secret value. Ordinary command text passes through unchanged.
/// </summary>
public static class SensitiveDataRedactor
{
    private const string Mask = "***";

    private static readonly Regex LabeledSecretPattern = new(
        @"(?<label>\b(?:api[-_ ]?key|apikey|secret|password|passwd|pwd|access[-_ ]?token|auth[-_ ]?token|private[-_ ]?key)\b\s*[:=]\s*)(?<value>[^\s,;""']+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex OpenAiKeyPattern = new(
        @"\bsk-[A-Za-z0-9_\-]{6,}\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex BearerPattern = new(
        @"(?<label>\bBearer\s+)[A-Za-z0-9_\-\.=]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex UrlUserInfoPattern = new(
        @"(?<scheme>[a-z][a-z0-9+.\-]*://)[^\s/@:]+:[^\s/@]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = LabeledSecretPattern.Replace(text, "${label}" + Mask);
        result = OpenAiKeyPattern.Replace(result, "sk-" + Mask);
        result = BearerPattern.Replace(result, "${label}" + Mask);
        result = UrlUserInfoPattern.Replace(result, "${scheme}" + Mask + "@");
        return result;
    }
}
