"""Extract Unity YAML Texture2D assets into lossless PNG source files."""

from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path

from PIL import Image


PROJECT_ROOT = Path(__file__).resolve().parents[1]
MANIFEST_PATH = PROJECT_ROOT / "Migration" / "unity_asset_manifest.json"
OUTPUT_ROOT = PROJECT_ROOT / "Migration" / "UnityEmbeddedTextures"
REPORT_PATH = PROJECT_ROOT / "Migration" / "unity_embedded_texture_extract_report.json"


def capture(pattern: str, text: str, source: Path) -> str:
    match = re.search(pattern, text, re.MULTILINE)
    if not match:
        raise ValueError(f"Missing {pattern!r} in {source}")
    return match.group(1)


def main() -> int:
    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    unity_root = Path(manifest["unityRoot"])
    results: list[dict] = []
    for entry in manifest["entries"]:
        if entry["kind"] != "unity-asset":
            continue
        source = unity_root / Path(entry["source"])
        text = source.read_text(encoding="utf-8", errors="replace")
        if "--- !u!28 " not in text:
            continue
        try:
            name = capture(r"^  m_Name:\s*(.+)$", text, source).strip()
            width = int(capture(r"^  m_Width:\s*(\d+)$", text, source))
            height = int(capture(r"^  m_Height:\s*(\d+)$", text, source))
            texture_format = int(capture(r"^  m_TextureFormat:\s*(\d+)$", text, source))
            if texture_format != 4:
                raise ValueError(
                    f"Unsupported Unity TextureFormat {texture_format}; expected RGBA32 (4)"
                )
            raw_hex = capture(r"^  _typelessdata:\s*([0-9a-fA-F]+)$", text, source)
            raw = bytes.fromhex(raw_hex)
            base_mip_size = width * height * 4
            if len(raw) < base_mip_size:
                raise ValueError(
                    f"Texture payload too small: {len(raw)} < {base_mip_size}"
                )
            image = Image.frombytes("RGBA", (width, height), raw[:base_mip_size])
            image = image.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
            relative = Path(entry["source"])
            destination = OUTPUT_ROOT / relative.parent / f"{name}.png"
            destination.parent.mkdir(parents=True, exist_ok=True)
            image.save(destination, format="PNG", optimize=False, compress_level=6)
            digest = hashlib.sha256(destination.read_bytes()).hexdigest()
            results.append(
                {
                    "source": entry["source"],
                    "guid": entry["guid"],
                    "sha256": entry["sha256"],
                    "name": name,
                    "width": width,
                    "height": height,
                    "format": "RGBA32",
                    "output": destination.relative_to(PROJECT_ROOT).as_posix(),
                    "outputSha256": digest,
                    "normalMap": name.lower().endswith(("_n", "_normal", "_normalmap")),
                    "status": "extracted",
                }
            )
        except Exception as exception:
            results.append(
                {
                    "source": entry["source"],
                    "guid": entry["guid"],
                    "sha256": entry["sha256"],
                    "status": "failed",
                    "error": str(exception),
                }
            )

    report = {
        "schema": 1,
        "requested": len(results),
        "extracted": sum(item["status"] == "extracted" for item in results),
        "failed": sum(item["status"] != "extracted" for item in results),
        "results": results,
    }
    REPORT_PATH.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(
        f"Unity embedded textures: extracted={report['extracted']} "
        f"failed={report['failed']} report={REPORT_PATH}"
    )
    return 0 if report["failed"] == 0 else 2


if __name__ == "__main__":
    raise SystemExit(main())
