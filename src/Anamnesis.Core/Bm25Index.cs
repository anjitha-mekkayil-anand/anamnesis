namespace Anamnesis.Core;

/// <summary>
/// In-memory BM25 lexical index over the chunk set. Exists because embeddings
/// are weak on exact identifiers — error codes, method names, ticket numbers,
/// proper nouns — which are the terms a reader is most likely to type verbatim.
/// Brute force over the loaded chunks is deliberate, matching <see cref="ChunkStore"/>:
/// at this corpus size it beats a maintained FTS table, and the swap path
/// (SQLite FTS5 with bm25()) starts the day LoadAll is the bottleneck.
/// </summary>
public sealed class Bm25Index
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    private readonly IReadOnlyList<EmbeddedChunk> _chunks;
    private readonly Dictionary<string, int>[] _termFrequencies;
    private readonly int[] _lengths;
    private readonly Dictionary<string, int> _documentFrequencies = new(StringComparer.Ordinal);
    private readonly double _averageLength;

    public Bm25Index(IReadOnlyList<EmbeddedChunk> chunks)
    {
        _chunks = chunks;
        _termFrequencies = new Dictionary<string, int>[chunks.Count];
        _lengths = new int[chunks.Count];

        for (var i = 0; i < chunks.Count; i++)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var length = 0;
            foreach (var token in Tokenize(chunks[i].Text))
            {
                counts[token] = counts.GetValueOrDefault(token) + 1;
                length++;
            }

            _termFrequencies[i] = counts;
            _lengths[i] = length;
            foreach (var term in counts.Keys)
                _documentFrequencies[term] = _documentFrequencies.GetValueOrDefault(term) + 1;
        }

        _averageLength = chunks.Count == 0 ? 0 : _lengths.Average();
    }

    /// <summary>
    /// Scores every chunk against the query and returns only those with a
    /// non-zero score, highest first. Chunks sharing no term with the query are
    /// omitted rather than returned at zero — a list of ties carries no ranking
    /// information, and feeding it into rank fusion would add noise.
    /// </summary>
    public IReadOnlyList<ScoredChunk> Search(string query)
    {
        if (_chunks.Count == 0) return [];

        var queryTerms = Tokenize(query).Distinct(StringComparer.Ordinal).ToArray();
        if (queryTerms.Length == 0) return [];

        var scored = new List<ScoredChunk>();
        for (var i = 0; i < _chunks.Count; i++)
        {
            var score = 0.0;
            foreach (var term in queryTerms)
                score += TermScore(term, i);

            if (score > 0)
                scored.Add(new ScoredChunk(_chunks[i], 0) { LexicalScore = score });
        }

        scored.Sort((a, b) => b.LexicalScore.CompareTo(a.LexicalScore));
        return scored;
    }

    private double TermScore(string term, int index)
    {
        if (!_termFrequencies[index].TryGetValue(term, out var frequency)) return 0;

        var documentFrequency = _documentFrequencies.GetValueOrDefault(term);
        // Probabilistic IDF, floored at zero: a term present in more than half the
        // corpus otherwise scores negative and would penalise chunks for containing it.
        var idf = Math.Log(1 + (_chunks.Count - documentFrequency + 0.5) / (documentFrequency + 0.5));

        var normalisedLength = _averageLength == 0 ? 1 : _lengths[index] / _averageLength;
        var denominator = frequency + (K1 * (1 - B + (B * normalisedLength)));
        return idf * (frequency * (K1 + 1) / denominator);
    }

    /// <summary>
    /// Lowercases and splits on any non-alphanumeric character. Digits are kept as
    /// tokens on purpose: identifiers such as <c>0x10E</c> or <c>ZYN-53673</c> are
    /// exactly the terms this index exists to catch, and they survive as their parts.
    /// </summary>
    internal static IEnumerable<string> Tokenize(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isTokenChar = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (isTokenChar && start < 0)
            {
                start = i;
            }
            else if (!isTokenChar && start >= 0)
            {
                yield return text[start..i].ToLowerInvariant();
                start = -1;
            }
        }
    }
}
