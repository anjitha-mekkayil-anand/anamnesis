namespace Anamnesis.Core;

public static class VectorMath
{
    public static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector length mismatch: {a.Length} vs {b.Length}");

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            magA += (double)a[i] * a[i];
            magB += (double)b[i] * b[i];
        }

        var denominator = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denominator == 0 ? 0 : dot / denominator;
    }
}
