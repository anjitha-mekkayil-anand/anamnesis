using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Anamnesis.Core;

public sealed class OpenAiAnswerClient(HttpClient httpClient, string model = "gpt-4o-mini", string providerName = "openai")
    : IAnswerClient
{
    // providerName distinguishes OpenAI itself from any OpenAI-compatible
    // endpoint (e.g. a local Ollama server) reusing this same wire format.
    public string ProviderName => providerName;

    public async Task<AnswerReply> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "v1/chat/completions",
            new ChatRequest(model,
                [new("system", systemPrompt), new("user", userPrompt)],
                MaxTokens: 1024),
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"OpenAI chat request failed ({(int)response.StatusCode}): {detail}");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<ChatResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty chat response.");

        return new AnswerReply(payload.Choices[0].Message.Content ?? "", model, ProviderName);
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<ChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] List<ChatChoice> Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatResponseMessage Message);

    private sealed record ChatResponseMessage(
        [property: JsonPropertyName("content")] string? Content);
}
