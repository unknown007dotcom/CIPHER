using System.Net;
using System.Text;
using CIPHER.Core;
using CIPHER.Delegation;
using Xunit;

namespace CIPHER.Tests;

public sealed class DelegationTests : IDisposable
{
    private readonly string _outputDirectory;
    private readonly CommandContext _context = new(
        "C:\\Users\\Public",
        "TestUser",
        "TEST-PC",
        new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero));

    public DelegationTests()
    {
        _outputDirectory = Path.Combine(Path.GetTempPath(), $"cipher-delegation-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Temporary test data.
        }
    }

    [Fact]
    public async Task ClientPostsToChatCompletionsAndParsesContent()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Hello from the stub.\"}}]}");
        var client = new OpenAiChatClient(new HttpClient(handler));

        var answer = await client.GetChatCompletionAsync(
            "https://api.example.test/v1", "test-model", "test-key", "system", "user prompt");

        Assert.Equal("Hello from the stub.", answer);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.example.test/v1/chat/completions", request.Uri);
        Assert.Equal("Bearer test-key", request.Authorization);
        Assert.Contains("user prompt", request.Body);
        Assert.Contains("test-model", request.Body);
    }

    [Fact]
    public async Task ClientSurfacesHttpErrors()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "boom");
        var client = new OpenAiChatClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<AiDelegationException>(() =>
            client.GetChatCompletionAsync("https://api.example.test/v1", "m", "k", "s", "u"));

        Assert.Contains("500", exception.Message);
    }

    [Fact]
    public async Task ExecutorRefusesWhenDelegationDisabled()
    {
        var handler = new StubHttpMessageHandler();
        var executor = CreateExecutor(handler, options => options with { Enabled = false });

        var result = await executor.ExecuteAsync(Parse("ask ai hello"), _context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("AI_DELEGATION_DISABLED", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecutorRefusesRemoteEndpointWithoutKey()
    {
        var handler = new StubHttpMessageHandler();
        var executor = CreateExecutor(handler, options => options);

        var result = await executor.ExecuteAsync(Parse("ask ai hello"), _context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("AI_KEY_MISSING", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecutorAnswersAndSavesVerifiedArtifact()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"The capital is Paris.\"}}]}");
        var executor = CreateExecutor(handler, options => options with
        {
            BaseUrl = "http://127.0.0.1:11434/v1",
            ApiKey = null
        });

        var result = await executor.ExecuteAsync(Parse("ask ai what is the capital of France"), _context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Verified);
        Assert.Contains("The capital is Paris.", result.Message);
        var artifactPath = result.Data?["artifactPath"];
        Assert.False(string.IsNullOrWhiteSpace(artifactPath));
        Assert.True(File.Exists(artifactPath));
        Assert.Contains("The capital is Paris.", await File.ReadAllTextAsync(artifactPath!));
        Assert.False(result.Data!.ContainsKey("suggestedCommand"));
    }

    [Fact]
    public async Task ExecutorSurfacesSuggestedCommandWithoutExecutingIt()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            "{\"choices\":[{\"message\":{\"content\":\"Try this:\\nCOMMAND: what time is it\"}}]}");
        var executor = CreateExecutor(handler, options => options with
        {
            BaseUrl = "http://localhost:11434/v1",
            ApiKey = null
        });

        var result = await executor.ExecuteAsync(Parse("ask ai suggest a command"), _context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("what time is it", result.Data?["suggestedCommand"]);
        Assert.Contains("NOT executed", result.Message);
    }

    [Fact]
    public void ApiKeyStoreRoundTripsThroughDpapi()
    {
        var keyPath = Path.Combine(_outputDirectory, "secrets", "ai-api-key.bin");
        var store = new DpapiApiKeyStore(keyPath);

        Assert.False(store.HasKey);
        Assert.Null(store.Load());

        store.Save("sk-test-1234567890");
        Assert.True(store.HasKey);
        Assert.Equal("sk-test-1234567890", store.Load());

        // The plaintext key must never appear in the stored file.
        var storedBytes = File.ReadAllBytes(keyPath);
        Assert.DoesNotContain("sk-test-1234567890", Encoding.UTF8.GetString(storedBytes));

        store.Delete();
        Assert.False(store.HasKey);
        Assert.Null(store.Load());
    }

    private DelegationActionExecutor CreateExecutor(
        StubHttpMessageHandler handler,
        Func<AiDelegationOptions, AiDelegationOptions> configure)
    {
        var defaults = new AiDelegationOptions(
            Enabled: true,
            BaseUrl: "https://api.example.test/v1",
            Model: "test-model",
            ApiKey: null,
            OutputDirectory: _outputDirectory);
        return new DelegationActionExecutor(
            () => configure(defaults),
            new OpenAiChatClient(new HttpClient(handler)));
    }

    private static ParsedCommand Parse(string text)
    {
        return new DeterministicCommandParser().Parse(text);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public List<(string Uri, string? Authorization, string Body)> Requests { get; } = new();

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri?.ToString() ?? string.Empty, request.Headers.Authorization?.ToString(), body));

            var (status, responseBody) = _responses.Count > 0
                ? _responses.Dequeue()
                : (HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"stub\"}}]}");
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
