using Anamnesis.Core;

namespace Anamnesis.Tests;

public class Bm25IndexTests
{
    private static EmbeddedChunk Chunk(long id, string text) =>
        new(id, "doc-1", "Doc One", (int)id, text, [1f, 0f]);

    private static Bm25Index Index(params string[] texts) =>
        new(texts.Select((t, i) => Chunk(i, t)).ToList());

    [Fact]
    public void Search_RanksExactTermMatchFirst()
    {
        var index = Index(
            "the deployment failed overnight and nobody noticed",
            "bugcheck 0x10E is VIDEO_MEMORY_MANAGEMENT_INTERNAL",
            "a general note about memory and machines");

        var hits = index.Search("what is bugcheck 0x10E");

        Assert.Equal(1, hits[0].Chunk.ChunkId);
    }

    [Fact]
    public void Search_OmitsChunksSharingNoTerm()
    {
        var index = Index("about cats", "about dogs", "entirely unrelated");

        var hits = index.Search("dogs");

        Assert.Single(hits);
        Assert.Equal("about dogs", hits[0].Chunk.Text);
    }

    [Fact]
    public void Search_ReturnsEmptyWhenNoTermMatches()
    {
        var index = Index("about cats", "about dogs");

        Assert.Empty(index.Search("xylophone"));
    }

    [Fact]
    public void Search_ReturnsEmptyForQueryWithNoTokens()
    {
        var index = Index("about cats");

        Assert.Empty(index.Search("   ---   "));
    }

    [Fact]
    public void Search_HandlesEmptyCorpus()
    {
        Assert.Empty(new Bm25Index([]).Search("anything"));
    }

    [Fact]
    public void Search_PrefersTheShorterChunkAtEqualTermFrequency()
    {
        // Length normalisation: one mention in a short chunk is stronger evidence
        // than one mention buried in a long one.
        var index = Index(
            "chlorine",
            "chlorine " + string.Join(' ', Enumerable.Repeat("filler", 200)));

        var hits = index.Search("chlorine");

        Assert.Equal(0, hits[0].Chunk.ChunkId);
    }

    [Fact]
    public void Search_DoesNotPenaliseATermPresentInMostOfTheCorpus()
    {
        // Raw BM25 IDF goes negative past 50% document frequency, which would rank
        // a chunk *below* one that lacks the term entirely. The floored form must not.
        var index = Index("common term here", "common term there", "common term everywhere", "unrelated");

        var hits = index.Search("common");

        Assert.Equal(3, hits.Count);
        Assert.All(hits, h => Assert.True(h.LexicalScore > 0));
    }

    [Theory]
    [InlineData("Bugcheck 0x10E!", new[] { "bugcheck", "0x10e" })]
    [InlineData("ZYN-53673", new[] { "zyn", "53673" })]
    [InlineData("  spaced   out  ", new[] { "spaced", "out" })]
    [InlineData("", new string[0])]
    [InlineData("---", new string[0])]
    public void Tokenize_SplitsOnNonAlphanumericAndLowercases(string input, string[] expected)
    {
        Assert.Equal(expected, Bm25Index.Tokenize(input).ToArray());
    }
}
