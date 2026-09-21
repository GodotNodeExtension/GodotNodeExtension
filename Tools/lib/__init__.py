"""Shared helpers for the repository tools.

Every tool in `Tools/` is component-agnostic: it discovers the components under `Component/`, reads
their `component_info.json` for metadata and dependencies, and works on any subset of them. Nothing
here depends on a third-party package, so the tools run with a plain Python 3 interpreter.
"""

from .component import (  # noqa: F401
    Component,
    ComponentGraph,
    component_of_path,
    load_graph,
    project_root,
)


def enable_utf8_output() -> None:
    """Let the tools print emoji and CJK on a console whose encoding cannot represent them.

    Windows terminals default to a legacy code page (GBK here), and printing a status emoji or a
    Chinese component name raised `UnicodeEncodeError` and aborted the tool. Reconfiguring the streams
    to UTF-8 with `errors="replace"` keeps the output readable instead of fatal.
    """
    import sys

    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")   # type: ignore[union-attr]
        except (AttributeError, OSError, ValueError):
            pass
