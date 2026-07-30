using System.Runtime.CompilerServices;

namespace Anamnesis.Core;

public abstract record AnswerStreamEvent;

/// <summary>Citations are known from retrieval before the answer exists — emitted first.</summary>
public sealed record CitationsStreamEvent(IReadOnlyList<Citation> Citations) : AnswerStreamEvent;

public sealed record DeltaStreamEvent(string Text) : AnswerStreamEvent;

/// <summary>Terminal event. <paramref name="Streamed"/> is false when the answer
/// arrived via the non-streaming fallback path (graceful degradation).</summary>
public sealed record DoneStreamEvent(string? Model, string? Provider, bool Streamed) : AnswerStreamEvent;

/// <summary>Streaming failed after output had already been sent — the partial
/// answer on screen is incomplete and the client should say so.</summary>
public sealed record ErrorStreamEvent(string Message) : AnswerStreamEvent;

/// <summary>
/// Streaming variant of <see cref="QueryService"/>: retrieval first (citations
/// stream immediately), then token deltas from the streaming client. If
/// streaming is unavailable or fails before the first token, the request
/// degrades to the non-streaming failover chain and the full answer arrives as
/// a single delta — same grounding, same citations, no user-visible failure.
/// </summary>
public sealed class StreamingQueryService(
    RetrievalService retrieval,
    IStreamingAnswerClient? streamingClient,
    IAnswerClient fallback)
{
    public async IAsyncEnumerable<AnswerStreamEvent> AskStreamingAsync(string question, int topK = 5,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var hits = await retrieval.SearchAsync(question, topK, cancellationToken).ConfigureAwait(false);
        await foreach (var streamEvent in StreamAnswerAsync(question, hits, cancellationToken).ConfigureAwait(false))
            yield return streamEvent;
    }

    internal async IAsyncEnumerable<AnswerStreamEvent> StreamAnswerAsync(string question, IReadOnlyList<ScoredChunk> hits,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (hits.Count == 0)
        {
            yield return new DeltaStreamEvent("The corpus is empty — run ingestion first.");
            yield return new DoneStreamEvent(null, null, Streamed: false);
            yield break;
        }

        yield return new CitationsStreamEvent(QueryService.BuildCitations(hits));

        var userPrompt = QueryService.BuildUserPrompt(question, hits);

        if (streamingClient is null)
        {
            await foreach (var streamEvent in CompleteViaFallbackAsync(userPrompt, cancellationToken).ConfigureAwait(false))
                yield return streamEvent;
            yield break;
        }

        // yield is not allowed inside try/catch, so drive the enumerator by hand:
        // a failure BEFORE any token degrades silently to the fallback chain; a
        // failure AFTER tokens is surfaced — the partial answer must not pass as complete.
        var enumerator = streamingClient
            .StreamTextAsync(QueryService.GroundingSystemPrompt, userPrompt, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var produced = false;
        try
        {
            while (true)
            {
                bool moved;
                string? failure = null;
                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    moved = false;
                    failure = ex.Message;
                }

                if (failure is not null)
                {
                    if (produced)
                    {
                        yield return new ErrorStreamEvent("The stream was interrupted — the answer above may be incomplete.");
                        yield return new DoneStreamEvent(streamingClient.Model, streamingClient.ProviderName, Streamed: true);
                        yield break;
                    }

                    await foreach (var streamEvent in CompleteViaFallbackAsync(userPrompt, cancellationToken).ConfigureAwait(false))
                        yield return streamEvent;
                    yield break;
                }

                if (!moved)
                    break;

                produced = true;
                yield return new DeltaStreamEvent(enumerator.Current);
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        yield return new DoneStreamEvent(streamingClient.Model, streamingClient.ProviderName, Streamed: true);
    }

    private async IAsyncEnumerable<AnswerStreamEvent> CompleteViaFallbackAsync(string userPrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reply = await fallback
            .CompleteAsync(QueryService.GroundingSystemPrompt, userPrompt, cancellationToken)
            .ConfigureAwait(false);
        yield return new DeltaStreamEvent(reply.Text);
        yield return new DoneStreamEvent(reply.Model, reply.Provider, Streamed: false);
    }
}
