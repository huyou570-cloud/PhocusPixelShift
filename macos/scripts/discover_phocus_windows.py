#!/usr/bin/env python3
"""Discover a user-installed Windows Phocus executable and record local integration clues.

This is an inventory/probing tool. It does not copy application files, decompile binaries,
patch Phocus, or claim that a filename is a working pixel-shift implementation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import subprocess
from datetime import datetime, timezone
from pathlib import Path

NAMES = ("Phocus.exe", "Phocus64.exe")
HINT_WORDS = ("multishot", "multi-shot", "pixel", "shift", "merge", "export", "batch", "cli", "headless")
LAYOUT_DIRS = ("Plugins", "plugins", "Extensions", "extensions", "Export", "exports", "bin")


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(8 * 1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def default_roots() -> list[Path]:
    values = []
    for key in ("ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA"):
        if os.environ.get(key):
            values.append(Path(os.environ[key]))
    return values


def candidates(roots: list[Path]) -> list[Path]:
    found = set()
    for root in roots:
        root = root.expanduser()
        if not root.is_dir():
            continue
        # Known layouts first; bounded recursive search avoids walking a whole drive.
        for name in NAMES:
            for path in (root / "Hasselblad" / "Phocus" / name,
                         root / "Phocus" / name,
                         root / "Hasselblad" / name,
                         root / name):
                if path.is_file():
                    found.add(path.resolve())
        # Prune traversal itself, rather than filtering only after a full recursive walk.
        wanted = {name.lower() for name in NAMES}
        for directory, subdirs, files in os.walk(root, followlinks=False):
            here = Path(directory)
            depth = len(here.relative_to(root).parts)
            subdirs[:] = [] if depth >= 4 else [n for n in subdirs if not (here / n).is_symlink()]
            for name in files:
                path = here / name
                if name.lower() in wanted and not path.is_symlink():
                    found.add(path.resolve())
    return sorted(found)


def probe(executable: Path, timeout: float) -> dict:
    result = {"path": str(executable), "sha256": digest(executable), "probes": []}
    for argument in ("--version", "--help"):
        command = [str(executable), argument]
        try:
            proc = subprocess.run(command, capture_output=True, timeout=timeout,
                                  text=True, errors="replace",
                                  creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            output = (proc.stdout + "\n" + proc.stderr).strip()
            result["probes"].append({"argument": argument, "exitCode": proc.returncode,
                                     "output": output[:12000], "timedOut": False})
        except subprocess.TimeoutExpired as exc:
            result["probes"].append({"argument": argument, "exitCode": None,
                                     "output": str(exc)[:1000], "timedOut": True})
        except OSError as exc:
            result["probes"].append({"argument": argument, "exitCode": None,
                                     "output": str(exc), "timedOut": False})
    parent = executable.parent
    clues = []
    for directory in LAYOUT_DIRS:
        candidate = parent / directory
        if not candidate.is_dir():
            continue
        try:
            names = sorted(p.name for p in candidate.iterdir() if p.is_file())
        except OSError as exc:
            clues.append({"directory": str(candidate), "error": str(exc)})
            continue
        matches = [name for name in names if any(word in name.lower() for word in HINT_WORDS)]
        clues.append({"directory": str(candidate), "fileCount": len(names), "nameHints": matches[:200]})
    result["adjacentLayouts"] = clues
    result["interpretation"] = "Inventory only. Names and help output do not prove a working 4/6/16-frame merge path."
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("-o", "--output", type=Path, default=Path("phocus-windows-discovery.json"))
    parser.add_argument("--root", action="append", type=Path, default=[],
                        help="Installation root to inspect; repeatable")
    parser.add_argument("--phocus", type=Path, help="Explicit Phocus.exe; skips discovery")
    parser.add_argument("--probe-timeout", type=float, default=8.0)
    parser.add_argument("--no-probe", action="store_true", help="Inventory paths without launching Phocus")
    args = parser.parse_args()
    if platform.system() != "Windows" and not args.root and not args.phocus:
        parser.error("Run on Windows, or pass --root/--phocus for an offline inventory fixture")
    if args.probe_timeout <= 0:
        parser.error("--probe-timeout must be positive")
    roots = [p.expanduser().resolve() for p in (args.root or default_roots())]
    paths = [args.phocus.expanduser().resolve()] if args.phocus else candidates(roots)
    paths = [p for p in paths if p.is_file() and p.suffix.lower() == ".exe"]
    report = {"schema": "phocus-pixel-shift/windows-discovery/1",
              "createdAt": datetime.now(timezone.utc).isoformat(),
              "platform": platform.platform(), "roots": [str(p) for p in roots],
              "phocusExecutables": []}
    for path in paths:
        report["phocusExecutables"].append(
            {"path": str(path), "sha256": digest(path)} if args.no_probe else probe(path, args.probe_timeout))
    if not report["phocusExecutables"]:
        report["warnings"] = ["No Phocus.exe found. Provide --phocus or an installation --root."]
    else:
        report["warnings"] = [
            "This report records local clues only; it does not prove a Windows 4/6/16-frame merge entry.",
            "A bridge must be implemented or configured separately after a user-visible Phocus test.",
        ]
    out = args.output.expanduser().resolve()
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        parser.error(f"Refusing to overwrite existing report: {out}")
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n")
    print(json.dumps({"report": str(out), "executables": len(report["phocusExecutables"]),
                      "status": "candidate-found" if report["phocusExecutables"] else "not-found"}, ensure_ascii=False))
    return 0 if report["phocusExecutables"] else 3


if __name__ == "__main__":
    raise SystemExit(main())
