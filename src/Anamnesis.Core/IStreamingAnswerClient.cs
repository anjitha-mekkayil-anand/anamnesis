namespace Anamnesis.Core;

/// <summary>
/// Answer client that can stream text deltas as the model generates them.
/// Streaming is a progressive-enhancement path: callers fall back to
/// <see cref="IAnswerClient.CompleteAsync"/> (and its failover chain) when
/// streaming is unavailable or fails before producing output.
/// </summary>
public interface IStreamingAnswerClient
{
    string ProviderName { get; }

    string Model { get; }

    IAsyncEnumerable<string> StreamTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
