using Anamnesis.Core;

namespace Anamnesis.Tests;

public class CorpusLoaderTests
{
    [Fact]
    public void SplitFrontmatter_ParsesKeysAndBody()
    {
        var (frontmatter, body) = MarkdownCorpusLoader.SplitFrontmatter(
            "---\nid: post-1\ntitle: \"Hello — World\"\ntype: post\n---\n\nBody text here.");

        Assert.Equal("post-1", frontmatter["id"]);
        Assert.Equal("Hello — World", frontmatter["title"]);
        Assert.Equal("post", frontmatter["type"]);
        Assert.Equal("Body text here.", body.Trim());
    }

    [Fact]
    public void SplitFrontmatter_NoFrontmatter_ReturnsWholeBody()
    {
        var (frontmatter, body) = MarkdownCorpusLoader.SplitFrontmatter("Just text.");

        Assert.Empty(frontmatter);
        Assert.Equal("Just text.", body);
    }

    [Fact]
    public void SplitFrontmatter_ValueContainingColon_KeepsFullValue()
    {
        var (frontmatter, _) = MarkdownCorpusLoader.SplitFrontmatter(
            "---\nsource: docs/draft-bank/file.md#section: one\n---\nx");

        Assert.Equal("docs/draft-bank/file.md#section: one", frontmatter["source"]);
    }
}
