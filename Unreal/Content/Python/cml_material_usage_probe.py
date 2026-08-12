"""Report and enable Landscape material usage for one master material."""

from __future__ import annotations

import json
import os
from pathlib import Path

import unreal


def main() -> None:
    object_path = os.environ.get(
        "CML_MATERIAL_OBJECT",
        "/Game/Migration/Masters/M_CML_Env_TerrainSplat.M_CML_Env_TerrainSplat",
    )
    material = unreal.EditorAssetLibrary.load_asset(object_path)
    if not isinstance(material, unreal.Material):
        raise RuntimeError(f"Not a Material: {object_path}")

    candidates = (
        "used_with_landscape",
        "b_used_with_landscape",
        "automatically_set_usage_in_editor",
        "b_automatically_set_usage_in_editor",
    )
    result = {"object": object_path, "properties": {}, "dirLandscape": []}
    result["dirLandscape"] = sorted(
        name for name in dir(material) if "landscape" in name.lower() or "usage" in name.lower()
    )
    for name in candidates:
        try:
            before = material.get_editor_property(name)
            result["properties"][name] = {"before": str(before)}
            if name in ("used_with_landscape", "b_used_with_landscape") and not bool(before):
                material.set_editor_property(name, True)
                result["properties"][name]["after"] = str(material.get_editor_property(name))
        except Exception as exc:
            result["properties"][name] = {"error": str(exc)}

    unreal.MaterialEditingLibrary.recompile_material(material)
    unreal.EditorAssetLibrary.save_loaded_asset(material, only_if_is_dirty=False)
    report = Path(unreal.Paths.project_saved_dir()) / "MigrationValidation" / "material_usage_probe.json"
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    unreal.log(f"CML_MATERIAL_USAGE_PROBE_SUCCEEDED report={report}")


main()
