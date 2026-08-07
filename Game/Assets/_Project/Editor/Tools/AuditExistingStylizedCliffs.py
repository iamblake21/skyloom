import bpy
import json
import math
import os
from mathutils import Vector

PROJECT = r"D:\Changing My Life\Game"
CLIFF_DIR = os.path.join(PROJECT, r"Assets\Proxy Games\Stylized Nature Kit Lite\Meshes\Rocks\Rock Cliffs")
MOUNTAIN = os.path.join(PROJECT, r"Assets\Proxy Games\Stylized Nature Kit Lite\Meshes\Rocks\Mountain\Mountain.fbx")
OUT_ROOT = os.path.join(PROJECT, r"Assets\_Project\Art\Environment\StarterIsland\VerticalRockKit_Sculpted")
RENDER_PATH = os.path.join(OUT_ROOT, "Renders", "existing_cliffs_audit.png")
REPORT_PATH = os.path.join(OUT_ROOT, "Reports", "existing_cliffs_audit.json")
os.makedirs(os.path.dirname(RENDER_PATH), exist_ok=True)
os.makedirs(os.path.dirname(REPORT_PATH), exist_ok=True)

ASSETS = [(f"Rock Cliff {i}", os.path.join(CLIFF_DIR, f"Rock Cliff {i}.fbx")) for i in range(1, 6)]
ASSETS.append(("Mountain", MOUNTAIN))


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)


def activate_only(objects, active=None):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = active or objects[0]


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    mins = Vector((min(v.x for v in corners), min(v.y for v in corners), min(v.z for v in corners)))
    maxs = Vector((max(v.x for v in corners), max(v.y for v in corners), max(v.z for v in corners)))
    return mins, maxs


def import_for_audit(label, path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=False)
    imported = [obj for obj in bpy.data.objects if obj not in before]
    meshes = [obj for obj in imported if obj.type == 'MESH']
    if not meshes:
        raise RuntimeError(f"No mesh imported from {path}")

    raw_vertices = sum(len(obj.data.vertices) for obj in meshes)
    raw_faces = sum(len(obj.data.polygons) for obj in meshes)
    activate_only(meshes, meshes[0])
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = "AUDIT_" + label.replace(" ", "_")
    activate_only([obj])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    mins, maxs = world_bounds(obj)
    dimensions = maxs - mins
    obj.location -= Vector(((mins.x + maxs.x) * 0.5, (mins.y + maxs.y) * 0.5, mins.z))
    activate_only([obj])
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    return obj, {
        "name": label,
        "source": path,
        "vertices": raw_vertices,
        "faces": raw_faces,
        "dimensions_m_xyz": [round(dimensions.x, 3), round(dimensions.y, 3), round(dimensions.z, 3)],
    }


def material(name, colour, roughness=0.9):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*colour, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


def add_area(name, location, energy, size, colour, target):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = energy
    data.shape = 'DISK'
    data.size = size
    data.color = colour
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def add_label(text, location, camera, text_material):
    curve = bpy.data.curves.new("AuditLabel", type='FONT')
    curve.body = text
    curve.align_x = 'CENTER'
    curve.align_y = 'CENTER'
    curve.size = 0.30
    curve.space_line = 0.86
    curve.extrude = 0.004
    obj = bpy.data.objects.new("AuditLabel", curve)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = camera.rotation_euler
    curve.materials.append(text_material)
    return obj


clear_scene()
clay = material("M_AuditClay", (0.47, 0.49, 0.52), 0.91)
label_mat = material("M_AuditText", (0.88, 0.91, 0.95), 0.8)

camera_data = bpy.data.cameras.new("AuditCamera")
camera = bpy.data.objects.new("AuditCamera", camera_data)
bpy.context.collection.objects.link(camera)
camera.location = (0.0, -30.0, 14.5)
camera.rotation_euler = (Vector((0.0, 0.0, 0.1)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera_data.type = 'ORTHO'
camera_data.ortho_scale = 22.0
bpy.context.scene.camera = camera

positions = [(-7.0, 4.50), (0.0, 4.50), (7.0, 4.50), (-7.0, -2.35), (0.0, -2.35), (7.0, -2.35)]
reports = []
for index, ((label, path), (px, pz)) in enumerate(zip(ASSETS, positions)):
    obj, report = import_for_audit(label, path)
    reports.append(report)
    max_dimension = max(obj.dimensions)
    scale = 4.20 / max_dimension if max_dimension > 1e-6 else 1.0
    obj.scale = (scale, scale, scale)
    obj.rotation_euler[2] = math.radians(24.0)
    obj.location = (px, 0.0, pz - 1.65)
    obj.data.materials.clear()
    obj.data.materials.append(clay)
    dims = report["dimensions_m_xyz"]
    label_text = f"{label}\n{dims[0]:.2f} x {dims[1]:.2f} x {dims[2]:.2f} m   |   {report['vertices']}v / {report['faces']}f"
    add_label(label_text, (px, -1.1, pz - 3.25), camera, label_mat)

target = (0.0, 0.0, 0.0)
add_area("Key", (-10.0, -11.0, 15.0), 2100, 7.5, (1.0, 0.78, 0.62), target)
add_area("Fill", (11.0, -7.0, 9.0), 1300, 8.0, (0.53, 0.66, 1.0), target)
add_area("Rim", (0.0, 8.0, 13.0), 1700, 7.0, (1.0, 0.54, 0.34), target)

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 2400
scene.render.resolution_y = 1500
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.world.color = (0.012, 0.016, 0.023)
scene.view_settings.look = 'AgX - Medium High Contrast'
scene.render.filepath = RENDER_PATH
bpy.ops.render.render(write_still=True)

with open(REPORT_PATH, 'w', encoding='utf-8') as handle:
    json.dump({
        "purpose": "Read-only clay contact-sheet audit of owned Stylized Nature Kit Lite cliff assets",
        "asset_count": len(reports),
        "consistent_preview_rotation_z_degrees": 24.0,
        "assets": reports,
    }, handle, indent=2)

print("EXISTING_CLIFF_AUDIT", json.dumps(reports))
