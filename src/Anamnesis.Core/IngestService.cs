namespace Anamnesis.Core;

public sealed class IngestService(ChunkStore store, IEmbeddingClient embeddingClient, Chunker chunker)
{
    public async Task<IngestResult> IngestDirectoryAsync(string corpusRoot, CancellationToken cancellationToken = default)
    {
        store.EnsureCreated();
        var documents = MarkdownCorpusLoader.LoadDirectory(corpusRoot);

        var totalChunks = 0;
        foreach (var document in documents)
        {
            var chunks = chunker.ChunkDocument(document);
            if (chunks.Count == 0) continue;

            var embeddings = await embeddingClient
                .EmbedAsync(chunks.Select(c => c.Text).ToList(), cancellationToken)
                .ConfigureAwait(false);

            store.ReplaceDocument(document, chunks, embeddings);
            totalChunks += chunks.Count;
        }

        return new IngestResult(documents.Count, totalChunks);
    }
}

public sealed record IngestResult(int Documents, int Chunks);
