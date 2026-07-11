using Anamnesis.Core;

namespace Anamnesis.Tests;

public class VectorMathTests
{
    [Fact]
    public void IdenticalVectors_ScoreOne()
    {
        float[] v = [0.5f, -1.2f, 3.3f];
        Assert.Equal(1.0, VectorMath.CosineSimilarity(v, v), precision: 6);
    }

    [Fact]
    public void OrthogonalVectors_ScoreZero()
    {
        Assert.Equal(0.0, VectorMath.CosineSimilarity([1f, 0f], [0f, 1f]), precision: 6);
    }

    [Fact]
    public void OppositeVectors_ScoreMinusOne()
    {
        Assert.Equal(-1.0, VectorMath.CosineSimilarity([1f, 2f], [-1f, -2f]), precision: 6);
    }

    [Fact]
    public void ZeroVector_ScoresZeroInsteadOfNaN()
    {
        Assert.Equal(0.0, VectorMath.CosineSimilarity([0f, 0f], [1f, 2f]));
    }

    [Fact]
    public void MismatchedLengths_Throw()
    {
        Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity([1f], [1f, 2f]));
    }
}
