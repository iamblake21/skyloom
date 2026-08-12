"""Convert Unity scenes into Unreal levels.

A Unity scene is overwhelmingly prefab placements, so the conversion is mostly a
matter of spawning the Blueprint each `PrefabInstance` points at, at the world
transform Unity would have produced. Loose GameObjects with a MeshFilter become
StaticMeshActors, and lights and cameras become their Unreal counterparts.

Transforms are composed down the scene hierarchy before conversion, because a
Unity placement is expressed relative to its parent while an Unreal actor is
spawned in world space.

Run with UnrealEditor-Cmd and the PythonScriptPlugin. Never writes to Unity.
"""

from __future__ import annotations

import json
import math
import os
import re
import sys
import traceback
from pathlib import Path, PurePosixPath

import unreal

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from cml_unity_yaml import index_by_file_id, load_unity_documents, parse_reference
from cml_material_slots import MaterialSlotIndex

MAP_ROOT = "/Game/Maps"
UNITY_METRES_TO_UNREAL = 100.0
UNITY_BUILTIN_MESH_GUID = "0000000000000000e000000000000000"
UNITY_BUILTIN_MESHES = {
    10202: "/Engine/BasicShapes/Cube.Cube",
    10206: "/Engine/BasicShapes/Cylinder.Cylinder",
    10207: "/Engine/BasicShapes/Sphere.Sphere",
    10209: "/Engine/BasicShapes/Plane.Plane",
    10210: "/Engine/BasicShapes/Plane.Plane",
}

# This obsolete mesh is a duplicate underbody left in the Unity review scene.
# Its baked vertex coordinates do not overlap either referenced TerrainData;
# the exact TerrainUnderbody mesh already replaces it.  Importing both creates
# the giant peach mass between the two review landscapes.
EXCLUDED_SOURCE_ACTOR_NAMES = {"terrain_bot"}


def _log(message: str) -> None:
    unreal.log(f"[CML Scene Migration] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Scene Migration] {message}")


def _sanitize(value: str) -> str:
    value = re.sub(r"[^A-Za-z0-9_]", "_", str(value).strip())
    value = re.sub(r"_+", "_", value).strip("_") or "Object"
    return f"A_{value}" if value[0].isdigit() else value


# --- Unity -> Unreal space (see cml_prefab_import for the axis rationale) ---


def _quaternion_multiply(a: tuple, b: tuple) -> tuple:
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def _quaternion_rotate(q: tuple, v: tuple) -> tuple:
    x, y, z, w = q
    vx, vy, vz = v
    ux, uy, uz = x, y, z
    dot_uv = ux * vx + uy * vy + uz * vz
    dot_uu = ux * ux + uy * uy + uz * uz
    cross = (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx)
    return (
        2.0 * dot_uv * ux + (w * w - dot_uu) * vx + 2.0 * w * cross[0],
        2.0 * dot_uv * uy + (w * w - dot_uu) * vy + 2.0 * w * cross[1],
        2.0 * dot_uv * uz + (w * w - dot_uu) * vz + 2.0 * w * cross[2],
    )


class UnityTransformValues:
    """Unity-space TRS, composable down a parent chain."""

    def __init__(self, position=(0.0, 0.0, 0.0), rotation=(0.0, 0.0, 0.0, 1.0), scale=(1.0, 1.0, 1.0)):
        self.position = position
        self.rotation = rotation
        self.scale = scale

    def compose(self, child: "UnityTransformValues") -> "UnityTransformValues":
        scaled = tuple(child.position[i] * self.scale[i] for i in range(3))
        rotated = _quaternion_rotate(self.rotation, scaled)
        return UnityTransformValues(
            position=tuple(self.position[i] + rotated[i] for i in range(3)),
            rotation=_quaternion_multiply(self.rotation, child.rotation),
            scale=tuple(self.scale[i] * child.scale[i] for i in range(3)),
        )

    def to_unreal(self) -> tuple:
        x, y, z = self.position
        location = unreal.Vector(
            z * UNITY_METRES_TO_UNREAL, x * UNITY_METRES_TO_UNREAL, y * UNITY_METRES_TO_UNREAL
        )
        qx, qy, qz, qw = self.rotation
        length = math.sqrt(qx * qx + qy * qy + qz * qz + qw * qw) or 1.0
        quaternion = unreal.Quat(qz / length, qx / length, qy / length, qw / length)
        try:
            rotator = quaternion.rotator()
        except AttributeError:
            rotator = unreal.MathLibrary.quat_rotator(quaternion)
        sx, sy, sz = self.scale
        return location, rotator, unreal.Vector(sz, sx, sy)


def _transform_from(values: dict) -> UnityTransformValues:
    position = values.get("m_LocalPosition") if isinstance(values, dict) else {}
    rotation = values.get("m_LocalRotation") if isinstance(values, dict) else {}
    scale = values.get("m_LocalScale") if isinstance(values, dict) else {}
    position = position if isinstance(position, dict) else {}
    rotation = rotation if isinstance(rotation, dict) else {}
    scale = scale if isinstance(scale, dict) else {}
    return UnityTransformValues(
        position=(float(position.get("x", 0.0)), float(position.get("y", 0.0)), float(position.get("z", 0.0))),
        rotation=(
            float(rotation.get("x", 0.0)),
            float(rotation.get("y", 0.0)),
            float(rotation.get("z", 0.0)),
            float(rotation.get("w", 1.0)),
        ),
        scale=(float(scale.get("x", 1.0)), float(scale.get("y", 1.0)), float(scale.get("z", 1.0))),
    )


def _transform_from_overrides(overrides: dict) -> UnityTransformValues:
    return UnityTransformValues(
        position=tuple(overrides.get(f"m_LocalPosition.{axis}", 0.0) for axis in "xyz"),
        rotation=(
            overrides.get("m_LocalRotation.x", 0.0),
            overrides.get("m_LocalRotation.y", 0.0),
            overrides.get("m_LocalRotation.z", 0.0),
            overrides.get("m_LocalRotation.w", 1.0),
        ),
        scale=tuple(overrides.get(f"m_LocalScale.{axis}", 1.0) for axis in "xyz"),
    )


def _load_indexes(project_dir: Path):
    manifest = json.loads((project_dir / "Migration" / "unity_asset_manifest.json").read_text("utf-8"))
    prefab_report = json.loads(
        (project_dir / "Migration" / "unity_prefab_import_report.json").read_text("utf-8")
    )
    asset_report = json.loads(
        (project_dir / "Migration" / "unity_asset_import_report.json").read_text("utf-8")
    )
    embedded_report_path = project_dir / "Migration" / "unity_embedded_mesh_import_report.json"
    embedded_results = []
    if embedded_report_path.is_file():
        embedded_results = json.loads(embedded_report_path.read_text("utf-8")).get("results", [])
    material_report = json.loads(
        (project_dir / "Migration" / "unity_material_import_report.json").read_text("utf-8")
    )
    blueprints = {
        item["guid"]: item["object"] for item in prefab_report["results"] if item["status"] == "converted"
    }
    meshes: dict[str, list[str]] = {}
    for result in [*asset_report["results"], *embedded_results]:
        if result["status"] == "imported" and result.get("guid") and result["objects"]:
            meshes.setdefault(result["guid"], []).extend(result["objects"])
    materials = {
        item["guid"]: item["object"] for item in material_report["results"] if item["status"] == "converted"
    }
    return manifest, blueprints, meshes, materials, MaterialSlotIndex.from_project(project_dir)


def _scene_world_transforms(documents) -> dict[int, UnityTransformValues]:
    """World transform of every Transform in the scene, keyed by fileID."""
    transforms = {
        document.file_id: document for document in documents if document.type_name == "Transform"
    }
    resolved: dict[int, UnityTransformValues] = {}

    def resolve(file_id: int, guard: set) -> UnityTransformValues:
        if file_id in resolved:
            return resolved[file_id]
        document = transforms.get(file_id)
        if document is None or file_id in guard:
            return UnityTransformValues()
        guard.add(file_id)
        local = _transform_from(document.values)
        parent = parse_reference(document.get("m_Father"))
        world = resolve(parent.file_id, guard).compose(local) if parent.file_id else local
        guard.discard(file_id)
        resolved[file_id] = world
        return world

    for file_id in list(transforms):
        resolve(file_id, set())
    return resolved


def _spawn(actor_class, location, rotation, label: str):
    subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    actor = subsystem.spawn_actor_from_class(actor_class, location, rotation)
    if actor:
        actor.set_actor_label(label)
    return actor


def _spawn_environment(render_settings: dict, spawned: dict) -> None:
    """Recreate Unity's global lighting boundary with native Unreal actors.

    Unity serialises ambient/fog/sky as RenderSettings rather than scene
    GameObjects.  Ignoring that document produced technically populated Unreal
    levels whose sky and indirect lighting were black.  Native atmosphere,
    skylight and exponential fog keep the migrated levels visible in editor,
    PIE and packaged builds without depending on an editor viewport override.
    """

    origin = unreal.Vector(0.0, 0.0, 0.0)
    zero_rotation = unreal.Rotator(0.0, 0.0, 0.0)

    atmosphere = _spawn(unreal.SkyAtmosphere, origin, zero_rotation, "CML_SkyAtmosphere")
    if atmosphere:
        atmosphere.sky_atmosphere_component.set_sky_luminance_factor(
            unreal.LinearColor(0.92, 0.98, 1.0, 1.0)
        )
        spawned["environment"] += 1

    ambient = render_settings.get("m_AmbientSkyColor") or {}
    ground = render_settings.get("m_AmbientGroundColor") or {}
    sky = _spawn(unreal.SkyLight, origin, zero_rotation, "CML_SkyLight")
    if sky:
        component = sky.light_component
        component.set_mobility(unreal.ComponentMobility.MOVABLE)
        component.set_intensity(
            float(render_settings.get("m_AmbientIntensity", 1.0) or 1.0) * 1.25
        )
        component.set_light_color(
            unreal.LinearColor(
                float(ambient.get("r", 0.40)),
                float(ambient.get("g", 0.45)),
                float(ambient.get("b", 0.50)),
                1.0,
            )
        )
        component.set_editor_property("lower_hemisphere_is_black", False)
        component.set_lower_hemisphere_color(
            unreal.LinearColor(
                float(ground.get("r", 0.15)),
                float(ground.get("g", 0.27)),
                float(ground.get("b", 0.32)),
                1.0,
            )
        )
        component.set_real_time_capture(True)
        spawned["environment"] += 1

    if int(render_settings.get("m_Fog", 0) or 0):
        fog = _spawn(unreal.ExponentialHeightFog, origin, zero_rotation, "CML_HeightFog")
        if fog:
            component = fog.component
            colour = render_settings.get("m_FogColor") or {}
            component.set_fog_density(float(render_settings.get("m_FogDensity", 0.01) or 0.01))
            component.set_fog_height_falloff(0.2)
            component.set_fog_inscattering_color(
                unreal.LinearColor(
                    float(colour.get("r", 0.62)),
                    float(colour.get("g", 0.84)),
                    float(colour.get("b", 0.86)),
                    1.0,
                )
            )
            component.set_directional_inscattering_color(
                unreal.LinearColor(1.0, 0.86, 0.58, 1.0)
            )
            component.set_directional_inscattering_exponent(5.0)
            component.set_start_distance(
                float(render_settings.get("m_LinearFogStart", 200.0) or 200.0) * UNITY_METRES_TO_UNREAL
            )
            component.set_fog_cutoff_distance(
                float(render_settings.get("m_LinearFogEnd", 1100.0) or 1100.0) * UNITY_METRES_TO_UNREAL
            )
            spawned["environment"] += 1


def _convert_scene(source: str, unity_root: Path, blueprints, meshes, materials, slot_materials) -> dict:
    documents = load_unity_documents(unity_root / Path(source))
    by_file_id = index_by_file_id(documents)
    world_transforms = _scene_world_transforms(documents)

    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    map_name = _sanitize(Path(source).stem)
    map_path = f"{MAP_ROOT}/{map_name}"
    if unreal.EditorAssetLibrary.does_asset_exist(map_path):
        if os.environ.get("CML_SCENE_REPLACE_EXISTING") != "1":
            raise RuntimeError(
                f"Level {map_path} already exists; set CML_SCENE_REPLACE_EXISTING=1 "
                "only after creating a recoverable map backup"
            )
        # Renaming is intentionally preferred to deleting here.  It releases the
        # destination package immediately for NewLevel and leaves a recoverable
        # Unreal-native copy in addition to the filesystem backup made by the
        # migration runner.
        backup_root = "/Game/Migration/MapBackups"
        backup_path = f"{backup_root}/{map_name}_BeforeReimport"
        unreal.EditorAssetLibrary.make_directory(backup_root)
        if unreal.EditorAssetLibrary.does_asset_exist(backup_path):
            raise RuntimeError(
                f"Map backup {backup_path} already exists; preserve or rename it before rerunning"
            )
        if not unreal.EditorAssetLibrary.rename_asset(map_path, backup_path):
            raise RuntimeError(f"Unable to move existing level {map_path} to {backup_path}")
    if not level_editor.new_level(map_path):
        raise RuntimeError(f"Unable to create level {map_path}")

    issues: list[str] = []
    scripts: set[str] = set()
    spawned = {
        "prefab": 0,
        "mesh": 0,
        "light": 0,
        "camera": 0,
        "playerStart": 0,
        "environment": 0,
    }
    player_start_spawned = False

    components_by_game_object: dict[int, list] = {}
    for document in documents:
        owner = parse_reference(document.get("m_GameObject"))
        if owner.file_id:
            components_by_game_object.setdefault(owner.file_id, []).append(document)

    for document in documents:
        if document.type_name != "PrefabInstance":
            continue
        source_reference = parse_reference(document.get("m_SourcePrefab"))
        modifications = document.get("m_Modification.m_Modifications") or []
        overrides: dict[str, float] = {}
        name = ""
        for modification in modifications:
            if not isinstance(modification, dict):
                continue
            path = str(modification.get("propertyPath", ""))
            value = modification.get("value")
            if path == "m_Name" and isinstance(value, str):
                name = value
            elif isinstance(value, (int, float)):
                overrides[path] = float(value)

        local = _transform_from_overrides(overrides)
        parent = parse_reference(document.get("m_Modification.m_TransformParent"))
        world = world_transforms.get(parent.file_id, UnityTransformValues()).compose(local) \
            if parent.file_id else local

        target = blueprints.get(source_reference.guid)
        if not target:
            # Unity also serialises an imported FBX/OBJ model as a PrefabInstance.
            # Those GUIDs intentionally have no generated Blueprint; they resolve
            # directly to the StaticMesh imported from the model asset.
            candidates = meshes.get(source_reference.guid) or []
            mesh = unreal.EditorAssetLibrary.load_asset(candidates[0]) if candidates else None
            if not isinstance(mesh, unreal.StaticMesh):
                issues.append(f"prefab {source_reference.guid} has no Blueprint or StaticMesh")
                continue
            if (name or mesh.get_name()).strip().lower() in EXCLUDED_SOURCE_ACTOR_NAMES:
                issues.append(
                    f"{name or mesh.get_name()}: excluded duplicate terrain underbody"
                )
                continue
            location, rotation, scale = world.to_unreal()
            actor = _spawn(unreal.StaticMeshActor, location, rotation, name or mesh.get_name())
            if actor:
                actor.static_mesh_component.set_static_mesh(mesh)
                slot_materials.apply_to_component(actor.static_mesh_component, mesh, issues)
                actor.set_actor_scale3d(scale)
                unreal.EditorAssetLibrary.set_metadata_tag(
                    actor, "CML.UnityModelPrefabGuid", source_reference.guid
                )
                spawned["mesh"] += 1
            continue
        blueprint = unreal.EditorAssetLibrary.load_asset(target)
        if not isinstance(blueprint, unreal.Blueprint):
            issues.append(f"{target} is not a Blueprint")
            continue
        location, rotation, scale = world.to_unreal()
        actor = _spawn(blueprint.generated_class(), location, rotation, name or map_name)
        if actor:
            actor.set_actor_scale3d(scale)
            spawned["prefab"] += 1

    for document in documents:
        owner = parse_reference(document.get("m_GameObject"))
        game_object = by_file_id.get(owner.file_id)
        transform_document = next(
            (item for item in components_by_game_object.get(owner.file_id, [])
             if item.type_name == "Transform"),
            None,
        )
        if game_object is None or transform_document is None:
            continue
        world = world_transforms.get(transform_document.file_id, UnityTransformValues())
        location, rotation, scale = world.to_unreal()
        label = _sanitize(str(game_object.get("m_Name", "Object")))

        if document.type_name == "MeshFilter":
            mesh_reference = parse_reference(document.get("m_Mesh"))
            candidates = meshes.get(mesh_reference.guid) or []
            builtin_path = (
                UNITY_BUILTIN_MESHES.get(mesh_reference.file_id)
                if mesh_reference.guid == UNITY_BUILTIN_MESH_GUID
                else None
            )
            mesh = (
                unreal.EditorAssetLibrary.load_asset(candidates[0])
                if candidates
                else unreal.EditorAssetLibrary.load_asset(builtin_path)
                if builtin_path
                else None
            )
            if not isinstance(mesh, unreal.StaticMesh):
                issues.append(f"{label}: mesh {mesh_reference.guid or 'builtin'} unresolved")
                continue
            actor = _spawn(unreal.StaticMeshActor, location, rotation, label)
            if actor and isinstance(mesh, unreal.StaticMesh):
                actor.static_mesh_component.set_static_mesh(mesh)
                slot_materials.apply_to_component(actor.static_mesh_component, mesh, issues)
                actor.set_actor_scale3d(scale)
                renderer = next(
                    (item for item in components_by_game_object.get(owner.file_id, [])
                     if item.type_name == "MeshRenderer"),
                    None,
                )
                if renderer is not None:
                    for slot, entry in enumerate(renderer.get("m_Materials") or []):
                        material_path = materials.get(parse_reference(entry).guid)
                        material = (
                            unreal.EditorAssetLibrary.load_asset(material_path) if material_path else None
                        )
                        if isinstance(material, unreal.MaterialInterface):
                            actor.static_mesh_component.set_material(slot, material)
                spawned["mesh"] += 1
        elif document.type_name == "Light":
            light_type = int(document.get("m_Type", 1) or 1)
            actor_class = {
                0: unreal.SpotLight,
                1: unreal.DirectionalLight,
                2: unreal.PointLight,
            }.get(light_type, unreal.PointLight)
            actor = _spawn(actor_class, location, rotation, label)
            if actor:
                colour = document.get("m_Color") or {}
                intensity = float(document.get("m_Intensity", 1.0) or 1.0)
                component = actor.light_component
                component.set_light_color(
                    unreal.LinearColor(
                        float(colour.get("r", 1.0)),
                        float(colour.get("g", 1.0)),
                        float(colour.get("b", 1.0)),
                        1.0,
                    )
                )
                # Unity's directional intensity is a plain multiplier; Unreal's
                # is lux. Carrying the number across unchanged would be wrong,
                # so it is recorded on the actor for the lighting pass.
                unreal.EditorAssetLibrary.set_metadata_tag(actor, "CML.UnityIntensity", str(intensity))
                component.set_mobility(unreal.ComponentMobility.MOVABLE)
                if isinstance(component, unreal.DirectionalLightComponent):
                    # Unity stores a unitless multiplier; Unreal expects lux.
                    # A value of 10 lux reproduces the authored stylised setup
                    # with fixed exposure without crushing the entire map.
                    component.set_intensity(max(0.0, intensity) * 10.0)
                    component.set_editor_property("light_source_angle", 1.2)
                    component.set_atmosphere_sun_light(True)
                    component.set_editor_property("cast_shadows_on_atmosphere", True)
                else:
                    component.set_intensity(intensity)
                spawned["light"] += 1
        elif document.type_name == "Camera":
            if _spawn(unreal.CameraActor, location, rotation, label):
                spawned["camera"] += 1
            if not player_start_spawned:
                # The migrated GameMode owns a first-person pawn; without a
                # PlayerStart Unreal spawns it at the world origin, which is
                # commonly inside/below the imported Landscape.  Reuse the
                # first authored Unity camera as the deterministic playable
                # viewpoint while preserving the CameraActor for review.
                start_location = unreal.Vector(location.x, location.y, location.z - 64.0)
                if _spawn(unreal.PlayerStart, start_location, rotation, "CML_PlayerStart"):
                    spawned["playerStart"] += 1
                    player_start_spawned = True
        elif document.type_name == "MonoBehaviour":
            script = parse_reference(document.get("m_Script"))
            if script.guid:
                scripts.add(script.guid)

    # Empty GameObjects. In Unity these are not decoration: they are the pivots
    # a cinematic orbits, the parents a group is shown and hidden by, and the
    # markers a script keys off. Dropping them because they draw nothing left
    # the intro map full of scenery with nothing to move it, and the failure was
    # invisible — every actor that *did* convert was present and correct.
    #
    # A GameObject whose only components are a Transform and MonoBehaviours can
    # never have produced an actor above, so this needs no bookkeeping to avoid
    # spawning one twice.
    for document in documents:
        if document.type_name != "GameObject":
            continue
        components = components_by_game_object.get(document.file_id, [])
        transform_document = next(
            (item for item in components if item.type_name == "Transform"), None)
        if transform_document is None:
            continue
        if any(item.type_name not in ("Transform", "MonoBehaviour") for item in components):
            continue

        world = world_transforms.get(transform_document.file_id, UnityTransformValues())
        location, rotation, _scale = world.to_unreal()
        label = _sanitize(str(document.get("m_Name", "Object")))
        # A TargetPoint rather than a bare AActor: it carries a root component,
        # so it has a transform that can actually be read and moved at runtime.
        if _spawn(unreal.TargetPoint, location, rotation, label):
            spawned["pivot"] = spawned.get("pivot", 0) + 1

    render_settings = next(
        (document.values for document in documents if document.type_name == "RenderSettings"),
        {},
    )
    _spawn_environment(render_settings, spawned)

    level_editor.save_current_level()
    return {
        "source": source,
        "status": "converted",
        "map": map_path,
        "spawned": spawned,
        "monoBehaviourScriptGuids": sorted(scripts),
        "issues": issues[:40],
        "issueCount": len(issues),
    }


def main() -> int:
    project_dir = Path(unreal.Paths.project_dir())
    manifest, blueprints, meshes, materials, slot_materials = _load_indexes(project_dir)
    unity_root = Path(manifest["unityRoot"])
    report_path = project_dir / "Migration" / "unity_scene_import_report.json"

    scenes = [entry for entry in manifest["entries"] if entry["kind"] == "scene"]
    scene_filter = os.environ.get("CML_SCENE_FILTER", "").strip().lower()
    if scene_filter:
        scenes = [
            entry
            for entry in scenes
            if scene_filter in entry["source"].lower()
            or scene_filter in Path(entry["source"]).stem.lower()
        ]
        if not scenes:
            raise RuntimeError(f"CML_SCENE_FILTER matched no scene: {scene_filter}")
    _log(f"Converting {len(scenes)} Unity scenes")

    results: list[dict] = []
    for entry in scenes:
        try:
            result = _convert_scene(
                entry["source"], unity_root, blueprints, meshes, materials, slot_materials
            )
            result.update({"guid": entry["guid"], "sha256": entry["sha256"]})
            results.append(result)
            _log(f"{entry['source']} -> {result['map']} {result['spawned']}")
        except Exception as exception:
            _error(f"{entry['source']}: {exception}")
            results.append(
                {"source": entry["source"], "guid": entry["guid"], "status": "failed", "error": str(exception)}
            )
        unreal.SystemLibrary.collect_garbage()

    report = {
        "schema": 1,
        "requested": len(scenes),
        "converted": sum(item["status"] == "converted" for item in results),
        "failed": sum(item["status"] != "converted" for item in results),
        "results": results,
    }
    temporary = report_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(report_path)
    unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
    _log(f"Complete: converted={report['converted']}, failed={report['failed']}")
    return 0 if report["failed"] == 0 else 2


try:
    _exit_code = main()
except Exception:
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    _error(f"CML_SCENE_IMPORT_FAILED code={_exit_code}")
else:
    _log("CML_SCENE_IMPORT_SUCCEEDED")
