#!/usr/bin/env python3
"""Compile check for the examples inside the documentation - works for any component.

Supported markers (written on the markdown code fence):

    ```csharp compile            a statement snippet; wrapped in a method body and compiled
    ```csharp compile-members    class members; wrapped in a generated Control-derived class
    ```csharp compile-class      a complete type declaration; compiled as a nested container class
                                 (the `using` directives inside the block are dropped)

The generated project lives in `Tools/doc-examples/` and is not part of the main build. The set of
failing examples is compared with `Tools/doc-examples-baseline.json`, so only **new** failures fail
the check: snippets that never compiled are recorded in the baseline instead of blocking every run.

Every markdown file of `Doc/<Component>/` is scanned; by default every component that owns a
`Doc/<Component>/` directory takes part, `--component NAME` (repeatable) narrows the selection down.
The `using` directives of the generated file are derived from the namespaces the selected components
declare, and `Tools/doc-examples-context.json` holds the ambient context (fields, locals) that the
snippets of a component assume - components without an entry simply get none.

Usage (from the repository root):

    python Tools/check_doc_examples.py            # check (a new failure -> exit code 1)
    python Tools/check_doc_examples.py --update   # refresh the baseline with the current failures

Exit codes: 0 = no new failure, 1 = at least one new failure (or the generated project did not
build), 2 = usage error.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import locale
import os
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from lib import enable_utf8_output
from lib.component import Component, ComponentGraph, ToolError, project_root  # noqa: E402

HERE = Path(__file__).resolve().parent
OUT_DIR = HERE / "doc-examples"
BASELINE = HERE / "doc-examples-baseline.json"
CONTEXT = HERE / "doc-examples-context.json"
GENERATED = "Examples.g.cs"

BASELINE_NOTE = (
    "Documentation examples that are known to not compile, keyed by "
    "'<Component>/<file>:<line>:<kind>:<hash>'. The check only reports new failures."
)

#: The markers, mapped to the wrapper used when the snippet is emitted.
KINDS = {"compile": "statements", "compile-members": "members", "compile-class": "class"}
MARKER = re.compile(r"^```csharp\s+(compile[-\w]*)\s*$")
CLOSE = re.compile(r"^```\s*$")
NAMESPACE = re.compile(r"^\s*namespace\s+([A-Za-z_][\w.]*)")
#: Errors inside the generated file, used to attribute a failure to the snippet that produced it.
GENERATED_ERROR = re.compile(re.escape(GENERATED) + r"\((\d+),\d+\):\s*error")

#: Fallback ambient context when a component has no entry in `doc-examples-context.json`.
NO_CONTEXT: dict[str, list[str]] = {"statements": [], "members": [], "usings": []}


# ── snippet extraction ──────────────────────────────────────────────────────

def read_markdown(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace").replace("\r\n", "\n")


def extract_blocks(component: Component, path: Path) -> list[dict]:
    """Every marked code fence of one markdown file, with the line the fence opened on."""
    blocks: list[dict] = []
    in_block = False
    kind = ""
    buffer: list[str] = []
    start = 0

    for number, line in enumerate(read_markdown(path).split("\n"), start=1):
        if not in_block:
            match = MARKER.match(line.strip())
            if match and match.group(1) in KINDS:
                in_block, kind, buffer, start = True, match.group(1), [], number
            continue
        if CLOSE.match(line):
            in_block = False
            blocks.append({"component": component.name, "file": path.name, "line": start,
                           "kind": kind, "code": "\n".join(buffer)})
            kind = ""
            continue
        buffer.append(line)

    return blocks


def load_context() -> dict[str, dict[str, list[str]]]:
    """Ambient context per component; a missing or unreadable file means "no context"."""
    if not CONTEXT.is_file():
        return {}
    try:
        payload = json.loads(CONTEXT.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"error: cannot read {CONTEXT}: {exc}", file=sys.stderr)
        raise SystemExit(2)
    components = payload.get("components") if isinstance(payload, dict) else {}
    return {str(name): {**NO_CONTEXT, **(value or {})}
            for name, value in (components or {}).items()}


def namespaces_of(component: Component) -> list[str]:
    """Namespaces the component's sources declare, so the snippets can use its types unqualified."""
    prefix = f"GodotNodeExtension.Component.{component.name}"
    declared: set[str] = set()
    for path in component.sources:
        for line in path.read_text(encoding="utf-8", errors="replace").split("\n"):
            match = NAMESPACE.match(line)
            if match:
                declared.add(match.group(1))

    matching = sorted(name for name in declared if name == prefix or name.startswith(prefix + "."))
    if matching:
        return matching
    return sorted(declared) or [prefix]


def build_header(components: list[Component], context: dict[str, dict[str, list[str]]]) -> str:
    namespaces = sorted({name for component in components for name in namespaces_of(component)})
    # A component may need a namespace its own sources do not import through the header (SkiaSharp, for
    # the GodotSkia converter snippets): the ambient context lists it per component.
    extra = sorted({name for component in components
                    for name in context.get(component.name, {}).get("usings", [])})
    lines = [
        "// Generated by Tools/check_doc_examples.py; do not edit by hand.",
        "using System;",
        "using System.Collections.Generic;",
        "using Godot;",
        *(f"using {name};" for name in namespaces),
        *(f"using {name};" for name in extra),
        "",
        "public static class DocExamples",
        "{",
    ]
    return "\n".join(lines) + "\n"


#: Framework the generated project falls back to when the main project cannot be read.
DEFAULT_FRAMEWORK = "net10.0"

PROJECT_FILE = "GodotNodeExtension.csproj"


def main_target_framework() -> str:
    """The target framework the generated project has to use: the one of the assembly it references.

    It follows the main project instead of being assumed. The reference between the two is checked by the
    compiler, and a mismatch fails as CS1705 ("the assembly uses System.Runtime 10.0.0.0, the referencing
    project references 9.0.0.0") - which takes the whole check down before a single snippet is compiled.
    """
    try:
        text = (HERE.parent / PROJECT_FILE).read_text(encoding="utf-8")
    except OSError:
        return DEFAULT_FRAMEWORK
    match = re.search(r"<TargetFramework>([^<]+)</TargetFramework>", text)
    return match.group(1).strip() if match else DEFAULT_FRAMEWORK


def build_project() -> str:
    """The generated project; it references the built assembly and never joins the main build."""
    dll = Path("..", "..", ".godot", "mono", "temp", "bin", "Debug", "GodotNodeExtension.dll").as_posix()
    return "\n".join([
        '<Project Sdk="Microsoft.NET.Sdk">',
        "  <PropertyGroup>",
        f"    <TargetFramework>{main_target_framework()}</TargetFramework>",
        "    <Nullable>disable</Nullable>",
        "    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>",
        "    <!-- Snippets often keep unused locals around; only compilability matters here. -->",
        "    <NoWarn>$(NoWarn);CS1591;CS0169;CS0414;CS0219;CS8321;CS0162</NoWarn>",
        "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
        "  </PropertyGroup>",
        "  <ItemGroup>",
        f'    <Compile Include="{GENERATED}" />',
        '    <PackageReference Include="GodotSharp" Version="4.7.2" />',
        '    <PackageReference Include="SkiaSharp" Version="3.119.3-preview.1.1" />',
        '    <Reference Include="GodotNodeExtension">',
        f"      <HintPath>{dll}</HintPath>",
        "    </Reference>",
        "  </ItemGroup>",
        "</Project>",
        "",
    ])


# ── generation ──────────────────────────────────────────────────────────────

def indent(code: str, prefix: str) -> str:
    return "\n".join(prefix + line if line else line for line in code.split("\n"))


def strip_usings(code: str) -> str:
    pattern = re.compile(r"^\s*using\s+[\w.]+\s*;\s*(//.*)?$")
    return "\n".join(line for line in code.split("\n") if not pattern.match(line))


def render(blocks: list[dict], header: str, context: dict[str, dict[str, list[str]]]) -> tuple[str, list]:
    """(generated file, [(start line, end line, block index)] used to map errors back to blocks)."""
    body = ""
    layout: list[tuple[int, int, int]] = []
    cursor = len(header.split("\n"))  # 1-based line the body starts at

    for index, block in enumerate(blocks):
        ambient = context.get(block["component"], NO_CONTEXT)
        start = cursor
        chunk = f"\n    // ---- {block['component']}/{block['file']}:{block['line']}" \
                f" (#{index}, {block['kind']}) ----\n"

        if block["kind"] == "compile":
            chunk += f"    public static void Example{index}()\n    {{\n"
            statements = "\n".join(ambient.get("statements", []) + [block["code"]])
            chunk += indent(statements, "        ") + "\n"
        elif block["kind"] == "compile-members":
            chunk += f"    public class Fragment{index} : Control\n    {{\n"
            members = "\n".join(ambient.get("members", []) + [block["code"]])
            chunk += indent(members, "        ") + "\n"
        else:
            # A complete type declaration: wrap it so identically named examples of the English and
            # the Chinese file do not collide, and drop the `using`s (the header already has them).
            chunk += f"    public static class Block{index}\n    {{\n"
            chunk += indent(strip_usings(block["code"]), "        ") + "\n"

        chunk += "    }\n"
        size = chunk.count("\n")
        body += chunk
        layout.append((start, start + size, index))
        cursor += size

    return header + body + "}\n", layout


def block_key(block: dict) -> str:
    """`<Component>/<file>:<line>:<kind>:<hash>` - the hash ties the entry to the snippet text."""
    digest = hashlib.sha256(block["code"].encode("utf-8")).hexdigest()[:12]
    return f"{block['component']}/{block['file']}:{block['line']}:{block['kind']}:{digest}"


def legacy_key(block: dict) -> str:
    """The key the single-component version of this tool wrote (no component prefix)."""
    return block_key(block).partition("/")[2]


# ── baseline ────────────────────────────────────────────────────────────────

def read_baseline(path: Path) -> list[str]:
    if not path.is_file():
        return []
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"error: cannot read {path}: {exc}", file=sys.stderr)
        raise SystemExit(2)
    entries = payload.get("expectedFailures") if isinstance(payload, dict) else None
    return [str(entry) for entry in (entries or [])]


def write_baseline(path: Path, entries: list[str], selected: list[str] | None) -> None:
    """Write the baseline; with a component filter only that component's entries are replaced."""
    if selected is not None:
        known = set(selected)
        kept = [entry for entry in read_baseline(path) if entry.split("/")[0] not in known]
        entries = sorted({*kept, *entries})
    path.write_text(json.dumps({"note": BASELINE_NOTE, "expectedFailures": entries},
                               indent=2) + "\n", encoding="utf-8", newline="\n")


# ── entry point ─────────────────────────────────────────────────────────────

def select(graph: ComponentGraph, names: list[str] | None) -> list[Component]:
    """The requested components, or every component that owns a `Doc/<Component>/` directory."""
    if names:
        return graph.resolve(names)
    return [component for component in graph if component.doc_dir.is_dir()]


def collect(components: list[Component]) -> list[dict]:
    blocks: list[dict] = []
    for component in components:
        if not component.doc_dir.is_dir():
            print(f"skip {component.name}: no Doc/{component.name}/ directory")
            continue
        for path in component.doc_files():
            blocks.extend(extract_blocks(component, path))
    return blocks


def error_lines(output: str, limit: int = 10) -> list[str]:
    """Up to `limit` real compiler/build error lines, short enough to be readable in a log."""
    pattern = re.compile(r":\s*error\b|\berror\s+[A-Z]{2,}\d+")
    lines = [printable(line.strip()[:400]) for line in output.split("\n") if pattern.search(line)]
    if len(lines) > limit:
        lines = lines[:limit] + [f"... and {len(lines) - limit} more"]
    return lines


def printable(text: str) -> str:
    """`text` with the characters the console cannot encode replaced, so printing never raises."""
    encoding = getattr(sys.stderr, "encoding", None) or "utf-8"
    try:
        return text.encode(encoding, errors="replace").decode(encoding, errors="replace")
    except LookupError:
        return text.encode("ascii", errors="replace").decode("ascii")


def decode_output(data: bytes) -> str:
    """Decode build output: UTF-8 when possible, otherwise the console codepage MSBuild writes in."""
    for encoding in ("utf-8", locale.getpreferredencoding(False), "cp1252"):
        try:
            return data.decode(encoding)
        except (UnicodeDecodeError, LookupError):
            continue
    return data.decode("utf-8", errors="replace")


def compile_blocks(blocks: list[dict], layout: list[tuple[int, int, int]]) -> list[int]:
    """Indexes of the blocks the compiler rejected (exit code 1 when the project did not build)."""
    completed = subprocess.run(["dotnet", "build", "DocExamples.csproj", "-nologo", "-v", "q"],
                               cwd=OUT_DIR, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    output = decode_output(completed.stdout + completed.stderr)

    failing: list[int] = []
    for line in output.split("\n"):
        match = GENERATED_ERROR.search(line)
        if not match:
            continue
        at = int(match.group(1))
        for start, end, index in layout:
            if start <= at < end and index not in failing:
                failing.append(index)
                break

    if completed.returncode != 0 and not failing:
        print("the generated example project did not build:", file=sys.stderr)
        for line in error_lines(output):
            print(f"  {line}", file=sys.stderr)
        raise SystemExit(1)
    return failing


def main(argv: list[str] | None = None) -> int:
    enable_utf8_output()
    parser = argparse.ArgumentParser(
        description="Compile the documentation examples marked with ```csharp compile.",
        epilog="Exit codes: 0 = no new failure, 1 = new failure, 2 = usage error.")
    parser.add_argument("--component", action="append", metavar="NAME",
                        help="component to check (repeatable; default: all documented components)")
    parser.add_argument("--update", action="store_true",
                        help="refresh the baseline with the current failure set")
    args = parser.parse_args(argv)

    try:
        graph = ComponentGraph.load(project_root())
        components = select(graph, args.component)
    except ToolError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    blocks = collect(components)
    if not blocks:
        print("no code block marked for compilation; mark the examples that should compile with "
              "```csharp compile / compile-members / compile-class")
        return 0

    if os.environ.get("DEBUG_BLOCKS"):
        for block in blocks:
            lines = block["code"].count("\n") + 1
            print(f"  block: {block['component']}/{block['file']}:{block['line']} "
                  f"{block['kind']} ({lines} lines)")

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    # Only the components that actually contribute snippets are imported, so the examples of one
    # component cannot start resolving names from an unrelated component.
    context = load_context()
    header = build_header([graph.get(name) for name in
                           sorted({block["component"] for block in blocks})], context)
    generated, layout = render(blocks, header, context)
    (OUT_DIR / "DocExamples.csproj").write_text(build_project(), encoding="utf-8", newline="\n")
    (OUT_DIR / GENERATED).write_text(generated, encoding="utf-8", newline="\n")

    failing = compile_blocks(blocks, layout)
    failing_blocks = sorted((blocks[index] for index in failing), key=block_key)
    current = [block_key(block) for block in failing_blocks]

    if args.update or not BASELINE.is_file():
        write_baseline(BASELINE, current, [component.name for component in components]
                       if args.component else None)
        print(f"checked {len(blocks)} marked example(s) in {len(components)} component(s); "
              f"baseline updated: {len(current)} currently failing")
        return 0

    baseline = read_baseline(BASELINE)
    known = set(baseline)
    # A baseline entry matches either the current key or the key the single-component tool wrote.
    matched = {spelling for block in failing_blocks
               for spelling in (block_key(block), legacy_key(block))}
    new_failures = [block_key(block) for block in failing_blocks
                    if block_key(block) not in known and legacy_key(block) not in known]
    fixed = [entry for entry in baseline if entry not in matched]

    print(f"checked {len(blocks)} marked example(s) in {len(components)} component(s): "
          f"{len(current)} failing (baseline {len(baseline)})")
    if fixed:
        print(f"  ({len(fixed)} fixed, run --update to tighten the baseline)")
    if new_failures:
        print("\nnew examples that do not compile:", file=sys.stderr)
        for key in new_failures:
            print(f"  - {key}", file=sys.stderr)
        print("\nFix the documentation, or drop the compile marker when the snippet is only "
              "illustrative.", file=sys.stderr)
        return 1

    print("no new example that does not compile")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
