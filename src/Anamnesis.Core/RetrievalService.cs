namespace Anamnesis.Core;

/// <summary>
/// Exact top-k retrieval over the whole corpus. Two rankings are produced —
/// cosine similarity over the embeddings, and BM25 over the raw text — and
/// combined by reciprocal rank fusion. Semantic search alone reliably misses
/// exact identifiers a reader types verbatim; lexical search alone misses
/// paraphrase. Brute force is deliberate: at this corpus size it beats any ANN
/// index, and the swap path (pgvector/Qdrant, SQLite FTS5) starts when
/// <see cref="ChunkStore.LoadAll"/> becomes the bottleneck.
/// </summary>
public sealed class RetrievalService(
    ChunkStore store,
    IEmbeddingClient embeddingClient,
    RetrievalMode mode = RetrievalMode.Hybrid)
{
    /// <summary>
    /// Fusion constant from the original RRF paper. Large relative to the ranks
    /// that matter, so no single list can dominate on its top hit alone.
    /// </summary>
    private const double RrfK = 60;

    public RetrievalMode Mode { get; } = mode;

    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(string question, int topK, CancellationToken cancellationToken = default)
    {
        var chunks = store.LoadAll();
        if (chunks.Count == 0) return [];

        var questionEmbedding = (await embeddingClient
            .EmbedAsync([question], cancellationToken).ConfigureAwait(false))[0];

        var vectorRanking = chunks
            .Select(chunk => new ScoredChunk(chunk, VectorMath.CosineSimilarity(questionEmbedding, chunk.Embedding)))
            .OrderByDescending(s => s.Score)
            .ToList();

        if (Mode == RetrievalMode.VectorOnly)
            return vectorRanking.Take(topK).ToList();

        var lexicalRanking = new Bm25Index(chunks).Search(question);
        return Fuse(vectorRanking, lexicalRanking).Take(topK).ToList();
    }

    /// <summary>
    /// Reciprocal rank fusion: each list contributes 1/(k + rank) per chunk, so
    /// ranks are combined without normalising a bounded cosine against an
    /// unbounded BM25 score. Chunks absent from the lexical list simply score
    /// nothing from it rather than being penalised.
    /// </summary>
    private static IEnumerable<ScoredChunk> Fuse(
        IReadOnlyList<ScoredChunk> vectorRanking,
        IReadOnlyList<ScoredChunk> lexicalRanking)
    {
        var lexicalByChunk = new Dictionary<long, (int Rank, double Score)>(lexicalRanking.Count);
        for (var i = 0; i < lexicalRanking.Count; i++)
            lexicalByChunk[lexicalRanking[i].Chunk.ChunkId] = (i + 1, lexicalRanking[i].LexicalScore);

        var fused = new List<ScoredChunk>(vectorRanking.Count);
        for (var i = 0; i < vectorRanking.Count; i++)
        {
            var hit = vectorRanking[i];
            var score = 1.0 / (RrfK + i + 1);
            var lexical = 0.0;

            if (lexicalByChunk.TryGetValue(hit.Chunk.ChunkId, out var match))
            {
                score += 1.0 / (RrfK + match.Rank);
                lexical = match.Score;
            }

            fused.Add(hit with { LexicalScore = lexical, FusedScore = score });
        }

        // Cosine breaks ties: chunks matched by neither list's ordering quirks
        // fall back to the ranking that was there before hybrid search.
        return fused
            .OrderByDescending(s => s.FusedScore)
            .ThenByDescending(s => s.Score);
    }
}
