#!/usr/bin/env python3
"""English/Chinese documentation parity guard - works for any component.

Every markdown file of `Doc/<Component>/` that has a `.cn.md` sibling is compared with that sibling, and
so is the repository's own `README.md` / `README.cn.md` pair:

  1. the heading sequence and their levels must match (a section cannot be added on one side only)
  2. the table cells of the "default value" column must match - only cells that look like a default
     value (a number or range, `true`/`false`, an em dash, a quoted literal) take part, because the
     description column is prose and legitimately differs
  3. the number of code fences must match

Components come from `Component/*/component_info.json`. By default every component that owns a
`Doc/<Component>/` directory is checked, together with the root README pair; `--component NAME`
(repeatable) narrows the selection down to those components (and then leaves the root pair out).
A markdown file without a counterpart is reported and skipped, so a component that only documents one
language does not fail the check.

Usage (from the repository root):

    python Tools/check_doc_parity.py
    python Tools/check_doc_parity.py --component GodotChart

Exit codes: 0 = every pair matches, 1 = at least one pair is inconsistent, 2 = usage error (for
example an unknown component name).
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from lib import enable_utf8_output
from lib.component import Component, ComponentGraph, ToolError, project_root  # noqa: E402

#: Suffix of the Chinese translation of `X.md` (`X.cn.md`).
CN_SUFFIX = ".cn.md"

#: The repository's own documentation pair, checked with the same rules as the component ones.
ROOT_README = "README.md"

HEADING = re.compile(r"^#{1,6}\s")
LEVEL = re.compile(r"^#+")

#: Cells that look like a default value. The description column (prose) is deliberately left out.
VALUE_LIKE = re.compile(r'^(-?[0-9.]+(\s*[\u2013-]\s*[0-9.]+)?|true|false|\u2014|"[^"]*")$')

#: The leading `| name | type | value |` cells of a documentation table row.
ROW = re.compile(r"^\|\s*`?([A-Za-z0-9_]+)`?\s*\|\s*([A-Za-z?\[\]<>]+)\s*\|\s*([^|]*)\|")


# ── markdown helpers ────────────────────────────────────────────────────────

def read_markdown(path: Path) -> str:
    """File content with LF line endings (`errors="replace"` keeps a broken byte from crashing)."""
    return path.read_text(encoding="utf-8", errors="replace").replace("\r\n", "\n")


def headings(text: str) -> list[str]:
    return [line.strip() for line in text.split("\n") if HEADING.match(line.strip())]


def default_values(text: str) -> list[str]:
    """`name=value` pairs of every table row whose value cell looks like a default value."""
    values: list[str] = []
    for line in text.split("\n"):
        match = ROW.match(line.strip())
        if not match:
            continue
        value = match.group(3).strip()
        if VALUE_LIKE.match(value):
            values.append(f"{match.group(1)}={value}")
    return values


def code_fences(text: str) -> int:
    return sum(1 for line in text.split("\n") if line.startswith("```"))


def pairs_of(component: Component) -> tuple[list[tuple[Path, Path]], list[Path], list[Path]]:
    """Split a component's markdown into (pairs, english without counterpart, chinese without one)."""
    english: list[Path] = []
    chinese: dict[str, Path] = {}
    for path in component.doc_files():
        if path.name.endswith(CN_SUFFIX):
            chinese[path.name] = path
        else:
            english.append(path)

    pairs: list[tuple[Path, Path]] = []
    unpaired_english: list[Path] = []
    paired: set[str] = set()
    for path in english:
        counterpart = chinese.get(path.stem + CN_SUFFIX)
        if counterpart is None:
            unpaired_english.append(path)
        else:
            pairs.append((path, counterpart))
            paired.add(counterpart.name)

    unpaired_chinese = [path for name, path in chinese.items() if name not in paired]
    return pairs, unpaired_english, unpaired_chinese


def compare(english: Path, chinese: Path) -> tuple[list[str], int, int]:
    """Problems of one pair plus the English heading/default-value counts (for the `ok` line)."""
    source, translation = read_markdown(english), read_markdown(chinese)
    source_headings, translation_headings = headings(source), headings(translation)
    problems: list[str] = []

    if len(source_headings) != len(translation_headings):
        problems.append(f"heading count: {len(source_headings)} vs {len(translation_headings)}")
    else:
        for index, (line_a, line_b) in enumerate(zip(source_headings, translation_headings)):
            level_a = LEVEL.match(line_a).group(0)
            level_b = LEVEL.match(line_b).group(0)
            if level_a != level_b:
                problems.append(f"heading #{index} level: {level_a} vs {level_b}")
                break

    values_a, values_b = default_values(source), default_values(translation)
    if len(values_a) != len(values_b):
        problems.append(f"default-value cell count: {len(values_a)} vs {len(values_b)}")
    for index, (value_a, value_b) in enumerate(zip(values_a, values_b)):
        if value_a != value_b:
            problems.append(f"default value #{index} differs: {value_a} vs {value_b}")

    fences_a, fences_b = code_fences(source), code_fences(translation)
    if fences_a != fences_b:
        problems.append(f"code fences: {fences_a} vs {fences_b}")

    return problems, len(source_headings), len(values_a)


# ── entry point ─────────────────────────────────────────────────────────────

def select(graph: ComponentGraph, names: list[str] | None) -> list[Component]:
    """The requested components, or every component that owns a `Doc/<Component>/` directory."""
    if names:
        return graph.resolve(names)
    return [component for component in graph if component.doc_dir.is_dir()]


def check_component(component: Component) -> tuple[int, int]:
    """Check one component, print its result lines, return (failing pairs, checked pairs)."""
    if not component.doc_dir.is_dir():
        print(f"skip {component.name}: no Doc/{component.name}/ directory")
        return 0, 0

    pairs, unpaired_english, unpaired_chinese = pairs_of(component)
    for path in unpaired_english:
        print(f"note {component.name}/{path.name}: no {path.stem}{CN_SUFFIX} counterpart (skipped)")
    for path in unpaired_chinese:
        print(f"note {component.name}/{path.name}: no counterpart (skipped)")
    if not pairs:
        print(f"skip {component.name}: nothing to compare in Doc/{component.name}/")
        return 0, 0

    failures = 0
    for english, chinese in pairs:
        problems, heading_count, value_count = compare(english, chinese)
        if problems:
            failures += 1
            print(f"FAIL {component.name}/{english.name} / {component.name}/{chinese.name}")
            for problem in problems:
                print(f"     - {problem}")
        else:
            print(f"ok   {component.name}/{english.name} / {component.name}/{chinese.name}"
                  f"  ({heading_count} headings, {value_count} default-value cells)")
    return failures, len(pairs)


def check_root_readme(root: Path) -> tuple[int, int]:
    """Check the repository's own README pair - the same rules, outside any component."""
    english, chinese = root / ROOT_README, root / (Path(ROOT_README).stem + CN_SUFFIX)
    if not english.is_file():
        print(f"skip {ROOT_README}: no root readme")
        return 0, 0
    if not chinese.is_file():
        print(f"note {ROOT_README}: no {chinese.name} counterpart (skipped)")
        return 0, 0

    problems, heading_count, value_count = compare(english, chinese)
    if problems:
        print(f"FAIL {english.name} / {chinese.name}")
        for problem in problems:
            print(f"     - {problem}")
        return 1, 1

    print(f"ok   {english.name} / {chinese.name}  ({heading_count} headings, {value_count} "
          f"default-value cells)")
    return 0, 1


def main(argv: list[str] | None = None) -> int:
    enable_utf8_output()
    parser = argparse.ArgumentParser(
        description="Check that every English/Chinese documentation pair of a component stays in sync.",
        epilog="By default the repository's own README pair and every component owning a Doc/<component>/ "
               "directory are checked.")
    parser.add_argument("--component", action="append", metavar="NAME",
                        help="component to check (repeatable; default: all documented components)")
    args = parser.parse_args(argv)

    try:
        graph = ComponentGraph.load(project_root())
        components = select(graph, args.component)
    except ToolError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    failures = checked = pairs = 0
    if not args.component:
        # The repository's own README pair follows the same rules; a component filter narrows the run to
        # that component, so the scaffolder can check a fresh component without the root pair in the way.
        root_failures, root_pairs = check_root_readme(graph.root)
        failures += root_failures
        pairs += root_pairs
        checked += 1 if root_pairs else 0

    for component in components:
        component_failures, component_pairs = check_component(component)
        failures += component_failures
        pairs += component_pairs
        checked += 1 if component_pairs else 0

    if not pairs:
        print("nothing to compare: no selected component (and no root readme pair) has an "
              "English/Chinese pair")
        return 0
    if failures:
        print(f"\n{failures} inconsistent documentation pair(s) out of {pairs}", file=sys.stderr)
        return 1

    print(f"\nall {pairs} documentation pair(s) in {checked} document set(s) are consistent")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
