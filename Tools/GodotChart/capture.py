#!/usr/bin/env python3
"""Regenerate the images the GodotChart documentation shows.

`Example/GodotChart/BasicsDemo.tscn` holds one cell per chart kind and one per variant (a grouped bar,
a stacked area, a line chart with reference lines). This runs the capture scene
(`Test/GodotChart/Support/DocsCapture.tscn`), which renders each of those charts on its own and writes
a PNG per cell into `Doc/GodotChart/assets/`.

    python Tools/GodotChart/capture.py                 # regenerate every image
    python Tools/GodotChart/capture.py --check         # only verify what is on disk
    python Tools/GodotChart/capture.py --godot PATH    # use an explicit engine binary

Run it from the repository root. **Not headless on purpose**: the charts draw through the engine's real
device, so a headless run would produce empty surfaces - the same reason the component's test suites
run with a device.

The plan (cell name, file name, canvas size) lives in the capture scene's source and is read from
there, so this script and the scene cannot drift apart.
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import struct
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CAPTURE_SOURCE = ROOT / "Test" / "GodotChart" / "Support" / "DocsCapture.cs"
CAPTURE_SCENE = "res://Test/GodotChart/Support/DocsCapture.tscn"
ASSETS = ROOT / "Doc" / "GodotChart" / "assets"

#: One tuple per image in the capture scene: ("Cell name", "file name", width, height).
PLAN_LINE = re.compile(r'\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*(\d+)\s*,\s*(\d+)\s*\)')


def read_plan() -> list[tuple[str, str, int, int]]:
    """The image plan, read out of the capture scene's source."""
    if not CAPTURE_SOURCE.is_file():
        raise SystemExit(f"the capture scene is missing: {CAPTURE_SOURCE}")
    text = CAPTURE_SOURCE.read_text(encoding="utf-8")
    start = text.find("Plan =")
    end = text.find("];", start)
    if start < 0 or end < 0:
        raise SystemExit("cannot find the image plan in the capture scene")
    plan = [(cell, name, int(w), int(h)) for cell, name, w, h in PLAN_LINE.findall(text[start:end])]
    if not plan:
        raise SystemExit("the image plan is empty")
    return plan


def png_size(path: Path) -> tuple[int, int] | None:
    """Width and height from the PNG header, without opening the image in Godot."""
    try:
        with open(path, "rb") as handle:
            head = handle.read(24)
    except OSError:
        return None
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        return None
    return struct.unpack(">II", head[16:24])


def verify(plan: list[tuple[str, str, int, int]]) -> list[str]:
    """Report the images that are missing or the wrong size."""
    problems: list[str] = []
    for _, name, width, height in plan:
        path = ASSETS / f"{name}.png"
        size = png_size(path)
        if size is None:
            problems.append(f"{name}.png is missing or not a PNG")
        elif size != (width, height):
            problems.append(f"{name}.png is {size[0]}x{size[1]}, expected {width}x{height}")
    return problems


def find_engine(explicit: str | None) -> str:
    """The engine binary: --godot, then GODOT_BIN, then a godot on PATH."""
    for candidate in (explicit, os.environ.get("GODOT_BIN")):
        if candidate:
            path = Path(candidate)
            if path.is_file():
                return str(path)
            raise SystemExit(f"no such engine binary: {candidate}")
    for name in ("godot4", "godot", "Godot", "Godot_v4.7-stable_mono_win64_console.exe"):
        found = shutil.which(name)
        if found:
            return found
    raise SystemExit("cannot find the engine; pass --godot PATH or set GODOT_BIN")


def main() -> None:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true",
                        help="verify the images on disk without rendering anything")
    parser.add_argument("--godot", help="engine binary (default: GODOT_BIN, then PATH)")
    args = parser.parse_args()

    plan = read_plan()
    print(f"{len(plan)} image(s) planned, {ASSETS.relative_to(ROOT).as_posix()}/")

    if not args.check:
        engine = find_engine(args.godot)
        print(f"engine: {engine}")
        print(f"running {CAPTURE_SCENE} (with a real rendering device)")
        result = subprocess.run(
            [engine, "--path", str(ROOT), CAPTURE_SCENE],
            cwd=ROOT, text=True, encoding="utf-8", errors="replace",
        )
        if result.returncode != 0:
            print(f"the capture scene failed (exit {result.returncode})", file=sys.stderr)

    problems = verify(plan)
    if problems:
        print(f"{len(problems)} problem(s):", file=sys.stderr)
        for problem in problems:
            print(f"  {problem}", file=sys.stderr)
        raise SystemExit(1)

    print(f"every image is present and the size the plan asks for "
          f"({sum(1 for _ in plan)} checked)")
    for _, name, width, height in plan:
        print(f"  {name}.png {width}x{height}")


if __name__ == "__main__":
    main()
