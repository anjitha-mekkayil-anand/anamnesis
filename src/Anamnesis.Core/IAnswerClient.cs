namespace Anamnesis.Core;

public interface IAnswerClient
{
    string ProviderName { get; }

    Task<AnswerReply> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}

public sealed record AnswerReply(string Text, string Model, string Provider);
