"""Convert Unity prefabs into Unreal Blueprint actors.

Every Unity prefab becomes one Blueprint whose component hierarchy mirrors the
Unity Transform hierarchy. Meshes and materials are resolved through the Unity
GUIDs recorded by the earlier import steps, never by display name, so renaming
an asset on either side cannot silently repoint a component.

Nested prefabs (`PrefabInstance`) become ChildActorComponents pointing at the
Blueprint generated for the source prefab, which is why conversion runs in
dependency order.

Run with UnrealEditor-Cmd and the PythonScriptPlugin. Never writes to Unity.
"""

from __future__ import annotations

import json
import os
import re
import traceback
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

import unreal

from cml_material_slots import MaterialSlotIndex

from cml_unity_yaml import (
    UnityObject,
    index_by_file_id,
    load_unity_documents,
    parse_reference,
)


BLUEPRINT_ROOT = "/Game/Migrated"
UNITY_METRES_TO_UNREAL = 100.0
LOD_CHILD = re.compile(r"^LOD(?P<index>\d+)$", re.IGNORECASE)


def _log(message: str) -> None:
    unreal.log(f"[CML Prefab Migration] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Prefab Migration] {message}")


def _sanitize(value: str) -> str:
    value = re.sub(r"[^A-Za-z0-9_]", "_", str(value).strip())
    value = re.sub(r"_+", "_", value).strip("_") or "Object"
    if value[0].isdigit():
        value = f"A_{value}"
    return value


def _destination_for(source: str) -> tuple[str, str]:
    parts = list(PurePosixPath(source).parts)
    if parts and parts[0].lower() == "assets":
        parts = parts[1:]
    if parts and parts[0] == "_Project":
        parts[0] = "Project"
    directory = [_sanitize(part) for part in parts[:-1]]
    destination = BLUEPRINT_ROOT
    if directory:
        destination += "/" + "/".join(directory)
    return destination, f"BP_{_sanitize(Path(parts[-1]).stem)}"


# ---------------------------------------------------------------------------
# Unity -> Unreal space
#
# Unreal.X = Unity.z, Unreal.Y = Unity.x, Unreal.Z = Unity.y; 1 m = 100 uu.
# The axis map is a cyclic permutation, so it is a proper rotation and the
# quaternion transfers by permuting its vector part alone. Both engines are
# left-handed, so no handedness flip is applied.
# ---------------------------------------------------------------------------


def _vector(values: dict | None, default: float) -> tuple[float, float, float]:
    values = values if isinstance(values, dict) else {}
    return (
        float(values.get("x", default)),
        float(values.get("y", default)),
        float(values.get("z", default)),
    )


def _unreal_location(unity_position: dict | None) -> unreal.Vector:
    x, y, z = _vector(unity_position, 0.0)
    return unreal.Vector(z * UNITY_METRES_TO_UNREAL, x * UNITY_METRES_TO_UNREAL, y * UNITY_METRES_TO_UNREAL)


def _unreal_scale(unity_scale: dict | None) -> unreal.Vector:
    x, y, z = _vector(unity_scale, 1.0)
    return unreal.Vector(z, x, y)


def _unreal_rotation(unity_rotation: dict | None) -> unreal.Rotator:
    values = unity_rotation if isinstance(unity_rotation, dict) else {}
    quaternion = unreal.Quat(
        float(values.get("z", 0.0)),
        float(values.get("x", 0.0)),
        float(values.get("y", 0.0)),
        float(values.get("w", 1.0)),
    )
    try:
        return quaternion.rotator()
    except AttributeError:
        return unreal.MathLibrary.quat_rotator(quaternion)


def _unreal_transform(transform: UnityObject) -> unreal.Transform:
    return unreal.Transform(
        location=_unreal_location(transform.get("m_LocalPosition")),
        rotation=_unreal_rotation(transform.get("m_LocalRotation")),
        scale=_unreal_scale(transform.get("m_LocalScale")),
    )


# ---------------------------------------------------------------------------
# Asset resolution
# ---------------------------------------------------------------------------


@dataclass
class AssetIndex:
    meshes_by_guid: dict[str, list[str]] = field(default_factory=dict)
    mesh_source_stem_by_guid: dict[str, str] = field(default_factory=dict)
    materials_by_guid: dict[str, str] = field(default_factory=dict)
    prefab_source_by_guid: dict[str, str] = field(default_factory=dict)
    slot_materials: MaterialSlotIndex | None = None

    def resolve_mesh(self, guid: str, preferred_name: str) -> tuple[object, str]:
        """Return (StaticMesh, note). Prefers a name match inside a multi-mesh FBX."""
        candidates = self.meshes_by_guid.get(guid) or []
        if not candidates:
            return None, "no-imported-mesh"
        chosen = candidates[0]
        note = ""
        if len(candidates) > 1:
            wanted = _sanitize(preferred_name).lower()
            matches = [path for path in candidates if path.rsplit(".", 1)[-1].lower() == wanted]
            if matches:
                chosen = matches[0]
            else:
                note = f"ambiguous-{len(candidates)}-meshes"
        asset = unreal.EditorAssetLibrary.load_asset(chosen)
        if not isinstance(asset, unreal.StaticMesh):
            return None, "not-a-static-mesh"
        return asset, note

    def resolve_meshes(self, guid: str) -> list[object]:
        """Return every StaticMesh imported from one Unity model GUID.

        A Unity prefab may nest an FBX as a single PrefabInstance even though
        that FBX contains dozens of authored child meshes. Unreal imports those
        children as separate StaticMesh assets when ``combine_meshes`` is off.
        Treating the nested model as one mesh and taking candidates[0] reduced
        presses, drills and conveyor modules to one arbitrary roller or beam.
        """
        meshes: list[object] = []
        for object_path in sorted(set(self.meshes_by_guid.get(guid) or [])):
            asset = unreal.EditorAssetLibrary.load_asset(object_path)
            if isinstance(asset, unreal.StaticMesh):
                meshes.append(asset)
        return meshes

    def resolve_material(self, guid: str):
        object_path = self.materials_by_guid.get(guid)
        if not object_path:
            return None
        asset = unreal.EditorAssetLibrary.load_asset(object_path)
        return asset if isinstance(asset, unreal.MaterialInterface) else None


def _build_asset_index(project_dir: Path) -> AssetIndex:
    manifest = json.loads((project_dir / "Migration" / "unity_asset_manifest.json").read_text("utf-8"))
    asset_report = json.loads(
        (project_dir / "Migration" / "unity_asset_import_report.json").read_text("utf-8")
    )
    material_report = json.loads(
        (project_dir / "Migration" / "unity_material_import_report.json").read_text("utf-8")
    )

    entries_by_source = {entry["source"]: entry for entry in manifest["entries"]}
    index = AssetIndex(slot_materials=MaterialSlotIndex.from_project(project_dir))
    for result in asset_report["results"]:
        if result["status"] != "imported" or not result["objects"]:
            continue
        guid = result.get("guid") or (entries_by_source.get(result["source"], {}) or {}).get("guid")
        if not guid:
            continue
        index.meshes_by_guid.setdefault(guid, []).extend(result["objects"])
    for entry in manifest["entries"]:
        if entry.get("kind") == "mesh":
            index.mesh_source_stem_by_guid[entry["guid"]] = Path(entry["source"]).stem
    for result in material_report["results"]:
        if result["status"] == "converted":
            index.materials_by_guid[result["guid"]] = result["object"]
    for entry in manifest["entries"]:
        if entry["kind"] == "prefab":
            index.prefab_source_by_guid[entry["guid"]] = entry["source"]
    return index


# ---------------------------------------------------------------------------
# Unity prefab model
# ---------------------------------------------------------------------------


@dataclass
class PrefabNode:
    name: str
    transform: UnityObject
    game_object: UnityObject
    components: dict[str, list[UnityObject]] = field(default_factory=dict)
    children: list["PrefabNode"] = field(default_factory=list)
    nested_prefab_guid: str = ""


def _descendant_mesh_guids(node: PrefabNode) -> set[str]:
    guids: set[str] = set()
    for mesh_filter in node.components.get("MeshFilter") or []:
        reference = parse_reference(mesh_filter.get("m_Mesh"))
        if reference.guid:
            guids.add(reference.guid)
    for child in node.children:
        guids.update(_descendant_mesh_guids(child))
    return guids


def _is_unity_model_axis_root(node: PrefabNode, index: AssetIndex) -> bool:
    """Detect Unity's technical FBX axis-correction node.

    With ModelImporter.bakeAxisConversion disabled Unity inserts a +/-90 degree
    root transform named exactly like the model file. Unreal's FBX importer has
    already converted that model into Z-up space, so applying the Unity helper
    transform a second time turns trees and other imported models on their side.
    This deliberately requires both the source-stem match and the canonical
    zero-position/unit-scale correction transform; authored rotations are kept.
    """
    matching_guids = {
        guid for guid in _descendant_mesh_guids(node)
        if index.mesh_source_stem_by_guid.get(guid) == node.name
    }
    if not matching_guids:
        return False

    rotation = node.transform.get("m_LocalRotation") or {}
    position = _vector(node.transform.get("m_LocalPosition"), 0.0)
    scale = _vector(node.transform.get("m_LocalScale"), 1.0)
    x = abs(float(rotation.get("x", 0.0)))
    y = abs(float(rotation.get("y", 0.0)))
    z = abs(float(rotation.get("z", 0.0)))
    w = abs(float(rotation.get("w", 1.0)))
    canonical_quarter_turn = abs(x - 0.70710678) < 1.0e-4 and y < 1.0e-4 and z < 1.0e-4 and abs(w - 0.70710678) < 1.0e-4
    zero_position = max(abs(value) for value in position) < 1.0e-5
    unit_scale = max(abs(value - 1.0) for value in scale) < 1.0e-5
    return canonical_quarter_turn and zero_position and unit_scale


def _read_prefab(path: Path) -> tuple[list[PrefabNode], list[dict]]:
    """Build the Transform tree plus the nested-prefab instances of one prefab."""
    documents = load_unity_documents(path)
    by_file_id = index_by_file_id(documents)

    components_by_game_object: dict[int, list[UnityObject]] = {}
    for document in documents:
        owner = parse_reference(document.get("m_GameObject"))
        if owner.file_id:
            components_by_game_object.setdefault(owner.file_id, []).append(document)

    nodes: dict[int, PrefabNode] = {}
    for document in documents:
        if document.type_name != "Transform":
            continue
        owner = parse_reference(document.get("m_GameObject"))
        game_object = by_file_id.get(owner.file_id)
        if game_object is None:
            continue
        node = PrefabNode(
            name=str(game_object.get("m_Name", "Object")),
            transform=document,
            game_object=game_object,
        )
        for component in components_by_game_object.get(owner.file_id, []):
            node.components.setdefault(component.type_name, []).append(component)
        nodes[document.file_id] = node

    roots: list[PrefabNode] = []
    for file_id, node in nodes.items():
        parent = parse_reference(node.transform.get("m_Father"))
        parent_node = nodes.get(parent.file_id)
        if parent_node is None:
            roots.append(node)
        else:
            parent_node.children.append(node)

    instances: list[dict] = []
    for document in documents:
        if document.type_name != "PrefabInstance":
            continue
        source = parse_reference(document.get("m_SourcePrefab"))
        if not source.guid:
            continue
        modifications = document.get("m_Modification.m_Modifications") or []
        overrides: dict[str, float] = {}
        material_overrides: dict[int, str] = {}
        name_override = ""
        for modification in modifications:
            if not isinstance(modification, dict):
                continue
            property_path = str(modification.get("propertyPath", ""))
            value = modification.get("value")
            if property_path == "m_Name" and isinstance(value, str):
                name_override = value
            elif isinstance(value, (int, float)):
                overrides[property_path] = float(value)
            material_match = re.fullmatch(r"m_Materials\.Array\.data\[(\d+)\]", property_path)
            if material_match:
                material_reference = parse_reference(modification.get("objectReference"))
                if material_reference.guid:
                    material_overrides[int(material_match.group(1))] = material_reference.guid
        instances.append(
            {
                "sourceGuid": source.guid,
                "parent": parse_reference(document.get("m_Modification.m_TransformParent")).file_id,
                "name": name_override,
                "overrides": overrides,
                "materialOverrides": material_overrides,
                "fileId": document.file_id,
            }
        )
    return roots, instances


def _instance_transform(overrides: dict[str, float]) -> unreal.Transform:
    position = {axis: overrides.get(f"m_LocalPosition.{axis}", 0.0) for axis in "xyz"}
    rotation = {axis: overrides.get(f"m_LocalRotation.{axis}", 0.0 if axis != "w" else 1.0)
                for axis in "xyzw"}
    scale = {axis: overrides.get(f"m_LocalScale.{axis}", 1.0) for axis in "xyz"}
    return unreal.Transform(
        location=_unreal_location(position),
        rotation=_unreal_rotation(rotation),
        scale=_unreal_scale(scale),
    )


# ---------------------------------------------------------------------------
# Blueprint construction
# ---------------------------------------------------------------------------


class BlueprintBuilder:
    def __init__(self, package_path: str, asset_name: str) -> None:
        self.subsystem = unreal.get_engine_subsystem(unreal.SubobjectDataSubsystem)
        package = f"{package_path}/{asset_name}"

        # Regenerating by delete-then-create does not work here: Unreal's delete
        # leaves the package loaded, and the following create is refused with
        # "already exists" while running unattended. Reusing the existing
        # Blueprint and emptying its component tree is both idempotent and
        # keeps every ChildActorComponent reference to it intact.
        existing = unreal.EditorAssetLibrary.load_asset(f"{package}.{asset_name}")
        if isinstance(existing, unreal.Blueprint):
            self.blueprint = existing
            handles = self.subsystem.k2_gather_subobject_data_for_blueprint(existing)
            if len(handles) > 1:
                self.subsystem.delete_subobjects(handles[0], list(handles[1:]), existing)
        else:
            factory = unreal.BlueprintFactory()
            factory.set_editor_property("parent_class", unreal.Actor)
            blueprint = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
                asset_name, package_path, unreal.Blueprint, factory
            )
            if not isinstance(blueprint, unreal.Blueprint):
                raise RuntimeError(f"Unable to create Blueprint {package}")
            self.blueprint = blueprint

        handles = self.subsystem.k2_gather_subobject_data_for_blueprint(self.blueprint)
        if not handles:
            raise RuntimeError(f"Blueprint {package} exposed no subobject data")
        self.root_handle = handles[0]

    def add_component(self, parent_handle, component_class, name: str):
        params = unreal.AddNewSubobjectParams(
            parent_handle=parent_handle,
            new_class=component_class,
            blueprint_context=self.blueprint,
        )
        handle, failure = self.subsystem.add_new_subobject(params)
        if not failure.is_empty():
            raise RuntimeError(f"add_new_subobject failed for {name}: {failure}")
        self.subsystem.rename_subobject(handle, unreal.Text(name))
        data = self.subsystem.k2_find_subobject_data_from_handle(handle)
        component = unreal.SubobjectDataBlueprintFunctionLibrary.get_object(data)
        return handle, component

    def save(self) -> str:
        unreal.BlueprintEditorLibrary.compile_blueprint(self.blueprint)
        unreal.EditorAssetLibrary.save_loaded_asset(self.blueprint, only_if_is_dirty=False)
        return self.blueprint.get_path_name()


def _apply_renderer(component, node: PrefabNode, index: AssetIndex, issues: list[str]) -> None:
    mesh_filters = node.components.get("MeshFilter") or []
    renderers = node.components.get("MeshRenderer") or []
    if not mesh_filters:
        return
    mesh_reference = parse_reference(mesh_filters[0].get("m_Mesh"))
    mesh, note = index.resolve_mesh(mesh_reference.guid, node.name)
    if mesh is None:
        issues.append(f"{node.name}: mesh {mesh_reference.guid or 'builtin'} unresolved ({note})")
        return
    if note:
        issues.append(f"{node.name}: {note}, used {mesh.get_path_name()}")
    component.set_editor_property("static_mesh", mesh)

    if not renderers:
        return
    materials = renderers[0].get("m_Materials") or []
    for slot, material_reference in enumerate(materials):
        reference = parse_reference(material_reference)
        material = index.resolve_material(reference.guid)
        if material is None:
            issues.append(f"{node.name}: material slot {slot} guid {reference.guid} unresolved")
            continue
        component.set_material(slot, material)
    if int(renderers[0].get("m_CastShadows", 1) or 0) == 0:
        component.set_editor_property("cast_shadow", False)


def _collision_from(node: PrefabNode) -> str:
    if node.components.get("MeshCollider"):
        return "mesh"
    if node.components.get("BoxCollider"):
        return "box"
    if node.components.get("CapsuleCollider"):
        return "capsule"
    return ""


def _convert_node(
    builder: BlueprintBuilder,
    parent_handle,
    node: PrefabNode,
    index: AssetIndex,
    blueprint_by_guid: dict[str, str],
    instances_by_parent: dict[int, list[dict]],
    issues: list[str],
    scripts: set[str],
    is_root: bool,
) -> None:
    has_mesh = bool(node.components.get("MeshFilter"))
    component_class = unreal.StaticMeshComponent if has_mesh else unreal.SceneComponent

    if is_root:
        handle, component = builder.add_component(builder.root_handle, component_class, _sanitize(node.name))
        # The generated Blueprint keeps its DefaultSceneRoot; attaching the
        # Unity root under it preserves the prefab's own root transform.
    else:
        handle, component = builder.add_component(parent_handle, component_class, _sanitize(node.name))

    component.set_editor_property("relative_location", _unreal_location(node.transform.get("m_LocalPosition")))
    relative_rotation = unreal.Rotator(0.0, 0.0, 0.0) if _is_unity_model_axis_root(node, index) else _unreal_rotation(node.transform.get("m_LocalRotation"))
    component.set_editor_property("relative_rotation", relative_rotation)
    component.set_editor_property("relative_scale3d", _unreal_scale(node.transform.get("m_LocalScale")))

    if has_mesh:
        _apply_renderer(component, node, index, issues)
        collision = _collision_from(node)
        if collision:
            # Unity's MeshCollider maps onto the imported mesh's own collision;
            # the primitive colliders are recorded for the gameplay gate rather
            # than approximated with a differently shaped body.
            component.set_collision_enabled(unreal.CollisionEnabled.QUERY_AND_PHYSICS)
            if collision != "mesh":
                issues.append(f"{node.name}: {collision} collider needs an explicit Unreal shape")
        else:
            component.set_collision_enabled(unreal.CollisionEnabled.NO_COLLISION)

    for behaviour in node.components.get("MonoBehaviour") or []:
        script = parse_reference(behaviour.get("m_Script"))
        if script.guid:
            scripts.add(script.guid)

    # A Unity LODGroup lists its levels as sibling GameObjects. Unreal carries
    # the LOD chain inside the StaticMesh itself (the importer consolidated
    # them), so only LOD0 becomes a component; emitting the rest would draw the
    # same rock three times.
    has_lod_group = bool(node.components.get("LODGroup"))
    for child in node.children:
        if has_lod_group:
            match = LOD_CHILD.match(child.name)
            if match and int(match.group("index")) != 0:
                continue
        _convert_node(
            builder, handle, child, index, blueprint_by_guid, instances_by_parent,
            issues, scripts, is_root=False,
        )

    for instance in instances_by_parent.get(node.transform.file_id, []):
        _add_child_actor(builder, handle, instance, blueprint_by_guid, index, issues)


def _add_child_actor(builder: BlueprintBuilder, parent_handle, instance: dict,
                     blueprint_by_guid: dict[str, str], index: AssetIndex,
                     issues: list[str]) -> None:
    target = blueprint_by_guid.get(instance["sourceGuid"])
    name = _sanitize(instance["name"] or f"Nested_{abs(instance['fileId']) % 100000}")
    if target:
        handle, component = builder.add_component(parent_handle, unreal.ChildActorComponent, name)
        generated = unreal.EditorAssetLibrary.load_asset(target)
        if isinstance(generated, unreal.Blueprint):
            component.set_editor_property("child_actor_class", generated.generated_class())
    else:
        # Unity lets a prefab nest an imported model directly: the instance then
        # points at the FBX rather than at a .prefab. One FBX can contain many
        # independently-authored meshes (all conveyors and machines do). Keep
        # the instance transform on one SceneComponent and expand every imported
        # child mesh below it. The FBX importer has already baked each node's
        # local transform into its StaticMesh vertices, so their component
        # transforms must remain identity relative to this shared root.
        meshes = index.resolve_meshes(instance["sourceGuid"])
        if not meshes:
            issues.append(
                f"{name}: nested source {instance['sourceGuid']} is neither a converted "
                "prefab nor an imported mesh"
            )
            return
        handle, component = builder.add_component(parent_handle, unreal.SceneComponent, name)
        used_names: set[str] = set()
        for mesh_index, mesh in enumerate(meshes):
            mesh_name = _sanitize(str(mesh.get_name()))
            component_name = f"{name}_{mesh_name}"
            if component_name.lower() in used_names:
                component_name = f"{component_name}_{mesh_index:03d}"
            used_names.add(component_name.lower())
            _, mesh_component = builder.add_component(
                handle, unreal.StaticMeshComponent, component_name
            )
            mesh_component.set_editor_property("static_mesh", mesh)
            mesh_component.set_editor_property("relative_location", unreal.Vector(0.0, 0.0, 0.0))
            mesh_component.set_editor_property("relative_rotation", unreal.Rotator(0.0, 0.0, 0.0))
            mesh_component.set_editor_property("relative_scale3d", unreal.Vector(1.0, 1.0, 1.0))
            mesh_component.set_collision_enabled(unreal.CollisionEnabled.NO_COLLISION)
            if index.slot_materials is not None:
                index.slot_materials.apply_to_component(mesh_component, mesh, issues)
            for slot, material_guid in sorted(instance.get("materialOverrides", {}).items()):
                material = index.resolve_material(material_guid)
                if material is None:
                    issues.append(
                        f"{component_name}: material override slot {slot} "
                        f"guid {material_guid} unresolved"
                    )
                    continue
                mesh_component.set_material(slot, material)
    transform = _instance_transform(instance["overrides"])
    component.set_editor_property("relative_location", transform.translation)
    component.set_editor_property("relative_rotation", transform.rotation.rotator())
    component.set_editor_property("relative_scale3d", transform.scale3d)


def _dependency_order(prefabs: list[dict], dependencies: dict[str, set[str]]) -> list[dict]:
    """Convert a prefab only after every prefab it nests."""
    by_guid = {prefab["guid"]: prefab for prefab in prefabs}
    ordered: list[dict] = []
    visited: set[str] = set()
    visiting: set[str] = set()

    def visit(guid: str) -> None:
        if guid in visited or guid not in by_guid:
            return
        if guid in visiting:
            # A prefab cycle cannot exist in Unity; treat defensively.
            return
        visiting.add(guid)
        for dependency in sorted(dependencies.get(guid, set())):
            visit(dependency)
        visiting.discard(guid)
        visited.add(guid)
        ordered.append(by_guid[guid])

    for prefab in prefabs:
        visit(prefab["guid"])
    return ordered


def main() -> int:
    project_dir = Path(unreal.Paths.project_dir())
    manifest = json.loads((project_dir / "Migration" / "unity_asset_manifest.json").read_text("utf-8"))
    unity_root = Path(manifest["unityRoot"])
    report_path = project_dir / "Migration" / "unity_prefab_import_report.json"

    index = _build_asset_index(project_dir)
    prefabs = [entry for entry in manifest["entries"] if entry["kind"] == "prefab"]
    source_filter = os.environ.get("CML_PREFAB_SOURCE_FILTER", "").strip()
    if source_filter:
        prefabs = [entry for entry in prefabs if source_filter.lower() in entry["source"].lower()]
        _log(f"Subset mode: source filter '{source_filter}' selected {len(prefabs)} prefabs")

    parsed: dict[str, tuple[list[PrefabNode], list[dict]]] = {}
    dependencies: dict[str, set[str]] = {}
    for entry in prefabs:
        try:
            roots, instances = _read_prefab(unity_root / Path(entry["source"]))
            parsed[entry["guid"]] = (roots, instances)
            dependencies[entry["guid"]] = {item["sourceGuid"] for item in instances}
        except Exception as exception:
            _error(f"{entry['source']}: parse failed: {exception}")
            parsed[entry["guid"]] = ([], [])
            dependencies[entry["guid"]] = set()

    ordered = _dependency_order(prefabs, dependencies)

    # Every generated Blueprint is deleted up front, in one pass, before any is
    # recreated. Deleting immediately before each create leaves the outgoing
    # package still loaded, and the subsequent create then silently returns
    # None - which is exactly how a half-regenerated tree appears.
    _log(f"Converting {len(ordered)} prefabs in dependency order")

    blueprint_by_guid: dict[str, str] = {}
    results: list[dict] = []
    scripts: set[str] = set()

    for position, entry in enumerate(ordered, 1):
        roots, instances = parsed[entry["guid"]]
        destination_path, asset_name = _destination_for(entry["source"])
        issues: list[str] = []
        try:
            if not roots and not instances:
                raise RuntimeError("prefab has neither a Transform root nor a nested prefab")
            builder = BlueprintBuilder(destination_path, asset_name)
            instances_by_parent: dict[int, list[dict]] = {}
            for instance in instances:
                instances_by_parent.setdefault(instance["parent"], []).append(instance)

            for root in roots:
                _convert_node(
                    builder, builder.root_handle, root, index, blueprint_by_guid,
                    instances_by_parent, issues, scripts, is_root=True,
                )
            # Instances parented to nothing sit directly under the actor root.
            for instance in instances_by_parent.get(0, []):
                _add_child_actor(builder, builder.root_handle, instance, blueprint_by_guid, index, issues)

            object_path = builder.save()
            unreal.EditorAssetLibrary.set_metadata_tag(builder.blueprint, "CML.UnityGuid", entry["guid"])
            unreal.EditorAssetLibrary.set_metadata_tag(builder.blueprint, "CML.UnitySource", entry["source"])
            unreal.EditorAssetLibrary.set_metadata_tag(builder.blueprint, "CML.UnitySha256", entry["sha256"])
            blueprint_by_guid[entry["guid"]] = object_path
            results.append(
                {
                    "source": entry["source"],
                    "guid": entry["guid"],
                    "sha256": entry["sha256"],
                    "status": "converted",
                    "object": object_path,
                    "issues": issues,
                }
            )
        except Exception as exception:
            _error(f"{entry['source']}: {exception}")
            results.append(
                {
                    "source": entry["source"],
                    "guid": entry["guid"],
                    "status": "failed",
                    "error": str(exception),
                    "issues": issues,
                }
            )
        if position % 16 == 0:
            unreal.SystemLibrary.collect_garbage()
            _log(f"Processed {position}/{len(ordered)}")

    report = {
        "schema": 1,
        "requested": len(prefabs),
        "converted": sum(item["status"] == "converted" for item in results),
        "failed": sum(item["status"] != "converted" for item in results),
        "withIssues": sum(1 for item in results if item.get("issues")),
        "monoBehaviourScriptGuids": sorted(scripts),
        "results": results,
    }
    if source_filter and report_path.exists():
        previous = json.loads(report_path.read_text("utf-8"))
        replaced = {item.get("guid") for item in results}
        merged_results = [item for item in previous.get("results", []) if item.get("guid") not in replaced]
        merged_results.extend(results)
        report["results"] = merged_results
        report["requested"] = len(merged_results)
        report["converted"] = sum(item.get("status") == "converted" for item in merged_results)
        report["failed"] = sum(item.get("status") != "converted" for item in merged_results)
        report["withIssues"] = sum(1 for item in merged_results if item.get("issues"))
        report["monoBehaviourScriptGuids"] = sorted(
            set(previous.get("monoBehaviourScriptGuids", [])) | scripts
        )

    temporary = report_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(report_path)
    if not source_filter:
        unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
    _log(
        f"Complete: converted={report['converted']}, failed={report['failed']}, "
        f"withIssues={report['withIssues']}"
    )
    return 0 if report["failed"] == 0 else 2


try:
    _exit_code = main()
except Exception:
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    _error(f"CML_PREFAB_IMPORT_FAILED code={_exit_code}")
else:
    _log("CML_PREFAB_IMPORT_SUCCEEDED")
