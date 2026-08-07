import bpy
import bmesh
import json
import math
import os
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

PROJECT = r"D:\Changing My Life\Game"
ROOT = os.path.join(PROJECT, r"Assets\_Project\Art\Environment\StarterIsland\VerticalRockKit_ReferenceV3")
MODELS = os.path.join(ROOT, "Models")
RENDERS = os.path.join(ROOT, "Renders")
REPORTS = os.path.join(ROOT, "Reports")
REFERENCE = r"C:\Users\slicc\AppData\Local\Temp\codex-clipboard-db984868-8918-4a06-867f-b0eff2430d10.png"
for folder in (MODELS, RENDERS, REPORTS):
    os.makedirs(folder, exist_ok=True)


SPECS = [
    {
        "name": "SM_VRKV3_Arch_A", "kind": "profile", "depth": 1.95, "bevel": 0.038,
        "profile": [
            (-3.28, 0.00), (-3.22, 2.75), (-2.92, 4.20), (-1.90, 5.02),
            (-0.15, 5.16), (1.65, 5.02), (2.72, 4.32), (3.12, 2.88),
            (3.22, 0.00), (2.00, 0.00), (1.98, 2.55), (1.62, 3.34),
            (0.78, 3.84), (-0.08, 3.95), (-0.92, 3.76), (-1.62, 3.25),
            (-2.00, 2.48), (-2.00, 0.00)
        ],
        "role": "true-through arch; tapered legs and thick lintel", "ratio_target": "~1:1",
    },
    {
        "name": "SM_VRKV3_Bridge_A", "kind": "profile", "depth": 1.85, "bevel": 0.026,
        "profile": [
            (-3.82, 0.26), (-3.55, 0.76), (-2.45, 1.06), (-0.95, 1.20),
            (0.72, 1.18), (2.28, 1.04), (3.48, 0.72), (3.82, 0.38),
            (3.42, 0.18), (2.05, 0.14), (0.48, 0.24), (-1.20, 0.16),
            (-2.72, 0.08)
        ],
        "role": "thin continuous bridge; uninterrupted underside and tapered ends", "ratio_target": "5-7:1",
    },
    {
        "name": "SM_VRKV3_Elevation_A", "kind": "profile", "depth": 2.20, "bevel": 0.036,
        "profile": [
            (-3.00, 0.12), (-2.82, 0.62), (-2.18, 0.92), (-1.28, 0.98),
            (-0.72, 1.04), (-0.18, 1.30), (0.34, 1.58), (1.32, 1.63),
            (2.20, 1.39), (3.00, 0.76), (2.66, 0.27), (1.72, 0.31),
            (0.94, 0.58), (0.36, 0.65), (-0.24, 0.44), (-1.42, 0.20),
            (-2.34, 0.28)
        ],
        "role": "two eroded grades joined by a broad diagonal transition", "ratio_target": "3.5-4:1",
    },
    {
        "name": "SM_VRKV3_Extension_A", "kind": "profile", "depth": 1.58, "bevel": 0.034, "warp": 0.35,
        "fold_line": ((-1.10, 1.18), (0.72, 3.62)), "fold_strength": 0.11,
        "profile": [
            (-1.00, 0.00), (-1.10, 1.18), (-0.98, 2.42), (-1.08, 3.50),
            (-0.66, 4.58), (-0.08, 4.90), (0.52, 4.46), (0.72, 3.62),
            (0.58, 2.78), (0.76, 1.82), (0.61, 0.70), (0.24, 0.18),
            (-0.34, 0.10)
        ],
        "role": "narrow leaning extension slab with one broad diagonal break", "ratio_target": "~0.4:1",
    },
    {
        "name": "SM_VRKV3_Flat_A", "kind": "profile", "depth": 3.10, "bevel": 0.040,
        "profile": [
            (-2.74, 0.10), (-2.66, 0.58), (-2.35, 1.00), (-1.92, 1.17),
            (-0.72, 1.20), (0.58, 1.19), (1.86, 1.16), (2.34, 0.98),
            (2.70, 0.56), (2.58, 0.16), (1.72, 0.07), (0.50, 0.13),
            (-0.82, 0.06), (-1.92, 0.14)
        ],
        "role": "low wide flat platform with a broad planar crown", "ratio_target": "3.0-3.5:1",
    },
    {
        "name": "SM_VRKV3_Overhang_A", "kind": "profile", "depth": 2.20, "bevel": 0.036, "warp": 0.45,
        "profile": [
            (-2.06, 0.08), (-1.55, 2.44), (-1.03, 3.02), (-0.32, 3.18),
            (0.72, 2.82), (1.62, 2.30), (2.30, 1.72), (2.02, 1.30),
            (0.86, 1.26), (0.02, 1.00), (-0.70, 0.52), (-1.26, 0.16)
        ],
        "role": "eroded triangular cantilever with inclined root and concave underside", "ratio_target": "1.35:1",
    },
    {
        "name": "SM_VRKV3_Overhang_Surface_A", "kind": "plan", "thickness": 0.50, "bevel": 0.040,
        "outline": [(0.00, -2.08), (0.90, -1.27), (1.48, -0.20),
                    (1.28, 0.78), (0.56, 1.48), (-0.42, 1.56),
                    (-1.20, 0.88), (-1.48, -0.08), (-0.82, -1.28)],
        "role": "shield-like top surface; rock volume, never a grass plane", "ratio_target": "~0.95:1 plan",
    },
    {
        "name": "SM_VRKV3_Overhang_Surface_B", "kind": "plan", "thickness": 0.48, "bevel": 0.040,
        "outline": [(0.28, -2.02), (1.08, -1.30), (1.50, -0.22),
                    (1.16, 0.84), (0.34, 1.52), (-0.58, 1.42),
                    (-1.28, 0.66), (-1.02, -0.02), (-1.20, -0.70),
                    (-0.30, -1.38)],
        "role": "directional asymmetric teardrop from the same overhang family", "ratio_target": "~0.85:1 plan",
    },
    {
        "name": "SM_VRKV3_Pillar_A", "kind": "profile", "depth": 1.85, "bevel": 0.040, "warp": 0.55,
        "profile": [
            (-1.08, 0.00), (-1.24, 0.48), (-0.94, 1.18), (-0.70, 2.14),
            (-0.82, 3.08), (-1.08, 3.78), (-0.52, 4.28), (0.32, 4.24),
            (0.98, 3.74), (0.70, 2.96), (0.54, 2.02), (0.82, 1.08),
            (1.00, 0.32), (0.54, 0.00)
        ],
        "role": "flared head/base with a narrow load-bearing waist", "ratio_target": "0.65:1",
    },
    {
        "name": "SM_VRKV3_Stone_A", "kind": "profile", "depth": 1.82, "bevel": 0.060,
        "profile": [
            (-1.90, 0.14), (-1.56, 0.58), (-0.72, 0.94), (0.34, 1.00),
            (1.28, 0.80), (1.86, 0.42), (1.66, 0.14), (0.82, 0.03),
            (-0.24, 0.00), (-1.26, 0.05)
        ],
        "role": "low oval stone with broad facets", "ratio_target": "1.75:1",
    },
    {
        "name": "SM_VRKV3_Stone_B", "kind": "profile", "depth": 1.78, "bevel": 0.060, "warp": 0.45,
        "profile": [
            (-1.18, 0.16), (-1.10, 0.76), (-0.72, 1.28), (-0.18, 1.48),
            (0.52, 1.38), (1.10, 0.92), (1.12, 0.34), (0.66, 0.09),
            (0.02, 0.00), (-0.70, 0.04)
        ],
        "role": "compact asymmetric stone", "ratio_target": "1.1:1",
    },
]


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)


def signed_area(points):
    return 0.5 * sum(points[i][0] * points[(i + 1) % len(points)][1] -
                     points[(i + 1) % len(points)][0] * points[i][1]
                     for i in range(len(points)))


def ccw(points):
    points = list(points)
    return points if signed_area(points) > 0.0 else list(reversed(points))


def triangulated_indices(points):
    vectors = [Vector((x, z, 0.0)) for x, z in points]
    triangles = tessellate_polygon([vectors])
    if triangles and isinstance(triangles[0][0], int):
        return [tuple(triangle) for triangle in triangles]
    key_map = {(round(v.x, 8), round(v.y, 8)): i for i, v in enumerate(vectors)}
    return [tuple(key_map[(round(v.x, 8), round(v.y, 8))] for v in tri) for tri in triangles]


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def finish_mesh(name, verts, faces, bevel_width):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    activate(obj)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    if bevel_width > 0.0:
        bevel = obj.modifiers.new("RockEdgeChamfer", 'BEVEL')
        bevel.width = bevel_width
        bevel.segments = 1
        bevel.limit_method = 'ANGLE'
        bevel.angle_limit = math.radians(32.0)
        bpy.ops.object.modifier_apply(modifier=bevel.name)
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    recenter_bottom(obj)
    return obj


def recenter_bottom(obj):
    coords = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    min_x, max_x = min(v.x for v in coords), max(v.x for v in coords)
    min_y, max_y = min(v.y for v in coords), max(v.y for v in coords)
    min_z = min(v.z for v in coords)
    obj.location -= Vector(((min_x + max_x) * 0.5, (min_y + max_y) * 0.5, min_z))
    activate(obj)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)


def create_profile_volume(spec, seed):
    points = ccw(spec["profile"])
    triangles = triangulated_indices(points)
    count = len(points)
    depth = spec["depth"]
    warp = spec.get("warp", 1.0)
    min_x, max_x = min(x for x, _ in points), max(x for x, _ in points)
    min_z, max_z = min(z for _, z in points), max(z for _, z in points)
    fold_line = spec.get("fold_line")
    fold_strength = spec.get("fold_strength", 0.0)
    fold_indices = None
    if fold_line:
        fold_indices = tuple(min(range(count), key=lambda idx: (points[idx][0] - p[0]) ** 2 +
                                                         (points[idx][1] - p[1]) ** 2)
                             for p in fold_line)
    verts = []
    for side in range(2):
        for i, (x, z) in enumerate(points):
            xn = (x - min_x) / max(1e-6, max_x - min_x)
            zn = (z - min_z) / max(1e-6, max_z - min_z)
            # Each large face stays on one broad plane. Earlier sinusoidal boundary
            # offsets made the hidden triangulation read as giant fan artifacts.
            broad = 0.040 * (xn - 0.5) + 0.026 * (zn - 0.5)
            if side == 0:
                y = -0.5 * depth + warp * broad
                if fold_line:
                    (ax, az), (bx, bz) = fold_line
                    length = max(1e-6, math.hypot(bx - ax, bz - az))
                    signed_distance = ((bx - ax) * (z - az) - (bz - az) * (x - ax)) / length
                    # Two planar regions meeting in one broad V-groove. Because
                    # abs(distance) is linear on each side, no fan triangulation
                    # is exposed, while the diagonal incision remains readable.
                    y += fold_strength * abs(signed_distance)
            else:
                y = 0.5 * depth - 0.30 * warp * broad
            verts.append((x, y, z))
    faces = []
    if fold_indices and fold_indices[0] != fold_indices[1]:
        start, end = fold_indices
        path_a = []
        cursor = start
        while True:
            path_a.append(cursor)
            if cursor == end:
                break
            cursor = (cursor + 1) % count
        path_b = []
        cursor = end
        while True:
            path_b.append(cursor)
            if cursor == start:
                break
            cursor = (cursor + 1) % count
        faces.extend((tuple(path_a), tuple(path_b)))
    else:
        for tri in triangles:
            faces.append(tri)
    for tri in triangles:
        faces.append(tuple(count + index for index in reversed(tri)))
    for i in range(count):
        nxt = (i + 1) % count
        faces.append((i, nxt, count + nxt, count + i))
    return finish_mesh(spec["name"], verts, faces, spec["bevel"])


def create_plan_volume(spec, seed):
    outline = ccw(spec["outline"])
    triangles = triangulated_indices(outline)
    count = len(outline)
    thickness = spec["thickness"]
    verts = []
    for layer in range(2):
        for i, (x, y) in enumerate(outline):
            if layer == 0:
                z = 0.010 * x - 0.006 * y
            else:
                z = thickness + 0.018 * x + 0.010 * y
            verts.append((x, y, z))
    faces = []
    for tri in triangles:
        faces.append(tuple(reversed(tri)))
        faces.append(tuple(count + index for index in tri))
    for i in range(count):
        nxt = (i + 1) % count
        faces.append((i, count + i, count + nxt, nxt))
    return finish_mesh(spec["name"], verts, faces, spec["bevel"])


def mesh_stats(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    remaining = set(bm.verts)
    components = 0
    while remaining:
        components += 1
        stack = [remaining.pop()]
        while stack:
            vert = stack.pop()
            for edge in vert.link_edges:
                other = edge.other_vert(vert)
                if other in remaining:
                    remaining.remove(other)
                    stack.append(other)
    non_manifold = sum(1 for edge in bm.edges if not edge.is_manifold)
    volume = abs(bm.calc_volume(signed=True))
    bm.free()
    mins = Vector((min((obj.matrix_world @ Vector(c)).x for c in obj.bound_box),
                   min((obj.matrix_world @ Vector(c)).y for c in obj.bound_box),
                   min((obj.matrix_world @ Vector(c)).z for c in obj.bound_box)))
    maxs = Vector((max((obj.matrix_world @ Vector(c)).x for c in obj.bound_box),
                   max((obj.matrix_world @ Vector(c)).y for c in obj.bound_box),
                   max((obj.matrix_world @ Vector(c)).z for c in obj.bound_box)))
    return {
        "vertices": len(obj.data.vertices), "faces": len(obj.data.polygons),
        "non_manifold_edges": non_manifold, "connected_components": components,
        "watertight": non_manifold == 0 and components == 1,
        "volume_m3": round(volume, 4),
        "dimensions_xyz_m": [round(v, 3) for v in obj.dimensions],
        "pivot_bottom_center_error_m": [round((mins.x + maxs.x) * 0.5, 5),
                                         round((mins.y + maxs.y) * 0.5, 5), round(mins.z, 5)],
    }


def export_obj(obj):
    activate(obj)
    bpy.ops.wm.obj_export(filepath=os.path.join(MODELS, obj.name + ".obj"),
                          export_selected_objects=True, export_materials=False,
                          forward_axis='NEGATIVE_Z', up_axis='Y')


def material_clay():
    mat = bpy.data.materials.new("M_V3_Clay")
    mat.diffuse_color = (0.46, 0.48, 0.51, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (0.46, 0.48, 0.51, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.92
    return mat


def material_auto_grass():
    mat = bpy.data.materials.new("M_V3_AutoGrassPreview")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    for node in list(nodes):
        nodes.remove(node)
    output = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    geometry = nodes.new("ShaderNodeNewGeometry")
    separate = nodes.new("ShaderNodeSeparateXYZ")
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 2.2
    noise.inputs["Detail"].default_value = 1.0
    noise.inputs["Roughness"].default_value = 0.35
    centre_noise = nodes.new("ShaderNodeMath")
    centre_noise.operation = 'SUBTRACT'
    centre_noise.inputs[1].default_value = 0.5
    noise_strength = nodes.new("ShaderNodeMath")
    noise_strength.operation = 'MULTIPLY'
    noise_strength.inputs[1].default_value = 0.10
    slope_with_breakup = nodes.new("ShaderNodeMath")
    slope_with_breakup.operation = 'ADD'
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = 'EASE'
    ramp.color_ramp.elements[0].position = 0.70
    ramp.color_ramp.elements[0].color = (0.62, 0.31, 0.19, 1.0)
    ramp.color_ramp.elements[1].position = 0.90
    ramp.color_ramp.elements[1].color = (0.38, 0.50, 0.15, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.88
    links.new(geometry.outputs["Normal"], separate.inputs["Vector"])
    links.new(geometry.outputs["Position"], noise.inputs["Vector"])
    links.new(noise.outputs["Fac"], centre_noise.inputs[0])
    links.new(centre_noise.outputs[0], noise_strength.inputs[0])
    links.new(separate.outputs["Z"], slope_with_breakup.inputs[0])
    links.new(noise_strength.outputs[0], slope_with_breakup.inputs[1])
    links.new(slope_with_breakup.outputs[0], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])
    return mat


def material_text():
    mat = bpy.data.materials.new("M_V3_Text")
    mat.diffuse_color = (0.90, 0.93, 0.97, 1.0)
    return mat


def material_reference():
    mat = bpy.data.materials.new("M_V3_Reference")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    for node in list(nodes):
        nodes.remove(node)
    out = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(REFERENCE, check_existing=True)
    links.new(texture.outputs["Color"], emission.inputs["Color"])
    links.new(emission.outputs["Emission"], out.inputs["Surface"])
    return mat


def assign(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def add_text(body, location, camera, mat, size=0.23):
    curve = bpy.data.curves.new("V3Label", 'FONT')
    curve.body = body
    curve.align_x = 'CENTER'
    curve.align_y = 'CENTER'
    curve.size = size
    curve.extrude = 0.003
    curve.materials.append(mat)
    text = bpy.data.objects.new("V3Label", curve)
    bpy.context.collection.objects.link(text)
    text.location = location
    text.rotation_euler = camera.rotation_euler
    return text


def add_area(name, location, energy, size, colour, target):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy, data.shape, data.size, data.color = energy, 'DISK', size, colour
    light = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(light)
    light.location = location
    light.rotation_euler = (Vector(target) - light.location).to_track_quat('-Z', 'Y').to_euler()


def render_contact(objects, reports):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 2800
    scene.render.resolution_y = 2000
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.world.color = (0.010, 0.014, 0.021)
    scene.view_settings.look = 'AgX - Medium High Contrast'
    camera_data = bpy.data.cameras.new("V3ContactCamera")
    camera = bpy.data.objects.new("V3ContactCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    camera_data.type = 'ORTHO'
    camera.location = (0.0, -38.0, 18.0)
    target = Vector((0.0, 0.0, 0.0))
    camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera_data.ortho_scale = 26.0
    scene.camera = camera
    add_area("V3Key", (-11.0, -13.0, 17.0), 2400, 8.0, (1.0, 0.73, 0.53), (0, 0, 0))
    add_area("V3Fill", (12.0, -8.0, 10.0), 1450, 8.0, (0.46, 0.62, 1.0), (0, 0, 0))
    add_area("V3Rim", (1.0, 10.0, 15.0), 1900, 7.0, (1.0, 0.50, 0.28), (0, 0, 0))
    color_mat = material_auto_grass()
    clay_mat = material_clay()
    text_mat = material_text()

    banner_mat = material_reference()
    bpy.ops.mesh.primitive_plane_add(size=2.0, location=(0.0, -2.5, 10.0))
    banner = bpy.context.object
    banner.name = "ReferenceBanner"
    banner.rotation_euler = camera.rotation_euler
    banner.scale = (8.7, 0.94, 1.0)
    assign(banner, banner_mat)

    x_positions = (-9.0, -3.0, 3.0, 9.0)
    bases = (3.2, -2.1, -7.2)
    labels = []
    original = [(obj.location.copy(), obj.rotation_euler.copy(), obj.scale.copy()) for obj in objects]
    for index, (obj, report) in enumerate(zip(objects, reports)):
        col, row = index % 4, index // 4
        x, base_z = x_positions[col], bases[row]
        scale = 3.50 / max(obj.dimensions)
        obj.scale = (scale, scale, scale)
        obj.rotation_euler[2] = math.radians(24.0)
        obj.location = (x, 0.0, base_z)
        assign(obj, color_mat)
        labels.append(add_text(obj.name.replace("SM_VRKV3_", "") +
                               f"\n{report['vertices']}v / {report['faces']}f",
                               (x, -1.8, base_z - 0.72), camera, text_mat, 0.22))

    scene.render.filepath = os.path.join(RENDERS, "ReferenceV3_R3_front_color.png")
    bpy.ops.render.render(write_still=True)
    for obj in objects:
        assign(obj, clay_mat)
    scene.render.filepath = os.path.join(RENDERS, "ReferenceV3_R3_front_clay.png")
    bpy.ops.render.render(write_still=True)

    # A dedicated orthographic top audit uses an XY grid. Reusing the front X/Z grid
    # caused projected overlap and made silhouette review unreliable.
    for label in labels:
        label.hide_render = True
    banner.hide_render = True
    camera.location = (0.0, 0.0, 40.0)
    camera.rotation_euler = (Vector((0.0, 0.0, 0.0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.ortho_scale = 24.0
    top_x_positions = (-9.0, -3.0, 3.0, 9.0)
    # The lower row sits slightly higher than an arithmetically even grid so its
    # plan silhouettes and captions remain fully inside the 2800x2000 frame.
    top_y_positions = (7.0, 0.8, -5.5)
    top_labels = []
    for index, (obj, report) in enumerate(zip(objects, reports)):
        col, row = index % 4, index // 4
        x, y = top_x_positions[col], top_y_positions[row]
        obj.location = (x, y, 0.0)
        top_labels.append(add_text(obj.name.replace("SM_VRKV3_", "") +
                                   f"\n{report['vertices']}v / {report['faces']}f",
                                   (x, y - 2.10, 6.0), camera, text_mat, 0.22))
    for obj in objects:
        assign(obj, color_mat)
    scene.render.filepath = os.path.join(RENDERS, "ReferenceV3_R3_top_color.png")
    bpy.ops.render.render(write_still=True)
    for obj in objects:
        assign(obj, clay_mat)
    scene.render.filepath = os.path.join(RENDERS, "ReferenceV3_R3_top_clay.png")
    bpy.ops.render.render(write_still=True)

    for label in top_labels:
        bpy.data.objects.remove(label, do_unlink=True)
    for label in labels:
        bpy.data.objects.remove(label, do_unlink=True)
    bpy.data.objects.remove(banner, do_unlink=True)
    for index, (obj, state) in enumerate(zip(objects, original)):
        obj.data.materials.clear()
        obj.location, obj.rotation_euler, obj.scale = state
        obj.location = ((index % 4) * 10.0, (index // 4) * 7.5, 0.0)


clear_scene()
objects = []
reports = []
for index, spec in enumerate(SPECS):
    obj = create_profile_volume(spec, index + 1) if spec["kind"] == "profile" else create_plan_volume(spec, index + 1)
    stats = mesh_stats(obj)
    if not stats["watertight"]:
        raise RuntimeError(f"Non-manifold V3 mesh {obj.name}: {stats}")
    report = {"name": obj.name, "archetype": spec["role"], "ratio_target": spec["ratio_target"], **stats}
    objects.append(obj)
    reports.append(report)
    export_obj(obj)

render_contact(objects, reports)
with open(os.path.join(REPORTS, "ReferenceV3_validation.json"), 'w', encoding='utf-8') as handle:
    json.dump({
        "revision": 3,
        "reference": REFERENCE,
        "asset_count": len(objects),
        "single_mesh_per_asset": True,
        "separate_grass_geometry": False,
        "unity_assets_or_scenes_modified": False,
        "audit_renders": [
            "ReferenceV3_R3_front_color.png", "ReferenceV3_R3_front_clay.png",
            "ReferenceV3_R3_top_color.png", "ReferenceV3_R3_top_clay.png"
        ],
        "assets": reports,
    }, handle, indent=2)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "VerticalRockKit_ReferenceV3_Source.blend"))
print("REFERENCE_V3_VALIDATION", json.dumps(reports))
