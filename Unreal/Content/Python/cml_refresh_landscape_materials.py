"""Rebuild component-local Landscape material permutations for one map.

Landscape components cache generated material instances.  Reimporting or
rewiring a master material does not always invalidate those instances, which
can leave the viewport rendering an older, broken permutation.  This command
performs the same explicit rebuild used by the C++ importer and saves only the
requested level.
"""

from __future__ import annotations

import os

import unreal


DEFAULT_MAP = "/Game/Maps/A_10_StarterIsland_AxisPreview"


def main() -> None:
    map_path = os.environ.get("CML_REFRESH_MAP", DEFAULT_MAP)
    material_override_path = os.environ.get("CML_REFRESH_MATERIAL", "").strip()
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)

    if not level_editor.load_level(map_path):
        raise RuntimeError(f"Unable to load map: {map_path}")

    landscapes = [
        actor
        for actor in actor_subsystem.get_all_level_actors()
        if isinstance(actor, unreal.Landscape)
    ]
    if not landscapes:
        raise RuntimeError(f"No Landscape actor found in {map_path}")

    refreshed = 0
    for landscape in landscapes:
        if material_override_path:
            material_override = unreal.EditorAssetLibrary.load_asset(material_override_path)
            if not isinstance(material_override, unreal.MaterialInterface):
                raise RuntimeError(
                    f"Landscape material override is missing: {material_override_path}"
                )
            landscape.set_editor_property("landscape_material", material_override)
        if not unreal.CMLLandscapeImportLibrary.refresh_landscape_materials(landscape):
            raise RuntimeError(
                f"Failed to refresh Landscape material instances: {landscape.get_actor_label()}"
            )
        refreshed += 1

    if not level_editor.save_current_level():
        raise RuntimeError(f"Unable to save refreshed map: {map_path}")

    unreal.log(
        f"CML_REFRESH_LANDSCAPE_MATERIALS_SUCCEEDED map={map_path} "
        f"landscapes={refreshed} materialOverride={material_override_path or '<unchanged>'}"
    )


try:
    main()
except Exception as exc:
    unreal.log_error(f"CML_REFRESH_LANDSCAPE_MATERIALS_FAILED: {exc}")
    raise
