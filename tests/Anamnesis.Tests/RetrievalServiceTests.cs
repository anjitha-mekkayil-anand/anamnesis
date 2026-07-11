using Anamnesis.Core;

namespace Anamnesis.Tests;

public class RetrievalServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"anamnesis-test-{Guid.NewGuid():N}.db");

    private sealed class FakeEmbeddingClient(Func<string, float[]> embed) : IEmbeddingClient
    {
        public Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
            => Task.FromResult(inputs.Select(embed).ToArray());
    }

    [Fact]
    public async Task Search_RanksMostSimilarChunkFirst()
    {
        var store = new ChunkStore(_dbPath);
        store.EnsureCreated();
        store.ReplaceDocument(
            new CorpusDocument("doc-1", "Doc One", "post", "2026-07-11", "a.md", "x"),
            [new Chunk("doc-1", 0, "about cats"), new Chunk("doc-1", 1, "about dogs")],
            [[1f, 0f], [0f, 1f]]);

        var retrieval = new RetrievalService(store, new FakeEmbeddingClient(
            q => q.Contains("dog") ? [0.1f, 0.9f] : [0.9f, 0.1f]));

        var hits = await retrieval.SearchAsync("tell me about dogs", topK: 2);

        Assert.Equal("about dogs", hits[0].Chunk.Text);
        Assert.True(hits[0].Score > hits[1].Score);
    }

    [Fact]
    public async Task Search_RespectsTopK()
    {
        var store = new ChunkStore(_dbPath);
        store.EnsureCreated();
        store.ReplaceDocument(
            new CorpusDocument("doc-1", "Doc One", "post", "2026-07-11", "a.md", "x"),
            Enumerable.Range(0, 10).Select(i => new Chunk("doc-1", i, $"chunk {i}")).ToList(),
            Enumerable.Range(0, 10).Select(i => new[] { (float)i, 1f }).ToArray());

        var retrieval = new RetrievalService(store, new FakeEmbeddingClient(_ => [1f, 0f]));

        var hits = await retrieval.SearchAsync("anything", topK: 3);

        Assert.Equal(3, hits.Count);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
