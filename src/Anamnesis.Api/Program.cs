using Anamnesis.Core;

var builder = WebApplication.CreateBuilder(args);

var dbPath = builder.Configuration["Anamnesis:DbPath"] ?? "data/anamnesis.db";
var corpusRoot = builder.Configuration["Anamnesis:CorpusRoot"] ?? "corpus";

builder.Services.AddSingleton(new ChunkStore(dbPath));
builder.Services.AddSingleton(new Chunker());
builder.Services.AddHttpClient<IEmbeddingClient, OpenAiEmbeddingClient>(client =>
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY is not set.");
    client.BaseAddress = new Uri("https://api.openai.com/");
    client.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);
});
builder.Services.AddSingleton<IngestService>();

var app = builder.Build();

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);

app.MapGet("/", () => Results.Ok(new
{
    name = "Anamnesis",
    description = "RAG over my published writing — grounded, cited, measured.",
    endpoints = new[] { "POST /ingest", "GET /stats" }
}));

app.MapPost("/ingest", async (IngestService ingest, CancellationToken cancellationToken) =>
{
    var result = await ingest.IngestDirectoryAsync(corpusRoot, cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/stats", (ChunkStore store) =>
{
    store.EnsureCreated();
    var (documents, chunks) = store.Counts();
    return Results.Ok(new { documents, chunks });
});

app.Run();
