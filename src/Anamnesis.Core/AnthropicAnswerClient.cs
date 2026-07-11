using Anthropic;
using Anthropic.Models.Messages;

namespace Anamnesis.Core;

public sealed class AnthropicAnswerClient(AnthropicClient client, string model = "claude-haiku-4-5")
    : IAnswerClient
{
    public string ProviderName => "anthropic";

    public async Task<AnswerReply> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = model,
            MaxTokens = 1024,
            System = new List<TextBlockParam> { new() { Text = systemPrompt } },
            Messages = [new() { Role = Role.User, Content = userPrompt }],
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var text = string.Concat(response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(t => t.Text));

        return new AnswerReply(text, model, ProviderName);
    }
}
