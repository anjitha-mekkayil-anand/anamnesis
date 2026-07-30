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
// ChatMode "local" routes answers to an OpenAI-compatible local server (e.g.
// Ollama) as primary, with the cloud chain as fallback. Embeddings stay on
// OpenAI either way — local embeddings are the documented next step.
var chatMode = builder.Configuration["Anamnesis:ChatMode"] ?? "cloud";
var localChatBaseUrl = builder.Configuration["Anamnesis:LocalChatBaseUrl"] ?? "http://localhost:11434/";
var localChatModel = builder.Configuration["Anamnesis:LocalChatModel"] ?? "llama3.2";

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
if (chatMode == "local")
{
    builder.Services.AddHttpClient("local-chat", client =>
    {
        client.BaseAddress = new Uri(localChatBaseUrl);  // no auth — local server
    });
}
builder.Services.AddSingleton(new AnthropicAnswerClient(new AnthropicClient(), chatModel));
builder.Services.AddSingleton<IAnswerClient>(sp =>
{
    var fallbackModel = builder.Configuration["Anamnesis:FallbackChatModel"] ?? "gpt-4o-mini";
    var cloudChain = new FailoverAnswerClient(
        sp.GetRequiredService<AnthropicAnswerClient>(),
        new OpenAiAnswerClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("openai-chat"), fallbackModel));
    if (chatMode != "local")
        return cloudChain;
    var local = new OpenAiAnswerClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("local-chat"), localChatModel, providerName: "local");
    return new FailoverAnswerClient(local, cloudChain);
});
builder.Services.AddSingleton<QueryService>();
builder.Services.AddSingleton(sp => new StreamingQueryService(
    sp.GetRequiredService<RetrievalService>(),
    // Streaming rides the Anthropic SDK; in local mode answers should stay
    // local, so the stream endpoint degrades to the (local-first) full path.
    chatMode == "local" ? null : sp.GetRequiredService<AnthropicAnswerClient>(),
    sp.GetRequiredService<IAnswerClient>()));
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
        ? new[] { "GET /", "POST /query", "POST /query/stream", "GET /stats" }
        : new[] { "GET /", "POST /ingest", "GET /stats", "POST /query", "POST /query/stream", "POST /evals/run" }
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

// Server-Sent Events: citations arrive first (retrieval is done before the
// answer starts), then token deltas, then a terminal `done`. If streaming
// fails before the first token, the same request silently degrades to the
// non-streaming failover chain and the answer arrives as one delta.
app.MapPost("/query/stream", async (HttpContext context, QueryRequest request, StreamingQueryService streamingQuery, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
        return Results.BadRequest(new { error = "Question is required." });
    if (request.Question.Length > 300)
        return Results.BadRequest(new { error = "Question is limited to 300 characters." });

    var topK = Math.Clamp(request.TopK ?? 5, 1, 8);

    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
    async Task WriteEventAsync(string eventName, object payload)
    {
        await context.Response.WriteAsync(
            $"event: {eventName}\ndata: {System.Text.Json.JsonSerializer.Serialize(payload, json)}\n\n",
            cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    await foreach (var streamEvent in streamingQuery.AskStreamingAsync(request.Question, topK, cancellationToken))
    {
        switch (streamEvent)
        {
            case CitationsStreamEvent citations:
                await WriteEventAsync("citations", citations.Citations);
                break;
            case DeltaStreamEvent delta:
                await WriteEventAsync("delta", new { text = delta.Text });
                break;
            case ErrorStreamEvent error:
                await WriteEventAsync("error", new { message = error.Message });
                break;
            case DoneStreamEvent done:
                await WriteEventAsync("done", new { model = done.Model, provider = done.Provider, streamed = done.Streamed });
                break;
        }
    }

    return Results.Empty;
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
