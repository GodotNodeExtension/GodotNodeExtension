#!/usr/bin/env python3
"""Run the gdUnit4 test suites of this repository, scoped to what a change can affect.

Three modes decide which components are tested:

    fast    only the components the working tree changes          (the inner development loop)
    normal  those components plus every component that depends on them
            (a change to GodotSkia also retests GodotChart and GodotMapsui)
    all     every component                                       (CI / release)

Examples (from the repository root):

    python Tools/run_tests.py                       # normal mode, headless
    python Tools/run_tests.py --mode fast
    python Tools/run_tests.py --mode all --render   # with a real rendering device
    python Tools/run_tests.py --component GodotChart
    python Tools/run_tests.py --integration --render
    python Tools/run_tests.py --list                # show the plan, run nothing

`--integration` adds the `Test/<Component>/Integration` suites, which drive the engine's real
rendering device instead of the fake canvas; they skip themselves when the run has none, so pass
`--render` to run them for real - and a `--render` run fails outright when a case still skips, because
a device-less run that reports green would hide exactly the regressions these cases exist for (use
`--fail-on-skip` to make any self-skipping case fail, e.g. for a nightly device job).
`--render` to actually execute them.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from lib.component import Component, ComponentGraph, ToolError, project_root  # noqa: E402
from lib.gitutil import changed_components  # noqa: E402
from lib import godot  # noqa: E402
from lib import enable_utf8_output  # noqa: E402

MODES = ("fast", "normal", "all")


class Plan:
    """The components to test and why each of them was selected."""

    def __init__(self, mode: str):
        self.mode = mode
        self.reasons: dict[str, str] = {}
        self.suites: list[str] = []
        self.escalated: str | None = None
        self.notes: list[str] = []

    def add(self, name: str, reason: str) -> None:
        self.reasons.setdefault(name, reason)

    @property
    def components(self) -> list[str]:
        return sorted(self.reasons)

    def describe(self) -> str:
        lines = [f"mode: {self.mode}"]
        if self.escalated:
            lines.append(f"escalated to all components: {self.escalated}")
        for note in self.notes:
            lines.append(f"note: {note}")
        if not self.reasons:
            lines.append("components: none")
        else:
            lines.append("components:")
            for name in self.components:
                lines.append(f"  {name:<20} {self.reasons[name]}")
        if self.suites:
            lines.append("suites:")
            for suite in self.suites:
                lines.append(f"  {suite}")
        return "\n".join(lines)


def build_plan(graph: ComponentGraph, args: argparse.Namespace) -> Plan:
    """Decide the components and the gdUnit4 suite paths for this run."""
    plan = Plan(args.mode)

    if args.suite:
        plan.suites = list(args.suite)
        plan.notes.append("suite paths given on the command line; component selection skipped")
        return plan

    if args.component:
        for component in graph.resolve(args.component):
            plan.add(component.name, "requested with --component")
    elif args.mode == "all":
        for component in graph:
            plan.add(component.name, "every component (--mode all)")
    elif args.changed:
        for name in args.changed:
            graph.get(name)                       # validates the name
            plan.add(name, "assumed changed (--changed)")
        if args.mode == "normal":
            for name in args.changed:
                for dependent in graph.dependents_of(name):
                    plan.add(dependent, f"depends on {name}")
    else:
        changes = changed_components(graph.root, graph, args.base)
        if changes.fell_back_to_head:
            plan.notes.append(f"could not diff against {args.base}; treating everything as changed")
        for name in changes.components:
            plan.add(name, "changed in the working tree")
        if args.mode == "normal":
            for name in changes.components:
                for dependent in graph.dependents_of(name):
                    plan.add(dependent, f"depends on {name}")
        if changes.shared_files:
            plan.escalated = (f"{len(changes.shared_files)} shared file(s) changed, e.g. "
                              f"{changes.shared_files[0]}")
            for component in graph:
                plan.add(component.name, "shared file changed")
        elif not changes.components:
            plan.notes.append("working tree is clean - nothing to test")

    # gdUnit4 discovers tests recursively, so one path per component is enough - and `--integration`
    # narrows the run to the suites that need a rendering device instead of adding duplicates.
    integration_only = args.integration
    selected = [graph.get(name) for name in plan.components]
    if integration_only:
        selected = _add_integration_dependents(graph, selected, plan)

    for component in selected:
        if integration_only:
            if component.has_integration_tests:
                plan.suites.append(f"res://{_relative(component.integration_dir, graph.root)}")
            continue
        if component.has_tests:
            plan.suites.append(f"res://{_relative(component.test_dir, graph.root)}")
        elif component.name in plan.reasons:
            plan.notes.append(f"{component.name}: no suite in Test/{component.name}/")

    plan.suites = _collapse(plan.suites)
    return plan


def _collapse(suites: list[str]) -> list[str]:
    """Drop suite paths that a parent path in the same list already covers."""
    ordered = sorted(set(suites), key=len)
    kept: list[str] = []
    for suite in ordered:
        if any(suite == parent or suite.startswith(parent.rstrip("/") + "/") for parent in kept):
            continue
        kept.append(suite)
    return sorted(kept)


def _add_integration_dependents(graph: ComponentGraph, selected: list[Component],
                                plan: Plan) -> list[Component]:
    """Integration suites exercise whole features, so their dependencies come along."""
    result = {c.name: c for c in selected}
    for component in list(selected):
        for dependency in graph.dependencies_of(component.name, transitive=True):
            if dependency not in result and graph.get(dependency).has_integration_tests:
                result[dependency] = graph.get(dependency)
                plan.add(dependency, f"integration dependency of {component.name}")
    return [result[name] for name in sorted(result)]


def _relative(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def main(argv: list[str] | None = None) -> int:
    enable_utf8_output()
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--mode", choices=MODES, default="normal",
                        help="fast = changed only, normal = changed + dependents (default), all")
    parser.add_argument("--component", action="append",
                        help="test one component (repeatable); overrides --mode selection")
    parser.add_argument("--suite", action="append",
                        help="explicit gdUnit4 suite path, e.g. res://Test/GodotChart/Marks")
    parser.add_argument("--integration", action="store_true",
                        help="run only the Test/<Component>/Integration suites (they need --render)")
    parser.add_argument("--render", action="store_true",
                        help="run with a real rendering device instead of --headless")
    parser.add_argument("--rendering-driver", default=None,
                        choices=("vulkan", "opengl3", "d3d12", "metal", "dummy"),
                        help="engine rendering driver to run under (default: the engine's own choice); "
                             "'dummy' has no rendering device, so use it without --render")
    parser.add_argument("--base", default="HEAD", help="git revision the fast/normal modes diff against")
    parser.add_argument("--changed", action="append",
                        help="pretend this component changed instead of asking git (repeatable)")
    parser.add_argument("--fail-on-skip", action="store_true",
                        help="fail when any case skips itself; a --render run fails on a device skip anyway")
    parser.add_argument("--no-build", action="store_true", help="skip the dotnet build")
    parser.add_argument("--list", action="store_true", help="print the plan and exit")
    parser.add_argument("--log", default=None, help="write the engine log to this path")
    args = parser.parse_args(argv)

    if args.rendering_driver == "dummy" and args.render:
        print("error: --rendering-driver dummy has no rendering device, so --render would fail every "
              "device-dependent case; drop one of the two", file=sys.stderr)
        return 2

    try:
        root = project_root()
        graph = ComponentGraph.load(root)
        plan = build_plan(graph, args)
    except ToolError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    print(plan.describe())
    if args.list:
        return 0

    if not plan.suites and not args.suite:
        print("nothing to run")
        return 0

    if not args.no_build:
        print("==> building C# assembly")
        ok, output = godot.build(root, quiet=True)
        if not ok:
            print(output)
            print("error: build failed", file=sys.stderr)
            return 1

    try:
        engine = godot.godot_binary()
    except ToolError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    log_path = Path(args.log) if args.log else root / "tmp" / "logs" / f"run-{plan.mode}.log"
    godot.ensure_import(root, engine, print)
    device = "rendering device" if args.render else "headless"
    driver = f", driver {args.rendering_driver}" if args.rendering_driver else ""
    print(f"==> running gdUnit4 ({device}{driver})")
    status, log = godot.run_gdunit(root, engine, plan.suites, render=args.render, log_path=log_path,
                                   rendering_driver=args.rendering_driver)

    summary = godot.overall_summary(log)
    if summary:
        print(summary)
    failures = godot.failed_cases(log)
    for case in failures:
        print(f"  {case}")

    skipped = godot.skipped_cases(log)
    skip_failure = ""
    if skipped:
        print(f"{len(skipped)} case(s) skipped themselves (device-dependent):")
        for case in skipped:
            print(f"  {case}")
        if args.render:
            # A render run asked for a device: skipping instead means the run silently covered less than
            # it claimed to, which is exactly how a real-backend regression slips through.
            skip_failure = f"--render was requested but {len(skipped)} case(s) skipped for lack of a device"
        elif args.fail_on_skip:
            skip_failure = f"--fail-on-skip: {len(skipped)} case(s) skipped themselves"
        else:
            print("  -> pass --render to execute them for real")

    print(f"(log: {log_path})")

    if skip_failure:
        print(f"error: {skip_failure}", file=sys.stderr)
        return 1

    unexpected = godot.engine_errors(log)
    if unexpected:
        print("\n==> unexpected engine errors (not assertion failures):", file=sys.stderr)
        for line in unexpected:
            print(line, file=sys.stderr)
        return 1

    return status


if __name__ == "__main__":
    raise SystemExit(main())
