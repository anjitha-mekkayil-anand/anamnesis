using Anamnesis.Core;
using Anthropic;

var builder = WebApplication.CreateBuilder(args);

var dbPath = builder.Configuration["Anamnesis:DbPath"] ?? "data/anamnesis.db";
var corpusRoot = builder.Configuration["Anamnesis:CorpusRoot"] ?? "corpus";
var chatModel = builder.Configuration["Anamnesis:ChatModel"] ?? "claude-haiku-4-5";

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
builder.Services.AddSingleton<RetrievalService>();
builder.Services.AddHttpClient("openai-chat", client =>
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY is not set.");
    client.BaseAddress = new Uri("https://api.openai.com/");
    client.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);
});
builder.Services.AddSingleton<IAnswerClient>(sp =>
{
    var fallbackModel = builder.Configuration["Anamnesis:FallbackChatModel"] ?? "gpt-4o-mini";
    var primary = new AnthropicAnswerClient(new AnthropicClient(), chatModel);
    var fallback = new OpenAiAnswerClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("openai-chat"), fallbackModel);
    return new FailoverAnswerClient(primary, fallback);
});
builder.Services.AddSingleton<QueryService>();
builder.Services.AddSingleton<EvalService>();

var app = builder.Build();

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);

app.MapGet("/", () => Results.Ok(new
{
    name = "Anamnesis",
    description = "RAG over my published writing — grounded, cited, measured.",
    endpoints = new[] { "POST /ingest", "GET /stats", "POST /query", "POST /evals/run" }
}));

app.MapPost("/ingest", async (IngestService ingest, CancellationToken cancellationToken) =>
{
    var result = await ingest.IngestDirectoryAsync(corpusRoot, cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/query", async (QueryRequest request, QueryService query, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
        return Results.BadRequest(new { error = "Question is required." });

    var result = await query.AskAsync(request.Question, request.TopK ?? 5, cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/evals/run", async (EvalService evals, int? k, bool? answers, CancellationToken cancellationToken) =>
{
    var goldenPath = app.Configuration["Anamnesis:GoldenPath"] ?? "evals/golden.json";
    var resultsPath = app.Configuration["Anamnesis:ResultsPath"] ?? "evals/results.jsonl";
    var summary = await evals.RunAsync(goldenPath, resultsPath, k ?? 5, answers ?? false, cancellationToken);
    return Results.Ok(summary);
});

app.MapGet("/stats", (ChunkStore store) =>
{
    store.EnsureCreated();
    var (documents, chunks) = store.Counts();
    return Results.Ok(new { documents, chunks });
});

app.Run();

internal sealed record QueryRequest(string Question, int? TopK);
