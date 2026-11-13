using CIPHER.Brain;
using CIPHER.Core;
using Xunit;

namespace CIPHER.Tests;

/// <summary>
/// The brain is only ever a classifier: these tests pin the label→intent mapping and
/// the deterministic argument extraction with a fake brain, so no model is needed.
/// </summary>
public sealed class BrainIntentMapperTests
{
    private sealed class FakeBrain : ILocalBrain
    {
        private readonly Func<string, string> _responder;

        public FakeBrain(Func<string, string> responder, bool isAvailable = true)
        {
            _responder = responder;
            IsAvailable = isAvailable;
        }

        public bool IsAvailable { get; }

        public string ModelDescription => "Fake brain (tests)";

        public string Generate(string prompt, int maxNewTokens, CancellationToken cancellationToken = default)
        {
            return _responder(prompt);
        }
    }

    [Fact]
    public void SearchLabel_MapsToWebSearch_WithExtractedQuery()
    {
        var mapper = new BrainIntentMapper(new FakeBrain(_ => "search web"));

        var command = mapper.TryMap(
            "find me information about the moon landing",
            "find me information about the moon landing");

        Assert.NotNull(command);
        Assert.Equal(IntentKind.WebSearch, command!.Intent);
        Assert.Equal("the moon landing", command.Target);
    }

    [Fact]
    public void OpenLabel_MapsToOpenApplication_WithExtractedTarget()
    {
        var mapper = new BrainIntentMapper(new FakeBrain(_ => "open app"));

        var command = mapper.TryMap(
            "can you open the calculator for me",
            "can you open the calculator for me");

        Assert.NotNull(command);
        Assert.Equal(IntentKind.OpenApplication, command!.Intent);
        Assert.Equal("calculator", command.Target);
    }

    [Fact]
    public void TypeLabel_MapsToTypeText_WithExtractedText()
    {
        var mapper = new BrainIntentMapper(new FakeBrain(_ => "type text"));

        var command = mapper.TryMap(
            "put the words good morning into the document",
            "put the words good morning into the document");

        Assert.NotNull(command);
        Assert.Equal(IntentKind.TypeText, command!.Intent);
        Assert.Equal("good morning", command.Argument);
    }

    [Fact]
    public void QuestionLabel_MapsToAskLocalBrain_WithWholeText()
    {
        var mapper = new BrainIntentMapper(new FakeBrain(_ => "answer question"));

        var command = mapper.TryMap("tell me about black holes", "tell me about black holes");

        Assert.NotNull(command);
        Assert.Equal(IntentKind.AskLocalBrain, command!.Intent);
        Assert.Equal("tell me about black holes", command.Target);
    }

    [Fact]
    public void NoneLabel_ReturnsNull()
    {
        var mapper = new BrainIntentMapper(new FakeBrain(_ => "none"));

        Assert.Null(mapper.TryMap("hmm", "hmm"));
    }

    [Fact]
    public void UnrecognizedLabel_FallsBackToQuestion()
    {
        var mapper = new BrainIntentMapper(new FakeBrain(_ => "banana"));

        var command = mapper.TryMap("something entirely new", "something entirely new");

        Assert.NotNull(command);
        Assert.Equal(IntentKind.AskLocalBrain, command!.Intent);
    }

    [Fact]
    public void BrainFailure_ReturnsNull()
    {
        var mapper = new BrainIntentMapper(
            new FakeBrain(_ => throw new InvalidOperationException("model exploded")));

        Assert.Null(mapper.TryMap("open the pod bay doors", "open the pod bay doors"));
    }

    [Fact]
    public void Parser_DeterministicGrammarWinsOverBrain()
    {
        var parser = new BrainCommandParser(
            new DeterministicCommandParser(),
            new FakeBrain(_ => "search web"));

        var command = parser.Parse("open Notepad");

        Assert.Equal(IntentKind.OpenApplication, command.Intent);
        Assert.Equal("Notepad", command.Target);
    }

    [Fact]
    public void Parser_UsesBrainWhenGrammarMisses()
    {
        var parser = new BrainCommandParser(
            new DeterministicCommandParser(),
            new FakeBrain(_ => "open app"));

        var command = parser.Parse("can you open the calculator for me");

        Assert.Equal(IntentKind.OpenApplication, command.Intent);
        Assert.Equal("calculator", command.Target);
    }

    [Fact]
    public void Parser_BrainUnavailable_KeepsDeterministicUnknown()
    {
        var parser = new BrainCommandParser(
            new DeterministicCommandParser(),
            new FakeBrain(_ => "open app", isAvailable: false));

        var command = parser.Parse("can you open the calculator for me");

        Assert.Equal(IntentKind.Unknown, command.Intent);
    }
}
