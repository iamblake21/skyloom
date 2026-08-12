import bpy
import os
import sys
from mathutils import Vector


def has_ancestor(obj, prefix):
    current = obj
    wanted = prefix.upper()
    while current is not None:
        if current.name.upper().startswith(wanted):
            return True
        current = current.parent
    return False


def export_group(source_path, destination_path, asset_name, predicate, pivot=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.ops.import_scene.gltf(filepath=source_path)

    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH" and predicate(obj)]
    if not meshes:
        raise RuntimeError(f"No meshes selected for {asset_name}")

    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    result = bpy.context.view_layer.objects.active
    result.name = asset_name
    result.data.name = asset_name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    if pivot is not None:
        offset = Vector(pivot)
        for vertex in result.data.vertices:
            vertex.co -= offset

    os.makedirs(os.path.dirname(destination_path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=destination_path,
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Y",
        axis_up="Z",
        bake_space_transform=False,
        add_leaf_bones=False,
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        use_tspace=True,
        embed_textures=False,
        path_mode="AUTO",
    )
    print(f"CML_RUNTIME_EXPORT={destination_path};meshes={len(meshes)}")


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    project_parent = os.path.abspath(args[0])
    output = os.path.abspath(args[1])
    manual_models = os.path.join(
        project_parent, "Game", "Assets", "_Project", "Art", "ManualEra", "Models")
    airship_source = os.path.join(
        project_parent, "Game", "Assets", "_Project", "Art", "Vehicles", "Airship", "Models",
        "AIR_Airship.glb")

    crate_source = os.path.join(manual_models, "STR_Crate.glb")
    workbench_source = os.path.join(manual_models, "STR_Workbench.glb")
    furnace_source = os.path.join(manual_models, "STR_CrudeFurnace.glb")
    export_group(
        crate_source, os.path.join(output, "SM_Crate_RuntimeBody.fbx"),
        "SM_Crate_RuntimeBody",
        lambda obj: obj.name.upper().startswith("GEO_CRATEBODY"))
    export_group(
        crate_source, os.path.join(output, "SM_Crate_RuntimeLid.fbx"),
        "SM_Crate_RuntimeLid",
        lambda obj: obj.name.upper().startswith("GEO_CRATELID"),
        # The prefab root mirrors the model's local depth. Bake the opposite
        # model edge so Unreal rotates around the visible rear hinge rather
        # than the front edge selected by the raw Unity-local coordinates.
        pivot=(0.0, 0.35, 0.54))
    export_group(
        workbench_source, os.path.join(output, "SM_Workbench_RuntimeVisual.fbx"),
        "SM_Workbench_RuntimeVisual",
        lambda obj: obj.name.upper().startswith("GEO_"))
    export_group(
        furnace_source, os.path.join(output, "SM_CrudeFurnace_RuntimeVisual.fbx"),
        "SM_CrudeFurnace_RuntimeVisual",
        # The source contains a disabled authoring cube. Unity's prefab renders
        # only the four GEO_* groups, including the emissive fire mesh.
        lambda obj: obj.name.upper().startswith("GEO_"))

    def visible_airship(obj):
        name = obj.name.upper()
        return not name.startswith(("COLMESH_", "SYS_"))

    export_group(
        airship_source, os.path.join(output, "SM_Airship_RuntimeVisual.fbx"),
        "SM_Airship_RuntimeVisual",
        lambda obj: visible_airship(obj) and not has_ancestor(obj, "ANM_AccessDoor"))
    export_group(
        airship_source, os.path.join(output, "SM_Airship_RuntimeDoor.fbx"),
        "SM_Airship_RuntimeDoor",
        lambda obj: visible_airship(obj) and has_ancestor(obj, "ANM_AccessDoor"),
        pivot=(1.43, -0.4, 1.3))


if __name__ == "__main__":
    main()
