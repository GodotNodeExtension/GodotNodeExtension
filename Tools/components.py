#!/usr/bin/env python3
"""Inspect the components of this repository: metadata, dependencies and per-component health.

Examples (run from the repository root):

    python Tools/components.py list
    python Tools/components.py dependents --component GodotSkia
    python Tools/components.py graph --format dot
    python Tools/components.py changed
    python Tools/components.py verify --strict

Every command works for any component: the component list is discovered from `Component/*/`, their
dependencies are read from `component_info.json`, and the directories a component owns
(`Doc/`, `Example/`, `Test/`) are derived from its name.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from lib.component import Component, ComponentGraph, ToolError, project_root
from lib import enable_utf8_output, templates  # noqa: E402
from lib.gitutil import changed_components, git  # noqa: E402

MIN_README_LINES = 10

#: Static parts of the registry file the generator writes.
REGISTRY_HEADER = """# Components Registry

This file contains all available components in the GodotNodeExtension project.

## Available Components

| Component | Version | Author | Description | Status |
|-----------|---------|--------|-------------|---------|
"""

REGISTRY_FOOTER = """

## Contributing

To add a new component:

```bash
python Tools/components.py create
```

Run it without arguments and it asks for the name, the type, the description, the author and the version
(Enter takes the value in brackets); a non-interactive run passes them as options, e.g.
`python Tools/components.py create MyComponent --description "What it does"`.

That writes the whole layout below (sources, example, English/Chinese documentation pair and a gdUnit4
suite) and refreshes this file. Doing it by hand works too:

1. Create a new directory under `Component/[ComponentName]/`
2. Add your component files with `[Tool]` and `[GlobalClass]` attributes
3. Create a `component_info.json` file with the following structure:
   ```json
   {
     "name": "ComponentName",
     "version": "1.0.0",
     "author": "YourName",
     "description": "Brief description of your component",
     "license": "MIT",
     "requirements": {
       "godot": ">=4.0.0",
       "dotnet": ">=6.0"
     },
     "dependencies": {
       "nuget": [
         {
           "name": "PackageName",
           "version": ">=1.0.0",
           "required": true
         }
       ],
       "components": [
         "DependentComponentName"
       ]
     }
   }
   ```
4. Add a `README.md` (plus the matching `README.cn.md`) under `Doc/[ComponentName]/` with usage documentation
5. Create examples under `Example/[ComponentName]/`
6. Add a gdUnit4 suite under `Test/[ComponentName]/`
7. Submit a pull request

This file is automatically updated when component_info.json files are modified.

---
"""

#: Line the generator stamps with the time; comparisons ignore it.
REGISTRY_TIMESTAMP_PREFIX = "*Last updated:"

#: Long lists are truncated in the `--format github` output; a workflow summary is not a log file.
MAX_GITHUB_LIST = 10


def _names(values: list[list[str]] | None) -> list[str]:
    """Flatten the `--component` values: the option accepts repeats and space-separated lists."""
    if not values:
        return []
    return [name for group in values for name in group]


# ── commands ────────────────────────────────────────────────────────────────

def cmd_list(graph: ComponentGraph, args: argparse.Namespace) -> int:
    if args.json:
        payload = [
            {
                "name": c.name,
                "version": c.version,
                "dependencies": list(c.dependencies),
                "dependents": graph.dependents_of(c.name),
                "nuget": list(c.nuget_dependencies()),
                "tests": c.has_tests,
                "integration_tests": c.has_integration_tests,
                "docs": len(c.doc_files()),
                "demos": len(list(c.example_dir.glob("*.tscn"))) if c.example_dir.is_dir() else 0,
            }
            for c in graph
        ]
        print(json.dumps(payload, indent=2, ensure_ascii=False))
        return 0

    print(f"{len(graph)} component(s) in {graph.root}")
    print(f"{'component':<20} {'ver':<7} {'tests':<6} {'docs':<5} {'deps':<26} dependents")
    for c in graph:
        deps = ", ".join(c.dependencies) or "-"
        dependents = ", ".join(graph.dependents_of(c.name)) or "-"
        tests = "yes" if c.has_tests else "no"
        print(f"{c.name:<20} {c.version:<7} {tests:<6} {len(c.doc_files()):<5} {deps:<26} {dependents}")
    return 0


def cmd_dependents(graph: ComponentGraph, args: argparse.Namespace) -> int:
    names = graph.dependents_of(args.component, transitive=not args.direct)
    if args.json:
        print(json.dumps({"component": args.component, "dependents": names},
                         indent=2, ensure_ascii=False))
        return 0

    scope = "directly" if args.direct else "directly or transitively"
    if not names:
        print(f"no component {scope} depends on {args.component}")
        return 0

    print(f"components that depend on {args.component} ({scope}):")
    for name in names:
        component = graph.get(name)
        direct = "direct" if args.component in component.dependencies else "transitive"
        print(f"  {name:<20} ({direct})")
    return 0


def cmd_dependencies(graph: ComponentGraph, args: argparse.Namespace) -> int:
    names = graph.dependencies_of(args.component, transitive=not args.direct)
    if args.json:
        print(json.dumps({"component": args.component, "dependencies": names},
                         indent=2, ensure_ascii=False))
        return 0

    if not names:
        print(f"{args.component} has no component dependencies")
        return 0
    scope = "direct" if args.direct else "direct and transitive"
    print(f"components {args.component} needs ({scope}):")
    for name in names:
        print(f"  {name:<20} {graph.get(name).version}")
    return 0


def cmd_graph(graph: ComponentGraph, args: argparse.Namespace) -> int:
    if args.format == "json":
        print(json.dumps({
            "components": {c.name: {"dependencies": list(c.dependencies)} for c in graph},
            "cycles": graph.cycles(),
            "missing": graph.missing_dependencies(),
        }, indent=2, ensure_ascii=False))
        return 0

    if args.format == "dot":
        print("digraph components {")
        for c in graph:
            print(f'  "{c.name}";')
        for c in graph:
            for dep in c.dependencies:
                print(f'  "{c.name}" -> "{dep}";')
        print("}")
        return 0

    # text: roots first, dependencies indented underneath
    def render(name: str, depth: int, seen: set[str]) -> None:
        component = graph.get(name)
        marker = " (cycle)" if name in seen else ""
        print(f"{'  ' * depth}- {name} {component.version}{marker}")
        if name in seen or depth >= 4:
            return
        for dep in component.dependencies:
            if dep in graph.components:
                render(dep, depth + 1, seen | {name})

    roots = [c.name for c in graph if not graph.dependents_of(c.name, transitive=False)]
    print("dependency graph (roots first, dependencies indented):")
    for name in roots or graph.names():
        render(name, 0, set())
    return 0


def cmd_changed(graph: ComponentGraph, args: argparse.Namespace) -> int:
    changes = changed_components(graph.root, graph, args.base)
    affected = sorted(set(changes.components)
                      | {d for name in changes.components
                         for d in graph.dependents_of(name)})

    if args.format == "github":
        # Key=value lines for $GITHUB_OUTPUT, so the workflow does not need a JSON parser.
        print(f"changed_components={' '.join(changes.components)}")
        print(f"affected_components={' '.join(affected)}")
        shown = changes.shared_files[:MAX_GITHUB_LIST]
        print(f"shared_files={' '.join(shown)}")
        print(f"shared_files_count={len(changes.shared_files)}")
        print("has_component_changes=" + ("true" if changes.components or changes.shared_files else "false"))
        return 0

    if args.json or args.format == "json":
        print(json.dumps({
            "base": changes.base,
            "files": changes.files,
            "components": changes.components,
            "shared_files": changes.shared_files,
            "affected_components": affected,
        }, indent=2, ensure_ascii=False))
        return 0

    print(changes.summary())
    if changes.components:
        print(f"changed components : {', '.join(changes.components)}")
    print(f"affected (with dependents): {', '.join(affected) or 'none'}")
    if changes.shared_files:
        print("shared files (affect every component):")
        for path in changes.shared_files[:20]:
            print(f"  {path}")
        if len(changes.shared_files) > 20:
            print(f"  ... and {len(changes.shared_files) - 20} more")
    if not changes.files:
        print("working tree is clean - nothing to test")
    return 0


def _status_of(component: Component) -> str:
    """Status label of a component.

    A prerelease version means work in progress; otherwise the label follows the same requirements the
    PR check applies (a README under `Doc/<Name>/` or in the component, plus an example scene), so the
    registry cannot claim a component is complete while `verify` reports missing files.
    """
    lowered = component.version.lower()
    if any(marker in lowered for marker in ("dev", "alpha", "beta", "rc")):
        return "🚧 In Progress"

    has_docs = (component.doc_dir / "README.md").is_file() or component.readme.is_file()
    has_example = component.example_dir.is_dir() and (
        any(component.example_dir.glob("*.tscn")) or any(component.example_dir.glob("*.cs")))
    return "✅ Complete" if has_docs and has_example else "📋 Planned"


def registry_text(graph: ComponentGraph, timestamp: str) -> str:
    """The generated registry file: one table row per component, then the static footer."""
    lines = [REGISTRY_HEADER]
    for component in sorted(graph, key=lambda c: c.name):
        docs = component.doc_dir / "README.md"
        target = docs.relative_to(graph.root).as_posix() if docs.is_file() \
            else component.root.relative_to(graph.root).as_posix()
        info = component.info
        lines.append(
            f"| [{info.get('name', component.name)}]({target}) "
            f"| {component.version or '1.0.0'} "
            f"| {info.get('author', 'Unknown')} "
            f"| {info.get('description', 'No description')} "
            f"| {_status_of(component)} |\n"
        )
    lines.append(REGISTRY_FOOTER)
    lines.append(f"{REGISTRY_TIMESTAMP_PREFIX} {timestamp}*\n")
    return "".join(lines)


def _without_timestamp(content: str) -> list[str]:
    """Content lines without the generated timestamp, for comparisons that must not drift daily."""
    return [line for line in content.splitlines()
            if not line.strip().startswith(REGISTRY_TIMESTAMP_PREFIX)]


def cmd_registry(graph: ComponentGraph, args: argparse.Namespace) -> int:
    timestamp = args.date or datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
    generated = registry_text(graph, timestamp)
    target = graph.root / "COMPONENTS.md"

    if args.check:
        if not target.is_file():
            print("error: COMPONENTS.md is missing")
            return 1
        current = target.read_text(encoding="utf-8")
        if _without_timestamp(current) == _without_timestamp(generated):
            print(f"COMPONENTS.md is in sync with {len(graph)} component(s)")
            return 0
        print("error: COMPONENTS.md is out of date - run "
              "`python Tools/components.py registry --write`")
        return 1

    if args.write:
        _write_like(target, generated)
        print(f"wrote {target.relative_to(graph.root).as_posix()} "
              f"({len(graph)} component(s))")
        return 0

    print(generated, end="")
    return 0


def cmd_verify(graph: ComponentGraph, args: argparse.Namespace) -> int:
    """Per-component completeness, the same rules the PR check applies."""
    selected = _names(args.component)
    if not selected and getattr(args, "changed", False):
        # CI-friendly: verify exactly the components a change can affect.
        changes = changed_components(graph.root, graph, args.base)
        affected = sorted(set(changes.components)
                          | {d for name in changes.components
                             for d in graph.dependents_of(name)})
        selected = affected
    targets = graph.resolve(selected) if selected else list(graph)
    errors: list[str] = []
    warnings: list[str] = []

    for component in targets:
        for problem in _verify_component(component):
            (errors if problem[0] == "error" else warnings).append(f"{component.name}: {problem[1]}")

    for name, dep in graph.missing_dependencies():
        errors.append(f"{name}: depends on unknown component '{dep}'")
    for cycle in graph.cycles():
        errors.append(f"dependency cycle: {' -> '.join(cycle)}")

    failed = bool(errors) or (args.strict and bool(warnings))
    checked = ", ".join(c.name for c in targets)

    if args.format == "github":
        print(f"completeness_status={'incomplete' if failed else 'complete'}")
        print(f"completeness_message={len(errors)} error(s), {len(warnings)} warning(s)")
        print("missing_files<<EOF")
        for message in [*errors, *warnings]:
            print(f"- {message}")
        print("EOF")
        return 1 if failed else 0

    for message in warnings:
        print(f"warning: {message}")
    for message in errors:
        print(f"error: {message}")

    print(f"checked {len(targets)} component(s): {checked}")
    print(f"{len(errors)} error(s), {len(warnings)} warning(s)")

    return 1 if failed else 0


_NODE_TYPE = re.compile(r"partial\s+class\s+\w+\s*:\s*(Node|Control|Resource|RefCounted|Node2D|Node3D|CanvasItem)")


def _has_node_type(text: str) -> bool:
    """True when the sources declare a Godot node/resource type of their own."""
    return bool(_NODE_TYPE.search(text))


def _short(path: Path) -> str:
    """Path relative to the repository root, for readable messages."""
    try:
        return path.relative_to(project_root()).as_posix()
    except ValueError:
        return str(path)


def _verify_component(component: Component) -> list[tuple[str, str]]:
    problems: list[tuple[str, str]] = []

    info = component.info
    for field in ("name", "version", "author", "description"):
        if not info.get(field):
            problems.append(("error", f"component_info.json is missing '{field}'"))

    readme = component.readme if component.readme.is_file() else component.doc_dir / "README.md"
    if not readme.is_file():
        problems.append(("error", f"no README (looked in Component/{component.name}/ and "
                                  f"Doc/{component.name}/)"))
    else:
        text = readme.read_text(encoding="utf-8", errors="replace")
        if len(text.splitlines()) < MIN_README_LINES:
            problems.append(("error", f"{_short(readme)} has fewer than {MIN_README_LINES} lines"))
        elif not any(word in text.lower() for word in ("usage", "example", "how to", "用法", "示例")):
            problems.append(("warning", f"{_short(readme)} has no usage/example section"))

    sources = component.sources
    if not sources:
        problems.append(("error", "no .cs file in the component"))

    if not component.example_dir.is_dir():
        problems.append(("error", f"Example/{component.name}/ is missing"))
    elif not any(component.example_dir.glob("*.cs")) and not any(component.example_dir.glob("*.tscn")):
        problems.append(("error", f"Example/{component.name}/ has no scene or script"))

    if sources:
        text = "\n".join(path.read_text(encoding="utf-8", errors="replace") for path in sources)
        # Only node-style components are expected to carry the Godot attributes a global class needs;
        # a pure library component (no Node/Control/Resource type of its own) has nothing to register.
        if _has_node_type(text) and "[Tool]" not in text and "[GlobalClass]" not in text:
            problems.append(("warning", "no [Tool]/[GlobalClass] attribute in the component's sources"))

    if not component.has_tests:
        problems.append(("warning", f"no test suite in Test/{component.name}/"))

    for path in (component.test_dir, component.integration_dir):
        if path.is_dir() and not any(path.rglob("*.cs")):
            problems.append(("warning", f"{path.relative_to(component.root.parent.parent)} has no C# file"))

    return problems


# ── scaffolding ─────────────────────────────────────────────────────────────

#: A component name is a C# identifier, a directory name and a namespace segment at once.
NAME_PATTERN = re.compile(r"^[A-Z][A-Za-z0-9]*$")

#: Types whose example is a scene with the component node in it; the others are demoed from code.
SCENE_TYPES = ("control", "node")

#: Types the scaffolded integration test can drive: it attaches the node to a viewport and asks for its
#: size, which only a Control has (a Node2D has no rect and no exportable size).
RENDERABLE_TYPES = ("control",)

#: Version a scaffold starts at; the options and the prompt share it.
DEFAULT_VERSION = "0.1.0"

#: What a freshly scaffolded component must still be told about before it is really a component.
SCAFFOLD_FOLLOW_UP = """next steps:
  {build}   # build and test inside the component's own copy
  after that build:
    python Tools/check_doc_coverage.py --component {name}   # every public member needs an XML comment
    python Tools/check_doc_examples.py --component {name}   # the doc snippet compiles
  the editor writes the .uid files for the new scripts on its next scan"""


def _git_author(root: Path) -> str:
    """The configured git user name, so a scaffolded component is attributed without asking."""
    return git(root, "config", "user.name", check=False).strip() or "unknown"


def _scaffold_files(name: str, kind: str, description: str, author: str, version: str,
                    license_name: str, no_example: bool, no_test: bool,
                    integration: bool) -> list[tuple[Path, str]]:
    """The (path, content) pairs of a new component, in creation order."""
    spec = templates.COMPONENT_TYPES[kind]
    values = {
        "Name": name,
        "Description": description,
        "Author": author,
        "Version": version,
        "License": license_name,
        "Base": str(spec["base"]),
    }

    if kind in SCENE_TYPES:
        source = templates.SOURCE_DRAWABLE
    elif kind == "resource":
        source = templates.SOURCE_RESOURCE
    else:
        source = templates.SOURCE_LIBRARY

    files: list[tuple[Path, str]] = [
        (Path("Component") / name / "component_info.json", templates.COMPONENT_INFO),
        (Path("Component") / name / f"{name}.cs", source),
    ]

    if not no_example:
        if kind in SCENE_TYPES:
            files.append((Path("Example") / name / f"{name}Demo.cs", templates.EXAMPLE_SCRIPT_NODE))
            files.append((Path("Example") / name / f"{name}Demo.tscn", templates.EXAMPLE_SCENE_NODE))
        else:
            files.append((Path("Example") / name / f"{name}Demo.cs", templates.EXAMPLE_SCRIPT_PLAIN))

    if not no_test:
        test = templates.TEST_NODE if kind in SCENE_TYPES else templates.TEST_PLAIN
        files.append((Path("Test") / name / f"{name}Test.cs", test))
        if integration:
            files.append((Path("Test") / name / "Integration" / f"{name}RenderIntegrationTest.cs",
                          templates.INTEGRATION_TEST))

    files.append((Path("Doc") / name / "README.md", templates.DOC_EN))
    files.append((Path("Doc") / name / "README.cn.md", templates.DOC_CN))

    return [(path, templates.render(content, **values)) for path, content in files]


def _run_guards(root: Path, name: str, args: argparse.Namespace) -> int:
    """Check the young component, so the scaffold starts out green instead of leaving it to CI.

    `verify` and the documentation guards run here because they are cheap and need no build;
    `check_doc_coverage` and `check_doc_examples` need the new sources inside a built assembly, so they
    are printed as the next step rather than run against a stale one.
    """
    graph = ComponentGraph.load(root)
    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
    _write_like(root / "COMPONENTS.md", registry_text(graph, stamp))
    print("wrote COMPONENTS.md (the new component is part of the registry now)")

    status = 0
    print(f"==> verify --component {name}")
    for level, message in _verify_component(graph.get(name)):
        skipped = (args.no_example and "Example/" in message) or \
                  (args.no_test and "no test suite" in message)
        print(f"{'note' if skipped else level} {message}"
              f"{' (expected: you asked for it)' if skipped else ''}")
        if level == "error" and not skipped:
            status = 1

    for tool, extra in (("check_doc_parity.py", ["--component", name]),
                        ("check_doc_links.py", [])):
        print(f"==> python Tools/{tool} {' '.join(extra)}".rstrip())
        result = subprocess.run([sys.executable or "python", f"Tools/{tool}", *extra], cwd=str(root))
        if result.returncode != 0:
            status = 1

    print(SCAFFOLD_FOLLOW_UP.format(name=name, build=f"python Tools/sandbox.py test {name}"))
    return status


def _ask(prompt: str, default: str = "") -> str:
    """One interactive answer; an empty line takes `default`."""
    while True:
        shown = f"{prompt} [{default}]: " if default else f"{prompt}: "
        answer = input(shown).strip()
        if answer:
            return answer
        if default:
            return default
        print("  a value is required")


def _ask_name(default: str = "") -> str:
    """Ask for the component name until it is a valid one."""
    while True:
        answer = _ask("Component name (PascalCase)", default)
        if NAME_PATTERN.match(answer):
            return answer
        print("  a component name starts with an uppercase letter and holds only letters and digits")


def _ask_type(default: str = "control") -> str:
    """Ask for the component type until it is one of the templates."""
    known = ", ".join(templates.COMPONENT_TYPES)
    while True:
        answer = _ask(f"Type ({known})", default)
        if answer in templates.COMPONENT_TYPES:
            return answer
        print(f"  choose one of: {known}")


def _resolve_answers(args: argparse.Namespace, graph: ComponentGraph) -> tuple[str, str, str, str, str] | None:
    """(name, kind, description, author, version) from the options, asking for what is missing.

    Prompting happens when a value was not given on the command line and the tool is talking to a person
    (`--interactive` forces it, `--yes` turns it off), so a scaffold can be filled in either way - and a
    piped answer works for tests and scripts.
    """
    interactive = (args.interactive or (args.name is None and sys.stdin.isatty())) and not args.yes
    if args.name is None and not interactive:
        print("error: no component name - pass one (`components.py create MyComponent`) or run with "
              "--interactive", file=sys.stderr)
        return None

    author_default = args.author or _git_author(graph.root)
    version_default = args.version or DEFAULT_VERSION
    name = args.name
    kind = args.type
    description, author, version = args.description, author_default, version_default

    if interactive:
        print("Scaffolding a component - press Enter to take the value in brackets.")
        name = name or _ask_name()
        kind = kind or _ask_type()
        description = description or _ask("Description", f"{name} component (scaffolded, not implemented yet)")
        author = args.author or _ask("Author", author_default)
        version = args.version or _ask("Version", version_default)

    name = name or ""
    kind = kind or "control"
    description = description or f"{name} component (scaffolded, not implemented yet)"
    return name, kind, description, author, version


def cmd_create(graph: ComponentGraph, args: argparse.Namespace) -> int:
    """Scaffold a component: sources, example, documentation pair and test suite."""
    try:
        answers = _resolve_answers(args, graph)
    except (EOFError, KeyboardInterrupt):
        print("\ncancelled - nothing was written", file=sys.stderr)
        return 2
    if answers is None:
        return 2
    name, kind, description, author, version = answers

    if not NAME_PATTERN.match(name):
        print(f"error: '{name}' is not a component name - use PascalCase, e.g. MyComponent",
              file=sys.stderr)
        return 2
    if args.integration and kind not in RENDERABLE_TYPES:
        print(f"error: --integration needs a drawable type ({', '.join(RENDERABLE_TYPES)}) - the "
              f"scaffolded integration case attaches the node to a viewport and checks its rect, and "
              f"'{kind}' has neither", file=sys.stderr)
        return 2

    files = _scaffold_files(name, kind, description, author, version, args.license,
                            args.no_example, args.no_test, args.integration)

    existing = [path for path, _ in files if (graph.root / path).exists()]
    if existing and not args.force:
        print(f"error: {name} already has {len(existing)} of these files - nothing was written:",
              file=sys.stderr)
        for path in existing[:8]:
            print(f"  {path.as_posix()}", file=sys.stderr)
        print("       pass --force to overwrite them", file=sys.stderr)
        return 1

    print(f"creating {name} ({kind}, v{version}, {author}): {description}")
    for path, content in files:
        target = graph.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8", newline="\n")
        print(f"created {path.as_posix()}")

    return _run_guards(graph.root, name, args)


# ── publish ─────────────────────────────────────────────────────────────────

#: The hand-written AUTHORS.md section the metadata is written into, and the heading that ends it.
AUTHORS_SECTION = "## Component Contributors"
AUTHORS_FOLLOWING = "## Contributing"
AUTHORS_NOTE = ("<!-- generated from Component/*/component_info.json by "
                "`python Tools/components.py publish` - do not edit this section by hand -->")


def _write_like(path: Path, text: str) -> None:
    """Write `text` keeping the line ending the file already uses.

    The repository mixes CRLF and LF, and both generated files are older than that rule: rewriting one of
    them with the other style turns a two-line change into a whole-file diff.
    """
    newline = "\r\n" if path.is_file() and b"\r\n" in path.read_bytes() else "\n"
    path.write_text(text, encoding="utf-8", newline=newline)


def _author_links(current: str) -> dict[str, str]:
    """handle -> address, taken from the hand-written maintainer entries that carry a mailto: link."""
    found: dict[str, str] = {}
    for match in re.finditer(r"\*\*\[([^\]]+)\]\((mailto:[^)]+)\)\*\*", current):
        found.setdefault(match.group(1), match.group(2))
    return found


def _author_entry(author: str, links: dict[str, str]) -> str:
    """One contributor bullet; an address in the metadata or a known handle becomes a link."""
    name, address = author.strip(), ""
    explicit = re.match(r"^(.*?)\s*<([^>]+)>$", name)
    if explicit:
        name, address = explicit.group(1).strip(), explicit.group(2).strip()
    if address:
        return f"- **[{name}](mailto:{address})**"
    if name in links:
        return f"- **[{name}]({links[name]})**"
    return f"- **{name}**"


def _authors_section(graph: ComponentGraph, current: str) -> str:
    """The generated 'Component Contributors' section: one subsection per component, name-sorted."""
    links = _author_links(current)
    lines = [AUTHORS_SECTION, "", AUTHORS_NOTE, ""]
    for component in sorted(graph, key=lambda c: c.name):
        authors = [part for part in re.split(r"[;,]", str(component.info.get("author", "")))
                   if part.strip()]
        lines.append(f"### {component.name}")
        for author in authors or ["unknown"]:
            lines.append(_author_entry(author, links))
        lines.append("")
    return "\n".join(lines)


def authors_text(graph: ComponentGraph, current: str) -> str:
    """AUTHORS.md with its contributor section regenerated; the rest of the file is left alone."""
    start = current.find(AUTHORS_SECTION)
    end = current.find(AUTHORS_FOLLOWING, start + 1) if start >= 0 else -1
    if start < 0 or end < 0:
        raise ToolError(f"AUTHORS.md has no '{AUTHORS_SECTION}' section followed by "
                        f"'{AUTHORS_FOLLOWING}' to replace")
    return current[:start] + _authors_section(graph, current) + "\n" + current[end:]


def _metadata_problems(graph: ComponentGraph) -> list[str]:
    """Metadata a published row needs; without it the file would carry placeholders."""
    problems: list[str] = []
    for component in graph:
        for field in ("name", "version", "author", "description"):
            if not component.info.get(field):
                problems.append(f"{component.name}: component_info.json is missing '{field}'")
    return problems


def _listed_names(text: str, pattern: str, section: str | None = None) -> set[str]:
    """Component names a generated file mentions, so a stale file can name what it is missing."""
    scope = text
    if section is not None:
        start = text.find(section)
        end = text.find("\n## ", start + 1) if start >= 0 else -1
        scope = text[start:end] if start >= 0 and end > start else ""
    return set(re.findall(pattern, scope, re.M))


def cmd_publish(graph: ComponentGraph, args: argparse.Namespace) -> int:
    """Write the files derived from the components' metadata: COMPONENTS.md and AUTHORS.md."""
    problems = _metadata_problems(graph)
    if problems:
        print(f"error: {len(problems)} metadata problem(s) - publish would write placeholders:",
              file=sys.stderr)
        for problem in problems:
            print(f"  {problem}", file=sys.stderr)
        return 1

    if not args.skip_verify and not args.check:
        print(f"==> verify ({len(graph)} component(s)); layout findings do not block a publish")
        for component in sorted(graph, key=lambda c: c.name):
            for level, message in _verify_component(component):
                if level == "error":
                    print(f"  error {message}")

    stamp = args.date or datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
    registry = registry_text(graph, stamp)
    registry_path = graph.root / "COMPONENTS.md"
    authors_path = graph.root / "AUTHORS.md"
    if not authors_path.is_file():
        print("error: AUTHORS.md is missing (publish updates its contributor section in place)",
              file=sys.stderr)
        return 1
    authors = authors_text(graph, authors_path.read_text(encoding="utf-8"))

    if args.check:
        stale: list[str] = []
        current_registry = registry_path.read_text(encoding="utf-8") if registry_path.is_file() else ""
        if _without_timestamp(current_registry) != _without_timestamp(registry):
            stale.append("COMPONENTS.md")
            _report_drift("COMPONENTS.md",
                          _listed_names(current_registry, r"^\| \[([^\]]+)\]"),
                          set(graph.names()))
        current_authors = authors_path.read_text(encoding="utf-8")
        if current_authors != authors:
            stale.append("AUTHORS.md")
            _report_drift("AUTHORS.md",
                          _listed_names(current_authors, r"^### (.+)$", AUTHORS_SECTION),
                          set(graph.names()))
        if stale:
            print(f"error: {', '.join(stale)} out of date - run `python Tools/components.py publish`",
                  file=sys.stderr)
            return 1
        print(f"COMPONENTS.md and AUTHORS.md are in sync with {len(graph)} component(s)")
        return 0

    _write_like(registry_path, registry)
    print(f"wrote COMPONENTS.md ({len(graph)} component(s))")
    _write_like(authors_path, authors)
    print("wrote AUTHORS.md (Component Contributors regenerated from the component metadata)")
    return 0


def _report_drift(label: str, listed: set[str], known: set[str]) -> None:
    """Name the components a stale generated file is missing (or lists without a component)."""
    missing = sorted(known - listed)
    extra = sorted(listed - known)
    if missing:
        print(f"  {label}: {len(missing)} component(s) not listed: {', '.join(missing)}")
    if extra:
        print(f"  {label}: {len(extra)} listed without a component: {', '.join(extra)}")


# ── entry point ─────────────────────────────────────────────────────────────

def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)

    p_list = sub.add_parser("list", help="list every component with its metadata")
    p_list.add_argument("--json", action="store_true")
    p_list.set_defaults(func=cmd_list)

    p_dep = sub.add_parser("dependents", help="components that depend on one component")
    p_dep.add_argument("--component", required=True)
    p_dep.add_argument("--direct", action="store_true", help="direct dependencies only")
    p_dep.add_argument("--json", action="store_true")
    p_dep.set_defaults(func=cmd_dependents)

    p_deps = sub.add_parser("dependencies", help="components one component needs")
    p_deps.add_argument("--component", required=True)
    p_deps.add_argument("--direct", action="store_true")
    p_deps.add_argument("--json", action="store_true")
    p_deps.set_defaults(func=cmd_dependencies)

    p_graph = sub.add_parser("graph", help="print the dependency graph")
    p_graph.add_argument("--format", choices=("text", "json", "dot"), default="text")
    p_graph.set_defaults(func=cmd_graph)

    p_changed = sub.add_parser("changed", help="components touched by the working tree changes")
    p_changed.add_argument("--base", default="HEAD", help="git revision to diff against")
    p_changed.add_argument("--format", choices=("text", "json", "github"), default="text",
                           help="'github' prints key=value lines for $GITHUB_OUTPUT")
    p_changed.add_argument("--json", action="store_true", help="alias for --format json")
    p_changed.set_defaults(func=cmd_changed)

    p_registry = sub.add_parser("registry", help="generate (or check) the COMPONENTS.md registry")
    p_registry.add_argument("--write", action="store_true", help="write COMPONENTS.md in place")
    p_registry.add_argument("--check", action="store_true",
                            help="fail when COMPONENTS.md does not match the metadata")
    p_registry.add_argument("--date", default=None, help="timestamp to stamp (default: now, UTC)")
    p_registry.set_defaults(func=cmd_registry)

    p_verify = sub.add_parser("verify", help="check the per-component requirements")
    p_verify.add_argument("--component", action="append", nargs="+",
                          help="limit to these components (space separated, repeatable)")
    p_verify.add_argument("--strict", action="store_true", help="treat warnings as failures")
    p_verify.add_argument("--changed", action="store_true",
                          help="verify the components the working tree changes (plus their dependents)")
    p_verify.add_argument("--base", default="HEAD", help="git revision --changed diffs against")
    p_verify.add_argument("--format", choices=("text", "github"), default="text",
                          help="'github' prints key=value lines for $GITHUB_OUTPUT")
    p_verify.set_defaults(func=cmd_verify)

    p_create = sub.add_parser("create", help="scaffold a new component (source, example, docs, tests)",
                              description="Scaffold a component. Run it without arguments to be asked "
                                          "for the name, type, description, author and version.")
    p_create.add_argument("name", nargs="?", metavar="Name", default=None,
                          help="PascalCase component name (ask for it when omitted)")
    p_create.add_argument("--type", choices=tuple(templates.COMPONENT_TYPES), default=None,
                          help="what the component is: a Control, a Node2D, a Resource or a plain class")
    p_create.add_argument("--description", default=None,
                          help="one-line description for the metadata and the docs")
    p_create.add_argument("--author", default=None, help="author (default: the git user name)")
    p_create.add_argument("--version", default=None, help=f"version (default: {DEFAULT_VERSION})")
    p_create.add_argument("--license", default="MIT")
    p_create.add_argument("--no-example", action="store_true", help="skip Example/<Name>/")
    p_create.add_argument("--no-test", action="store_true", help="skip Test/<Name>/")
    p_create.add_argument("--integration", action="store_true",
                          help="also scaffold Test/<Name>/Integration/ (Control only)")
    p_create.add_argument("--interactive", action="store_true",
                          help="ask for every value that was not given on the command line")
    p_create.add_argument("--yes", action="store_true",
                          help="never ask: take the defaults for whatever is missing")
    p_create.add_argument("--force", action="store_true", help="overwrite existing files")
    p_create.set_defaults(func=cmd_create)

    p_publish = sub.add_parser("publish",
                               help="write the files derived from the metadata (COMPONENTS.md, AUTHORS.md)")
    p_publish.add_argument("--check", action="store_true",
                           help="fail when the files do not match the metadata (CI)")
    p_publish.add_argument("--skip-verify", action="store_true",
                           help="do not print the per-component findings first")
    p_publish.add_argument("--date", default=None, help="registry timestamp to stamp (default: now, UTC)")
    p_publish.set_defaults(func=cmd_publish)

    return parser


def main(argv: list[str] | None = None) -> int:
    enable_utf8_output()
    args = build_parser().parse_args(argv)
    try:
        graph = ComponentGraph.load(project_root())
    except ToolError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    return args.func(graph, args)


if __name__ == "__main__":
    raise SystemExit(main())
