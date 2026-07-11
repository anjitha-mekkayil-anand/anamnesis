using Anamnesis.Core;

namespace Anamnesis.Tests;

public class ChunkStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"anamnesis-test-{Guid.NewGuid():N}.db");

    [Fact]
    public void EmbeddingBlob_RoundTripsExactly()
    {
        float[] vector = [0.1f, -2.5f, float.MaxValue, 0f];
        Assert.Equal(vector, ChunkStore.FromBlob(ChunkStore.ToBlob(vector)));
    }

    [Fact]
    public void ReplaceDocument_ThenLoadAll_ReturnsChunksWithEmbeddings()
    {
        var store = new ChunkStore(_dbPath);
        store.EnsureCreated();

        var document = new CorpusDocument("doc-1", "Title", "post", "2026-07-11", "src.md", "body");
        var chunks = new[] { new Chunk("doc-1", 0, "first"), new Chunk("doc-1", 1, "second") };
        float[][] embeddings = [[1f, 2f], [3f, 4f]];

        store.ReplaceDocument(document, chunks, embeddings);
        var loaded = store.LoadAll();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Title", loaded[0].DocumentTitle);
        Assert.Equal([1f, 2f], loaded[0].Embedding);
        Assert.Equal("second", loaded[1].Text);
    }

    [Fact]
    public void ReplaceDocument_Twice_DoesNotDuplicateChunks()
    {
        var store = new ChunkStore(_dbPath);
        store.EnsureCreated();

        var document = new CorpusDocument("doc-1", "Title", "post", "2026-07-11", "src.md", "body");
        store.ReplaceDocument(document, [new Chunk("doc-1", 0, "v1")], [[1f]]);
        store.ReplaceDocument(document, [new Chunk("doc-1", 0, "v2")], [[2f]]);

        var (documents, chunks) = store.Counts();
        Assert.Equal(1, documents);
        Assert.Equal(1, chunks);
        Assert.Equal("v2", store.LoadAll()[0].Text);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
