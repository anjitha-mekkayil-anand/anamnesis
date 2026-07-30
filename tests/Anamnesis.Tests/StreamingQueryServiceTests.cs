using System.Runtime.CompilerServices;
using Anamnesis.Core;

namespace Anamnesis.Tests;

public class StreamingQueryServiceTests
{
    private static ScoredChunk Hit(int n) => new(
        new EmbeddedChunk(n, $"doc-{n}", $"Post {n}", 0, $"excerpt {n}", [1f]), 0.9 - n * 0.1);

    private sealed class StubFallback(string provider) : IAnswerClient
    {
        public int Calls { get; private set; }
        public string ProviderName => provider;

        public Task<AnswerReply> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new AnswerReply("full fallback answer", "fallback-model", provider));
        }
    }

    private sealed class StubStreamer(string[] chunks, Exception? throwAfter = null, int failBeforeIndex = -1)
        : IStreamingAnswerClient
    {
        public string ProviderName => "anthropic";
        public string Model => "stream-model";

        public async IAsyncEnumerable<string> StreamTextAsync(string systemPrompt, string userPrompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < chunks.Length; i++)
            {
                if (i == failBeforeIndex)
                    throw throwAfter ?? new HttpRequestException("stream broke");
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunks[i];
                await Task.Yield();
            }
            if (failBeforeIndex == chunks.Length)
                throw throwAfter ?? new HttpRequestException("stream broke");
        }
    }

    private static async Task<List<AnswerStreamEvent>> Collect(StreamingQueryService service,
        IReadOnlyList<ScoredChunk> hits, CancellationToken ct = default)
    {
        var events = new List<AnswerStreamEvent>();
        await foreach (var e in service.StreamAnswerAsync("q", hits, ct))
            events.Add(e);
        return events;
    }

    // RetrievalService is only exercised by AskStreamingAsync; these tests drive
    // the internal StreamAnswerAsync with fabricated hits, same pattern as
    // QueryServiceTests using BuildUserPrompt directly.
    private static StreamingQueryService Service(IStreamingAnswerClient? streamer, IAnswerClient fallback) =>
        new(retrieval: null!, streamer, fallback);

    [Fact]
    public async Task HappyPath_CitationsFirst_ThenDeltas_ThenStreamedDone()
    {
        var fallback = new StubFallback("openai");
        var service = Service(new StubStreamer(["Hel", "lo"]), fallback);

        var events = await Collect(service, [Hit(1), Hit(2)]);

        Assert.IsType<CitationsStreamEvent>(events[0]);
        Assert.Equal(2, ((CitationsStreamEvent)events[0]).Citations.Count);
        Assert.Equal("Hel", ((DeltaStreamEvent)events[1]).Text);
        Assert.Equal("lo", ((DeltaStreamEvent)events[2]).Text);
        var done = (DoneStreamEvent)events[^1];
        Assert.True(done.Streamed);
        Assert.Equal("anthropic", done.Provider);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task StreamFailsBeforeFirstToken_DegradesToFallback_SingleDelta()
    {
        var fallback = new StubFallback("openai");
        var service = Service(new StubStreamer(["never"], failBeforeIndex: 0), fallback);

        var events = await Collect(service, [Hit(1)]);

        Assert.IsType<CitationsStreamEvent>(events[0]);
        Assert.Equal("full fallback answer", ((DeltaStreamEvent)events[1]).Text);
        var done = (DoneStreamEvent)events[^1];
        Assert.False(done.Streamed);
        Assert.Equal("openai", done.Provider);
        Assert.Equal(1, fallback.Calls);
        Assert.DoesNotContain(events, e => e is ErrorStreamEvent);
    }

    [Fact]
    public async Task StreamFailsAfterTokens_SurfacesError_DoesNotSilentlyFallBack()
    {
        var fallback = new StubFallback("openai");
        var service = Service(new StubStreamer(["partial "], failBeforeIndex: 1), fallback);

        var events = await Collect(service, [Hit(1)]);

        Assert.Contains(events, e => e is DeltaStreamEvent d && d.Text == "partial ");
        Assert.Contains(events, e => e is ErrorStreamEvent);
        Assert.Equal(0, fallback.Calls); // a partial answer must not be silently replaced
    }

    [Fact]
    public async Task NoStreamingClient_UsesFallbackPath()
    {
        var fallback = new StubFallback("local->anthropic->openai");
        var service = Service(streamer: null, fallback);

        var events = await Collect(service, [Hit(1)]);

        Assert.Equal("full fallback answer", ((DeltaStreamEvent)events[1]).Text);
        Assert.False(((DoneStreamEvent)events[^1]).Streamed);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task EmptyHits_ReportsEmptyCorpus_NoLlmCalls()
    {
        var fallback = new StubFallback("openai");
        var service = Service(new StubStreamer(["x"]), fallback);

        var events = await Collect(service, []);

        Assert.Contains("corpus is empty", ((DeltaStreamEvent)events[0]).Text);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task CallerCancellation_IsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var fallback = new StubFallback("openai");
        var service = Service(new StubStreamer(["a", "b"]), fallback);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Collect(service, [Hit(1)], cts.Token));
        Assert.Equal(0, fallback.Calls);
    }
}
