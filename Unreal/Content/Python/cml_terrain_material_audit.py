"""Read-only audit of the active Landscape material contract and bindings."""

from __future__ import annotations

import json
import os
import traceback
from pathlib import Path

import unreal


MAP_PATH = os.environ.get(
    "CML_TERRAIN_AUDIT_MAP", "/Game/Maps/A_10_StarterIsland"
).strip()


def _path(value) -> str:
    return value.get_path_name() if value is not None else ""


def _parameter_names(kind: str, material):
    function = getattr(unreal.MaterialEditingLibrary, f"get_{kind}_parameter_names")
    return [str(value) for value in function(material)]


def _parameter_value(kind: str, material, name: str):
    function = getattr(
        unreal.MaterialEditingLibrary, f"get_material_instance_{kind}_parameter_value"
    )
    value = function(material, name)
    if kind == "texture":
        return _path(value)
    if kind == "vector":
        return [float(value.r), float(value.g), float(value.b), float(value.a)]
    return float(value)


def main() -> int:
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    landscapes = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Landscape)
    results = []
    for actor in landscapes:
        material = actor.get_editor_property("landscape_material")
        entry = {
            "actor": actor.get_actor_label(),
            "material": _path(material),
            "parent": _path(material.get_editor_property("parent"))
            if isinstance(material, unreal.MaterialInstanceConstant)
            else "",
            "textures": {},
            "scalars": {},
            "vectors": {},
        }
        for kind, key in (("texture", "textures"), ("scalar", "scalars"), ("vector", "vectors")):
            for name in _parameter_names(kind, material):
                try:
                    entry[key][name] = _parameter_value(kind, material, name)
                except Exception as error:  # noqa: BLE001 - preserve all other bindings
                    entry[key][name] = f"ERROR: {error}"
        results.append(entry)

    output = Path(unreal.Paths.project_dir()) / "Migration" / "terrain_material_audit.json"
    temporary = output.with_suffix(".json.tmp")
    temporary.write_text(
        json.dumps({"schema": 1, "map": MAP_PATH, "landscapes": results}, indent=2)
        + "\n",
        encoding="utf-8",
    )
    temporary.replace(output)
    unreal.log(f"[CML Terrain Material Audit] wrote {output}")
    return 0 if results else 2


try:
    _code = main()
except Exception:  # noqa: BLE001
    unreal.log_error(traceback.format_exc())
    _code = 1

if _code:
    unreal.log_error(f"CML_TERRAIN_MATERIAL_AUDIT_FAILED code={_code}")
else:
    unreal.log("CML_TERRAIN_MATERIAL_AUDIT_SUCCEEDED")
