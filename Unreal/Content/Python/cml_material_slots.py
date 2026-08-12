"""Resolve material slots left unbound by Unity model imports.

Unity can reference an FBX as a prefab even when its MeshRenderer/material
bindings live in the model importer rather than in a YAML prefab. Unreal's FBX
import then preserves the slot *name* but assigns WorldGridMaterial. Companion
GLB imports in this project contain the authored materials, so slot-name plus
source-path proximity gives us a deterministic, data-backed repair.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

import unreal


WORLD_GRID_PATHS = {
    "/Engine/EngineMaterials/WorldGridMaterial.WorldGridMaterial",
    "/Engine/EngineMaterials/DefaultMaterial.DefaultMaterial",
}

SLOT_ALIASES = {
    # terrain_bot.fbx names its sole section Terrain_Surface. The matching
    # Unity material is the authored cliff/underbody material used by the
    # exact TerrainUnderbody mesh in the same environment.
    "terrain_surface": (
        "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Materials/"
        "M_StarterIsland_UnderbodyCliff.M_StarterIsland_UnderbodyCliff"
    ),
}

# Some FBX/GLB conversions collapse an authored material name to the generic
# ``Material_0`` (or even retain WorldGridMaterial).  That name is ambiguous on
# its own, but the source folder is not.  Keep these rules path-specific so a
# preview cloud can never receive a cliff material merely because both slots
# are called Material_0.
MESH_CONTEXT_ALIASES = (
    (
        "/migration/embeddedmeshes/sm_mesh_starterisland_underbody_exact",
        "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Materials/"
        "M_StarterIsland_UnderbodyCliff.M_StarterIsland_UnderbodyCliff",
    ),
    (
        "/originalcliffmasskit/",
        "/Game/Migrated/Project/Art/Environment/OriginalCliffMassKit/Materials/"
        "M_OriginalCliffMass.M_OriginalCliffMass",
    ),
    (
        "/starterisland/portal/",
        "/Game/Migrated/Project/Art/Environment/StarterIsland/Portal/Materials/"
        "M_ENV_AncientStonePortal_Stone.M_ENV_AncientStonePortal_Stone",
    ),
    (
        "/verticalrockkit_sculpted/",
        "/Game/Migrated/Project/Art/Environment/StarterIsland/VerticalRockKit_Sculpted/Materials/"
        "M_VRKS_AutoGrass.M_VRKS_AutoGrass",
    ),
    (
        "/stylizedrockworldkit/",
        "/Game/Migrated/Project/Art/Environment/StylizedRockWorldKit/Materials/"
        "M_ReferenceFacetedRock.M_ReferenceFacetedRock",
    ),
    (
        "/cleanroomvisualtests/rocks/",
        "/Game/Migrated/Project/Art/Environment/CleanRoomVisualTests/Materials/"
        "M_CR_Cliff.M_CR_Cliff",
    ),
    (
        "/cleanroomvisualtests/grass/",
        "/Game/Migrated/Project/Art/Environment/CleanRoomVisualTests/Materials/"
        "M_CR_GrassWind.M_CR_GrassWind",
    ),
    (
        "/cleanroomvisualtests/clouds/",
        "/Game/Migrated/Project/Art/Environment/CleanRoomVisualTests/Materials/"
        "M_CR_Cloud.M_CR_Cloud",
    ),
)


def _object_name(path: str) -> str:
    return path.rsplit(".", 1)[-1].lower()


def _package_segments(path: str) -> list[str]:
    package = path.split(".", 1)[0]
    return [segment.lower() for segment in package.strip("/").split("/")]


def _common_prefix_score(left: str, right: str) -> int:
    score = 0
    for a, b in zip(_package_segments(left), _package_segments(right)):
        if a != b:
            break
        score += 1
    return score


def material_is_missing(material) -> bool:
    if material is None:
        return True
    try:
        return material.get_path_name() in WORLD_GRID_PATHS
    except Exception:
        return True


def _slot_names(static_material) -> list[str]:
    names: list[str] = []
    for property_name in ("material_slot_name", "imported_material_slot_name"):
        try:
            value = str(static_material.get_editor_property(property_name)).strip()
        except Exception:
            value = ""
        if value and value.lower() != "none" and value.lower() not in {name.lower() for name in names}:
            names.append(value)
    return names


@dataclass
class MaterialSlotIndex:
    paths_by_name: dict[str, list[str]] = field(default_factory=dict)

    @classmethod
    def from_project(cls, project_dir: Path) -> "MaterialSlotIndex":
        index = cls()
        migration = project_dir / "Migration"
        material_report = json.loads(
            (migration / "unity_material_import_report.json").read_text("utf-8")
        )
        asset_report = json.loads(
            (migration / "unity_asset_import_report.json").read_text("utf-8")
        )

        paths: set[str] = set()
        for result in material_report.get("results", []):
            if result.get("status") == "converted" and result.get("object"):
                paths.add(result["object"])
        for result in asset_report.get("results", []):
            if result.get("status") != "imported":
                continue
            for object_path in result.get("objects", []):
                if "/Materials/" in object_path:
                    paths.add(object_path)

        for path in sorted(paths):
            index.paths_by_name.setdefault(_object_name(path), []).append(path)
        return index

    def resolve(self, mesh, static_material):
        mesh_path = mesh.get_path_name() if mesh is not None else ""
        names = _slot_names(static_material)
        candidates: list[str] = []
        lower_mesh_path = mesh_path.lower()
        for marker, material_path in MESH_CONTEXT_ALIASES:
            if marker in lower_mesh_path:
                candidates.append(material_path)
        for name in names:
            alias = SLOT_ALIASES.get(name.lower())
            if alias:
                candidates.append(alias)
            candidates.extend(self.paths_by_name.get(name.lower(), []))

        # Highest common package prefix wins. This selects the companion GLB
        # material beside an FBX over unrelated same-named materials elsewhere.
        candidates = sorted(
            set(candidates),
            key=lambda path: (_common_prefix_score(mesh_path, path), path),
            reverse=True,
        )
        for path in candidates:
            asset = unreal.EditorAssetLibrary.load_asset(path)
            if isinstance(asset, unreal.MaterialInterface):
                return asset, names, path
        return None, names, ""

    def apply_to_component(self, component, mesh, issues: list[str] | None = None) -> int:
        if not isinstance(mesh, unreal.StaticMesh):
            return 0
        try:
            static_materials = mesh.get_editor_property("static_materials") or []
        except Exception:
            static_materials = []
        changed = 0
        for slot, static_material in enumerate(static_materials):
            current = component.get_material(slot)
            if not material_is_missing(current):
                continue
            material, names, path = self.resolve(mesh, static_material)
            if material is None:
                if issues is not None:
                    issues.append(
                        f"{mesh.get_name()}: unresolved model material slot {slot} "
                        f"({', '.join(names) or 'unnamed'})"
                    )
                continue
            component.set_material(slot, material)
            changed += 1
        return changed

    def apply_to_mesh_defaults(self, mesh, issues: list[str] | None = None) -> int:
        if not isinstance(mesh, unreal.StaticMesh):
            return 0
        try:
            static_materials = mesh.get_editor_property("static_materials") or []
        except Exception:
            static_materials = []
        changed = 0
        for slot, static_material in enumerate(static_materials):
            current = static_material.get_editor_property("material_interface")
            if not material_is_missing(current):
                continue
            material, names, path = self.resolve(mesh, static_material)
            if material is None:
                if issues is not None:
                    issues.append(
                        f"{mesh.get_path_name()}: unresolved default slot {slot} "
                        f"({', '.join(names) or 'unnamed'})"
                    )
                continue
            mesh.set_material(slot, material)
            changed += 1
        return changed
