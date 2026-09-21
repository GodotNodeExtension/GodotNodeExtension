#!/usr/bin/env python3
"""XML documentation coverage guard (CS1591) - works for any component.

`dotnet build -p:NoWarn=NU1605` forces CS1591 back on (the project silences it by default, otherwise
a few hundred warnings drown the log). Every "missing XML comment for publicly visible type or
member" warning is mapped back to the component that owns the file and counted per file, then compared
with `Tools/doc-coverage-baseline.json`:

  * a file with **more** warnings than the baseline  -> exit code 1 (a new public member without docs)
  * a file with **fewer** warnings                   -> reported, the baseline can be tightened

The warnings carry absolute paths (`D:\\...\\Component\\<Name>\\File.cs`), which are turned into
`<Component>/<file>` keys - the same key space the single-component version of this tool used, so an
existing baseline keeps matching. Components are discovered from `Component/*/component_info.json`.

Usage (from the repository root):

    python Tools/check_doc_coverage.py             # check against the baseline
    python Tools/check_doc_coverage.py --update    # refresh the baseline
    python Tools/check_doc_coverage.py --report    # print the current distribution only

`--component NAME` (repeatable) limits the run to some components; the check defaults to every
component, because CS1591 counts source members rather than documentation files.

Exit codes: 0 = no regression, 1 = at least one file got worse (or the build failed), 2 = usage error.
"""

from __future__ import annotations

import argparse
import json
import locale
import os
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from lib import enable_utf8_output
from lib.component import ComponentGraph, ToolError, component_of_path, project_root  # noqa: E402

BASELINE_NAME = "doc-coverage-baseline.json"

#: The build that turns CS1591 on again; `-v n` keeps the log small but still prints every warning.
BUILD_ARGS = ("build", "-t:Rebuild", "-nologo", "-v", "n", "-p:NoWarn=NU1605")

BASELINE_NOTE = (
    "Number of public members missing an XML comment (CS1591) per file. Keys are repository paths "
    "relative to Component/, i.e. '<Component>/<file>': the same key space the single-component tool "
    "used, so a baseline written for one component keeps working for every component."
)

# MSBuild prefixes batch lines with `1>`.
MSBUILD_PREFIX = re.compile(r"^\s*\d+>\s*")
# A CS1591 warning line: `<path>(<line>,<col>): warning CS1591: ...`
WARNING = re.compile(r"^(?P<path>.+?\.cs)\(\d+,\d+\):\s*warning\s+CS1591\b")
# The trailing `[C:\...\Project.csproj]` MSBuild appends to a diagnostic.
PROJECT_SUFFIX = re.compile(r"\s*\[[^\]]*\]\s*$")


# ── build output ────────────────────────────────────────────────────────────

def project_file(root: Path) -> str:
    """The project to build: the repository's own csproj (the root marker), if there is one."""
    candidates = sorted(path.name for path in root.glob("*.csproj"))
    return candidates[0] if candidates else "GodotNodeExtension.csproj"


def decode(data: bytes) -> str:
    """Decode build output: UTF-8 when possible, otherwise the console codepage MSBuild writes in."""
    for encoding in ("utf-8", locale.getpreferredencoding(False), "cp1252"):
        try:
            return data.decode(encoding)
        except (UnicodeDecodeError, LookupError):
            continue
    return data.decode("utf-8", errors="replace")


def printable(text: str) -> str:
    """`text` with the characters the console cannot encode replaced, so printing never raises."""
    encoding = getattr(sys.stderr, "encoding", None) or "utf-8"
    try:
        return text.encode(encoding, errors="replace").decode(encoding, errors="replace")
    except LookupError:
        return text.encode("ascii", errors="replace").decode("ascii")


def error_lines(output: str, limit: int = 10) -> list[str]:
    """Up to `limit` real compiler/build error lines, short enough to be readable in a log."""
    pattern = re.compile(r":\s*error\b|\berror\s+[A-Z]{2,}\d+")
    lines = [printable(line.strip()[:400]) for line in output.split("\n") if pattern.search(line)]
    if len(lines) > limit:
        lines = lines[:limit] + [f"... and {len(lines) - limit} more"]
    return lines


def run_build(root: Path) -> str:
    """Run the CS1591 build and return its combined output (exit code 1 when the build really failed)."""
    command = ["dotnet", *BUILD_ARGS, project_file(root)]
    completed = subprocess.run(command, cwd=root, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    output = decode(completed.stdout) + decode(completed.stderr)
    # Warnings do not fail the build; only a real error does.
    errors = error_lines(output)
    if completed.returncode != 0 and errors:
        print("the build failed, the documentation coverage cannot be measured:", file=sys.stderr)
        for line in errors:
            print(f"  {line}", file=sys.stderr)
        raise SystemExit(1)
    return output


def repository_key(root: Path, raw_path: str) -> str | None:
    """`<Component>/<file>` for a component source file a warning points at, None for anything else.

    The warnings hold absolute paths, so the repository root is stripped first; only files below
    `Component/` are counted, because test, example and tool code ships no API and its public members
    do not need an XML comment.
    """
    text = raw_path.replace("\\", "/")
    root_text = root.as_posix().rstrip("/")
    if text.lower().startswith(root_text.lower() + "/"):
        text = text[len(root_text) + 1:]
    else:
        # The path may come without its drive letter (`\\workspace\\...`): keep the tail from
        # `Component/` on, which is what identifies the component.
        marker = "/Component/"
        at = text.find(marker)
        if at < 0:
            return None
        text = text[at + 1:]

    if not text.startswith("Component/"):
        return None
    component = component_of_path(root, Path(text))
    if component is None:
        return None
    tail = Path(text).relative_to(Path("Component") / component).as_posix()
    return f"{component}/{tail}"


def count_warnings(root: Path, output: str) -> tuple[dict[str, int], int, int]:
    """(counts per `<Component>/<file>`, number of raw CS1591 lines, number of distinct warnings)."""
    counts: dict[str, int] = {}
    raw = 0
    seen: set[str] = set()

    for raw_line in output.split("\n"):
        if "warning CS1591" not in raw_line:
            continue
        raw += 1
        # The detailed and the summary section print the same warning twice, so deduplicate on the
        # prefix/suffix-free line instead of on the (localized) message text.
        line = MSBUILD_PREFIX.sub("", raw_line, count=1).strip()
        match = WARNING.match(line)
        if not match:
            continue
        key = repository_key(root, match.group("path"))
        if key is None:
            continue  # test, example or tool code: its public members do not need comments

        # Set.add returns the set itself (always truthy), hence the explicit has/add pair.
        duplicate = f"{key}|{PROJECT_SUFFIX.sub('', line).strip()}"
        if duplicate in seen:
            continue
        seen.add(duplicate)
        counts[key] = counts.get(key, 0) + 1

    return counts, raw, len(seen)


# ── baseline ────────────────────────────────────────────────────────────────

def baseline_keys(key: str) -> tuple[str, ...]:
    """Key spellings to look up, newest first.

    `<Component>/<file>` is written today. The other two are fallbacks for baselines written when this
    tool only knew a single component (bare file, or the path including the `Component/` prefix).
    """
    _, _, tail = key.partition("/")
    spellings = [key]
    if tail:
        spellings.append(tail)
    spellings.append(f"Component/{key}")
    return tuple(spellings)


def baseline_count(per_file: dict, key: str) -> int:
    for spelling in baseline_keys(key):
        if spelling in per_file:
            return int(per_file[spelling] or 0)
    return 0


def read_baseline(path: Path) -> dict:
    if not path.is_file():
        return {}
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"error: cannot read {path}: {exc}", file=sys.stderr)
        raise SystemExit(2)
    return payload if isinstance(payload, dict) else {}


def write_baseline(path: Path, counts: dict[str, int], selected: list[str] | None) -> int:
    """Write the baseline; with a component filter only that component's entries are replaced."""
    if selected is None:
        per_file = dict(counts)
    else:
        known = set(selected)
        kept = {key: value for key, value in (read_baseline(path).get("perFile") or {}).items()
                if key.split("/")[0] not in known}
        per_file = {**kept, **counts}

    ordered = dict(sorted(per_file.items(), key=lambda item: (-int(item[1]), item[0])))
    path.write_text(json.dumps({"note": BASELINE_NOTE, "total": sum(ordered.values()),
                                "perFile": ordered}, indent=2) + "\n",
                    encoding="utf-8", newline="\n")
    return sum(ordered.values())


# ── entry point ─────────────────────────────────────────────────────────────

def compare_with_baseline(scoped: dict[str, int], per_file: dict,
                          scope: set[str] | None) -> tuple[int, list[str], list[str]]:
    """(expected baseline total, regressions, improvements) over the selected components.

    A file that was improved down to zero no longer shows up in the build, so the baseline entries of
    the selection take part as well. Entries that a current file already matched through a legacy key
    spelling are not counted twice.
    """
    claimed = {spelling for key in scoped for spelling in baseline_keys(key) if spelling in per_file}
    keys = set(scoped)
    for key in per_file:
        if key not in claimed and in_scope(key, scope) and baseline_count(per_file, key):
            keys.add(key)

    expected = sum(baseline_count(per_file, key) for key in keys)
    regressions: list[str] = []
    improvements: list[str] = []
    for key in sorted(keys):
        now, was = scoped.get(key, 0), baseline_count(per_file, key)
        if now > was:
            regressions.append(f"{key}: {was} -> {now}")
        elif now < was:
            improvements.append(f"{key}: {was} -> {now}")
    return expected, regressions, improvements


def in_scope(key: str, selected: set[str] | None) -> bool:
    return selected is None or key.split("/")[0] in selected


def main(argv: list[str] | None = None) -> int:
    enable_utf8_output()
    parser = argparse.ArgumentParser(
        description="Count public members missing an XML comment (CS1591) and compare with a baseline.",
        epilog="Exit codes: 0 = no regression, 1 = regression or build failure, 2 = usage error.")
    parser.add_argument("--component", action="append", metavar="NAME",
                        help="component to check (repeatable; default: every component)")
    parser.add_argument("--update", action="store_true", help="refresh the baseline")
    parser.add_argument("--report", action="store_true",
                        help="print the current distribution without comparing")
    args = parser.parse_args(argv)

    try:
        graph = ComponentGraph.load(project_root())
        selected = [component.name for component in graph.resolve(args.component)] \
            if args.component else None
    except ToolError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    root = graph.root
    output = run_build(root)
    counts, raw_lines, distinct = count_warnings(root, output)

    scope = set(selected) if selected else None
    scoped = {key: value for key, value in counts.items() if in_scope(key, scope)}
    total = sum(scoped.values())
    ordered = sorted(scoped.items(), key=lambda item: (-item[1], item[0]))

    if os.environ.get("DEBUG_COV"):
        print(f"[diag] raw CS1591 lines: {raw_lines}, distinct warnings: {distinct}, "
              f"counted: {total}", file=sys.stderr)

    baseline_path = Path(__file__).resolve().parent / BASELINE_NAME

    if args.report:
        described = f" in {len(selected)} selected component(s)" if selected else ""
        print(f"public members missing an XML comment (CS1591): {total}{described}")
        for key, value in ordered:
            print(f"  {value:>4}  {key}")
        return 0

    if args.update or not baseline_path.is_file():
        written = write_baseline(baseline_path, scoped, selected)
        print(f"baseline updated: {written} missing comment(s) in {len(scoped)} file(s)"
              f" ({baseline_path.name})")
        return 0

    per_file = read_baseline(baseline_path).get("perFile") or {}
    expected, regressions, improvements = compare_with_baseline(scoped, per_file, scope)

    described = f" in {len(selected)} selected component(s)" if selected else ""
    print(f"XML documentation coverage: {total} missing comment(s){described} (baseline {expected})")
    if improvements:
        print(f"  improved: {', '.join(improvements)} (run --update to tighten the baseline)")

    if regressions:
        print("\nnew public members without an XML comment:", file=sys.stderr)
        for regression in regressions:
            print(f"  - {regression}", file=sys.stderr)
        print("\nAdd an English XML <summary>, or confirm the member does not ship "
              "(test, example and tool code is ignored).", file=sys.stderr)
        return 1

    print("no new public member without an XML comment")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
