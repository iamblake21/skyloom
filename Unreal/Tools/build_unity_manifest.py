#!/usr/bin/env python3
"""Build a deterministic, read-only inventory of the Unity source project."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path


GUID_RE = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)
SKIP_PARTS = {"Library", "Temp", "Logs", "Obj", "UserSettings", "Artifacts", "_Recovery"}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def unity_guid(asset: Path) -> str | None:
    meta = Path(str(asset) + ".meta")
    if not meta.is_file():
        return None
    match = GUID_RE.search(meta.read_text(encoding="utf-8", errors="replace"))
    return match.group(1).lower() if match else None


def classify(path: Path) -> str:
    suffix = path.suffix.lower()
    return {
        ".unity": "scene",
        ".prefab": "prefab",
        ".fbx": "mesh",
        ".obj": "mesh",
        ".blend": "source-mesh",
        ".png": "texture",
        ".jpg": "texture",
        ".jpeg": "texture",
        ".tga": "texture",
        ".exr": "texture",
        ".hdr": "texture",
        ".wav": "audio",
        ".ogg": "audio",
        ".mp3": "audio",
        ".anim": "animation",
        ".controller": "animation-controller",
        ".shader": "unity-shader",
        ".compute": "unity-compute",
        ".cs": "csharp",
        ".asset": "unity-asset",
        ".mat": "unity-material",
        ".terrainlayer": "terrain-layer",
    }.get(suffix, "other")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--unity", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    unity_root = args.unity.resolve()
    assets_root = unity_root / "Assets" / "_Project"
    if not assets_root.is_dir():
        raise SystemExit(f"Unity asset root not found: {assets_root}")

    entries = []
    counts: dict[str, int] = {}
    for asset in sorted(assets_root.rglob("*"), key=lambda p: p.as_posix().lower()):
        if not asset.is_file() or asset.suffix.lower() == ".meta":
            continue
        if any(part in SKIP_PARTS for part in asset.parts):
            continue
        relative = asset.relative_to(unity_root).as_posix()
        kind = classify(asset)
        counts[kind] = counts.get(kind, 0) + 1
        entries.append(
            {
                "source": relative,
                "guid": unity_guid(asset),
                "kind": kind,
                "bytes": asset.stat().st_size,
                "sha256": sha256(asset),
                "unrealObject": None,
                "status": "pending",
            }
        )

    document = {
        "schema": 1,
        "unityRoot": unity_root.as_posix(),
        "assetRoot": assets_root.relative_to(unity_root).as_posix(),
        "counts": dict(sorted(counts.items())),
        "total": len(entries),
        "entries": entries,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({"total": len(entries), "counts": document["counts"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
