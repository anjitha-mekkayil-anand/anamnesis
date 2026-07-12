# Anamnesis demo video — script

Target: ~60–70s, narrated (edge-tts, en-US-AriaNeural), AI narration disclosed on publish.

| # | Scene (clip) | Visual | Narration |
|---|---|---|---|
| 1 | `1-intro` | Landing page, idle — title, tagline, eval numbers in the header | "Anamnesis answers questions about my published writing — a retrieval-augmented pipeline I built in .NET." |
| 2 | `2-ask` | Types "What is the audit problem?", submits, answer renders with citations; scroll to citations | "Ask it something. The question is embedded, matched against chunked posts by cosine similarity, and Claude answers from the retrieved excerpts only — with inline citations." Then: "Every answer shows its provider and latency. A resilience router retries with backoff and fails over to OpenAI automatically." |
| 3 | `3-declines` | Types an uncovered question ("What did I write about cricket?"); the answer declines. Caption bar underlines the point | "And when the sources don't cover a question, it declines instead of inventing — that guard held at one hundred percent faithfulness on the eval baseline." |
| 4 | `4-outro` | Outro card: baseline metrics, stack line, repo URL | "Quality is measured, not claimed — a twenty-question golden set scores retrieval and grounding on every run. Source, evals, and architecture on GitHub." |

Rules honored: overlap check before mixing; listen end-to-end before delivering; pipeline frozen once published.
