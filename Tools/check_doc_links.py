#!/usr/bin/env python3
"""Check the internal links and anchors of the public documentation.

`Doc/<Component>/*.md` links to its neighbours by file and by anchor, and nothing verified those anchors:
a renamed heading silently turns every link to it into a no-op, and a link to a `####` heading is often
unreachable in the renderers that fold deep sections.

What is checked
- every `[text](path)` / `[text](path#anchor)` inside `Doc/` points at a file that exists;
- every `#anchor` matches a heading of the target document (same slug rule as the renderers: lowercase,
  punctuation dropped by Unicode category, spaces turned into `-`);
- a link from another document that points at a `####` heading is reported as a warning: per the project
  convention such content belongs in a `##`/`###` section, since deep anchors are not reliably reachable.

Usage (from the repository root):

    python Tools/check_doc_links.py            # check, exit 1 on an error
    python Tools/check_doc_links.py --verbose  # also list every link that was checked
"""

from __future__ import annotations

import argparse
import re
import sys
import unicodedata
from pathlib import Path

HEADING = re.compile(r"^(?P<level>#{1,6})\s+(?P<text>.+?)\s*$")
LINK = re.compile(r"\[[^\]]*\]\((?P<target>[^)\s]+)\)")
FENCE = re.compile(r"^\s*(```|~~~)")

#: Punctuation that survives in an anchor even though it is punctuation (`-` and `_` stay in every renderer).
_KEPT_PUNCTUATION = {"-", "_"}

#: An inline code span: markdown *examples* written inside it are not links.
INLINE_CODE = re.compile(r"`[^`]*`")


def slug(text: str) -> str:
    """Anchor a renderer derives from a heading text.

    Punctuation is dropped by Unicode category - that is what the renderers do: `Bar Chart — IntervalMark`
    becomes `bar-chart--intervalmark` (em dash gone, both spaces hyphenated) - while `-` and `_` survive.
    """
    kept = []
    for char in text.strip().lower():
        if char in _KEPT_PUNCTUATION:
            kept.append(char)
            continue
        if char.isspace():
            kept.append("-")
            continue
        if unicodedata.category(char).startswith("P"):
            continue
        kept.append(char)
    return "".join(kept)


def headings(path: Path) -> dict[str, int]:
    """anchor -> heading level, for one document (fenced code blocks are skipped)."""
    result: dict[str, int] = {}
    in_fence = False
    for line in path.read_text(encoding="utf-8").splitlines():
        if FENCE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        match = HEADING.match(line)
        if match:
            result.setdefault(slug(match.group("text")), len(match.group("level")))
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--docs", default="Doc", help="directory to scan (default: Doc)")
    parser.add_argument("--verbose", action="store_true", help="list every link that was checked")
    args = parser.parse_args(argv)

    root = Path.cwd()
    docs = root / args.docs
    if not docs.is_dir():
        print(f"error: {docs} is not a directory", file=sys.stderr)
        return 2

    documents = sorted(docs.rglob("*.md"))
    cache: dict[Path, dict[str, int]] = {}
    errors: list[str] = []
    warnings: list[str] = []
    checked = 0

    for document in documents:
        text = document.read_text(encoding="utf-8")
        in_fence = False
        for lineno, line in enumerate(text.splitlines(), start=1):
            if FENCE.match(line):
                in_fence = not in_fence
                continue
            if in_fence:
                continue
            searchable = INLINE_CODE.sub("", line)   # `[text](url)` in a table is an example, not a link
            for match in LINK.finditer(searchable):
                target = match.group("target")
                if target.startswith(("http://", "https://", "mailto:")):
                    continue
                checked += 1
                path_part, _, anchor = target.partition("#")
                target_path = document if path_part == "" else (document.parent / path_part)
                try:
                    target_path = target_path.resolve().relative_to(root.resolve())
                except ValueError:
                    errors.append(f"{document.relative_to(root)}:{lineno}  link leaves the repository: {target}")
                    continue
                if not (root / target_path).is_file():
                    errors.append(f"{document.relative_to(root)}:{lineno}  missing file: {target}")
                    continue
                if anchor:
                    anchors = cache.setdefault(root / target_path, headings(root / target_path))
                    if anchor not in anchors:
                        errors.append(f"{document.relative_to(root)}:{lineno}  no such anchor in "
                                      f"{target_path}: #{anchor}")
                    elif anchors[anchor] >= 4 and target_path != document.relative_to(root):
                        warnings.append(f"{document.relative_to(root)}:{lineno}  points at a level-"
                                        f"{anchors[anchor]} heading in {target_path}: #{anchor} "
                                        "(promote it to a ##/### section)")
                if args.verbose:
                    print(f"  ok {document.relative_to(root)}:{lineno} -> {target}")

    print(f"checked {checked} internal link(s) in {len(documents)} document(s)")
    for warning in warnings:
        print(f"warning: {warning}")
    for error in errors:
        print(f"error: {error}")
    print(f"{len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
