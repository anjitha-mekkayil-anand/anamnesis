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

public sealed record ScoredChunk(EmbeddedChunk Chunk, double Score);
