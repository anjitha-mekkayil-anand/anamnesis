---
id: letter-06
title: "The line behind the dropdown"
type: letter
published: 2026-07-04
source: docs/substack-drafts/2026-06-13-mathesis-pivot.md
---

At 11:59 on a Tuesday night, the organizer confirmed what I'd been worried about for two hours.

The scenario was mandatory. I'd missed it — behind a panel, behind a card, behind a "Show more." Enterprise learning system or D&D game. My project was a predictive maintenance system for industrial machines. Sensors, drift detection, AI agents diagnosing faults before they happened. A week of building, a grounded knowledge base, a working multi-agent pipeline — none of it fit.

I had a week until the deadline.

---

There's a version of this I've lived before. Where one missed line becomes "I'm not careful enough" becomes "I'm not good at this" becomes three days of silence, the kind where nothing gets done but everything hurts.

I know that version well — I grew up in it.

Instead, I opened a new repo at midnight.

---

Mathesis is the Greek word for the act of learning — Pronoia's sibling. Pronoia watches outward (machines, sensors, faults forming in the dark before anyone notices). Mathesis watches inward: where are the gaps in what a person knows, and what does it take to close them?

I stripped the sensor data. I kept almost everything else.

The MCP server. The agent loop. The grounded knowledge retrieval — the layer that fetches real procedures and cites the source before the agent reasons over them. The deterministic pre-filter that decides, without any AI involvement, whether a person needs the full pipeline or can skip straight to the exam. The fail-open design that degrades gracefully when a service is unavailable.

All of it was specific to the problem of reasoning over a gap.

Four agents this time: a learning curator, a study plan generator, an assessor, a manager insights reporter. The spec suggested one human approval gate. I built two: the learner confirms readiness before assessment begins, the manager approves every study plan before it activates. The AI still only reasons. The human still decides. Two gates instead of one is what that costs in practice.

Built in under 19 hours with Claude CLI, Fable 5, Azure AI Foundry. By morning the pipeline was live — Ready learners fast-tracking to the assessor, borderline learners moving through the full chain, study plans sitting in the manager approval queue, cited knowledge grounding every question the assessment agent asked.

---

Pronoia didn't die. It became more itself — a personal project, free of the brief, exactly what I always wanted to build. The roadmap is intact.

What I'm keeping from that night isn't the speed of the build. It's the hour before it, when I noticed what I was doing.

I noticed I was solving the problem instead of collapsing into it.

That used to be the part I'd get stuck on.

---

The judging closed at the end of June. No email came. The winners list, if I want it, is available by request sometime around August. Mathesis sits in a public repo with seventeen passing tests either way.
