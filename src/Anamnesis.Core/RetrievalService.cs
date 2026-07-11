namespace Anamnesis.Core;

/// <summary>
/// Exact top-k retrieval: embeds the question and scores every stored chunk
/// with cosine similarity. Brute force is deliberate — at this corpus size it
/// beats any ANN index; the swap path (pgvector/Qdrant) starts when LoadAll
/// becomes the bottleneck.
/// </summary>
public sealed class RetrievalService(ChunkStore store, IEmbeddingClient embeddingClient)
{
    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(string question, int topK, CancellationToken cancellationToken = default)
    {
        var questionEmbedding = (await embeddingClient
            .EmbedAsync([question], cancellationToken).ConfigureAwait(false))[0];

        return store.LoadAll()
            .Select(chunk => new ScoredChunk(chunk, VectorMath.CosineSimilarity(questionEmbedding, chunk.Embedding)))
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList();
    }
}
