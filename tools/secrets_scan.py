#!/usr/bin/env python3
"""Secrets gate: fail if a key-shaped string is committed.

Deliberately boring. It scans git-tracked text files for provider key shapes and
high-entropy assignments to secret-looking names. No dependencies, no service, no
network - so it runs identically in CI and on a laptop:

    python tools/secrets_scan.py

Exit 0 = clean, exit 1 = findings (the gate fails). Anything this flags is either
a real leak or a pattern that belongs in ALLOW_SUBSTRINGS below with a reason.
"""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

# Provider key shapes. Kept narrow on purpose: a gate that cries wolf gets muted,
# and a muted gate is worse than no gate.
PATTERNS: list[tuple[str, re.Pattern[str]]] = [
    ("Anthropic API key", re.compile(r"sk-ant-[A-Za-z0-9_\-]{20,}")),
    ("OpenAI API key", re.compile(r"\bsk-(?:proj-)?[A-Za-z0-9]{32,}")),
    ("AWS access key id", re.compile(r"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")),
    ("Google API key", re.compile(r"\bAIza[0-9A-Za-z_\-]{35}\b")),
    ("GitHub token", re.compile(r"\bgh[pousr]_[A-Za-z0-9]{36,}\b")),
    ("Slack token", re.compile(r"\bxox[abprs]-[A-Za-z0-9\-]{10,}")),
    ("Private key block", re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----")),
    ("Azure connection string", re.compile(r"AccountKey=[A-Za-z0-9+/=]{40,}")),
    (
        "Assigned secret literal",
        re.compile(
            r"(?i)\b(api[_-]?key|secret|password|passwd|token|connectionstring)\b"
            r"\s*[:=]\s*[\"'][^\"'\s${}<>]{16,}[\"']"
        ),
    ),
]

# Extensions that are never worth scanning (binary or generated).
SKIP_SUFFIXES = {
    ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".pdf", ".mp4", ".mp3",
    ".wav", ".zip", ".gz", ".tar", ".dll", ".exe", ".pdb", ".db", ".sqlite",
    ".woff", ".woff2", ".ttf", ".eot",
}

SKIP_PATH_PARTS = {"node_modules", "bin", "obj", ".git"}

# Substrings that make a match a known false positive. Every entry needs a reason.
ALLOW_SUBSTRINGS = [
    "OPENAI_API_KEY is not set",          # the guard clause's own message
    "dummy-key-for-a11y-only",            # CI placeholder, see .github/workflows/ci.yml
    "your-key-here",                      # docs placeholder
    "sk-xxxx",                            # docs placeholder
]

# This file necessarily contains the patterns it looks for.
SELF = Path("tools/secrets_scan.py").as_posix()


def tracked_files() -> list[Path]:
    out = subprocess.run(
        ["git", "ls-files", "-z"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout
    return [Path(p) for p in out.split("\0") if p]


def should_scan(path: Path) -> bool:
    if path.as_posix() == SELF:
        return False
    if path.suffix.lower() in SKIP_SUFFIXES:
        return False
    if any(part in SKIP_PATH_PARTS for part in path.parts):
        return False
    return True


def allowed(line: str) -> bool:
    return any(token in line for token in ALLOW_SUBSTRINGS)


def main() -> int:
    findings: list[str] = []
    scanned = 0

    for path in tracked_files():
        if not should_scan(path):
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, FileNotFoundError, OSError):
            continue  # binary or gone; not a secret we can read
        scanned += 1

        for lineno, line in enumerate(text.splitlines(), start=1):
            if allowed(line):
                continue
            for label, pattern in PATTERNS:
                match = pattern.search(line)
                if match:
                    shown = match.group(0)
                    if len(shown) > 12:
                        shown = shown[:6] + "..." + shown[-4:]
                    findings.append(f"{path.as_posix()}:{lineno}: {label} -> {shown}")
                    break

    print(f"secrets scan: {scanned} tracked text files checked")
    if findings:
        print(f"\nFAIL - {len(findings)} finding(s):\n")
        for f in findings:
            print(f"  {f}")
        print(
            "\nIf a finding is a placeholder, add its exact substring to "
            "ALLOW_SUBSTRINGS in tools/secrets_scan.py with a reason.\n"
            "If it is real: rotate the key first, then remove it from history."
        )
        return 1

    print("clean - no key-shaped strings in tracked files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
