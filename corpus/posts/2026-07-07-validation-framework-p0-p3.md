---
id: post-2026-07-07
title: "Validation framework (P0-P3)"
type: post
published: 2026-07-07
source: docs/linkedin-post-draft-validation-framework.md
---

For most of my career I added validations the same way: think of everything that could go wrong, handle it, move on.

It felt thorough, and it was over-blocking.

A mentor asked me one question that stuck:

"If a validation fails in a medication dispensing system versus an invoice generator — should they behave the same way?"

In a medication system, a wrong patient or wrong dose must stop execution completely — there is no "handle gracefully." In an invoice system, a missing optional enrichment field doesn't prevent a correct invoice from being generated. Blocking on it is just crossing it off a list.

That question pushed me toward a classification I've used since.

P0 — fatal. The system cannot proceed. Fail fast, always first.
P1 — business-critical. The system could continue technically, but the business says it shouldn't. Return a meaningful response.
P2 — non-critical. The user still gets correct behaviour. Log and continue.
P3 — informational. No immediate impact. Record it, don't fight it now.

The shift was in the question I now ask before writing any check: will this scenario actually come up in a real customer workflow — or am I just handling it to cross it off from a code POV?

When I wasn't sure, I'd check with someone closer to actual usage — usually enough to settle it.

The same question applies to rare combinations — when a customer hits a weird sequence of circumstances. Instead of adding a validation for every edge case support flags, the question became: is this a one-off? Skip it. Does it actually recur in real usage? Then add it.

The system got more flexible. Future enhancements stopped running into a wall of hard stops that were never load-bearing in the first place.

Where's the line, for you, between validating for correctness and validating just to feel thorough?
