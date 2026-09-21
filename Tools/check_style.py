#!/usr/bin/env python3
"""Check the code style of the project with ReSharper's command line (`jb inspectcode`).

Examples (run from the repository root):

    python Tools/check_style.py --all                        # the whole solution, WARNING and above
    python Tools/check_style.py --component Typography       # that component, its example, its dependencies
    python Tools/check_style.py --component Typography GodotSkia --no-build
    python Tools/check_style.py --all --baseline Tools/style-baseline.txt
    python Tools/check_style.py --all --write-baseline Tools/style-baseline.txt
    python Tools/check_style.py --all --keep-sarif           # keep jb's own report too

The findings are written as a plain text report - one line per finding, `path:line  Rule  message`, grouped
with a short summary - because the SARIF document jb produces is about a kilobyte per finding and unreadable
in a CI log, and the report is what a person opens in an editor and greps. The SARIF itself is a temporary
file unless `--keep-sarif` asks for it.

Exit codes: 0 = nothing to report, 1 = findings (the report names them), 2 = usage or setup error (unknown
component, jb missing or refusing to run).
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import shutil
import subprocess
import sys
import time
from collections import Counter
from typing import NamedTuple
from urllib.parse import unquote, urlparse

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

from lib import enable_utf8_output  # noqa: E402
from lib.component import ComponentGraph, ToolError, load_graph, project_root  # noqa: E402

#: Severity levels jb understands, weakest first.
SEVERITIES = ("HINT", "SUGGESTION", "WARNING", "ERROR")

#: The gate is "no warning": anything weaker is a suggestion the repository deliberately keeps.
DEFAULT_SEVERITY = "WARNING"

#: Paths the repository never gates on: the suites are driven by reflection (AGENTS.md §4) and the addons
#: are vendored as they come.
DEFAULT_EXCLUDES = ("Test/**", "addons/**")

#: A run that has not finished by then is reported as a failure rather than hanging a workflow.
DEFAULT_TIMEOUT = 900.0

#: Findings printed to the console; the report has all of them.
PRINTED_FINDINGS = 20


class Finding(NamedTuple):
    """One reported inspection result."""

    rule: str
    title: str
    path: str
    line: int
    message: str

    @property
    def key(self) -> str:
        """Identity used by a baseline: deliberately without the line number, which moves with any edit above it."""
        return f"{self.rule}\t{self.path}\t{self.message}"


def find_jb(explicit: str | None) -> str:
    """Path of the `jb` tool: the one given, the one on PATH, or the global dotnet tool."""
    if explicit:
        path = pathlib.Path(explicit)

        if not path.is_file():
            raise ToolError(f"the jb tool given with --jb does not exist: {path}")

        return str(path)

    found = shutil.which("jb")

    if found:
        return found

    for candidate in (pathlib.Path.home() / ".dotnet" / "tools" / "jb",
                      pathlib.Path.home() / ".dotnet" / "tools" / "jb.exe"):
        if candidate.is_file():
            return str(candidate)

    raise ToolError("could not find the 'jb' command - install it with\n"
                    "  dotnet tool install -g jetbrains.resharper.globaltools\n"
                    "or point --jb at it")


def scope_of(graph: ComponentGraph, names: list[str], with_dependencies: bool, with_examples: bool) -> tuple[list[str], str]:
    """Inspection patterns for the selected components, and a human-readable description of the scope.

    A component owns `Component/<name>/` and `Example/<name>/`; the components it declares in its
    `component_info.json` are inspected with it, because a finding in a dependency is a finding the caller
    can be the cause of.
    """
    components: list[str] = []

    for name in names:
        graph.get(name)  # raises ToolError for an unknown name

        if name not in components:
            components.append(name)

    if with_dependencies:
        for name in list(components):
            for dependency in graph.dependencies_of(name, transitive=True):
                if dependency not in components:
                    components.append(dependency)

    patterns: list[str] = []
    directories: list[str] = []

    for name in components:
        patterns.append(f"Component/{name}/**")
        directories.append(f"Component/{name}")

        if with_examples and graph.get(name).example_dir.is_dir():
            patterns.append(f"Example/{name}/**")
            directories.append(f"Example/{name}")

    description = ", ".join(directories)
    dependencies = [name for name in components if name not in names]

    if dependencies:
        description += f" (+ dependencies: {', '.join(dependencies)})"

    return patterns, description


def run_inspect(jb: str, solution: pathlib.Path, report: pathlib.Path, includes: list[str], excludes: list[str],
                severity: str, configuration: str | None, build: bool, caches: pathlib.Path | None,
                timeout: float, verbosity: str = "WARN") -> list[str]:
    """Run jb, writing its SARIF report to `report`; returns the command it ran."""
    command = [jb, "inspectcode", str(solution), f"--output={report}", "--format=Sarif",
               f"--severity={severity}", "--absolute-paths", f"--exclude={';'.join(excludes)}",
               "--swea", f"--verbosity={verbosity}", "--no-updates"]

    if includes:
        command.append(f"--include={';'.join(includes)}")

    if configuration:
        # The properties are what makes jb read the same output the CI build just produced.
        command.append(f"--properties=Configuration={configuration}")

    if caches:
        command.append(f"--caches-home={caches}")

    command.append("--build" if build else "--no-build")

    print(f"==> {' '.join(command)}")

    started = time.monotonic()

    try:
        result = subprocess.run(command, timeout=timeout, check=False)
    except subprocess.TimeoutExpired as expired:
        raise ToolError(f"jb did not finish within {expired.timeout:.0f}s") from expired
    except OSError as error:
        raise ToolError(f"could not run jb: {error}") from error

    elapsed = time.monotonic() - started

    if result.returncode != 0:
        raise ToolError(f"jb exited with {result.returncode} - its own output above says why")

    if not report.is_file():
        raise ToolError(f"jb wrote no report to {report}")

    print(f"    inspection finished in {elapsed:.0f}s, report {report.stat().st_size:,} B (SARIF)")

    return command


def relative_path(uri: str, root: pathlib.Path) -> str:
    """Repository-relative path of a SARIF artifact location."""
    raw = unquote(urlparse(uri).path) if uri.startswith("file:") else uri

    if os.name == "nt" and raw.startswith("/") and len(raw) > 2 and raw[2] == ":":
        raw = raw[1:]  # /D:/workspace/... -> D:/workspace/...

    path = pathlib.Path(raw)

    try:
        return path.relative_to(root).as_posix()
    except ValueError:
        return path.as_posix()


def parse_sarif(report: pathlib.Path, root: pathlib.Path) -> list[Finding]:
    """Every finding of the report, ordered by file and line so the report reads like compiler output."""
    payload = json.loads(report.read_text(encoding="utf-8"))
    findings: list[Finding] = []

    for run in payload.get("runs", []):
        titles = {rule.get("id"): (rule.get("shortDescription", {}).get("text") or "")
                  for rule in run.get("tool", {}).get("driver", {}).get("rules", [])}

        for result in run.get("results", []):
            locations = result.get("locations") or [{}]
            physical = locations[0].get("physicalLocation", {})

            findings.append(Finding(
                rule=result.get("ruleId") or "?",
                title=titles.get(result.get("ruleId"), ""),
                path=relative_path(physical.get("artifactLocation", {}).get("uri", "?"), root),
                line=physical.get("region", {}).get("startLine", 0),
                message=(result.get("message", {}).get("text") or "").strip(),
            ))

    findings.sort(key=lambda finding: (finding.path, finding.line, finding.rule))

    return findings


def read_baseline(path: pathlib.Path) -> set[str]:
    """The accepted findings, one `Rule<TAB>path<TAB>message` per line; comments start with #."""
    if not path.is_file():
        raise ToolError(f"no such baseline: {path} (write one with --write-baseline)")

    keys = set()

    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()

        if line and not line.startswith("#"):
            keys.add(line)

    return keys


def write_baseline(path: pathlib.Path, findings: list[Finding], severity: str) -> None:
    """Record the current findings as accepted, so a gate can still catch what is added on top."""
    lines = [
        "# Accepted code-style findings (Tools/check_style.py --baseline).",
        "# One finding per line: Rule<TAB>path<TAB>message - no line numbers, which move with edits above.",
        f"# Recorded with --severity {severity}; regenerate with --write-baseline after paying some down.",
        "",
    ]

    for finding in sorted(findings, key=lambda item: item.key):
        lines.append(finding.key)

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_report(path: pathlib.Path, findings: list[Finding], fresh: list[Finding], known: int, scope: str,
                 severity: str, command: list[str], baseline_used: bool) -> None:
    """Write the plain-text report: a summary to triage by, then one line per finding."""
    rules = Counter(finding.rule for finding in findings)
    areas = Counter("/".join(finding.path.split("/")[:2]) for finding in findings)
    lines = [
        f"# code style - {severity} and above",
        f"# scope: {scope}",
        f"# findings: {len(findings)}"
        + (f" ({len(fresh)} new, {known} accepted by the baseline)" if baseline_used else ""),
        f"# command: {' '.join(command)}",
        "",
        "## by rule",
        "",
    ]

    width = max((len(name) for name in rules), default=4)

    for rule, count in rules.most_common():
        lines.append(f"{count:>6}  {rule:<{width}}  {next(f.title for f in findings if f.rule == rule)}")

    lines += ["", "## by area", ""]

    for area, count in areas.most_common():
        lines.append(f"{count:>6}  {area}")

    lines += ["", "## findings", ""]

    for finding in findings:
        marker = ("new  " if finding in fresh else "     ") if baseline_used else ""
        lines.append(f"{marker}{finding.path}:{finding.line}  {finding.rule}  {finding.message}")

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0],
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    scope_group = parser.add_mutually_exclusive_group(required=True)
    scope_group.add_argument("--all", action="store_true", help="inspect the whole solution")
    scope_group.add_argument("--component", nargs="+", metavar="NAME",
                             help="inspect these components (of Component/<NAME>/)")
    parser.add_argument("--no-dependencies", action="store_true",
                        help="leave the components a selected one depends on out of the scope")
    parser.add_argument("--no-examples", action="store_true", help="leave Example/<name>/ out of the scope")
    parser.add_argument("--severity", choices=SEVERITIES, default=DEFAULT_SEVERITY,
                        help=f"weakest level to report (default {DEFAULT_SEVERITY})")
    parser.add_argument("--exclude", action="append", default=[], metavar="PATTERN",
                        help="extra exclusion pattern (repeatable; Test/** and addons/** are always excluded)")
    parser.add_argument("--out", help="where to write the text report (default tmp/logs/code-style*.txt)")
    parser.add_argument("--baseline", help="accepted findings to ignore; only new ones fail the run")
    parser.add_argument("--write-baseline", metavar="FILE",
                        help="record the current findings as accepted and exit")
    parser.add_argument("--keep-sarif", action="store_true", help="keep jb's own SARIF report next to --out")
    parser.add_argument("--jb", help="path to the jb tool (default: the one on PATH)")
    parser.add_argument("--caches-home", default="tmp/cache/jb",
                        help="where jb keeps its caches (default tmp/cache/jb, AGENTS.md §2)")
    parser.add_argument("--configuration", default="", help="MSBuild configuration to inspect (default: jb's own)")
    parser.add_argument("--no-build", action="store_true",
                        help="do not build first - use the binaries already there (fast, what CI does)")
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT, help=f"seconds (default {DEFAULT_TIMEOUT:.0f})")
    parser.add_argument("--print", type=int, default=PRINTED_FINDINGS, dest="printed",
                        help=f"findings printed to the console (default {PRINTED_FINDINGS}; the report has all)")
    parser.add_argument("--format", choices=("text", "github"), default="text",
                        help="'github' prints key=value lines for $GITHUB_OUTPUT")
    parser.add_argument("--quiet", action="store_true", help="only the summary line")
    parser.add_argument("--verbose", action="store_true", help="let jb print its progress (very chatty)")
    args = parser.parse_args(argv)

    try:
        root = project_root()
        graph = load_graph(root)
        jb = find_jb(args.jb)

        if args.all:
            includes: list[str] = []
            scope = "the whole solution"
            default_out = "tmp/logs/code-style.txt"
        else:
            includes, scope = scope_of(graph, args.component, not args.no_dependencies, not args.no_examples)
            default_out = "tmp/logs/code-style-" + "-".join(args.component) + ".txt"

        excludes = list(DEFAULT_EXCLUDES) + args.exclude
        out = root / (args.out or default_out)
        sarif = out.with_suffix(".sarif")
        caches = (root / args.caches_home) if args.caches_home else None
        solution = root / "GodotNodeExtension.sln"
        command = run_inspect(jb, solution, sarif, includes, excludes, args.severity,
                              args.configuration or None, not args.no_build, caches, args.timeout,
                              "INFO" if args.verbose else "WARN")
        findings = parse_sarif(sarif, root)

        if args.write_baseline:
            # Accepting today's findings is a deliberate step, so it writes no report and cannot fail.
            baseline = root / args.write_baseline
            write_baseline(baseline, findings, args.severity)
            sarif.unlink(missing_ok=True)
            print(f"baseline written: {baseline} ({len(findings)} finding(s) accepted)")
            return 0

        if not args.keep_sarif:
            # The JSON is a working format, not an artifact: the report below is what is kept.
            sarif.unlink(missing_ok=True)

        accepted = read_baseline(root / args.baseline) if args.baseline else None
        fresh = [finding for finding in findings if accepted is None or finding.key not in accepted]
        known = len(findings) - len(fresh)

        write_report(out, findings, fresh, known, scope, args.severity, command, accepted is not None)

        if args.format == "github":
            # Key=value lines for $GITHUB_OUTPUT (the same convention Tools/components.py uses).
            print(f"style_status={'failed' if fresh else 'passed'}")
            print(f"style_findings={len(findings)}")
            print(f"style_new={len(fresh)}")
            print(f"style_accepted={known}")
            print(f"style_report={out.relative_to(root).as_posix()}")
        else:
            print(f"\n{len(findings)} finding(s) at {args.severity} or above in {scope}"
                  + (f"; {len(fresh)} new, {known} accepted by the baseline" if args.baseline else ""))

            if not args.quiet:
                for finding in fresh[:args.printed]:
                    print(f"  {finding.path}:{finding.line}  {finding.rule}  {finding.message}")

                if len(fresh) > args.printed:
                    print(f"  ... {len(fresh) - args.printed} more")

            print(f"report: {out}")

        return 1 if fresh else 0
    except ToolError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    enable_utf8_output()
    raise SystemExit(main())
