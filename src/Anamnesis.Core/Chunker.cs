namespace Anamnesis.Core;

/// <summary>
/// Paragraph-aware chunker: packs whole paragraphs up to a target size,
/// carrying the last paragraph of each chunk into the next as overlap so
/// retrieval doesn't lose context at chunk boundaries.
/// </summary>
public sealed class Chunker(int targetChars = 1800, int minChars = 200)
{
    public IReadOnlyList<Chunk> ChunkDocument(CorpusDocument document)
    {
        var paragraphs = document.Body
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToList();

        var chunks = new List<Chunk>();
        var current = new List<string>();
        var currentLength = 0;

        foreach (var paragraph in paragraphs)
        {
            if (currentLength > 0 && currentLength + paragraph.Length > targetChars)
            {
                chunks.Add(new Chunk(document.Id, chunks.Count, string.Join("\n\n", current)));
                var overlap = current[^1];
                current = overlap.Length < targetChars / 2 ? [overlap] : [];
                currentLength = current.Sum(p => p.Length);
            }

            current.Add(paragraph);
            currentLength += paragraph.Length;
        }

        if (current.Count > 0)
        {
            var text = string.Join("\n\n", current);
            // A trailing fragment that is only overlap carried from the previous
            // chunk adds nothing — require new content beyond minChars.
            if (chunks.Count == 0 || text.Length >= minChars)
                chunks.Add(new Chunk(document.Id, chunks.Count, text));
        }

        return chunks;
    }
}
