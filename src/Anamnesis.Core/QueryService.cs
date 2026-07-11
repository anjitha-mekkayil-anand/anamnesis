using System.Text;

namespace Anamnesis.Core;

public sealed class QueryService(RetrievalService retrieval, IAnswerClient answerClient)
{
    internal const string GroundingSystemPrompt =
        """
        You answer questions about the author's published writing (LinkedIn posts and Substack letters).
        Use ONLY the numbered source excerpts provided in the user message.
        Cite sources inline as [1], [2] etc. matching the excerpt numbers.
        If the excerpts do not contain the answer, say so plainly — do not invent content or draw on outside knowledge.
        Answer in a few sentences, direct and concrete.
        """;

    public async Task<QueryResult> AskAsync(string question, int topK = 5, CancellationToken cancellationToken = default)
    {
        var hits = await retrieval.SearchAsync(question, topK, cancellationToken).ConfigureAwait(false);
        if (hits.Count == 0)
            return new QueryResult(question, "The corpus is empty — run ingestion first.", [], null, null);

        var reply = await answerClient
            .CompleteAsync(GroundingSystemPrompt, BuildUserPrompt(question, hits), cancellationToken)
            .ConfigureAwait(false);

        var citations = hits
            .Select((hit, i) => new Citation(
                Number: i + 1,
                DocumentId: hit.Chunk.DocumentId,
                Title: hit.Chunk.DocumentTitle,
                Ordinal: hit.Chunk.Ordinal,
                Score: Math.Round(hit.Score, 4)))
            .ToList();

        return new QueryResult(question, reply.Text, citations, reply.Model, reply.Provider);
    }

    internal static string BuildUserPrompt(string question, IReadOnlyList<ScoredChunk> hits)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Source excerpts:");
        for (var i = 0; i < hits.Count; i++)
        {
            prompt.AppendLine();
            prompt.AppendLine($"[{i + 1}] From \"{hits[i].Chunk.DocumentTitle}\":");
            prompt.AppendLine(hits[i].Chunk.Text);
        }
        prompt.AppendLine();
        prompt.AppendLine($"Question: {question}");
        return prompt.ToString();
    }
}

public sealed record Citation(int Number, string DocumentId, string Title, int Ordinal, double Score);

public sealed record QueryResult(
    string Question,
    string Answer,
    IReadOnlyList<Citation> Citations,
    string? Model,
    string? Provider);
