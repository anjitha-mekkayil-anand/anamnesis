namespace Anamnesis.Core;

public interface IEmbeddingClient
{
    Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);
}
