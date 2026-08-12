"""Read-only diagnostics for the currently loaded migrated Unreal level.

This is intentionally safe to run in the interactive editor: it does not open,
modify, build or save a level.  The report captures the state that determines
whether a migrated map can actually be seen (Landscape materials, visibility,
lights, atmosphere, fog, exposure and playable starts).
"""

from __future__ import annotations

import json
import os
from pathlib import Path

import unreal


def _path(value) -> str:
    return value.get_path_name() if value else ""


def _vector(value) -> list[float]:
    return [float(value.x), float(value.y), float(value.z)]


def _rotator(value) -> list[float]:
    return [float(value.pitch), float(value.yaw), float(value.roll)]


def _property(value, name, default=None):
    try:
        return value.get_editor_property(name)
    except Exception:  # noqa: BLE001 - diagnostics must survive API drift.
        return default


def _actor_common(actor) -> dict:
    return {
        "label": actor.get_actor_label(),
        "class": actor.get_class().get_name(),
        "location": _vector(actor.get_actor_location()),
        "rotation": _rotator(actor.get_actor_rotation()),
        "hiddenEditor": bool(_property(actor, "is_temporarily_hidden_in_editor", False)),
        "hiddenGame": bool(_property(actor, "hidden", False)),
    }


def _light(actor) -> dict:
    result = _actor_common(actor)
    component = _property(actor, "light_component")
    if component:
        colour = _property(component, "light_color")
        result.update(
            {
                "visible": bool(_property(component, "visible", True)),
                "intensity": float(_property(component, "intensity", 0.0) or 0.0),
                "colour": (
                    [float(colour.r), float(colour.g), float(colour.b), float(colour.a)]
                    if colour
                    else []
                ),
                "mobility": str(_property(component, "mobility", "")),
                "castShadows": bool(_property(component, "cast_shadows", False)),
            }
        )
    return result


def _material_instance(interface) -> dict:
    """Report the values the compiled Landscape instance actually resolves."""
    result = {"object": _path(interface), "class": ""}
    if not interface:
        return result
    result["class"] = interface.get_class().get_name()
    parent = _property(interface, "parent")
    result["parent"] = _path(parent)
    if not isinstance(interface, unreal.MaterialInstance):
        return result

    texture_parameters = (
        "_Control",
        "_Splat0",
        "_Splat1",
        "_Splat2",
        "_Normal0",
        "_Normal1",
        "_Normal2",
        "_LandmassCliffAlbedo",
        "_LandmassCliffNormal",
        "_LandmassVariationMask",
    )
    vector_parameters = (
        "_CMLTerrainOriginInvSize",
        "_Splat0_ST",
        "_Splat1_ST",
        "_Splat2_ST",
        "_ClayColor",
    )
    scalar_parameters = (
        "_NormalScale0",
        "_NormalScale1",
        "_NormalScale2",
        "_LandmassWorldSize",
        "_LandmassNormalStrength",
        "_LandmassSlopeOffset",
        "_LandmassSlopeHardness",
        "_ClayMode",
    )
    result["textures"] = {}
    result["textureDetails"] = {}
    for name in texture_parameters:
        value = unreal.MaterialEditingLibrary.get_material_instance_texture_parameter_value(
            interface, name
        )
        result["textures"][name] = _path(value)
        if not isinstance(value, unreal.Texture2D):
            continue

        def safe_property(property_name):
            try:
                return str(value.get_editor_property(property_name))
            except Exception:
                return "<unavailable>"

        result["textureDetails"][name] = {
            "srgb": safe_property("srgb"),
            "compression": safe_property("compression_settings"),
            "virtualTextureStreaming": safe_property("virtual_texture_streaming"),
            "filter": safe_property("filter"),
            "addressX": safe_property("address_x"),
            "addressY": safe_property("address_y"),
            "sizeX": int(value.blueprint_get_size_x()),
            "sizeY": int(value.blueprint_get_size_y()),
        }
    result["vectors"] = {}
    for name in vector_parameters:
        value = unreal.MaterialEditingLibrary.get_material_instance_vector_parameter_value(
            interface, name
        )
        result["vectors"][name] = [
            float(value.r),
            float(value.g),
            float(value.b),
            float(value.a),
        ]
    result["scalars"] = {
        name: float(
            unreal.MaterialEditingLibrary.get_material_instance_scalar_parameter_value(
                interface, name
            )
        )
        for name in scalar_parameters
    }
    return result


def main() -> None:
    requested_map = os.environ.get("CML_DIAGNOSE_MAP", "").strip()
    if requested_map:
        level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
        if not level_editor.load_level(requested_map):
            raise RuntimeError(f"Unable to load diagnostic map {requested_map}")

    editor = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors()
    world = editor.get_editor_world()
    landscapes = []
    for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Landscape):
        entry = _actor_common(actor)
        origin, extent = actor.get_actor_bounds(False, True)
        material = _property(actor, "landscape_material")
        components = actor.get_components_by_class(unreal.LandscapeComponent)
        component_materials = []
        for component in components[:8]:
            material_count = int(component.get_num_materials())
            component_materials.append(
                {
                    "component": _path(component),
                    "materials": [
                        _material_instance(component.get_material(index))
                        for index in range(material_count)
                    ],
                }
            )
        entry.update(
            {
                "boundsOrigin": _vector(origin),
                "boundsExtent": _vector(extent),
                "material": _path(material),
                "componentCount": len(components),
                "visibleComponents": sum(
                    1 for component in components if bool(_property(component, "visible", True))
                ),
                "naniteEnabled": _property(actor, "enable_nanite", None),
                "naniteSkirtEnabled": _property(actor, "nanite_skirt_enabled", None),
                "landscapeMaterialInstance": _material_instance(material),
                "componentMaterials": component_materials,
                "componentMaterialChains": list(
                    unreal.CMLLandscapeImportLibrary.describe_landscape_material_instances(
                        actor
                    )
                ),
            }
        )
        landscapes.append(entry)

    mesh_actors = []
    for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.StaticMeshActor):
        component = _property(actor, "static_mesh_component")
        mesh = _property(component, "static_mesh") if component else None
        origin, extent = actor.get_actor_bounds(False, True)
        materials = []
        if component:
            for index in range(int(component.get_num_materials())):
                materials.append(_path(component.get_material(index)))
        slots = []
        for slot in (_property(mesh, "static_materials", []) or []):
            slots.append(
                {
                    "slotName": str(_property(slot, "material_slot_name", "")),
                    "importedSlotName": str(
                        _property(slot, "imported_material_slot_name", "")
                    ),
                    "material": _path(_property(slot, "material_interface")),
                }
            )
        mesh_actors.append(
            {
                **_actor_common(actor),
                "mesh": _path(mesh),
                "materials": materials,
                "meshSlots": slots,
                "boundsOrigin": _vector(origin),
                "boundsExtent": _vector(extent),
                "boundsVolume": float(extent.x) * float(extent.y) * float(extent.z),
            }
        )
    mesh_actors.sort(key=lambda item: item["boundsVolume"], reverse=True)

    report = {
        "schema": 1,
        "world": _path(world),
        "actorCount": len(actors),
        "landscapes": landscapes,
        "largestStaticMeshActors": mesh_actors[:40],
        "directionalLights": [
            _light(actor)
            for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.DirectionalLight)
        ],
        "skyLights": [
            _light(actor)
            for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.SkyLight)
        ],
        "skyAtmospheres": [
            _actor_common(actor)
            for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.SkyAtmosphere)
        ],
        "heightFogs": [
            _actor_common(actor)
            for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.ExponentialHeightFog)
        ],
        "postProcessVolumes": [
            {
                **_actor_common(actor),
                "unbound": bool(_property(actor, "unbound", False)),
                "priority": float(_property(actor, "priority", 0.0) or 0.0),
                "blendWeight": float(_property(actor, "blend_weight", 0.0) or 0.0),
            }
            for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.PostProcessVolume)
        ],
        "playerStarts": [
            _actor_common(actor)
            for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.PlayerStart)
        ],
        "cameras": [
            _actor_common(actor)
            for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.CameraActor)
        ],
    }

    project = Path(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir()))
    output = project / "Saved" / "MigrationValidation" / "live_world_diagnostics.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(output)
    unreal.log(f"[CML Live Diagnose] wrote {output}")
    unreal.log("CML_LIVE_DIAGNOSE_SUCCEEDED")


main()
