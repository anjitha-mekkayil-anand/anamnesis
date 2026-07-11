using Anamnesis.Core;

namespace Anamnesis.Tests;

public class QueryServiceTests
{
    [Fact]
    public void BuildUserPrompt_NumbersExcerptsAndAppendsQuestion()
    {
        var chunk1 = new EmbeddedChunk(1, "doc-1", "First Post", 0, "excerpt one", [1f]);
        var chunk2 = new EmbeddedChunk(2, "doc-2", "Second Post", 3, "excerpt two", [1f]);

        var prompt = QueryService.BuildUserPrompt("what did I write?",
            [new ScoredChunk(chunk1, 0.9), new ScoredChunk(chunk2, 0.8)]);

        Assert.Contains("[1] From \"First Post\":", prompt);
        Assert.Contains("excerpt one", prompt);
        Assert.Contains("[2] From \"Second Post\":", prompt);
        Assert.Contains("Question: what did I write?", prompt);
        var questionIndex = prompt.IndexOf("Question:", StringComparison.Ordinal);
        Assert.True(prompt.IndexOf("excerpt two", StringComparison.Ordinal) < questionIndex);
    }
}
