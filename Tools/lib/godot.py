"""Locating Godot, building the assembly and running the gdUnit4 suites.

The engine path is taken from `GODOT_BIN` first, then from a list of usual install locations, then
from `PATH`. Engine-level `ERROR:` lines fail a run: gdUnit4 reports assertion failures separately, so
an error logged by the engine (a broken signal connection, a leaked object, a stack overflow) would
otherwise hide inside a green run. The allow-list below names the fixtures that provoke an error on
purpose.
"""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

from .component import ToolError

#: Error lines the test fixtures raise on purpose (renderers that fail, a mark that throws).
ALLOWED_ENGINE_ERRORS = re.compile(
    r"GodotChart: (background|title|grid) renderer failed"
    # ThrowingMark's stages that report and continue (Render, ContributeScales): the case that provokes each
    # asserts the frame survived. Its HitTest failure is host-facing and propagates, so it logs nothing.
    r"|ThrowingMark\.\w+ failed"
    r"|AnimationController exit callback failed"
    # A draw callback that throws on purpose (Canvas2DControl retries the frame and reports the failure
    # through GD.PushError, which the engine prints as ERROR).
    r"|Canvas2DControl: canvas draw failed"
)

#: Usual Godot .NET install locations, checked in order after GODOT_BIN.
_GODOT_CANDIDATES = (
    "D:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe",
    "C:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe",
    "D:/Godot/Godot_v4.7.2-stable_mono_win64_console.exe",
    "C:/Program Files/Godot/Godot_v4.7.2-stable_mono_win64_console.exe",
)

GDUNIT_SCRIPT = "res://addons/gdUnit4/bin/GdUnitCmdTool.gd"


def godot_binary() -> Path:
    """Path of the Godot console executable to use."""
    override = os.environ.get("GODOT_BIN", "").strip()
    if override:
        path = Path(override)
        if not path.is_file():
            raise ToolError(f"GODOT_BIN points at a missing file: {path}")
        return path

    for candidate in _GODOT_CANDIDATES:
        path = Path(candidate)
        if path.is_file():
            return path

    for name in ("godot", "godot4", "Godot_v4.7.2-stable_mono_win64_console"):
        found = shutil.which(name)
        if found:
            return Path(found)

    raise ToolError(
        "Godot .NET executable not found - set GODOT_BIN to the mono/console build, e.g.\n"
        "  GODOT_BIN=/path/to/Godot_v4.7.2-stable_mono_win64_console"
    )


def _timed_out(expired: subprocess.TimeoutExpired, what: str, timeout: float | None) -> str:
    """Whatever a killed process printed, plus the reason it was killed."""
    parts: list[str] = []
    for stream in (expired.stdout, expired.stderr):
        if isinstance(stream, bytes):
            stream = stream.decode("utf-8", errors="replace")
        if stream:
            parts.append(stream)
    parts.append(f"\n[{what}] still running after {timeout} seconds - killed.\n")
    return "".join(parts)


def build(root: Path, quiet: bool = True, extra_args: tuple[str, ...] = (),
          timeout: float | None = None) -> tuple[bool, str]:
    """Build the C# assembly once; returns (ok, output).

    `extra_args` is forwarded to `dotnet build` - the sandbox tool passes `-nodeReuse:false` so a copy's
    build never depends on a shared MSBuild node - and `timeout` (seconds) reports a build that hangs
    instead of waiting forever (a stale compiler node has hung this repository's builds before).
    """
    command = ["dotnet", "build", "GodotNodeExtension.csproj", "-nologo", "-v", "q", *extra_args]
    try:
        result = subprocess.run(
            command, cwd=str(root), capture_output=True, text=True,
            encoding="utf-8", errors="replace", timeout=timeout,
        )
    except subprocess.TimeoutExpired as expired:
        return False, _timed_out(expired, "dotnet build", timeout)
    output = (result.stdout or "") + (result.stderr or "")
    if not quiet:
        print(output, end="" if output.endswith("\n") else "\n")
    return result.returncode == 0, output


def ensure_import(root: Path, engine: Path, log, timeout: float | None = None) -> None:
    """Run the import pass once when the project has never been opened."""
    if (root / ".godot" / "global_script_class_cache.cfg").exists():
        return
    log("==> importing project (first run)")
    try:
        subprocess.run(
            [str(engine), "--headless", "--path", ".", "--import"],
            cwd=str(root), capture_output=True, text=True,
            encoding="utf-8", errors="replace", timeout=timeout,
        )
    except subprocess.TimeoutExpired:
        log(f"warning: the import pass was still running after {timeout} seconds")


def gdunit_command(engine: Path, paths: list[str], render: bool,
                   rendering_driver: str | None = None) -> list[str]:
    """Command line that runs the given gdUnit4 suite paths.

    gdUnit4 takes every suite as `-a <res://path>`; a bare path is silently ignored, which looks like a
    green run that executed nothing.

    `rendering_driver` (e.g. `opengl3`) is forwarded to the engine so a run can exercise a renderer the
    default one never touches. Its absence means "let the engine pick".
    """
    command = [str(engine)]
    if rendering_driver:
        command += ["--rendering-driver", rendering_driver]
    if not render:
        command.append("--headless")
    command += ["--path", ".", "-s", GDUNIT_SCRIPT]
    for path in paths:
        command += ["-a", path]
    command.append("-c")
    if not render:
        command.append("--ignoreHeadlessMode")
    return command


def run_gdunit(root: Path, engine: Path, paths: list[str], render: bool = False,
               log_path: Path | None = None, rendering_driver: str | None = None,
               timeout: float | None = None) -> tuple[int, str]:
    """Run the suites and return (exit code, full log).

    With `timeout` (seconds) a run that never finishes is killed and reported as exit code 124, with the
    output it produced until then - an engine run does occasionally hang here (a leftover process from an
    earlier crash), and waiting for it is worse than failing it.
    """
    command = gdunit_command(engine, paths, render, rendering_driver)
    try:
        process = subprocess.run(
            command, cwd=str(root), capture_output=True, text=True,
            encoding="utf-8", errors="replace", timeout=timeout,
        )
        log = (process.stdout or "") + (process.stderr or "")
        status = process.returncode
    except subprocess.TimeoutExpired as expired:
        log = _timed_out(expired, "gdUnit4", timeout)
        status = 124
    if log_path is not None:
        log_path.parent.mkdir(parents=True, exist_ok=True)
        log_path.write_text(log, encoding="utf-8")
    return status, log


_ANSI = re.compile(r"\x1b\[[0-9;]*m")


def strip_ansi(text: str) -> str:
    return _ANSI.sub("", text)


def engine_errors(log: str) -> list[str]:
    """Engine `ERROR:` lines that are not on the allow-list."""
    return [
        line for line in strip_ansi(log).splitlines()
        if "ERROR:" in line and not ALLOWED_ENGINE_ERRORS.search(line)
    ]


def overall_summary(log: str) -> str:
    """The `Overall Summary:` line gdUnit4 prints, or an empty string."""
    for line in reversed(strip_ansi(log).splitlines()):
        if line.strip().startswith("Overall Summary:"):
            return line.strip()
    return ""


def skipped_cases(log: str) -> list[str]:
    """Cases that skipped themselves, e.g. because the run has no rendering device."""
    return [
        line.strip() for line in strip_ansi(log).splitlines()
        if line.strip().startswith("[skip]")
    ]


def failed_cases(log: str) -> list[str]:
    """Names of the test cases reported as FAILED."""
    return [
        line.strip() for line in strip_ansi(log).splitlines()
        if " FAILED" in line and line.strip().startswith("res://")
    ]


def python_executable() -> str:
    """Interpreter used to re-enter the tools from a wrapper script."""
    return sys.executable or "python"
