#!/usr/bin/env python3
"""Generate the Unicode property tables the typography engine compiles against.

Source of truth: the Unicode Character Database. Every generated file carries the UCD version and the
SHA-256 of the files it came from, because a line-breaking rule that depends on an unstated Unicode
version is not reproducible.

Usage:
    python Tools/Typography/gen_unicode_tables.py --ucd-dir tmp/cache/ucd --out Component/Typography/Core/Unicode

The input files (download from https://www.unicode.org/Public/UCD/latest/ucd/):
    LineBreak.txt, auxiliary/WordBreakProperty.txt, Scripts.txt, EastAsianWidth.txt
"""
from __future__ import annotations

import argparse
import hashlib
import re
import sys
from pathlib import Path

# (property file, generated file, table name, doc summary)
# emoji-data.txt lists several properties; only the one UAX #14/29 need is generated.
EMOJI_TABLE = ("emoji-data.txt", "ExtendedPictographicData.g.cs", "ExtendedPictographic",
               "Unicode Extended_Pictographic property, used by UAX #14 (LB30b) and UAX #29 (WB3c).")

TABLES = [
    ("LineBreak.txt", "LineBreakData.g.cs", "LineBreak",
     "Unicode line breaking class (UAX #14), used to find break opportunities."),
    ("WordBreakProperty.txt", "WordBreakData.g.cs", "WordBreak",
     "Unicode word break property (UAX #29), used to segment text into words."),
    ("Scripts.txt", "ScriptData.g.cs", "Script",
     "Unicode script, used to decide which language profile applies to a run."),
    ("EastAsianWidth.txt", "EastAsianWidthData.g.cs", "EastAsianWidth",
     "Unicode East Asian Width, needed by UAX #14 (LB30) and by the CJK spacing rules."),
    ("DerivedBidiClass.txt", "BidiClassData.g.cs", "BidiClass",
     "Unicode bidirectional class (UAX #9), used to resolve the display order of RTL text."),
]

# The bidi class of an unassigned code point is not one value: DerivedBidiClass.txt states it per range
# (Hebrew unassigned points are R, Arabic ones are AL, currency symbols are ET, and so on), so the file's
# own @missing lines are the default table instead of a single fallback name.
BIDI_MISSING_RE = re.compile(r"^#\s*@missing:\s*([0-9A-Fa-f]{4,6})(?:\.\.([0-9A-Fa-f]{4,6}))?\s*;\s*([A-Za-z_]+)")

# Long property names as the @missing lines spell them, mapped to the abbreviations the data lines use.
BIDI_VALUE_NAMES = {
    "Left_To_Right": "L", "Right_To_Left": "R", "Arabic_Letter": "AL",
    "European_Number": "EN", "European_Separator": "ES", "European_Terminator": "ET",
    "Arabic_Number": "AN", "Common_Separator": "CS", "Nonspacing_Mark": "NSM",
    "Boundary_Neutral": "BN", "Paragraph_Separator": "B", "Segment_Separator": "S",
    "White_Space": "WS", "Other_Neutral": "ON", "Left_To_Right_Embedding": "LRE",
    "Left_To_Right_Override": "LRO", "Pop_Directional_Format": "PDF",
    "Left_To_Right_Isolate": "LRI", "Right_To_Left_Isolate": "RLI",
    "First_Strong_Isolate": "FSI", "Pop_Directional_Isolate": "PDI",
}

LINE_RE = re.compile(r"^([0-9A-Fa-f]{4,6})(?:\.\.([0-9A-Fa-f]{4,6}))?\s*;\s*([A-Za-z_]+)")
HEADER_RE = re.compile(r"^#\s*(\S+)-(\d+\.\d+\.\d+)\.txt")
VERSION_RE = re.compile(r"^#\s*Version:\s*(\d+\.\d+(?:\.\d+)?)")


def parse(path: Path) -> tuple[str, str, list[tuple[int, int, str]]]:
    """Return (version, sha256, sorted ranges)."""
    raw = path.read_bytes()
    text = raw.decode("utf-8")
    digest = hashlib.sha256(raw).hexdigest()

    version = "unknown"
    for line in text.splitlines()[:12]:
        m = HEADER_RE.match(line)
        if m:
            version = m.group(2)
            break
        m = VERSION_RE.match(line)
        if m:
            version = m.group(1)
            break

    only = "Extended_Pictographic" if "emoji-data" in path.name else None

    ranges: list[tuple[int, int, str]] = []
    for line in text.splitlines():
        if not line or line.startswith("#"):
            continue
        m = LINE_RE.match(line)
        if not m:
            continue
        if only is not None and m.group(3) != only:
            # Other properties in the same file (Emoji, Emoji_Presentation, ...) are not generated.
            continue
        if only is not None:
            ranges.append((int(m.group(1), 16),
                           int(m.group(2), 16) if m.group(2) else int(m.group(1), 16),
                           "Y"))
            continue
        start = int(m.group(1), 16)
        end = int(m.group(2), 16) if m.group(2) else start
        ranges.append((start, end, m.group(3)))

    ranges.sort()
    return version, digest, ranges


def merge(ranges: list[tuple[int, int, str]]) -> list[tuple[int, int, str]]:
    """Merge adjacent and overlapping ranges that share a value (keeps the tables small)."""
    out: list[tuple[int, int, str]] = []
    for start, end, value in ranges:
        if out and out[-1][2] == value and start <= out[-1][1] + 1:
            out[-1] = (out[-1][0], max(out[-1][1], end), value)
        else:
            out.append((start, end, value))
    return out


def gapped(ranges: list[tuple[int, int, str]], enum_name: str, default_value: str) -> list[tuple[int, int, str]]:
    """Fill the gaps between ranges with the property's default value.

    UCD files list only assigned values; everything between the ranges has the file's @missing default
    (given in each file's header). Filling them here means the lookup is a plain range search with no
    special case for "unassigned".
    """
    filled: list[tuple[int, int, str]] = []
    cursor = 0x110000
    for start, end, value in ranges:
        if start > cursor:
            filled.append((cursor, start - 1, default_value))
        filled.append((start, end, value))
        cursor = end + 1
    return filled


def bidi_defaults(path: Path) -> list[tuple[int, int, str]]:
    """Read the @missing ranges of DerivedBidiClass.txt, least specific first."""
    ranges: list[tuple[int, int, str]] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        match = BIDI_MISSING_RE.match(line)
        if not match:
            continue
        start = int(match.group(1), 16)
        end = int(match.group(2), 16) if match.group(2) else start
        value = BIDI_VALUE_NAMES.get(match.group(3))
        if value is None:
            raise ValueError(f"unknown bidi value name in @missing: {match.group(3)}")
        ranges.append((start, end, value))
    return ranges


def fill_with_defaults(ranges: list[tuple[int, int, str]],
                       defaults: list[tuple[int, int, str]]) -> list[tuple[int, int, str]]:
    """Fill every gap in ranges with the default range that covers it (later defaults win)."""
    filled: list[tuple[int, int, str]] = []
    cursor = 0
    for start, end, value in ranges:
        filled.extend(default_between(cursor, start - 1, defaults))
        filled.append((start, end, value))
        cursor = end + 1
    filled.extend(default_between(cursor, 0x10FFFF, defaults))
    return filled


def default_between(start: int, end: int, defaults: list[tuple[int, int, str]]) -> list[tuple[int, int, str]]:
    """The default value of every code point in [start, end], split where the defaults change."""
    if start > end:
        return []
    pieces: list[tuple[int, int, str]] = []
    point = start
    while point <= end:
        value = "L"
        for dstart, dend, dvalue in defaults:
            if dstart <= point <= dend:
                value = dvalue
        # Extend while the value stays the same.
        last = point
        while last < end:
            nxt = last + 1
            nvalue = "L"
            for dstart, dend, dvalue in defaults:
                if dstart <= nxt <= dend:
                    nvalue = dvalue
            if nvalue != value:
                break
            last = nxt
        pieces.append((point, last, value))
        point = last + 1
    return pieces


MIRROR_LINE_RE = re.compile(r"^([0-9A-Fa-f]{4,6})\s*;\s*([0-9A-Fa-f]{4,6})")


def emit_mirroring(out_dir: Path, path: Path, version: str, digest: str, source: str) -> None:
    """Generate the Bidi_Mirroring_Glyph table.

    The file is a list of pairs (a code point and the code point it is drawn as in right-to-left text), not the
    range/value shape the other tables use, so it gets its own emitter instead of being forced into theirs.
    """
    pairs: list[tuple[int, int]] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        body = line.split("#")[0].strip()
        if not body:
            continue
        match = MIRROR_LINE_RE.match(body)
        if not match:
            continue
        pairs.append((int(match.group(1), 16), int(match.group(2), 16)))

    pairs.sort()
    lines = [
        "// <auto-generated>",
        f"//     Generated by Tools/Typography/gen_unicode_tables.py from {source}",
        f"//     Unicode version: {version}",
        f"//     Source SHA-256: {digest}",
        "//     Do not edit; run the generator instead.",
        "// </auto-generated>",
        "",
        "using System.Collections.Generic;",
        "",
        "namespace GodotNodeExtension.Component.Typography.Core.Unicode;",
        "",
        "/// <summary>",
        "/// The characters that are drawn as their mirror image in right-to-left text (Bidi_Mirroring_Glyph):",
        "/// brackets, quotation marks, angle brackets and the few other paired marks.",
        "/// </summary>",
        "internal static class BidiMirroringData",
        "{",
        "    /// <summary>Mirror pairs, ordered by code point.</summary>",
        "    private static readonly Dictionary<int, int> Mirrors = new()",
        "    {",
    ]
    for left, right in pairs:
        lines.append(f"        [{left}] = {right},")
    lines += [
        "    };",
        "",
        "    /// <summary>",
        "    /// The code point that replaces this one in right-to-left text, or the input when it has no mirror.",
        "    /// </summary>",
        '    /// <param name="codePoint">Code point to mirror.</param>',
        "    /// <returns>The mirrored code point, or the input when there is none.</returns>",
        "    public static int Mirror(int codePoint) =>",
        "        Mirrors.TryGetValue(codePoint, out int mirrored) ? mirrored : codePoint;",
        "",
        "    /// <summary>Whether this code point has a mirror image at all.</summary>",
        '    /// <param name="codePoint">Code point to test.</param>',
        "    /// <returns>True when a mirror exists.</returns>",
        "    public static bool HasMirror(int codePoint) => Mirrors.ContainsKey(codePoint);",
        "}",
    ]
    (out_dir / "BidiMirroringData.g.cs").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"  BidiMirroringData.g.cs: {len(pairs)} mirror pairs, version {version}")


def emit(out_dir: Path, table: str, enum_name: str, version: str, digest: str,
         source: str, doc: str, ranges: list[tuple[int, int, str]], default: str) -> None:
    values = sorted({value for _s, _e, value in ranges} | {default})
    class_name = f"{table}Data"

    lines = [
        "// <auto-generated>",
        f"//     Generated by Tools/Typography/gen_unicode_tables.py from {source}",
        f"//     Unicode version: {version}",
        f"//     Source SHA-256: {digest}",
        "//     Do not edit by hand: re-run the generator instead.",
        "// </auto-generated>",
        "",
        "namespace GodotNodeExtension.Component.Typography.Core.Unicode;",
        "",
        f"/// <summary>",
        f"/// {doc}",
        f"/// <para>",
        f"/// Generated from <c>{source}</c>, Unicode {version}. Ranges are sorted and non-overlapping;",
        f"/// every code point the file does not list is <see cref=\"{enum_name}.{default}\"/>.",
        f"/// </para>",
        f"/// </summary>",
        f"public static class {class_name}",
        "{",
        f"    /// <summary>Unicode version of the data in this file.</summary>",
        f"    public const string UnicodeVersion = \"{version}\";",
        "",
        f"    /// <summary>SHA-256 of the source file, so a table can be traced back to its data.</summary>",
        f"    public const string SourceSha256 = \"{digest}\";",
        "",
        f"    /// <summary>Default value for code points the source file does not list.</summary>",
        f"    public const {enum_name} Default = {enum_name}.{default};",
        "",
        "    // [start, end, value] triples, sorted by start; the search is a binary search over Start.",
        f"    private static readonly int[] Starts =",
        "    [",
    ]

    chunk = []
    for start, _end, _value in ranges:
        chunk.append(f"0x{start:X}")
    for i in range(0, len(chunk), 12):
        lines.append("        " + ", ".join(chunk[i:i + 12]) + ",")

    lines += [
        "    ];",
        "",
        "    private static readonly int[] Ends =",
        "    [",
    ]

    chunk = []
    for _start, end, _value in ranges:
        chunk.append(f"0x{end:X}")
    for i in range(0, len(chunk), 12):
        lines.append("        " + ", ".join(chunk[i:i + 12]) + ",")

    lines += [
        "    ];",
        "",
        f"    private static readonly {enum_name}[] Values =",
        "    [",
    ]

    chunk = []
    for _start, _end, value in ranges:
        chunk.append(f"{enum_name}.{value}")
    for i in range(0, len(chunk), 6):
        lines.append("        " + ", ".join(chunk[i:i + 6]) + ",")

    lines += [
        "    ];",
        "",
        f"    /// <summary>Look up the property value of a code point.</summary>",
        f"    /// <param name=\"codePoint\">The code point to look up.</param>",
        f"    /// <returns>The value, or <see cref=\"{enum_name}.{default}\"/> when the code point is unassigned.</returns>",
        f"    public static {enum_name} Lookup(int codePoint)",
        "    {",
        "        int low = 0;",
        "        int high = Starts.Length - 1;",
        "",
        "        while (low <= high)",
        "        {",
        "            int middle = (low + high) >> 1;",
        "",
        "            if (codePoint < Starts[middle])",
        "            {",
        "                high = middle - 1;",
        "            }",
        "            else if (codePoint > Ends[middle])",
        "            {",
        "                low = middle + 1;",
        "            }",
        "            else",
        "            {",
        "                return Values[middle];",
        "            }",
        "        }",
        "",
        "        return Default;",
        "    }",
        "}",
        "",
    ]

    (out_dir / f"{class_name}.g.cs").write_text("\n".join(lines), encoding="utf-8", newline="\n")
    print(f"  {class_name}.g.cs: {len(ranges)} ranges, {len(values)} values, version {version}")


def emit_enum(out_dir: Path, table: str, enum_name: str, source: str, values: list[str]) -> None:
    lines = [
        "// <auto-generated>",
        f"//     Generated by Tools/Typography/gen_unicode_tables.py from {source}",
        "//     Do not edit by hand: re-run the generator instead.",
        "// </auto-generated>",
        "",
        "namespace GodotNodeExtension.Component.Typography.Core.Unicode;",
        "",
        f"/// <summary>Values of the {table} property, as they appear in {source}.</summary>",
        f"public enum {enum_name}",
        "{",
    ]

    for value in values:
        lines.append(f"    /// <summary>{value}.</summary>")
        lines.append(f"    {value},")
        lines.append("")

    lines += ["}", ""]
    (out_dir / f"{enum_name}.g.cs").write_text("\n".join(lines), encoding="utf-8", newline="\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--ucd-dir", default="tmp/cache/ucd", help="directory holding the UCD files")
    parser.add_argument("--out", default="Component/Typography/Core/Unicode", help="output directory")
    args = parser.parse_args()

    ucd_dir = Path(args.ucd_dir)
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    for filename, generated, table, doc in list(TABLES) + [EMOJI_TABLE]:
        path = ucd_dir / filename
        if not path.exists():
            print(f"missing {path}; download it from https://www.unicode.org/Public/UCD/latest/ucd/")
            return 1

        version, digest, ranges = parse(path)
        ranges = merge(ranges)

        name = f"{table}Data"
        # The @missing default per file: line/word break and script default to the "other" value,
        # East Asian Width has no default (unassigned code points are Narrow or Wide by context) so it
        # uses N, which the LB30 rule treats as "not East Asian".
        default = {"LineBreak": "XX", "WordBreak": "Other", "Script": "Unknown",
                   "EastAsianWidth": "N", "ExtendedPictographic": "N", "BidiClass": "L"}[table]

        if table == "BidiClass":
            filled = fill_with_defaults(ranges, bidi_defaults(path))
            default = "L"
        else:
            filled = gapped(ranges, name, default)
        emit(out_dir, table, f"{table}Value", version, digest, filename, doc, filled, default)

        enum_name = f"{table}Value"
        values = sorted({value for _s, _e, value in filled})
        emit_enum(out_dir, table, enum_name, filename, values)

    mirror_path = ucd_dir / "BidiMirroring.txt"
    if not mirror_path.exists():
        print(f"missing {mirror_path}; download it from https://www.unicode.org/Public/UCD/latest/ucd/")
        return 1

    mirror_version, mirror_digest, _ranges = parse(mirror_path)
    emit_mirroring(out_dir, mirror_path, mirror_version, mirror_digest, "BidiMirroring.txt")

    print("done")
    return 0


if __name__ == "__main__":
    sys.exit(main())
