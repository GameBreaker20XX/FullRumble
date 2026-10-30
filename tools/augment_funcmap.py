#!/usr/bin/env python3
"""Add literal funcMap targets that RecompOne left without a dispatcher.

Derived from augment_recompone_funcmap.py of the MIT-licensed NASCAR Rumble
ModSDK (c) andreigor17, reused with attribution; see LICENSE.
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


CALL_PATTERN = re.compile(r"Dispatcher\.Call\(c, m, 0x([0-9A-Fa-f]+)u\)")


def augment(map_path: Path, generated_path: Path) -> list[dict[str, object]]:
    document = json.loads(map_path.read_text(encoding="utf-8"))
    functions = document["functions"]
    known = {int(function["address"], 16) for function in functions}
    calls = {
        int(address, 16)
        for address in CALL_PATTERN.findall(generated_path.read_text(encoding="utf-8"))
    }
    missing = sorted(calls - known)
    if not missing:
        return []

    ranges = sorted(
        (
            int(function["address"], 16),
            int(function["size"]),
        )
        for function in functions
    )
    additions: list[dict[str, object]] = []
    for target in missing:
        owners = [entry for entry in ranges if entry[0] < target < entry[0] + entry[1]]
        if owners:
            start, size = min(owners, key=lambda entry: entry[1])
            end = start + size
        else:
            following = [start for start, _ in ranges if start > target]
            if not following:
                raise ValueError(f"0x{target:08x}: no upper bound in the funcMap")
            end = min(following)
        size = end - target
        if target % 4 != 0 or size <= 0 or size % 4 != 0:
            raise ValueError(f"0x{target:08x}: invalid auxiliary range of {size} bytes")
        additions.append(
            {
                "address": f"{target:08x}",
                "name": f"RECOMP_LABEL_{target:08X}",
                "size": size,
            }
        )

    functions.extend(additions)
    functions.sort(key=lambda function: int(function["address"], 16))
    map_path.write_text(json.dumps(document, indent=1) + "\n", encoding="utf-8")
    return additions


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--map", type=Path, default=Path("recompone/nascar_funcmap.json")
    )
    parser.add_argument(
        "--generated", type=Path, default=Path("recompone/Recompiled/main.cs")
    )
    args = parser.parse_args()
    additions = augment(args.map, args.generated)
    print(f"PASS — {len(additions)} auxiliary targets added to the RecompOne funcMap")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
