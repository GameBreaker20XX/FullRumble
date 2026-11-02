#!/usr/bin/env python3
"""Legal gate: reject game-media artifacts and oversized payload files.

Usage:
    python tools/legal_lint.py <root> [--artifacts]

Default mode scans a repository tree, skipping VCS metadata and third-party
submodule contents. --artifacts scans a release staging directory, including
the members of ZIP archives.
"""

from __future__ import annotations

import argparse
import subprocess
import sys
import zipfile
from pathlib import Path
from pathlib import PurePosixPath

FORBIDDEN_EXTENSIONS = {
    ".bin", ".cue", ".iso", ".img", ".mdf", ".mds", ".chd", ".sav", ".mcd",
}
FORBIDDEN_PATH_PARTS = {"generated", "private", "recompiled", "ppc"}
FORBIDDEN_NAME_MARKERS = ("slus_010", "slus-01068", "glbldata")
MAX_FILE_BYTES = 25 * 1024 * 1024
SKIP_DIRS = {".git", ".github", "thirdparty", "backup", "out", "bin", "obj"}


def tracked_files(root: Path) -> list[Path] | None:
    if not (root / ".git").exists():
        return None
    result = subprocess.run(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        cwd=root,
        capture_output=True,
        check=True,
    )
    return [root / name for name in result.stdout.decode("utf-8").split("\0") if name]


def scan(root: Path, artifacts_mode: bool) -> list[str]:
    violations: list[str] = []
    if artifacts_mode:
        files = [p for p in sorted(root.rglob("*")) if p.is_file()]
    else:
        files = tracked_files(root)
        if files is None:
            files = [
                p for p in sorted(root.rglob("*"))
                if p.is_file() and not ({part.lower() for part in p.relative_to(root).parts[:-1]} & SKIP_DIRS)
            ]
    for path in files:
        relative = PurePosixPath(path.relative_to(root).as_posix())
        check_item(relative, path.stat().st_size, violations, check_size=path.suffix.lower() != ".zip")

        if path.suffix.lower() == ".zip":
            try:
                with zipfile.ZipFile(path) as archive:
                    for member in archive.infolist():
                        if member.is_dir():
                            continue
                        member_path = PurePosixPath(member.filename.replace("\\", "/"))
                        check_item(
                            PurePosixPath(f"{relative}!/{member_path}"),
                            member.file_size,
                            violations,
                        )
            except zipfile.BadZipFile:
                violations.append(f"invalid ZIP archive: {relative}")
    return violations


def check_item(relative: PurePosixPath, size: int, violations: list[str],
               check_size: bool = True) -> None:
    lowered = {part.lower() for part in relative.parts}
    name = relative.name.lower()

    if relative.suffix.lower() in FORBIDDEN_EXTENSIONS:
        violations.append(f"forbidden extension: {relative}")
    if lowered & FORBIDDEN_PATH_PARTS:
        violations.append(f"generated/recompiled path: {relative}")
    if any(marker in name for marker in FORBIDDEN_NAME_MARKERS):
        violations.append(f"game-derived name: {relative}")
    if check_size and size > MAX_FILE_BYTES:
        violations.append(
            f"size limit ({MAX_FILE_BYTES // (1024 * 1024)} MiB) exceeded: {relative}"
        )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--artifacts", action="store_true",
                        help="scan a release staging directory with no skips")
    args = parser.parse_args()

    root = args.root.resolve()
    if not root.is_dir():
        print(f"legal lint: not a directory: {root}", file=sys.stderr)
        return 2

    violations = scan(root, args.artifacts)
    if violations:
        print(f"LEGAL LINT FAILED — {len(violations)} violation(s) in {root}:")
        for violation in violations:
            print(f"  - {violation}")
        return 1

    print(f"legal lint: OK ({root})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
