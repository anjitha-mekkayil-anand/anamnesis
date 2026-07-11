using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Anamnesis.Core;

public sealed class OpenAiEmbeddingClient(HttpClient httpClient, string model = "text-embedding-3-small")
    : IEmbeddingClient
{
    private const int BatchSize = 64;

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        var results = new float[inputs.Count][];

        for (var offset = 0; offset < inputs.Count; offset += BatchSize)
        {
            var batch = inputs.Skip(offset).Take(BatchSize).ToArray();
            var response = await httpClient.PostAsJsonAsync(
                "v1/embeddings",
                new EmbeddingRequest(model, batch),
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Embedding request failed ({(int)response.StatusCode}): {detail}");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<EmbeddingResponse>(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Empty embedding response.");

            foreach (var item in payload.Data)
                results[offset + item.Index] = item.Embedding;
        }

        return results;
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string[] Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingDatum> Data);

    private sealed record EmbeddingDatum(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
