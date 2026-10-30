#!/usr/bin/env python3
"""Developer-path recompile pipeline: validated disc -> generated C# -> NascarRumbleRecompLib.dll.

Usage:
    python tools/build_lib.py [--cue PATH]

Without --cue, scans NascarRumbleRecompLib/private/ (one level deep) for a disc image.
The player path runs the same stages from the setup screen; see docs/BUILDING.md.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LIB = ROOT / "NascarRumbleRecompLib"
LOCK = json.loads((LIB / "recompone.lock.json").read_text(encoding="utf-8"))
TOOL = ROOT / "thirdparty" / "RecompOne"
PATCH = LIB / "patches" / "recompone-runtime.patch"
CONFIG = LIB / "config" / "nascar.json"
FUNCMAP = LIB / "config" / "nascar_funcmap.json"
GENERATED = LIB / "generated"
HOST = ROOT / "NascarRumbleRecomp" / "bin" / "Release" / "net10.0" / "FullRumble.exe"

sys.path.insert(0, str(ROOT / "tools"))
from augment_funcmap import augment  # noqa: E402


def run(command: list[str], cwd: Path = ROOT) -> None:
    print("+", " ".join(str(part) for part in command))
    subprocess.run(command, cwd=cwd, check=True)


def dotnet_path() -> str:
    candidates = [shutil.which("dotnet"), str(Path.home() / ".dotnet" / "dotnet")]
    for candidate in candidates:
        if not candidate or not Path(candidate).is_file():
            continue
        result = subprocess.run([candidate, "--version"], text=True, capture_output=True, check=True)
        if result.stdout.strip().startswith("10."):
            return candidate
    raise SystemExit(".NET 10 SDK not found on PATH.")


def find_disc(cue: str | None) -> str:
    if cue:
        return str(Path(cue).resolve())
    private = LIB / "private"
    if not private.is_dir():
        raise SystemExit(
            "No disc given. Pass --cue PATH or put your .cue/.bin/.iso in NascarRumbleRecompLib/private/."
        )
    candidates: list[Path] = []
    for pattern in ("*.cue", "*.bin", "*.iso"):
        candidates += list(private.glob(pattern)) + [p for sub in private.iterdir() if sub.is_dir() for p in sub.glob(pattern)]
    cues = {p.with_suffix(".bin") for p in candidates if p.suffix == ".cue"}
    candidates = [p for p in candidates if not (p.suffix == ".bin" and p in cues)]
    if not candidates:
        raise SystemExit("NascarRumbleRecompLib/private/ contains no .cue, .bin or .iso.")
    if len(candidates) > 1:
        raise SystemExit("Several images in private/; pass --cue PATH with the one to use: "
                         + ", ".join(p.name for p in candidates))
    return str(candidates[0].resolve())


def ensure_tool(dotnet: str) -> None:
    if not (TOOL / "RecompOne.Runtime").is_dir():
        run(["git", "submodule", "update", "--init", "--recursive", "--", "thirdparty/RecompOne"])
    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=TOOL, text=True, capture_output=True, check=True).stdout.strip()
    if head != LOCK["commit"]:
        subprocess.run(["git", "checkout", "-q", LOCK["commit"]], cwd=TOOL, check=True)
    applied = subprocess.run(["git", "apply", "--unidiff-zero", "--reverse", "--check", str(PATCH)], cwd=TOOL).returncode == 0
    if not applied:
        run(["git", "apply", "--unidiff-zero", str(PATCH)], cwd=TOOL)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cue")
    args = parser.parse_args()

    disc = find_disc(args.cue)
    if not HOST.is_file():
        run([dotnet_path(), "build", str(ROOT / "NascarRumbleRecomp"), "-c", "Release"])
    check = subprocess.run([str(HOST), "--check-disc", disc], text=True)
    if check.returncode != 0:
        raise SystemExit("Disc validation failed; refusing to recompile.")

    dotnet = dotnet_path()
    ensure_tool(dotnet)
    run([dotnet, "build", str(TOOL / "RecompOne.Recompiler"), "-c", "Release"])
    recompiler = TOOL / "RecompOne.Recompiler" / "bin" / "Release" / "net10.0" / "recompone.dll"

    GENERATED.mkdir(exist_ok=True)
    build_map = GENERATED / "nascar_funcmap.build.json"
    shutil.copyfile(FUNCMAP, build_map)
    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    config["cue"] = disc
    config["funcMap"] = str(build_map)
    build_config = GENERATED / "nascar.build.json"
    build_config.write_text(json.dumps(config, indent=1), encoding="utf-8")

    for _ in range(6):
        run([dotnet, str(recompiler), str(build_config)])
        if not augment(build_map, GENERATED / "Recompiled" / "main.cs"):
            break
    else:
        raise SystemExit("funcMap did not converge after six recompiler passes.")

    run([dotnet, "build", str(LIB), "-c", "Release"])

    # Install the library next to the host so it resolves RecompOne.Runtime against the copy the
    # host already loaded (the host loads this dll by reflection at startup).
    built = LIB / "bin" / "Release" / "net10.0" / "NascarRumbleRecompLib.dll"
    host_dir = HOST.parent
    if built.is_file() and host_dir.is_dir():
        shutil.copyfile(built, host_dir / built.name)
        print("+ installed", built.name, "->", host_dir)

    print("PASS — NascarRumbleRecompLib.dll built from", disc)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
