---
id: post-2026-06-18
title: "Mathesis hackathon submission"
type: post
published: 2026-06-18
source: docs/linkedin-post-draft.md
---

*Post after hackathon close (Jun 14). Run `/next-post` before posting.*
*Angle: guardrail-by-design — the tool surface as the safety layer. Distinct from the
hidden-requirement post (on hold until results) and the Jun 4 "20-year thread" post.*
*Rewritten Jun 12 — previous version was the pre-pivot Pronoia draft.*

---

The hackathon brief asked for human oversight in important decisions. The usual answer is a confirmation dialog.

I went with a different one: the approve tool doesn't exist.

Mathesis is my entry to Microsoft's Agents League hackathon — a multi-agent system that manages a team's certification prep. Four agents on Azure AI Foundry: one curates learning paths with citations, one builds study plans around real meeting load and focus hours, one assesses readiness and gives an honest verdict, one reports to the manager. Seven MCP tools between them, and each agent gets only the subset its role needs.

Nowhere in those seven tools is approve_plan. Every plan the agents produce lands in a queue as pending. It activates one way: a manager clicks a button on a dashboard. The agents cannot approve their own work — the capability was never built. The learner gets the same respect: nobody is assessed until they say they're ready.

The part I ended up caring about most was proving it behaves. A deterministic readiness score decides who even needs an agent pass — no LLM in that path, and 17 unit tests covering it, runnable with zero credentials. Then an evaluation harness runs the full pipeline repeatedly and scores consistency: readiness bands stable for every learner, 100% agreement on next-step direction across 8 runs — "The agent is reliable" is a claim; 8/8 is the measurement.

Built on .NET 10, Azure AI Foundry (Mistral-small), MCP stdio transport, Azure AI Search grounding with cited retrieval.

Project link in the comments.

If an agent's restraint lives in its prompt, you have a request. If it lives in the tool surface, you have a guarantee. Where do your guardrails live?
