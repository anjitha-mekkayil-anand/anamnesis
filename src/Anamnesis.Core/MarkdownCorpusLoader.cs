namespace Anamnesis.Core;

public static class MarkdownCorpusLoader
{
    public static IReadOnlyList<CorpusDocument> LoadDirectory(string corpusRoot)
    {
        if (!Directory.Exists(corpusRoot))
            throw new DirectoryNotFoundException($"Corpus directory not found: {corpusRoot}");

        return Directory.EnumerateFiles(corpusRoot, "*.md", SearchOption.AllDirectories)
            .Select(Load)
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static CorpusDocument Load(string path)
    {
        var text = File.ReadAllText(path);
        var (frontmatter, body) = SplitFrontmatter(text);

        return new CorpusDocument(
            Id: frontmatter.GetValueOrDefault("id", Path.GetFileNameWithoutExtension(path)),
            Title: frontmatter.GetValueOrDefault("title", Path.GetFileNameWithoutExtension(path)),
            Type: frontmatter.GetValueOrDefault("type", "unknown"),
            Published: frontmatter.GetValueOrDefault("published", ""),
            SourcePath: frontmatter.GetValueOrDefault("source", path),
            Body: body.Trim());
    }

    internal static (Dictionary<string, string> Frontmatter, string Body) SplitFrontmatter(string text)
    {
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalized = text.Replace("\r\n", "\n");

        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            return (frontmatter, normalized);

        var end = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0)
            return (frontmatter, normalized);

        foreach (var line in normalized[4..end].Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim().Trim('"');
            if (key.Length > 0) frontmatter[key] = value;
        }

        var bodyStart = normalized.IndexOf('\n', end + 4);
        return (frontmatter, bodyStart < 0 ? "" : normalized[(bodyStart + 1)..]);
    }
}
