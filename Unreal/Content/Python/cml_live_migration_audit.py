"""Read-only audit of the assets actually rendered in the open Unreal level.

Run from the Unreal Python console.  The script intentionally does not save,
spawn, select, move, or otherwise mutate level actors or assets.
"""

from __future__ import annotations

import unreal


PORTAL_BLUEPRINT = (
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Portal/Prefabs/"
    "BP_PF_AncientStonePortal"
)
PORTAL_MATERIAL = (
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Portal/Materials/"
    "M_ENV_AncientStonePortal_Stone"
)
SEARCH_TERMS = ("portal", "airship", "water", "rock", "cliff")


def _asset_path(value: object) -> str:
    if not value:
        return "None"
    try:
        return value.get_path_name()
    except Exception:
        return str(value)


def _component_summary(component: unreal.StaticMeshComponent) -> str:
    mesh = component.get_editor_property("static_mesh")
    materials = []
    try:
        count = component.get_num_materials()
    except Exception:
        count = 0
    for slot in range(count):
        materials.append(f"{slot}:{_asset_path(component.get_material(slot))}")
    return (
        f"component={component.get_name()} "
        f"mesh={_asset_path(mesh)} "
        f"materials=[{', '.join(materials)}]"
    )


def _audit_blueprint(path: str) -> None:
    unreal.log_warning(f"[CML_LIVE_AUDIT] blueprint={path}")
    try:
        generated_class = unreal.EditorAssetLibrary.load_blueprint_class(path)
        if not generated_class:
            unreal.log_error(f"[CML_LIVE_AUDIT] blueprint-class-missing={path}")
            return
        default_object = unreal.get_default_object(generated_class)
        components = default_object.get_components_by_class(unreal.StaticMeshComponent)
        unreal.log_warning(
            f"[CML_LIVE_AUDIT] blueprint-static-mesh-components={len(components)}"
        )
        for component in components:
            unreal.log_warning(f"[CML_LIVE_AUDIT] {_component_summary(component)}")
    except Exception as exc:
        unreal.log_error(f"[CML_LIVE_AUDIT] blueprint-audit-failed={exc}")


def _audit_portal_material() -> None:
    material = unreal.EditorAssetLibrary.load_asset(PORTAL_MATERIAL)
    unreal.log_warning(
        f"[CML_LIVE_AUDIT] portal-material={_asset_path(material)}"
    )
    if not material:
        return
    for parameter in ("BaseTexture", "NormalTexture", "PackedTexture"):
        value = None
        try:
            value = unreal.MaterialEditingLibrary.get_material_instance_texture_parameter_value(
                material, parameter
            )
        except Exception:
            pass
        unreal.log_warning(
            f"[CML_LIVE_AUDIT] portal-material-param {parameter}={_asset_path(value)}"
        )


def _audit_world() -> None:
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    unreal.log_warning(f"[CML_LIVE_AUDIT] world={_asset_path(world)}")
    actors = unreal.get_editor_subsystem(
        unreal.EditorActorSubsystem
    ).get_all_level_actors()
    hits = []
    for actor in actors:
        label = actor.get_actor_label()
        class_path = _asset_path(actor.get_class())
        searchable = f"{label} {class_path}".lower()
        if any(term in searchable for term in SEARCH_TERMS):
            hits.append(actor)

    unreal.log_warning(f"[CML_LIVE_AUDIT] matching-world-actors={len(hits)}")
    for actor in hits:
        unreal.log_warning(
            f"[CML_LIVE_AUDIT] actor={actor.get_actor_label()} "
            f"class={_asset_path(actor.get_class())}"
        )
        components = actor.get_components_by_class(unreal.StaticMeshComponent)
        for component in components:
            unreal.log_warning(f"[CML_LIVE_AUDIT] {_component_summary(component)}")


def main() -> None:
    unreal.log_warning("[CML_LIVE_AUDIT] BEGIN")
    _audit_blueprint(PORTAL_BLUEPRINT)
    _audit_portal_material()
    _audit_world()
    unreal.log_warning("[CML_LIVE_AUDIT] END")


main()
