using Polly;
using Polly.Retry;

namespace Anamnesis.Core;

/// <summary>
/// Provider router: primary answer client with retry + per-attempt timeout
/// (Polly), automatic failover to the fallback provider when the primary is
/// exhausted. Caller cancellation is never swallowed.
/// </summary>
public sealed class FailoverAnswerClient : IAnswerClient
{
    private readonly IAnswerClient _primary;
    private readonly IAnswerClient _fallback;
    private readonly ResiliencePipeline _primaryPipeline;

    public FailoverAnswerClient(
        IAnswerClient primary,
        IAnswerClient fallback,
        int maxRetryAttempts = 2,
        TimeSpan? retryDelay = null,
        TimeSpan? attemptTimeout = null)
    {
        _primary = primary;
        _fallback = fallback;
        _primaryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = retryDelay ?? TimeSpan.FromMilliseconds(300),
                BackoffType = DelayBackoffType.Exponential,
            })
            .AddTimeout(attemptTimeout ?? TimeSpan.FromSeconds(45))
            .Build();
    }

    public string ProviderName => $"{_primary.ProviderName}->{_fallback.ProviderName}";

    public async Task<AnswerReply> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _primaryPipeline.ExecuteAsync(
                async ct => await _primary.CompleteAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return await _fallback.CompleteAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
        }
    }
}
