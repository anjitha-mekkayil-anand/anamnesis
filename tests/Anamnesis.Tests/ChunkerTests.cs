using Anamnesis.Core;

namespace Anamnesis.Tests;

public class ChunkerTests
{
    private static CorpusDocument Doc(string body) =>
        new("doc-1", "Test", "post", "2026-07-11", "test.md", body);

    [Fact]
    public void ShortDocument_ProducesSingleChunk()
    {
        var chunks = new Chunker().ChunkDocument(Doc("One paragraph only."));

        var chunk = Assert.Single(chunks);
        Assert.Equal("One paragraph only.", chunk.Text);
        Assert.Equal(0, chunk.Ordinal);
    }

    [Fact]
    public void LongDocument_SplitsAtParagraphBoundaries()
    {
        var paragraphs = Enumerable.Range(1, 10)
            .Select(i => $"Paragraph {i}. " + new string('x', 400));
        var chunks = new Chunker(targetChars: 1000).ChunkDocument(Doc(string.Join("\n\n", paragraphs)));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.DoesNotContain("Paragraph 1. Paragraph 2", c.Text));
    }

    [Fact]
    public void ConsecutiveChunks_ShareOverlapParagraph()
    {
        var paragraphs = Enumerable.Range(1, 6).Select(i => $"P{i} " + new string('x', 300));
        var chunks = new Chunker(targetChars: 700).ChunkDocument(Doc(string.Join("\n\n", paragraphs)));

        Assert.True(chunks.Count >= 2);
        var firstChunkLastParagraph = chunks[0].Text.Split("\n\n")[^1];
        Assert.StartsWith(firstChunkLastParagraph, chunks[1].Text);
    }

    [Fact]
    public void Ordinals_AreSequentialFromZero()
    {
        var paragraphs = Enumerable.Range(1, 8).Select(i => new string((char)('a' + i), 500));
        var chunks = new Chunker(targetChars: 800).ChunkDocument(Doc(string.Join("\n\n", paragraphs)));

        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Ordinal));
    }

    [Fact]
    public void EmptyBody_ProducesNoChunks()
    {
        Assert.Empty(new Chunker().ChunkDocument(Doc("")));
    }
}
