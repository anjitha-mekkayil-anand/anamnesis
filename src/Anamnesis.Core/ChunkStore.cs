using Microsoft.Data.Sqlite;

namespace Anamnesis.Core;

/// <summary>
/// SQLite-backed store for documents and embedded chunks. Embeddings are
/// float32 little-endian BLOBs. Search loads all vectors and scores them
/// exactly — at this corpus size brute-force beats any ANN index; the swap
/// path (pgvector/Qdrant) starts the day this class is the bottleneck.
/// </summary>
public sealed class ChunkStore(string databasePath)
{
    private string ConnectionString => $"Data Source={databasePath}";

    public void EnsureCreated()
    {
        using var connection = Open();
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS documents (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                type TEXT NOT NULL,
                published TEXT NOT NULL,
                source_path TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL,
                text TEXT NOT NULL,
                embedding BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_chunks_document ON chunks(document_id);
            """);
    }

    public void ReplaceDocument(CorpusDocument document, IReadOnlyList<Chunk> chunks, float[][] embeddings)
    {
        if (chunks.Count != embeddings.Length)
            throw new ArgumentException($"Chunk/embedding count mismatch: {chunks.Count} vs {embeddings.Length}");

        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        Execute(connection, "DELETE FROM chunks WHERE document_id = $id",
            ("$id", document.Id));
        Execute(connection, """
            INSERT INTO documents (id, title, type, published, source_path)
            VALUES ($id, $title, $type, $published, $source)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title, type = excluded.type,
                published = excluded.published, source_path = excluded.source_path
            """,
            ("$id", document.Id), ("$title", document.Title), ("$type", document.Type),
            ("$published", document.Published), ("$source", document.SourcePath));

        for (var i = 0; i < chunks.Count; i++)
        {
            Execute(connection, """
                INSERT INTO chunks (document_id, ordinal, text, embedding)
                VALUES ($doc, $ordinal, $text, $embedding)
                """,
                ("$doc", document.Id), ("$ordinal", chunks[i].Ordinal),
                ("$text", chunks[i].Text), ("$embedding", ToBlob(embeddings[i])));
        }

        transaction.Commit();
    }

    public IReadOnlyList<EmbeddedChunk> LoadAll()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.document_id, d.title, c.ordinal, c.text, c.embedding
            FROM chunks c JOIN documents d ON d.id = c.document_id
            ORDER BY c.document_id, c.ordinal
            """;

        var results = new List<EmbeddedChunk>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new EmbeddedChunk(
                ChunkId: reader.GetInt64(0),
                DocumentId: reader.GetString(1),
                DocumentTitle: reader.GetString(2),
                Ordinal: reader.GetInt32(3),
                Text: reader.GetString(4),
                Embedding: FromBlob((byte[])reader.GetValue(5))));
        }

        return results;
    }

    public (int Documents, int Chunks) Counts()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM documents), (SELECT COUNT(*) FROM chunks)";
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    internal static byte[] ToBlob(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    internal static float[] FromBlob(byte[] blob)
    {
        var vector = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, vector, 0, blob.Length);
        return vector;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys = ON");
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
