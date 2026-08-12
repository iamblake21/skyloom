import bpy
import os
import sys


def main():
    argv = sys.argv
    separator = argv.index("--") + 1
    source_path = os.path.abspath(argv[separator])
    destination_path = os.path.abspath(argv[separator + 1])

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.ops.import_scene.gltf(filepath=source_path)

    visual_meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and not obj.name.upper().startswith(("COLMESH_", "SYS_"))
    ]
    if not visual_meshes:
        raise RuntimeError("No visual airship meshes found")

    bpy.ops.object.select_all(action="DESELECT")
    for obj in visual_meshes:
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")

    bpy.context.view_layer.objects.active = visual_meshes[0]
    bpy.ops.object.join()
    airship = bpy.context.view_layer.objects.active
    airship.name = "SM_Airship_Visual"
    airship.data.name = "SM_Airship_Visual"

    # Bake the glTF hierarchy into a single mesh at the world origin. Material
    # slots remain separate so Unreal can bind the migrated atlas materials.
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

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

    print(f"CML_AIRSHIP_EXPORT={destination_path}")
    print(f"dimensions_m={tuple(round(value, 4) for value in airship.dimensions)}")
    print("material_slots=" + ",".join(slot.material.name if slot.material else "None" for slot in airship.material_slots))


if __name__ == "__main__":
    main()
