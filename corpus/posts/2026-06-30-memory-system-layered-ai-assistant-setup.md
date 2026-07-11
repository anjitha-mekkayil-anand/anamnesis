---
id: post-2026-06-30
title: "Memory system — layered AI assistant setup"
type: post
published: 2026-06-30
source: docs/linkedin-post-draft-memory-system.md
---

I stopped re-explaining myself to AI coding assistants every session. Here's what I built instead.

By default, AI assistants have no memory between sessions. Every time you open a new chat, it's a blank slate. You re-explain your tech stack, your preferences, your conventions.

So I built a layered memory system:

- Global layer — who I am, how I like to work, lessons learned across every project
- Per-repo layer — recurring patterns, architectural decisions, gotchas specific to that codebase
- Per-session layer — current ticket, last known state, exactly where to pick up next

Each layer is just markdown files, loaded automatically and kept current.

The result: sessions start warm, not cold. The assistant already knows I don't want trailing summaries, that I work across C# and JS, that I prefer the narrowest possible fix first.

---
This week I ran an analysis of my own sessions. It flagged something I'd missed: I'd built a 12-item todo list in a morning brain dump — and never saved it to a file. Session ended. List gone.

Claude dug through buried terminal transcripts to recover it — digital archaeology, and it worked.

Then I built a session-close hook. Now every time I exit, a timestamp is written automatically. A `/close` command runs the save-and-update ritual. The system diagnosed its own gap and fixed it.

---
Treat every session as an opportunity to make the next one smarter. The compounding is real — but only if you build it on purpose and return to it every session.

What does your next session start with?
