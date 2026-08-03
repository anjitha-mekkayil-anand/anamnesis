namespace Anamnesis.Core;

public sealed record CorpusDocument(
    string Id,
    string Title,
    string Type,
    string Published,
    string SourcePath,
    string Body);

public sealed record Chunk(
    string DocumentId,
    int Ordinal,
    string Text);

public sealed record EmbeddedChunk(
    long ChunkId,
    string DocumentId,
    string DocumentTitle,
    int Ordinal,
    string Text,
    float[] Embedding);

/// <summary>
/// <paramref name="Score"/> stays the cosine similarity, which is what citations
/// surface to callers. Hybrid retrieval orders by <see cref="FusedScore"/> instead,
/// so a chunk found only by the lexical index still reports a meaningful vector score.
/// </summary>
public sealed record ScoredChunk(EmbeddedChunk Chunk, double Score)
{
    /// <summary>BM25 score; zero when the chunk shares no term with the query.</summary>
    public double LexicalScore { get; init; }

    /// <summary>Reciprocal-rank-fusion score across the vector and lexical rankings.</summary>
    public double FusedScore { get; init; }
}

public enum RetrievalMode
{
    /// <summary>Cosine similarity only — the behaviour before hybrid search landed.</summary>
    VectorOnly,

    /// <summary>Cosine similarity fused with BM25 by reciprocal rank fusion.</summary>
    Hybrid,
}
