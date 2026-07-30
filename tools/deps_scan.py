#!/usr/bin/env python3
"""Dependency gate: fail if any NuGet package (direct or transitive) has a known advisory.

    python tools/deps_scan.py

Wraps `dotnet list package --vulnerable --include-transitive --format json`, because
that command exits 0 whether or not it finds anything - so on its own it cannot gate
anything. Parsing the JSON rather than grepping prose also means a wording change in
the CLI can't silently turn this gate off.

Exit 0 = no advisories, 1 = advisories found, 2 = the check itself could not run
(which is also a failure: an unrunnable gate must never look like a pass).
"""

from __future__ import annotations

import json
import subprocess
import sys

SOLUTION = "Anamnesis.slnx"
SEVERITY_ORDER = ["low", "moderate", "high", "critical"]


def run_list() -> dict:
    proc = subprocess.run(
        [
            "dotnet", "list", SOLUTION, "package",
            "--vulnerable", "--include-transitive", "--format", "json",
        ],
        capture_output=True,
        text=True,
    )
    if proc.returncode != 0:
        print("dotnet list failed:", file=sys.stderr)
        print(proc.stdout, file=sys.stderr)
        print(proc.stderr, file=sys.stderr)
        sys.exit(2)

    # The command prints restore chatter before the JSON document.
    start = proc.stdout.find("{")
    if start == -1:
        print("No JSON found in dotnet list output:", file=sys.stderr)
        print(proc.stdout, file=sys.stderr)
        sys.exit(2)

    try:
        return json.loads(proc.stdout[start:])
    except json.JSONDecodeError as exc:
        print(f"Could not parse dotnet list JSON: {exc}", file=sys.stderr)
        sys.exit(2)


def main() -> int:
    doc = run_list()
    projects = doc.get("projects", [])
    if not projects:
        print("No projects reported by dotnet list - refusing to report a pass.", file=sys.stderr)
        return 2

    findings: list[tuple[str, str, str, str, str]] = []

    for project in projects:
        name = project.get("path", "<unknown>").replace("\\", "/").rsplit("/", 1)[-1]
        # A clean project has no "frameworks" key at all.
        for framework in project.get("frameworks") or []:
            target = framework.get("framework", "?")
            for kind in ("topLevelPackages", "transitivePackages"):
                for pkg in framework.get(kind) or []:
                    for vuln in pkg.get("vulnerabilities") or []:
                        findings.append((
                            name,
                            target,
                            f"{pkg.get('id')} {pkg.get('resolvedVersion')}",
                            str(vuln.get("severity", "unknown")).lower(),
                            vuln.get("advisoryurl", ""),
                        ))

    print(f"dependency scan: {len(projects)} project(s) checked against nuget.org advisories")

    if not findings:
        print("clean - no known advisories on direct or transitive packages")
        return 0

    findings.sort(
        key=lambda f: SEVERITY_ORDER.index(f[3]) if f[3] in SEVERITY_ORDER else -1,
        reverse=True,
    )
    print(f"\nFAIL - {len(findings)} advisory match(es):\n")
    for project, target, package, severity, url in findings:
        print(f"  [{severity}] {package}  ({project} / {target})")
        if url:
            print(f"          {url}")
    print("\nBump the offending package, or pin a patched transitive version explicitly.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
