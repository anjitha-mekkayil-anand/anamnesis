using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anamnesis.Core;

public sealed class EvalService(RetrievalService retrieval, IAnswerClient answerClient)
{
    private const string JudgeSystemPrompt =
        """
        You are a strict evaluation judge for a retrieval-augmented answering system.
        You will receive source excerpts, a question, and the system's answer.
        Decide whether the answer is faithful: every claim in it must be supported by the excerpts.
        An answer that correctly says the excerpts don't contain the information is faithful.
        Reply with ONLY a JSON object: {"faithful": true|false, "reason": "<one sentence>"}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public async Task<EvalRunSummary> RunAsync(
        string goldenPath,
        string resultsPath,
        int k = 5,
        bool evaluateAnswers = false,
        CancellationToken cancellationToken = default)
    {
        var golden = JsonSerializer.Deserialize<List<GoldenItem>>(
                await File.ReadAllTextAsync(goldenPath, cancellationToken).ConfigureAwait(false), JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse golden set: {goldenPath}");

        var results = new List<EvalItemResult>();
        foreach (var item in golden)
        {
            results.Add(await EvaluateItemAsync(item, k, evaluateAnswers, cancellationToken).ConfigureAwait(false));
        }

        var judged = results.Where(r => r.Faithful is not null).ToList();
        var summary = new EvalRunSummary(
            RunAtUtc: DateTime.UtcNow,
            K: k,
            Items: results.Count,
            HitRate: Math.Round(results.Average(r => r.Hit ? 1.0 : 0.0), 4),
            Mrr: Math.Round(results.Average(r => r.ReciprocalRank), 4),
            AvgRetrievalMs: Math.Round(results.Average(r => r.RetrievalMs), 1),
            AnswersEvaluated: evaluateAnswers,
            FaithfulRate: judged.Count > 0
                ? Math.Round(judged.Average(r => r.Faithful == true ? 1.0 : 0.0), 4)
                : null,
            AvgAnswerMs: evaluateAnswers ? Math.Round(results.Average(r => r.AnswerMs ?? 0), 1) : null,
            Results: results);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultsPath))!);
        await File.AppendAllTextAsync(
            resultsPath,
            JsonSerializer.Serialize(summary, JsonOptions) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);

        return summary;
    }

    private async Task<EvalItemResult> EvaluateItemAsync(GoldenItem item, int k, bool evaluateAnswers, CancellationToken cancellationToken)
    {
        var retrievalTimer = Stopwatch.StartNew();
        var hits = await retrieval.SearchAsync(item.Question, k, cancellationToken).ConfigureAwait(false);
        retrievalTimer.Stop();

        var (rank, reciprocalRank) = RankOf(hits, item.ExpectedDocumentId);

        string? answer = null;
        long? answerMs = null;
        bool? faithful = null;
        string? judgeReason = null;

        if (evaluateAnswers && hits.Count > 0)
        {
            var userPrompt = QueryService.BuildUserPrompt(item.Question, hits);

            var answerTimer = Stopwatch.StartNew();
            var reply = await answerClient
                .CompleteAsync(QueryService.GroundingSystemPrompt, userPrompt, cancellationToken)
                .ConfigureAwait(false);
            answerTimer.Stop();
            answer = reply.Text;
            answerMs = answerTimer.ElapsedMilliseconds;

            var verdict = TryParseVerdict((await answerClient.CompleteAsync(
                JudgeSystemPrompt,
                $"{userPrompt}\n\nSystem's answer:\n{answer}",
                cancellationToken).ConfigureAwait(false)).Text);
            faithful = verdict?.Faithful;
            judgeReason = verdict?.Reason;
        }

        return new EvalItemResult(
            item.Id, item.Question, item.ExpectedDocumentId,
            rank, rank is not null && rank <= k, reciprocalRank,
            retrievalTimer.ElapsedMilliseconds, answerMs, answer, faithful, judgeReason);
    }

    internal static (int? Rank, double ReciprocalRank) RankOf(IReadOnlyList<ScoredChunk> hits, string expectedDocumentId)
    {
        for (var i = 0; i < hits.Count; i++)
        {
            if (hits[i].Chunk.DocumentId == expectedDocumentId)
                return (i + 1, 1.0 / (i + 1));
        }
        return (null, 0.0);
    }

    internal static JudgeVerdict? TryParseVerdict(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            return JsonSerializer.Deserialize<JudgeVerdict>(text[start..(end + 1)], JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record GoldenItem(string Id, string Question, string ExpectedDocumentId);

public sealed record JudgeVerdict(
    [property: JsonPropertyName("faithful")] bool Faithful,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record EvalItemResult(
    string Id,
    string Question,
    string ExpectedDocumentId,
    int? Rank,
    bool Hit,
    double ReciprocalRank,
    long RetrievalMs,
    long? AnswerMs,
    string? Answer,
    bool? Faithful,
    string? JudgeReason);

public sealed record EvalRunSummary(
    DateTime RunAtUtc,
    int K,
    int Items,
    double HitRate,
    double Mrr,
    double AvgRetrievalMs,
    bool AnswersEvaluated,
    double? FaithfulRate,
    double? AvgAnswerMs,
    IReadOnlyList<EvalItemResult> Results);
