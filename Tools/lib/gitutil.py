"""Git queries used to decide what has to be retested.

The test runner scopes itself from `git`: a change to a component (or to a file the component owns)
selects that component, and a change to a shared file (the csproj, the scene tree, the addons)
selects everything, because nothing can tell which components it affects.
"""

from __future__ import annotations

import subprocess
from dataclasses import dataclass, field
from pathlib import Path

from .component import ComponentGraph, ToolError, component_of_path

#: Paths that are not project content: the harness scratch, generated engine files and our own logs.
#: They are ignored when the changes are mapped onto components, otherwise every run would look like a
#: change to a shared file and escalate to the full suite.
IGNORED_PREFIXES = (
    ".agent-harness/",
    "tmp/",
    ".godot/",
)


def is_relevant(path: str) -> bool:
    """False for scratch/generated files that must not influence component selection."""
    return not path.replace("\\", "/").startswith(IGNORED_PREFIXES)


def git(root: Path, *args: str, check: bool = True) -> str:
    """Run a git command in `root` and return its stdout."""
    result = subprocess.run(
        ["git", *args],
        cwd=str(root), capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    if check and result.returncode != 0:
        message = (result.stderr or result.stdout).strip()
        raise ToolError(f"git {' '.join(args)} failed: {message}")
    return result.stdout


def head_revision(root: Path) -> str | None:
    """Current HEAD hash, or None in a repository without commits."""
    out = git(root, "rev-parse", "--verify", "HEAD", check=False).strip()
    return out or None


@dataclass
class ChangeSet:
    """Components affected by the working-tree changes, plus the shared files that were touched."""

    components: list[str] = field(default_factory=list)
    shared_files: list[str] = field(default_factory=list)
    files: list[str] = field(default_factory=list)
    base: str = "HEAD"
    fell_back_to_head: bool = False

    @property
    def shared_change(self) -> bool:
        return bool(self.shared_files)

    def summary(self) -> str:
        parts = [f"{len(self.files)} changed file(s) vs {self.base}"]
        parts.append(f"components: {', '.join(self.components) or 'none'}")
        if self.shared_files:
            parts.append(f"shared: {', '.join(self.shared_files)}")
        return "; ".join(parts)


def changed_files(root: Path, base: str = "HEAD") -> tuple[list[str], bool]:
    """Repository-relative paths changed against `base`, including untracked files.

    Returns (paths, fell_back_to_head). A missing `base` (shallow clone, no commits yet) falls back to
    the empty tree, which is reported so the caller can escalate instead of silently testing nothing.
    """
    fell_back = False
    if base.lower() in ("", "head"):
        if head_revision(root) is None:
            base = "4b825dc642cb6eb9a060e54bf8d69288fbee4904"  # git's empty tree
            fell_back = True
        else:
            base = "HEAD"

    out = git(root, "diff", "--name-only", "--diff-filter=ACMR", base, check=False)
    if not out.strip() and base not in ("HEAD", ""):
        fell_back = True
    files = {line.strip() for line in out.splitlines() if line.strip()}

    untracked = git(root, "ls-files", "--others", "--exclude-standard", check=False)
    files.update(line.strip() for line in untracked.splitlines() if line.strip())

    return sorted(files), fell_back


def changed_components(root: Path, graph: ComponentGraph, base: str = "HEAD") -> ChangeSet:
    """Map the working-tree changes onto components."""
    files, fell_back = changed_files(root, base)
    files = [path for path in files if is_relevant(path)]
    result = ChangeSet(files=files, base=base, fell_back_to_head=fell_back)

    for relative in files:
        name = component_of_path(root, relative)
        if name is None:
            result.shared_files.append(relative)
        elif name in graph.components and name not in result.components:
            result.components.append(name)

    result.components.sort()
    return result
