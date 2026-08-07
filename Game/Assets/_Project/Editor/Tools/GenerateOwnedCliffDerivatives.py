import bpy
import bmesh
import json
import math
import os
from mathutils import Vector

PROJECT = r"D:\Changing My Life\Game"
SOURCE_ROOT = os.path.join(PROJECT, r"Assets\Proxy Games\Stylized Nature Kit Lite\Meshes\Rocks\Rock Cliffs")
OUT_ROOT = os.path.join(PROJECT, r"Assets\_Project\Art\Environment\StarterIsland\VerticalRockKit_Sculpted")
MODELS = os.path.join(OUT_ROOT, "Models")
RENDERS = os.path.join(OUT_ROOT, "Renders")
REPORTS = os.path.join(OUT_ROOT, "Reports")
for folder in (MODELS, RENDERS, REPORTS):
    os.makedirs(folder, exist_ok=True)

SPECS = [
    {
        "name": "SM_VRKS_Straight_A", "source": "Rock Cliff 2.fbx",
        "target_dims": (7.50, 3.35, 6.10), "rotation_z": -6.0,
        "target_faces": 1450, "recommended_overlap_m": 1.35,
    },
    {
        "name": "SM_VRKS_Corner_A", "source": "Rock Cliff 5.fbx",
        "target_dims": (6.20, 5.10, 6.00), "rotation_z": 14.0,
        "target_faces": 1700, "recommended_overlap_m": 1.45,
    },
    {
        "name": "SM_VRKS_End_A", "source": "Rock Cliff 1.fbx",
        "target_dims": (5.10, 3.55, 5.45), "rotation_z": -16.0,
        "target_faces": 1400, "recommended_overlap_m": 1.20,
    },
    {
        "name": "SM_VRKS_Straight_B", "source": "Rock Cliff 3.fbx",
        "target_dims": (8.40, 3.55, 5.80), "rotation_z": 6.0,
        "target_faces": 1650, "recommended_overlap_m": 1.55,
        "deformation": "wide_convex_taper",
    },
    {
        "name": "SM_VRKS_Corner_B", "source": "Rock Cliff 1.fbx",
        "target_dims": (5.65, 4.75, 5.95), "rotation_z": 18.0,
        "target_faces": 1450, "recommended_overlap_m": 1.35,
        "deformation": "corner_shear_warp",
    },
    {
        "name": "SM_VRKS_End_B", "source": "Rock Cliff 5.fbx",
        "target_dims": (5.10, 3.65, 5.25), "rotation_z": -20.0,
        "target_faces": 1650, "recommended_overlap_m": 1.25,
        "deformation": "end_nonlinear_profile",
    },
    {
        "name": "SM_VRKS_Ledge_A", "source": "Rock Cliff 4.fbx",
        "target_dims": (9.60, 3.90, 3.15), "rotation_z": -4.0,
        "target_faces": 1900, "recommended_overlap_m": 1.80,
        "deformation": "wide_local_shelf",
    },
    {
        "name": "SM_VRKS_Transition_A", "source": "Rock Cliff 3.fbx",
        "target_dims": (7.40, 3.25, 4.35), "rotation_z": -9.0,
        "target_faces": 1600, "recommended_overlap_m": 1.45,
        "deformation": "descending_transition",
    },
]


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def bounds(obj):
    coords = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    mins = Vector((min(v.x for v in coords), min(v.y for v in coords), min(v.z for v in coords)))
    maxs = Vector((max(v.x for v in coords), max(v.y for v in coords), max(v.z for v in coords)))
    return mins, maxs


def recenter_bottom(obj):
    bpy.context.view_layer.update()
    mins, maxs = bounds(obj)
    obj.location -= Vector(((mins.x + maxs.x) * 0.5, (mins.y + maxs.y) * 0.5, mins.z))
    activate(obj)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)


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
    return {
        "vertices": len(obj.data.vertices),
        "faces": len(obj.data.polygons),
        "non_manifold_edges": non_manifold,
        "connected_components": components,
        "volume_m3": round(volume, 4),
    }


def keep_largest_component(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    remaining = set(bm.verts)
    groups = []
    while remaining:
        group = set()
        seed = remaining.pop()
        stack = [seed]
        group.add(seed)
        while stack:
            vert = stack.pop()
            for edge in vert.link_edges:
                other = edge.other_vert(vert)
                if other in remaining:
                    remaining.remove(other)
                    group.add(other)
                    stack.append(other)
        groups.append(group)
    largest = max(groups, key=len)
    remove = [vert for vert in bm.verts if vert not in largest]
    bmesh.ops.delete(bm, geom=remove, context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def import_lod0(spec):
    path = os.path.join(SOURCE_ROOT, spec["source"])
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=False)
    imported = [obj for obj in bpy.data.objects if obj not in before]
    meshes = [obj for obj in imported if obj.type == 'MESH']
    if not meshes:
        raise RuntimeError("No meshes imported from " + path)
    # Explicit LOD0 selection: largest polygon shell in the FBX.
    lod0 = max(meshes, key=lambda obj: len(obj.data.polygons))
    for obj in imported:
        if obj is not lod0:
            bpy.data.objects.remove(obj, do_unlink=True)
    lod0.name = spec["name"]
    activate(lod0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    raw = mesh_stats(lod0)
    raw["selected_source_mesh"] = lod0.data.name
    raw["source_file"] = path

    recenter_bottom(lod0)
    lod0.rotation_euler[2] = math.radians(spec["rotation_z"])
    activate(lod0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    recenter_bottom(lod0)
    dims = lod0.dimensions
    lod0.scale = tuple(spec["target_dims"][axis] / dims[axis] for axis in range(3))
    activate(lod0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    recenter_bottom(lod0)
    lod0.data.materials.clear()
    return lod0, raw


def apply_art_deformation(obj, mode):
    """Topology-preserving vertex warps: each B/utility silhouette is genuinely unique."""
    if not mode:
        return {"mode": "none", "vertex_positions_modified": False}
    xs = [vert.co.x for vert in obj.data.vertices]
    ys = [vert.co.y for vert in obj.data.vertices]
    zs = [vert.co.z for vert in obj.data.vertices]
    xmin, xmax = min(xs), max(xs)
    ymin, ymax = min(ys), max(ys)
    zmin, zmax = min(zs), max(zs)
    width, depth, height = xmax - xmin, ymax - ymin, zmax - zmin
    cx, cy = 0.5 * (xmin + xmax), 0.5 * (ymin + ymax)

    for vert in obj.data.vertices:
        x, y, z = vert.co
        xn = (x - xmin) / max(width, 1e-6)
        yn = (y - ymin) / max(depth, 1e-6)
        zn = (z - zmin) / max(height, 1e-6)
        if mode == "wide_convex_taper":
            # One broad convex wall: narrower crown, asymmetric shoulder and front belly.
            taper = 1.035 - 0.145 * (zn ** 1.35)
            x = cx + (x - cx) * taper + 0.23 * (zn ** 2) - 0.10 * math.sin(math.pi * xn) * zn
            if yn < 0.55:
                y -= 0.30 * math.sin(math.pi * xn) ** 2 * math.sin(math.pi * zn) ** 2
        elif mode == "corner_shear_warp":
            # Curved plan-view turn plus vertical shear; cannot be recreated by object rotation.
            taper = 1.08 - 0.20 * zn
            x = cx + (x - cx) * taper + 0.48 * (zn ** 1.6) + 0.12 * (yn - 0.5) * zn
            y += 0.52 * (xn - 0.42) * (0.30 + 0.70 * zn) + 0.12 * math.sin(math.pi * zn)
        elif mode == "end_nonlinear_profile":
            # Directional termination: narrows and descends toward +X, while rear depth collapses.
            directional = xn ** 1.45
            x = xmin + width * (xn ** 1.12)
            z = zmin + (z - zmin) * (1.03 - 0.27 * directional)
            y = cy + (y - cy) * (1.0 - 0.24 * directional * (0.35 + 0.65 * zn))
            if yn < 0.50:
                y -= 0.16 * math.sin(math.pi * zn) * (1.0 - directional)
        elif mode == "wide_local_shelf":
            # Low, wide shelf with a local overhang that pinches out before both ends.
            crown_taper = 1.02 - 0.08 * zn
            x = cx + (x - cx) * crown_taper
            shelf = math.sin(math.pi * xn) ** 4
            vertical_band = math.exp(-26.0 * (zn - 0.63) ** 2)
            if yn < 0.58:
                y -= 0.38 * shelf * vertical_band
            z += 0.10 * math.sin(math.pi * xn) ** 2 * (zn ** 2)
        elif mode == "descending_transition":
            # True transition profile: high shoulder at -X, progressively lower and shallower at +X.
            descent = 1.03 - 0.42 * (xn ** 1.35)
            z = zmin + (z - zmin) * descent
            x += 0.30 * zn * (xn - 0.25)
            y = cy + (y - cy) * (1.02 - 0.22 * xn)
            if yn < 0.50:
                y -= 0.14 * math.sin(math.pi * xn) * (1.0 - zn)
        else:
            raise RuntimeError("Unknown deformation mode: " + mode)
        vert.co = (x, y, z)
    obj.data.update()
    recenter_bottom(obj)
    return {"mode": mode, "vertex_positions_modified": True}


def repair_if_needed(obj):
    before = mesh_stats(obj)
    repaired = False
    removed_components = 0
    voxel_size = None
    if before["non_manifold_edges"] or before["connected_components"] != 1:
        repaired = True
        voxel_size = round(max(obj.dimensions) / 145.0, 5)
        activate(obj)
        obj.data.remesh_voxel_size = voxel_size
        obj.data.remesh_voxel_adaptivity = 0.0
        bpy.ops.object.voxel_remesh()
        after_voxel = mesh_stats(obj)
        if after_voxel["connected_components"] != 1:
            removed_components = after_voxel["connected_components"] - 1
            keep_largest_component(obj)
            activate(obj)
            obj.data.remesh_voxel_size = voxel_size
            obj.data.remesh_voxel_adaptivity = 0.0
            bpy.ops.object.voxel_remesh()
    after = mesh_stats(obj)
    if after["non_manifold_edges"] or after["connected_components"] != 1:
        raise RuntimeError(f"Repair failed for {obj.name}: {after}")
    return {"required": repaired, "voxel_size_m": voxel_size,
            "discarded_small_components": removed_components, "before": before, "after": after}


def controlled_back_embedding(obj):
    ys = [vert.co.y for vert in obj.data.vertices]
    ymin, ymax = min(ys), max(ys)
    depth = ymax - ymin
    start = ymin + depth * 0.62
    for vert in obj.data.vertices:
        if vert.co.y > start:
            t = (vert.co.y - start) / max(1e-5, ymax - start)
            # Smooth compression of only the rear 38%; front vertices stay byte-for-byte untouched.
            eased = t * t * (3.0 - 2.0 * t)
            target = start + (vert.co.y - start) * 0.62
            vert.co.y = vert.co.y * (1.0 - eased) + target * eased
    obj.data.update()
    recenter_bottom(obj)
    return {"front_preserved_fraction_of_depth": 0.62, "rear_depth_scale": 0.62}


def restrained_cleanup(obj, target_faces, repaired):
    # Tiny relaxation only; this normalizes micro spikes without erasing authored planes.
    smooth = obj.modifiers.new("MicroFacetRelax", 'SMOOTH')
    smooth.factor = 0.055 if not repaired else 0.09
    smooth.iterations = 1
    activate(obj)
    bpy.ops.object.modifier_apply(modifier=smooth.name)

    current_faces = len(obj.data.polygons)
    if current_faces > target_faces:
        decimate = obj.modifiers.new("MacroPlaneDecimate", 'DECIMATE')
        decimate.decimate_type = 'COLLAPSE'
        decimate.ratio = max(0.08, min(0.92, target_faces / current_faces))
        decimate.use_collapse_triangulate = True
        activate(obj)
        bpy.ops.object.modifier_apply(modifier=decimate.name)
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    recenter_bottom(obj)


def make_material(name, colour, roughness=0.92):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*colour, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


def assign(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def add_area(name, location, energy, size, colour, target):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy, data.shape, data.size, data.color = energy, 'DISK', size, colour
    light = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(light)
    light.location = location
    light.rotation_euler = (Vector(target) - light.location).to_track_quat('-Z', 'Y').to_euler()


def camera_object():
    data = bpy.data.cameras.new("AuditCamera")
    camera = bpy.data.objects.new("AuditCamera", data)
    bpy.context.collection.objects.link(camera)
    data.type = 'ORTHO'
    bpy.context.scene.camera = camera
    return camera


def aim(camera, location, target, scale):
    camera.location = location
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.ortho_scale = scale


def add_text(body, location, camera, mat, size=0.34):
    curve = bpy.data.curves.new("AuditText", 'FONT')
    curve.body = body
    curve.align_x = 'CENTER'
    curve.align_y = 'CENTER'
    curve.size = size
    curve.extrude = 0.003
    curve.materials.append(mat)
    text = bpy.data.objects.new("AuditText", curve)
    bpy.context.collection.objects.link(text)
    text.location = location
    text.rotation_euler = camera.rotation_euler
    return text


def setup_render_scene():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1800
    scene.render.resolution_y = 1050
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.world.color = (0.012, 0.016, 0.023)
    scene.view_settings.look = 'AgX - Medium High Contrast'
    target = (0.0, 0.0, 2.5)
    add_area("Key", (-8.0, -10.0, 13.0), 1700, 6.0, (1.0, 0.76, 0.58), target)
    add_area("Fill", (9.0, -6.0, 8.0), 1050, 6.0, (0.48, 0.63, 1.0), target)
    add_area("Rim", (2.0, 7.0, 12.0), 1400, 5.0, (1.0, 0.50, 0.30), target)
    return scene


def render_audit(objects, final_reports):
    scene = setup_render_scene()
    scene.render.resolution_x = 2400
    scene.render.resolution_y = 1400
    camera = camera_object()
    clay = make_material("M_DerivativeAuditClay", (0.47, 0.49, 0.52), 0.92)
    text_mat = make_material("M_DerivativeAuditText", (0.88, 0.91, 0.95), 0.82)

    # Eight-way contact sheet only: consistent 3/4, two rows, normalized visual scale.
    original = [(obj.location.copy(), obj.rotation_euler.copy(), obj.scale.copy()) for obj in objects]
    contact_positions = [
        (-7.8, 3.30), (-2.6, 3.30), (2.6, 3.30), (7.8, 3.30),
        (-7.8, -4.50), (-2.6, -4.50), (2.6, -4.50), (7.8, -4.50),
    ]
    labels = []
    for obj, report, (x, base_z) in zip(objects, final_reports, contact_positions):
        assign(obj, clay)
        factor = 3.65 / max(obj.dimensions)
        obj.scale = (factor, factor, factor)
        obj.rotation_euler[2] = math.radians(22.0)
        obj.location = (x, 0.0, base_z)
        final = report["final"]
        labels.append(add_text(f"{obj.name}\n{final['vertices']}v / {final['faces']}f", (x, -1.6, base_z - 0.72), camera, text_mat, 0.20))
    aim(camera, (0.0, -31.0, 15.5), (0.0, 0.0, 0.0), 22.0)
    for label in labels:
        label.rotation_euler = camera.rotation_euler
    scene.render.filepath = os.path.join(RENDERS, "owned_cliff_derivatives_contact_8.png")
    bpy.ops.render.render(write_still=True)

    # Restore clean source layout; no audit material is retained in the .blend or OBJ files.
    for label in labels:
        bpy.data.objects.remove(label, do_unlink=True)
    for index, (obj, state) in enumerate(zip(objects, original)):
        obj.data.materials.clear()
        obj.location, obj.rotation_euler, obj.scale = state
        obj.location = ((index % 4) * 11.0, (index // 4) * 9.0, 0.0)


def export_obj(obj):
    activate(obj)
    bpy.ops.wm.obj_export(filepath=os.path.join(MODELS, obj.name + ".obj"),
                          export_selected_objects=True, export_materials=False,
                          forward_axis='NEGATIVE_Z', up_axis='Y')


clear_scene()
objects = []
reports = []
for spec in SPECS:
    obj, source_stats = import_lod0(spec)
    deformation = apply_art_deformation(obj, spec.get("deformation"))
    repair = repair_if_needed(obj)
    embedding = controlled_back_embedding(obj)
    restrained_cleanup(obj, spec["target_faces"], repair["required"])
    final_stats = mesh_stats(obj)
    mins, maxs = bounds(obj)
    final_stats["dimensions_xyz_m"] = [round(v, 3) for v in obj.dimensions]
    final_stats["pivot_bottom_center_error_m"] = [round((mins.x + maxs.x) * 0.5, 5), round((mins.y + maxs.y) * 0.5, 5), round(mins.z, 5)]
    if final_stats["non_manifold_edges"] or final_stats["connected_components"] != 1:
        raise RuntimeError(f"Final derivative validation failed for {obj.name}: {final_stats}")
    report = {
        "name": obj.name,
        "source": source_stats,
        "art_deformation": deformation,
        "repair": repair,
        "back_embedding": embedding,
        "recommended_overlap_m": spec["recommended_overlap_m"],
        "recommended_burial_m": 0.18,
        "final": final_stats,
    }
    reports.append(report)
    objects.append(obj)
    export_obj(obj)

render_audit(objects, reports)
with open(os.path.join(REPORTS, "owned_cliff_derivatives_validation.json"), 'w', encoding='utf-8') as handle:
    json.dump({"asset_count": len(objects), "source_assets_modified": False, "derivatives": reports}, handle, indent=2)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_ROOT, "Owned_Cliff_Derivatives_Source.blend"))
print("OWNED_CLIFF_DERIVATIVES", json.dumps(reports))
