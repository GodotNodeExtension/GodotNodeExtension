"""Component discovery, the dependency graph and the per-component layout of the repository.

A component is a directory under `Component/` that ships a `component_info.json`. Everything else a
component owns is derived from its name, which is what makes the tools work for any component:

    Component/<Name>/                 sources, README.md, component_info.json
    Doc/<Name>/                       documentation
    Example/<Name>/                   demo scenes
    Test/<Name>/                      gdUnit4 test suites
    Test/<Name>/Integration/          suites that need Godot's rendering device
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Iterator

# Marker files that identify the repository root (checked in this order).
_ROOT_MARKERS = ("GodotNodeExtension.csproj", "Component")

#: Directories a component owns, keyed by the prefix used in a repository path.
OWNED_PREFIXES = ("Component", "Doc", "Example", "Test")


class ToolError(RuntimeError):
    """Raised when a tool cannot continue (missing project, unreadable metadata, ...)."""


def project_root(start: Path | str | None = None) -> Path:
    """Return the repository root, walking up from `start` (default: the tools directory)."""
    here = Path(start or Path(__file__).resolve().parent).resolve()
    for candidate in (here, *here.parents):
        if all((candidate / marker).exists() for marker in _ROOT_MARKERS):
            return candidate
    raise ToolError(f"no project root above {here} (looked for {', '.join(_ROOT_MARKERS)})")


@dataclass
class Component:
    """One component of the repository and the paths it owns."""

    name: str
    root: Path
    info: dict = field(default_factory=dict)
    dependencies: tuple[str, ...] = ()

    @property
    def info_path(self) -> Path:
        return self.root / "component_info.json"

    @property
    def sources(self) -> list[Path]:
        return sorted(self.root.rglob("*.cs"))

    @property
    def readme(self) -> Path:
        return self.root / "README.md"

    @property
    def doc_dir(self) -> Path:
        return self.root.parent.parent / "Doc" / self.name

    @property
    def example_dir(self) -> Path:
        return self.root.parent.parent / "Example" / self.name

    @property
    def test_dir(self) -> Path:
        return self.root.parent.parent / "Test" / self.name

    @property
    def integration_dir(self) -> Path:
        return self.test_dir / "Integration"

    @property
    def has_tests(self) -> bool:
        return self.test_dir.is_dir() and any(self.test_dir.rglob("*Test*.cs"))

    @property
    def has_integration_tests(self) -> bool:
        return self.integration_dir.is_dir() and any(self.integration_dir.rglob("*.cs"))

    @property
    def version(self) -> str:
        return str(self.info.get("version", ""))

    @property
    def description(self) -> str:
        return str(self.info.get("description", ""))

    def doc_files(self) -> list[Path]:
        """Markdown files of this component, English and Chinese pairs together."""
        if not self.doc_dir.is_dir():
            return []
        return sorted(self.doc_dir.glob("*.md"))

    def nuget_dependencies(self) -> tuple[str, ...]:
        deps = (self.info.get("dependencies") or {}).get("nuget") or []
        return tuple(str(d.get("name", "")) for d in deps if d.get("name"))


class ComponentGraph:
    """All components of the repository, indexed by name, with dependency queries."""

    def __init__(self, root: Path, components: dict[str, Component]):
        self.root = root
        self.components = components

    # ── construction ────────────────────────────────────────────────────────

    @classmethod
    def load(cls, root: Path | None = None) -> "ComponentGraph":
        root = project_root(root)
        components: dict[str, Component] = {}
        component_dir = root / "Component"
        if not component_dir.is_dir():
            raise ToolError(f"no Component directory in {root}")

        for path in sorted(component_dir.iterdir()):
            info_path = path / "component_info.json"
            if not path.is_dir() or not info_path.is_file():
                continue
            try:
                info = json.loads(info_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError as exc:
                raise ToolError(f"{info_path} is not valid JSON: {exc}") from exc
            direct = tuple(
                str(name)
                for name in ((info.get("dependencies") or {}).get("components") or [])
                if name
            )
            components[path.name] = Component(name=path.name, root=path, info=info,
                                              dependencies=direct)

        return cls(root, components)

    # ── queries ─────────────────────────────────────────────────────────────

    def __iter__(self) -> Iterator[Component]:
        return iter(self.components.values())

    def __len__(self) -> int:
        return len(self.components)

    def names(self) -> list[str]:
        return sorted(self.components)

    def get(self, name: str) -> Component:
        try:
            return self.components[name]
        except KeyError:
            raise ToolError(f"unknown component '{name}' (known: {', '.join(self.names())})") from None

    def resolve(self, names: Iterable[str]) -> list[Component]:
        return [self.get(name) for name in names]

    def dependencies_of(self, name: str, transitive: bool = False) -> list[str]:
        """Components `name` depends on (directly, or including their own dependencies)."""
        return self._walk(name, lambda c: c.dependencies, transitive)

    def dependents_of(self, name: str, transitive: bool = True) -> list[str]:
        """Components that depend on `name` - the reverse edge.

        This is what decides which components have to be retested after a change: a modification to
        `GodotSkia` can break `GodotChart` and `GodotMapsui`, so they belong in a normal test run.
        """
        return self._walk(name, lambda c: self._reverse_index().get(c.name, ()), transitive)

    def _walk(self, name: str, edges, transitive: bool) -> list[str]:
        start = self.get(name)
        seen: set[str] = set()
        stack = list(edges(start))
        while stack:
            current = stack.pop(0)
            if current in seen or current == name:
                continue
            seen.add(current)
            if transitive and current in self.components:
                stack.extend(edges(self.components[current]))
        return sorted(seen)

    def _reverse_index(self) -> dict[str, list[str]]:
        if not hasattr(self, "_reverse"):
            reverse: dict[str, list[str]] = {name: [] for name in self.components}
            for component in self.components.values():
                for dep in component.dependencies:
                    reverse.setdefault(dep, []).append(component.name)
            self._reverse = {k: sorted(v) for k, v in reverse.items()}
        return self._reverse

    def cycles(self) -> list[list[str]]:
        """Dependency cycles, reported as the component lists that form them."""
        found: list[list[str]] = []
        state: dict[str, int] = {}

        def visit(name: str, path: list[str]) -> None:
            state[name] = 1
            for dep in self.components[name].dependencies:
                if dep not in self.components:
                    continue
                if state.get(dep) == 1:
                    found.append(path[path.index(dep):] + [dep] if dep in path else path + [dep])
                elif state.get(dep, 0) == 0:
                    visit(dep, path + [dep])
            state[name] = 2

        for name in self.components:
            if state.get(name, 0) == 0:
                visit(name, [name])
        return found

    def missing_dependencies(self) -> list[tuple[str, str]]:
        """Pairs of (component, unknown dependency) declared in `component_info.json`."""
        return [
            (component.name, dep)
            for component in self.components.values()
            for dep in component.dependencies
            if dep not in self.components
        ]


def component_of_path(root: Path, path: Path | str) -> str | None:
    """Component a repository path belongs to, or None for repository-wide files.

    Only the four directories a component owns are mapped; anything else (the csproj, MainScene,
    addons, ...) is shared, and a change there can affect every component.
    """
    relative = Path(path)
    if relative.is_absolute():
        try:
            relative = relative.resolve().relative_to(root)
        except ValueError:
            return None

    parts = relative.parts
    if len(parts) >= 2 and parts[0] in OWNED_PREFIXES:
        return parts[1]
    return None


def load_graph(root: Path | None = None) -> ComponentGraph:
    """Convenience wrapper: load the component graph of the current project."""
    return ComponentGraph.load(root)
