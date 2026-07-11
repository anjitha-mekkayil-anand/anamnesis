# Anamnesis

*ἀνάμνησις — Plato's claim that learning is recollection: the soul retrieves knowledge it already holds.*

Anamnesis is a .NET-native RAG (retrieval-augmented generation) service that answers questions over my own published writing — grounded, cited, measured. It demonstrates production-shaped RAG engineering end to end: ingestion, retrieval, multi-provider LLM routing with fallback, and an evaluation harness with real quality metrics.

## What it does

| Stage | How |
|---|---|
| **Ingest** | Paragraph-aware chunking with overlap → OpenAI `text-embedding-3-small` (batched) → SQLite (embeddings as float32 BLOBs) |
| **Query** | Embed the question → exact cosine top-k → numbered-excerpt prompt → grounded LLM answer with inline `[n]` citations |
| **Route** | Claude primary (official Anthropic SDK) → retry with exponential backoff + per-attempt timeout (Polly) → automatic failover to OpenAI. Caller cancellation is never swallowed. |
| **Measure** | Golden question set → retrieval hit-rate@k + MRR, LLM-judged answer faithfulness, latency per stage — every run appends a dated row to `evals/results.jsonl` |

## Corpus

My published LinkedIn posts and Substack letters — public text I own, exported from my personal knowledge base through a reviewed, published-only manifest (`tools/export_corpus.py`). Frontmatter carries id/title/type/date; the body is the post as published.

## Architecture

```
            ┌──────────────┐
  corpus ──▶│  Ingestion    │ chunker (paragraph-aware, overlap)
            │  pipeline     │ OpenAI embeddings (batched)
            └──────┬───────┘
                   ▼
            ┌──────────────┐
            │ SQLite store  │ chunks + float32 embedding BLOBs
            └──────┬───────┘
                   ▼
 question ─▶ embed ─▶ exact cosine top-k ─▶ prompt (numbered excerpts)
                   │
                   ▼
            ┌──────────────┐  primary : Claude  (Anthropic SDK)
            │ Failover      │  policy  : retry ×2, backoff, 45s timeout
            │ router        │  fallback: OpenAI chat completions
            └──────┬───────┘
                   ▼
        answer + [n] citations + provider/model + eval telemetry
```

## Run it

Requires the .NET 10 SDK and two environment variables:

```
OPENAI_API_KEY      embeddings (and fallback answers)
ANTHROPIC_API_KEY   primary answers
```

```bash
dotnet test                                  # unit tests, no keys needed
dotnet run --project src/Anamnesis.Api      # http://localhost:5000

# 1. Ingest the corpus (idempotent — re-runs replace per document)
curl -X POST http://localhost:5000/ingest

# 2. Ask it something
curl -X POST http://localhost:5000/query \
  -H "Content-Type: application/json" \
  -d '{"question": "What is the audit problem?", "topK": 5}'

# 3. Run the eval suite (retrieval metrics; add &answers=true for LLM-judged faithfulness)
curl -X POST "http://localhost:5000/evals/run?k=5&answers=true"

# Corpus stats
curl http://localhost:5000/stats
```

Configuration (`appsettings.json` or environment): `Anamnesis:DbPath`, `Anamnesis:CorpusRoot`, `Anamnesis:ChatModel` (default `claude-haiku-4-5`), `Anamnesis:FallbackChatModel` (default `gpt-4o-mini`).

## Evaluation

`evals/golden.json` holds question → expected-source pairs covering nearly the whole corpus. Each `/evals/run`:

- **Retrieval**: hit-rate@k (did the expected document appear in top-k) and MRR (how high)
- **Answers** (optional): LLM-as-judge faithfulness — every claim must be supported by the retrieved excerpts; correctly saying "the excerpts don't cover this" counts as faithful
- **Ops**: retrieval and answer latency per item

Results append to `evals/results.jsonl`, so tuning changes (chunk size, k, prompts, models) show up as a trend across dated rows.

| Run | k | Hit-rate@k | MRR | Faithful | Notes |
|---|---|---|---|---|---|
| 2026-07-11 | 5 | **0.95** | **0.90** | **1.00** | Baseline — 21 docs / 29 chunks. One retrieval miss (abstractly-phrased question vs. metaphor-heavy letter); the answer for it correctly declined rather than inventing, so faithfulness held at 100%. Answers served by `claude-haiku-4-5` (primary path); retrieval ~550ms, answers ~3.5s avg. |

## Honest scale notes

At this corpus size, exact brute-force cosine over in-memory vectors beats any ANN index — so that's what it does, on purpose. The swap path is clean: `RetrievalService` is the only place that scores, and `ChunkStore` is the only persistence surface. When `LoadAll()` becomes the bottleneck (≈10⁵+ chunks), swap SQLite for pgvector/Qdrant and top-k for ANN — the interfaces don't change. Knowing *when* to make that swap is the point of writing it this way.

Other deliberate choices: embeddings come from OpenAI because Anthropic doesn't offer an embeddings API; the chat model is configurable per provider; the eval judge reuses the same routed client, so a provider outage degrades gracefully all the way down the stack.

## Tests

Unit tests cover the chunker (boundaries, overlap, ordinals), vector math (edge cases incl. zero vectors), frontmatter parsing, SQLite round-trip and re-ingest idempotency, retrieval ranking, prompt assembly, router failover behavior (healthy / retry-then-failover / both-fail / cancellation), and eval metrics (rank, MRR, judge-verdict parsing). All run without API keys.
