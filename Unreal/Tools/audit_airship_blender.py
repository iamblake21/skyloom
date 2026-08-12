import bpy
import sys
from mathutils import Vector


def main():
    argv = sys.argv
    source_index = argv.index("--") + 1
    source_path = argv[source_index]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=source_path)

    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    visible = [obj for obj in meshes if not obj.name.upper().startswith(("COLMESH_", "SYS_"))]
    collisions = [obj for obj in meshes if obj.name.upper().startswith("COLMESH_")]

    bounds = []
    for obj in visible:
        bounds.extend(obj.matrix_world @ Vector(corner) for corner in obj.bound_box)

    mins = Vector((min(v.x for v in bounds), min(v.y for v in bounds), min(v.z for v in bounds)))
    maxs = Vector((max(v.x for v in bounds), max(v.y for v in bounds), max(v.z for v in bounds)))

    print("CML_AIRSHIP_AUDIT")
    print(f"mesh_count={len(meshes)}")
    print(f"visual_mesh_count={len(visible)}")
    print(f"collision_mesh_count={len(collisions)}")
    print(f"bounds_min={tuple(round(value, 4) for value in mins)}")
    print(f"bounds_max={tuple(round(value, 4) for value in maxs)}")
    print(f"dimensions={tuple(round(value, 4) for value in (maxs - mins))}")
    print("materials=" + ",".join(sorted({slot.material.name for obj in visible for slot in obj.material_slots if slot.material})))
    print("roots=" + ",".join(obj.name for obj in bpy.context.scene.objects if obj.parent is None))
    print("visual_sample=" + ",".join(obj.name for obj in visible[:30]))


if __name__ == "__main__":
    main()
