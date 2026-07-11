"""Export published writing from the source vault into corpus/.

Usage: python tools/export_corpus.py <vault-root>

Only items listed in ITEMS are exported (mirrors the vault-side corpus
manifest, CONFIRMED rows only). Extraction strips draft frontmatter,
editorial notes, and scheduling metadata, keeping the published body text.
All I/O is explicit UTF-8.
"""

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent

# (corpus_id, title, type, published, vault-relative source, section)
# section: None = whole file; otherwise the "## POST X" heading prefix.
ITEMS = [
    ("post-2026-05-06", "The Sleepless 3 Nights", "post", "2026-05-06",
     "docs/linkedin-posts-co-intelligence-all-drafts.md", "POST 1"),
    ("post-2026-05-12", "Bags of water convincing well-organized sand", "post", "2026-05-12",
     "docs/linkedin-post-2026-05-12-bags-of-water.md", None),
    ("post-2026-05-19", "Eucatastrophe", "post", "2026-05-19",
     "docs/linkedin-posts-co-intelligence-all-drafts.md", "POST 15"),
    ("post-2026-05-21", "Noesis Letters — newsletter announcement", "post", "2026-05-21",
     "docs/linkedin-posts-thursday-drafts.md", "POST T1"),
    ("post-2026-05-26", "AI History Thread — Poe to Shannon to Turing", "post", "2026-05-26",
     "docs/linkedin-posts-co-intelligence-all-drafts.md", "POST 2"),
    ("post-2026-06-04", "The 20-year thread", "post", "2026-06-04",
     "docs/linkedin-posts-thursday-drafts.md", "POST T2"),
    ("post-2026-06-09", "Centaur vs Cyborg", "post", "2026-06-09",
     "docs/linkedin-posts-co-intelligence-all-drafts.md", "POST 5"),
    ("post-2026-06-16", "The AI epilogue test", "post", "2026-06-16",
     "docs/linkedin-posts-co-intelligence-all-drafts.md", "POST 19"),
    ("post-2026-06-18", "Mathesis hackathon submission", "post", "2026-06-18",
     "docs/linkedin-post-draft.md", None),
    ("post-2026-06-23", "Hyrum's Law", "post", "2026-06-23",
     "docs/linkedin-post-draft-hyrums-law.md", None),
    ("post-2026-06-25", "Parallel invention — Pelle Wessman", "post", "2026-06-25",
     "docs/linkedin-post-draft-pelle-wessman.md", None),
    ("post-2026-06-30", "Memory system — layered AI assistant setup", "post", "2026-06-30",
     "docs/linkedin-post-draft-memory-system.md", None),
    ("post-2026-07-06", "Aletheia launch", "post", "2026-07-06",
     "docs/linkedin-post-draft-aletheia-launch.md", None),
    ("post-2026-07-07", "Validation framework (P0-P3)", "post", "2026-07-07",
     "docs/linkedin-post-draft-validation-framework.md", None),
    ("post-2026-07-09", "The CCA repost story", "post", "2026-07-09",
     "docs/linkedin-post-draft-cca-repost-story.md", None),
    ("letter-01", "Why learning gets harder as AI gets better", "letter", "2026-05-09",
     "docs/substack-posts-noesisletters.md", "POST 1"),
    ("letter-02", "The audit problem", "letter", "2026-05-22",
     "docs/substack-posts-noesisletters.md", "POST 2"),
    ("letter-03", "The other thing Noesis gave me", "letter", "2026-06-02",
     "docs/2026-06-02-substack-noesis-the-other-thing.md", None),
    ("letter-04", "The project I didn't know I was building", "letter", "2026-06-04",
     "docs/Pronoia/2026-06-04-substack-pronoia-draft.md", None),
    ("letter-05", "The Response That Came Too Late", "letter", "2026-06-16",
     "docs/substack-drafts/2026-06-10-tubelight-processing-style.md", None),
    ("letter-06", "The line behind the dropdown", "letter", "2026-07-04",
     "docs/substack-drafts/2026-06-13-mathesis-pivot.md", None),
]

NOTE_LINE = re.compile(r"^(\*.*\*\s*$|\*\*|- |> |#)")
TRAILING_SECTION = re.compile(r"^## (Notes|Facts locked)", re.MULTILINE)


def strip_frontmatter(text):
    if text.startswith("---"):
        end = text.find("\n---", 3)
        if end != -1:
            return text[end + 4:]
    return text


def extract_section(text, prefix):
    pattern = re.compile(rf"^## {re.escape(prefix)}(?:\s|—|-).*$", re.MULTILINE)
    m = pattern.search(text)
    if not m:
        raise ValueError(f"section '{prefix}' not found")
    start = m.end()
    nxt = re.compile(r"^## POST ", re.MULTILINE).search(text, start)
    return text[start:nxt.start() if nxt else len(text)]


def clean(body):
    body = re.sub(r"<!--.*?-->", "", body, flags=re.DOTALL)
    # drop trailing editorial sections
    m = TRAILING_SECTION.search(body)
    if m:
        body = body[:m.start()]
    lines = body.split("\n")
    # drop the H1 title line if present
    for i, line in enumerate(lines):
        if line.strip():
            if line.startswith("# "):
                lines = lines[i + 1:]
            break
    # leading junk: italic/blockquote/checkbox lines up to and incl. a '---'
    i = 0
    while i < len(lines) and (not lines[i].strip()
                              or re.match(r"^(\*.*\*\s*$|> )", lines[i])):
        i += 1
    if i < len(lines) and lines[i].strip() == "---":
        i += 1
        lines = lines[i:]
    # trailing: a final '---' followed only by note lines
    while True:
        idx = None
        for j in range(len(lines) - 1, -1, -1):
            if lines[j].strip() == "---":
                idx = j
                break
        if idx is None:
            break
        tail = [l for l in lines[idx + 1:] if l.strip()]
        if all(NOTE_LINE.match(l) for l in tail):
            lines = lines[:idx]
            if tail:
                continue  # more note blocks may sit above another '---'
        break
    text = "\n".join(lines).strip()
    return re.sub(r"\n{3,}", "\n\n", text) + "\n"


def slug(title):
    s = re.sub(r"[^a-z0-9]+", "-", title.lower())
    return s.strip("-")[:60]


def main():
    if len(sys.argv) != 2:
        sys.exit("usage: python tools/export_corpus.py <vault-root>")
    vault = Path(sys.argv[1])
    written = []
    for cid, title, ctype, published, source, section in ITEMS:
        raw = (vault / source).read_text(encoding="utf-8")
        text = extract_section(raw, section) if section else strip_frontmatter(raw)
        body = clean(text)
        if not body.strip():
            sys.exit(f"EMPTY BODY: {cid}")
        folder = REPO_ROOT / "corpus" / ("posts" if ctype == "post" else "letters")
        folder.mkdir(parents=True, exist_ok=True)
        out = folder / f"{published}-{slug(title)}.md"
        front = (f"---\nid: {cid}\ntitle: \"{title}\"\ntype: {ctype}\n"
                 f"published: {published}\nsource: {source}\n---\n\n")
        out.write_text(front + body, encoding="utf-8", newline="\n")
        written.append(out.relative_to(REPO_ROOT))
    print(f"exported {len(written)} files")
    for w in written:
        print(f"  {w}")


if __name__ == "__main__":
    main()
