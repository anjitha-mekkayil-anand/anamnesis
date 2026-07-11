---
id: post-2026-06-23
title: "Hyrum's Law"
type: post
published: 2026-06-23
source: docs/linkedin-post-draft-hyrums-law.md
---

Ten years on the same platform teaches you things that don't appear in any documentation.

One of them: "internal implementation detail" is not really a thing once you have enough callers.

I'd seen it happen repeatedly — a team changes something that wasn't in any public spec, something buried three layers down, and a downstream service breaks anyway. No one had told them. No one could have.

Hyrum Wright gave it a name: with enough callers, every observable behaviour of your system becomes a dependency — including the bugs.

The contract isn't what you wrote in the docs. It's whatever the system currently does.

This changes how you think about breaking changes. A response field moving from camelCase to PascalCase. An error message rewording. A timing change that makes a call return 20ms faster. A null that's been null long enough that someone started treating it as a deliberate signal.

None of these are in any spec, and all of them are load-bearing somewhere.

The longer a system has been running, the more true this becomes. Assume everything observable is a contract — because to someone, it already is.

AI tooling with codebase access has made the internal half more tractable — grep every caller before you change a shared signature, trace what each one depends on — but the external half still bites.

What behaviour in your codebase are callers depending on that was never in any contract?
