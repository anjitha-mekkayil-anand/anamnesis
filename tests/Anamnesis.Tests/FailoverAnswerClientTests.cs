using Anamnesis.Core;

namespace Anamnesis.Tests;

public class FailoverAnswerClientTests
{
    private sealed class StubClient(string provider, Func<Task<AnswerReply>> respond) : IAnswerClient
    {
        public int Calls { get; private set; }
        public string ProviderName => provider;

        public Task<AnswerReply> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            Calls++;
            return respond();
        }
    }

    private static StubClient Working(string provider) =>
        new(provider, () => Task.FromResult(new AnswerReply($"answer from {provider}", "model", provider)));

    private static StubClient Failing(string provider) =>
        new(provider, () => Task.FromException<AnswerReply>(new HttpRequestException("boom")));

    [Fact]
    public async Task PrimaryHealthy_FallbackNeverCalled()
    {
        var primary = Working("anthropic");
        var fallback = Working("openai");
        var router = new FailoverAnswerClient(primary, fallback, retryDelay: TimeSpan.Zero);

        var reply = await router.CompleteAsync("s", "u");

        Assert.Equal("anthropic", reply.Provider);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task PrimaryFails_RetriesThenFallsOver()
    {
        var primary = Failing("anthropic");
        var fallback = Working("openai");
        var router = new FailoverAnswerClient(primary, fallback, maxRetryAttempts: 2, retryDelay: TimeSpan.Zero);

        var reply = await router.CompleteAsync("s", "u");

        Assert.Equal("openai", reply.Provider);
        Assert.Equal(3, primary.Calls); // initial + 2 retries
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task BothFail_ThrowsFallbackError()
    {
        var router = new FailoverAnswerClient(Failing("anthropic"), Failing("openai"), retryDelay: TimeSpan.Zero);

        await Assert.ThrowsAsync<HttpRequestException>(() => router.CompleteAsync("s", "u"));
    }

    [Fact]
    public async Task CallerCancellation_IsNotSwallowedByFallback()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var primary = new StubClient("anthropic",
            () => Task.FromException<AnswerReply>(new OperationCanceledException(cts.Token)));
        var fallback = Working("openai");
        var router = new FailoverAnswerClient(primary, fallback, retryDelay: TimeSpan.Zero);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => router.CompleteAsync("s", "u", cts.Token));
        Assert.Equal(0, fallback.Calls);
    }
}
