using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CIPHER.Delegation;

/// <summary>
/// Minimal OpenAI-compatible chat-completions client. Works with cloud endpoints and
/// local servers (Ollama, LM Studio, …) alike. Only the prompt the user approved is sent;
/// the client has no access to files, history, or memory.
/// </summary>
public sealed class OpenAiChatClient
{
    /// <summary>Hard cap on accepted response size; larger replies are discarded.</summary>
    internal const int MaxResponseCharacters = 1_000_000;

    private readonly HttpClient _httpClient;

    public OpenAiChatClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    }

    public async Task<string> GetChatCompletionAsync(
        string baseUrl,
        string model,
        string? apiKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new AiDelegationException($"The AI endpoint '{baseUrl}' is not a valid http(s) URL.");
        }

        var endpoint = baseUrl.TrimEnd('/') + "/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        var payload = new
        {
            model,
            temperature = 0.2,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AiDelegationException($"Could not reach the AI endpoint at {baseUri.Host}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiDelegationException("The AI request timed out.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (body.Length > MaxResponseCharacters)
            {
                throw new AiDelegationException(
                    $"The AI endpoint returned an unexpectedly large response ({body.Length} characters); it was discarded.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = body.Length > 300 ? body[..300] : body;
                throw new AiDelegationException(
                    $"The AI endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}");
            }

            return ExtractContent(body);
        }
    }

    private static string ExtractContent(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new AiDelegationException("The AI endpoint returned an empty answer.");
            }

            return content.Trim();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new AiDelegationException("The AI endpoint returned a response CIPHER could not understand.", ex);
        }
    }
}
