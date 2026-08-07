import bpy
import bmesh
import json
import math
import os
from mathutils import Vector

PROJECT = r"D:\Changing My Life\Game"
SOURCE = os.path.join(PROJECT, r"Assets\_Project\Art\Environment\StarterIsland\Rocks\Models")
ROOT = os.path.join(PROJECT, r"Assets\_Project\Art\Environment\StarterIsland\VerticalRockKit_Sculpted")
MODELS = os.path.join(ROOT, "Models")
RENDERS = os.path.join(ROOT, "Renders")
REPORTS = os.path.join(ROOT, "Reports")
for folder in (MODELS, RENDERS, REPORTS):
    os.makedirs(folder, exist_ok=True)


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def recenter_and_ground(obj):
    bpy.context.view_layer.update()
    coords = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    min_x, max_x = min(v.x for v in coords), max(v.x for v in coords)
    min_y, max_y = min(v.y for v in coords), max(v.y for v in coords)
    min_z = min(v.z for v in coords)
    obj.location.x -= 0.5 * (min_x + max_x)
    obj.location.y -= 0.5 * (min_y + max_y)
    obj.location.z -= min_z
    activate(obj)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)


def import_stamp(filename, target_dimensions, rotation_degrees, location):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SOURCE, filename))
    meshes = [obj for obj in bpy.data.objects if obj not in before and obj.type == 'MESH']
    if not meshes:
        raise RuntimeError("No mesh imported from " + filename)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = "STAMP_" + os.path.splitext(filename)[0]
    recenter_and_ground(obj)
    dims = obj.dimensions
    if min(dims) <= 1e-5:
        raise RuntimeError("Degenerate source dimensions: " + filename)
    obj.scale = tuple(target_dimensions[i] / dims[i] for i in range(3))
    obj.rotation_euler = tuple(math.radians(v) for v in rotation_degrees)
    activate(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    recenter_and_ground(obj)
    obj.location = location
    activate(obj)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    obj.data.materials.clear()
    return obj


def connected_components(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    remaining = set(bm.verts)
    count = 0
    while remaining:
        count += 1
        stack = [remaining.pop()]
        while stack:
            vert = stack.pop()
            for edge in vert.link_edges:
                other = edge.other_vert(vert)
                if other in remaining:
                    remaining.remove(other)
                    stack.append(other)
    bm.free()
    return count


def fuse_wall_a():
    stamps = [
        # Dominant core: 85% of the final width, so the wall reads as one mass.
        import_stamp("ENV_Rock_BoulderLarge_A.glb", (8.20, 3.05, 4.95), (1.0, -3.0, 1.5), (0.0, 0.10, 0.0)),
        # Shoulder and buttress are buried 75-82% into the core volume.
        import_stamp("ENV_Rock_BoulderMedium_A.glb", (3.60, 3.10, 5.35), (-2.0, 4.0, 5.0), (-3.10, 0.14, 0.0)),
        import_stamp("ENV_Rock_BoulderMedium_B.glb", (3.40, 3.35, 3.95), (3.0, -5.0, -7.0), (3.00, -0.25, 0.0)),
        # One local, deeply embedded ledge (33% of width), never a full-width groove.
        import_stamp("ENV_Rock_ShoreFlat_A.glb", (3.15, 2.55, 1.20), (-4.0, 3.0, -4.0), (-0.65, -0.92, 2.05)),
    ]
    bpy.ops.object.select_all(action='DESELECT')
    for stamp in stamps:
        stamp.select_set(True)
    bpy.context.view_layer.objects.active = stamps[1]
    bpy.ops.object.join()
    wall = bpy.context.view_layer.objects.active
    wall.name = "SM_VRK_Wall_A"
    activate(wall)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # A real volume reconstruction: overlapping source shells become one exterior shell.
    wall.data.remesh_voxel_size = 0.105
    wall.data.remesh_voxel_adaptivity = 0.0
    try:
        bpy.ops.object.voxel_remesh()
    except Exception as exc:
        raise RuntimeError("VOXEL_REMESH_OPERATOR_FAILED: " + repr(exc))
    if connected_components(wall) != 1:
        raise RuntimeError("VOXEL_REMESH_DISCONNECTED_COMPONENTS")

    smooth = wall.modifiers.new("SculptRelax", 'SMOOTH')
    smooth.factor = 0.44
    smooth.iterations = 5
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    return wall


def art_direct_front(wall):
    xs = [v.co.x for v in wall.data.vertices]
    ys = [v.co.y for v in wall.data.vertices]
    zs = [v.co.z for v in wall.data.vertices]
    xmin, xmax = min(xs), max(xs)
    ymin, ymax = min(ys), max(ys)
    zmin, zmax = min(zs), max(zs)
    width, depth, height = xmax - xmin, ymax - ymin, zmax - zmin
    centre_y = 0.5 * (ymin + ymax)

    for vert in wall.data.vertices:
        x, y, z = vert.co
        if y >= centre_y:
            continue
        xn = (x - xmin) / width
        zn = (z - zmin) / height
        # Preserve the organic border and crest inherited from the stamps.
        border_x = min(1.0, max(0.0, min(xn, 1.0 - xn) / 0.12))
        border_z = min(1.0, max(0.0, zn / 0.12))
        frontness = min(1.0, max(0.0, (centre_y - y) / (0.38 * depth)))
        weight = border_x * border_z * frontness
        if weight <= 0.0:
            continue

        # Five broad, overlapping geological planes. Gaussian blending removes stamp necks;
        # the later flat decimation restores a deliberate faceted read without cut lines.
        planes = (
            (0.18, 0.54, 0.29, 0.46, 0.105, 0.035, 0.050),
            (0.43, 0.28, 0.31, 0.31, 0.155, -0.030, 0.025),
            (0.48, 0.70, 0.34, 0.34, 0.175, 0.025, -0.035),
            (0.72, 0.48, 0.29, 0.43, 0.090, 0.040, 0.015),
            (0.87, 0.30, 0.22, 0.33, 0.065, 0.020, 0.045),
        )
        total_w = 0.0
        target_y = 0.0
        for cx, cz, rx, rz, offset, slope_x, slope_z in planes:
            dx, dz = (xn - cx) / rx, (zn - cz) / rz
            pw = math.exp(-2.2 * (dx * dx + dz * dz))
            py = ymin + depth * (offset + slope_x * (xn - cx) + slope_z * (zn - cz))
            total_w += pw
            target_y += pw * py
        if total_w > 1e-5:
            plane = target_y / total_w
            vert.co.y = y * (1.0 - 0.58 * weight) + plane * (0.58 * weight)
    wall.data.update()

    decimate = wall.modifiers.new("SculptDecimate", 'DECIMATE')
    decimate.decimate_type = 'COLLAPSE'
    decimate.ratio = 0.085
    decimate.use_collapse_triangulate = True
    activate(wall)
    bpy.ops.object.modifier_apply(modifier=decimate.name)
    for polygon in wall.data.polygons:
        polygon.use_smooth = False
    recenter_and_ground(wall)


def validate(wall):
    bm = bmesh.new()
    bm.from_mesh(wall.data)
    bm.normal_update()
    non_manifold = sum(1 for edge in bm.edges if not edge.is_manifold)
    volume = abs(bm.calc_volume(signed=True))
    bm.free()
    components = connected_components(wall)
    report = {
        "name": wall.name,
        "version": 2,
        "pipeline": "dominant organic core + deeply embedded shoulder/buttress/local ledge -> voxel remesh -> relax -> soft macro planes -> decimate",
        "source_stamps": ["ENV_Rock_BoulderLarge_A.glb", "ENV_Rock_BoulderMedium_A.glb", "ENV_Rock_BoulderMedium_B.glb", "ENV_Rock_ShoreFlat_A.glb"],
        "vertices": len(wall.data.vertices),
        "faces": len(wall.data.polygons),
        "non_manifold_edges": non_manifold,
        "connected_components": components,
        "watertight_single_volume": non_manifold == 0 and components == 1,
        "volume_m3": round(volume, 3),
        "dimensions_xyz_m": [round(v, 3) for v in wall.dimensions],
        "voxel_size_m": 0.105,
        "dominant_core_target_width_m": 8.2,
    }
    if not report["watertight_single_volume"]:
        raise RuntimeError("WALL_A_VALIDATION_FAILED: " + json.dumps(report))
    with open(os.path.join(REPORTS, "Wall_A_voxel_validation.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    print("VOXEL_WALL_A_VALIDATION", json.dumps(report))


def material(name, colour, roughness):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*colour, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


def add_area(name, location, energy, size, colour, target):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy, data.shape, data.size, data.color = energy, 'DISK', size, colour
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def render(wall):
    beauty = material("M_VoxelWall_Review", (0.39, 0.12, 0.046), 0.88)
    clay = material("M_VoxelWall_Clay", (0.48, 0.51, 0.54), 0.92)
    wall.data.materials.append(beauty)
    bpy.ops.mesh.primitive_plane_add(size=30, location=(0, 0.5, -0.10))
    bpy.context.object.data.materials.append(material("M_ReviewGround", (0.045, 0.052, 0.062), 0.96))
    target = (0.0, 0.1, 2.4)
    add_area("Key", (-5.5, -8.0, 9.5), 1450, 5.5, (1.0, 0.67, 0.44), target)
    add_area("Fill", (7.0, -4.0, 6.2), 800, 5.0, (0.43, 0.60, 1.0), target)
    add_area("Rim", (2.5, 5.0, 8.5), 1200, 4.0, (1.0, 0.46, 0.25), target)
    cam_data = bpy.data.cameras.new("ReviewCamera")
    camera = bpy.data.objects.new("ReviewCamera", cam_data)
    bpy.context.collection.objects.link(camera)
    camera.location = (10.5, -15.5, 7.5)
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 11.0
    scene = bpy.context.scene
    scene.camera = camera
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = 1000, 760
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.world.color = (0.014, 0.018, 0.026)
    scene.view_settings.look = 'AgX - Medium High Contrast'
    scene.render.filepath = os.path.join(RENDERS, "Wall_A_voxel_v2_beauty.png")
    bpy.ops.render.render(write_still=True)

    # Strict silhouette gate: frontal, neutral and without a ground plane.
    wall.data.materials.clear()
    wall.data.materials.append(clay)
    bpy.context.object.hide_render = True
    camera.location = (0.0, -16.0, 4.5)
    camera.rotation_euler = (Vector((0.0, 0.0, 2.45)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.ortho_scale = 10.8
    scene.render.filepath = os.path.join(RENDERS, "Wall_A_voxel_v2_clay_front.png")
    bpy.ops.render.render(write_still=True)


def export(wall):
    activate(wall)
    bpy.ops.wm.obj_export(filepath=os.path.join(MODELS, "SM_VRK_Wall_A.obj"),
                          export_selected_objects=True, export_materials=False,
                          forward_axis='NEGATIVE_Z', up_axis='Y')


clear_scene()
wall_a = fuse_wall_a()
art_direct_front(wall_a)
validate(wall_a)
export(wall_a)
render(wall_a)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "Wall_A_Voxel_Source.blend"))
