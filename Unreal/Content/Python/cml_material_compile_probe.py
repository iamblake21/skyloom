"""Compile one existing Unreal material in a fresh editor process.

Material graph authoring can emit transient compile warnings while expressions
are being deleted and rebuilt.  This probe loads the saved graph in a clean
process and recompiles only that final state, so the RHI log is an unambiguous
validation source.
"""

from __future__ import annotations

import os

import unreal


def main() -> None:
    object_path = os.environ.get("CML_MATERIAL_OBJECT", "").strip()
    if not object_path:
        raise RuntimeError("CML_MATERIAL_OBJECT is required")

    material = unreal.EditorAssetLibrary.load_asset(object_path)
    if not isinstance(material, unreal.Material):
        raise RuntimeError(f"Not a Material: {object_path}")

    unreal.MaterialEditingLibrary.recompile_material(material)
    unreal.log(f"CML_MATERIAL_COMPILE_PROBE_SUCCEEDED object={object_path}")


try:
    main()
except Exception as exc:
    unreal.log_error(f"CML_MATERIAL_COMPILE_PROBE_FAILED: {exc}")
    raise
