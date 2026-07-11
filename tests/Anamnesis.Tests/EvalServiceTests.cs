using Anamnesis.Core;

namespace Anamnesis.Tests;

public class EvalServiceTests
{
    private static ScoredChunk Hit(string docId, double score) =>
        new(new EmbeddedChunk(0, docId, docId, 0, "text", [1f]), score);

    [Fact]
    public void RankOf_ExpectedDocFirst_RankOneReciprocalOne()
    {
        var (rank, rr) = EvalService.RankOf([Hit("doc-a", 0.9), Hit("doc-b", 0.5)], "doc-a");

        Assert.Equal(1, rank);
        Assert.Equal(1.0, rr);
    }

    [Fact]
    public void RankOf_ExpectedDocThird_ReciprocalIsOneThird()
    {
        var hits = new[] { Hit("doc-a", 0.9), Hit("doc-b", 0.8), Hit("doc-c", 0.7) };

        var (rank, rr) = EvalService.RankOf(hits, "doc-c");

        Assert.Equal(3, rank);
        Assert.Equal(1.0 / 3, rr, precision: 6);
    }

    [Fact]
    public void RankOf_ExpectedDocMissing_NullRankZeroReciprocal()
    {
        var (rank, rr) = EvalService.RankOf([Hit("doc-a", 0.9)], "doc-z");

        Assert.Null(rank);
        Assert.Equal(0.0, rr);
    }

    [Fact]
    public void TryParseVerdict_PlainJson_Parses()
    {
        var verdict = EvalService.TryParseVerdict("{\"faithful\": true, \"reason\": \"all claims cited\"}");

        Assert.NotNull(verdict);
        Assert.True(verdict.Faithful);
        Assert.Equal("all claims cited", verdict.Reason);
    }

    [Fact]
    public void TryParseVerdict_JsonWrappedInProse_StillParses()
    {
        var verdict = EvalService.TryParseVerdict(
            "Sure, here is my verdict:\n{\"faithful\": false, \"reason\": \"invented a date\"}\nHope that helps.");

        Assert.NotNull(verdict);
        Assert.False(verdict.Faithful);
    }

    [Fact]
    public void TryParseVerdict_Garbage_ReturnsNull()
    {
        Assert.Null(EvalService.TryParseVerdict("no json here"));
        Assert.Null(EvalService.TryParseVerdict("{broken"));
    }
}
