# Anamnesis

*ἀνάμνησις — Plato's claim that learning is recollection: the soul retrieves knowledge it already holds.*

Anamnesis is a .NET-native RAG (retrieval-augmented generation) service that answers questions over my own published writing — grounded, cited, measured. It exists to demonstrate production-shaped RAG engineering end to end: ingestion, retrieval, multi-provider LLM routing with fallback, and an evaluation harness with real quality metrics.

## What it does

- **Ingest** — chunks documents (heading/paragraph-aware, with overlap), generates embeddings, stores them in SQLite.
- **Query** — embeds the question, runs top-k vector search, and answers with an LLM grounded in the retrieved chunks, returning citations.
- **Route** — primary LLM provider with automatic fallback on error/timeout/rate-limit (resilience policies via Polly).
- **Measure** — a golden question set with retrieval hit-rate@k, MRR, LLM-judged faithfulness, and latency/cost per query, logged per run so tuning changes show up as a trend.

## Corpus

My published LinkedIn posts and Noesis Letters (Substack) — public text I own. Mixed registers (technical prose and poetry) make retrieval quality a real problem rather than a toy one.

## Honest scale notes

At this corpus size, exact brute-force cosine search over in-memory vectors beats any ANN index — so that's what it uses, with SQLite as the persistent store. The swap path (vector DB / ANN) is documented where the search lives; knowing *when* to swap is the point.

## Status

Under construction — phase plan and architecture decisions live in the build log. This section will be replaced by run instructions and the eval trend table.
