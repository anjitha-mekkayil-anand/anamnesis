using System.Threading.RateLimiting;
using Anamnesis.Core;
using Anthropic;

var builder = WebApplication.CreateBuilder(args);

// `dotnet run` sets the working directory to the project folder — anchor
// relative paths at the repo root (first ancestor containing corpus/ or .git).
var repoRoot = FindRepoRoot();
string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.Combine(repoRoot, path);

var dbPath = Resolve(builder.Configuration["Anamnesis:DbPath"] ?? "data/anamnesis.db");
var corpusRoot = Resolve(builder.Configuration["Anamnesis:CorpusRoot"] ?? "corpus");
var chatModel = builder.Configuration["Anamnesis:ChatModel"] ?? "claude-haiku-4-5";
// Public-demo hardening: read-only mode exposes only the UI, /query and /stats.
var readOnlyMode = builder.Configuration.GetValue("Anamnesis:ReadOnlyMode", false);

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

// Every LLM call spends real money — throttle per client IP and cap the whole
// day globally so a public demo cannot run up the bill.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                })),
        PartitionedRateLimiter.Create<HttpContext, string>(_ =>
            RateLimitPartition.GetFixedWindowLimiter(
                "global-daily",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 500,
                    Window = TimeSpan.FromDays(1),
                    QueueLimit = 0,
                })));
});

var app = builder.Build();

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);

app.UseDefaultFiles();   // serves wwwroot/index.html at /
app.UseStaticFiles();
app.UseRateLimiter();    // static assets above are unthrottled; API below is

app.MapGet("/about", () => Results.Ok(new
{
    name = "Anamnesis",
    description = "RAG over my published writing — grounded, cited, measured.",
    readOnly = readOnlyMode,
    endpoints = readOnlyMode
        ? new[] { "GET /", "POST /query", "GET /stats" }
        : new[] { "GET /", "POST /ingest", "GET /stats", "POST /query", "POST /evals/run" }
}));

app.MapPost("/query", async (QueryRequest request, QueryService query, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
        return Results.BadRequest(new { error = "Question is required." });
    if (request.Question.Length > 300)
        return Results.BadRequest(new { error = "Question is limited to 300 characters." });

    var topK = Math.Clamp(request.TopK ?? 5, 1, 8);
    var result = await query.AskAsync(request.Question, topK, cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/stats", (ChunkStore store) =>
{
    store.EnsureCreated();
    var (documents, chunks) = store.Counts();
    return Results.Ok(new { documents, chunks });
});

if (!readOnlyMode)
{
    app.MapPost("/ingest", async (IngestService ingest, CancellationToken cancellationToken) =>
    {
        var result = await ingest.IngestDirectoryAsync(corpusRoot, cancellationToken);
        return Results.Ok(result);
    });

    app.MapPost("/evals/run", async (EvalService evals, int? k, bool? answers, CancellationToken cancellationToken) =>
    {
        var goldenPath = Resolve(app.Configuration["Anamnesis:GoldenPath"] ?? "evals/golden.json");
        var resultsPath = Resolve(app.Configuration["Anamnesis:ResultsPath"] ?? "evals/results.jsonl");
        var summary = await evals.RunAsync(goldenPath, resultsPath, k ?? 5, answers ?? false, cancellationToken);
        return Results.Ok(summary);
    });
}

app.Run();

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "corpus")) ||
            Directory.Exists(Path.Combine(dir.FullName, ".git")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();
}

internal sealed record QueryRequest(string Question, int? TopK);
