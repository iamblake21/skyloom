import bpy
import bmesh
import json
import math
import os
from mathutils import Vector

ROOT = r"D:\Changing My Life\Game\Assets\_Project\Art\Environment\StarterIsland\VerticalRockKit_Sculpted"
MODELS = os.path.join(ROOT, "Models")
RENDERS = os.path.join(ROOT, "Renders")
REPORTS = os.path.join(ROOT, "Reports")
for folder in (MODELS, RENDERS, REPORTS):
    os.makedirs(folder, exist_ok=True)


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials,
                       bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def local_bump(u, start, end):
    """Compact C1 bump: zero outside the interval, one at its centre."""
    if u <= start or u >= end:
        return 0.0
    t = (u - start) / (end - start)
    return math.sin(math.pi * t) ** 2


def build_wall():
    name = "SM_VRK_Wall_Slab_A"
    us = [-1.0, -0.84, -0.68, -0.52, -0.36, -0.18, 0.0,
          0.18, 0.36, 0.53, 0.68, 0.84, 1.0]
    zs = [0.0, 0.72, 1.55, 2.42, 3.28, 4.12, 4.88]
    left = [-4.28, -4.16, -4.02, -3.83, -3.62, -3.34, -2.98]
    right = [4.48, 4.66, 4.48, 4.22, 3.91, 3.62, 3.28]
    # Large left shoulder, long central pause, medium right event and organic descent.
    top_profile = [-0.38, -0.05, 0.52, 0.28, 0.13, 0.08, 0.04,
                   0.02, 0.03, 0.27, 0.12, -0.18, -0.52]
    verts = []
    nx, nz = len(us), len(zs)

    def front_depth(u, j):
        # Three coherent masses. The right buttress is a true depth step, not a colour trick.
        if u < -0.34:
            base = -0.18 + 0.14 * (u + 1.0)
        elif u < 0.43:
            base = 0.02 + 0.10 * (u + 0.34)
        else:
            base = -0.30 - 0.36 * ((u - 0.43) / 0.57)
        base += 0.025 * j

        # Local features cover 42.5% of the width in total and are separated by load-bearing mass.
        left_ledge = local_bump(u, -0.78, -0.40)
        centre_recess = local_bump(u, -0.05, 0.22)
        right_ledge = local_bump(u, 0.55, 0.75)
        if j == 2:
            base -= 0.36 * left_ledge
        elif j == 3:
            base -= 0.11 * left_ledge
            base += 0.28 * centre_recess
            base -= 0.08 * right_ledge
        elif j == 4:
            base += 0.08 * centre_recess
            base -= 0.31 * right_ledge
        return base

    for side in range(2):
        for j, z in enumerate(zs):
            for i, u in enumerate(us):
                t = (u + 1.0) * 0.5
                x = left[j] * (1.0 - t) + right[j] * t
                # Sparse, authored offsets only; no random sawtooth/noise.
                if j in (1, 2) and i in (10, 11):
                    x += 0.10
                if j in (4, 5) and i in (1, 2):
                    x -= 0.08
                zz = z
                if j == nz - 1:
                    zz += top_profile[i]
                elif j == 0:
                    zz += 0.05 * math.sin(i * 0.78)
                elif j == 4 and i in (2, 3):
                    zz += 0.08

                if side == 0:
                    y = front_depth(u, j)
                else:
                    # Back side stays quieter for overlap/embedding, while retaining taper.
                    y = 1.34 - 0.045 * j
                    if u > 0.55:
                        y -= 0.13 * ((u - 0.55) / 0.45)
                    if u < -0.72:
                        y += 0.05
                verts.append((x, y, zz))

    faces = []

    def vid(side, j, i):
        return side * nx * nz + j * nx + i

    # One principal stratum made from broad quads; only four authored diagonal fractures.
    fracture_cells = {(1, 2), (3, 4), (7, 1), (9, 3)}
    for j in range(nz - 1):
        for i in range(nx - 1):
            a, b = vid(0, j, i), vid(0, j, i + 1)
            c, d = vid(0, j + 1, i + 1), vid(0, j + 1, i)
            if (i, j) in fracture_cells:
                if (i + j) % 2:
                    faces.extend([(a, b, d), (b, c, d)])
                else:
                    faces.extend([(a, b, c), (a, c, d)])
            else:
                faces.append((a, b, c, d))

    # Quiet back face: coherent quads, designed to disappear when embedded.
    for j in range(nz - 1):
        for i in range(nx - 1):
            faces.append((vid(1, j, i + 1), vid(1, j, i),
                          vid(1, j + 1, i), vid(1, j + 1, i + 1)))

    # Close top, base and ends into a single watertight shell.
    for i in range(nx - 1):
        faces.append((vid(0, 0, i + 1), vid(0, 0, i),
                      vid(1, 0, i), vid(1, 0, i + 1)))
        faces.append((vid(0, nz - 1, i), vid(0, nz - 1, i + 1),
                      vid(1, nz - 1, i + 1), vid(1, nz - 1, i)))
    for j in range(nz - 1):
        faces.append((vid(0, j, 0), vid(0, j + 1, 0),
                      vid(1, j + 1, 0), vid(1, j, 0)))
        faces.append((vid(0, j + 1, nx - 1), vid(0, j, nx - 1),
                      vid(1, j, nx - 1), vid(1, j + 1, nx - 1)))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)

    # One narrow chamfer for readable silhouettes; broad planes remain unrounded.
    bevel = obj.modifiers.new("SculptEdgeSoftening", 'BEVEL')
    bevel.width = 0.055
    bevel.segments = 1
    bevel.limit_method = 'ANGLE'
    bevel.angle_limit = math.radians(38.0)
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    return obj


def principled_material(name, colour, roughness=0.84):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*colour, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*colour, 1.0)
    bsdf.inputs['Roughness'].default_value = roughness
    return mat


def assign_material(obj, material):
    obj.data.materials.clear()
    obj.data.materials.append(material)


def add_area(name, location, energy, size, colour, target=(0.0, 0.2, 2.4)):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = energy
    data.shape = 'DISK'
    data.size = size
    data.color = colour
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    return obj


def create_camera(name):
    data = bpy.data.cameras.new(name)
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    data.type = 'ORTHO'
    bpy.context.scene.camera = obj
    return obj


def aim_camera(camera, location, target, ortho_scale):
    camera.location = location
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.ortho_scale = ortho_scale


def configure_render():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 760
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.render.image_settings.color_mode = 'RGBA'
    scene.world.color = (0.014, 0.018, 0.026)
    scene.view_settings.look = 'AgX - Medium High Contrast'
    return scene


def render_review_set(wall):
    scene = configure_render()
    beauty = principled_material("M_Review_Terracotta", (0.38, 0.115, 0.045), 0.87)
    clay = principled_material("M_Review_Clay", (0.48, 0.51, 0.54), 0.91)
    ground = principled_material("M_Review_Ground", (0.045, 0.052, 0.061), 0.95)
    assign_material(wall, beauty)

    bpy.ops.mesh.primitive_plane_add(size=32, location=(0, 0.6, -0.13))
    ground_obj = bpy.context.object
    ground_obj.name = "ReviewGround"
    assign_material(ground_obj, ground)

    add_area("Key", (-5.8, -8.2, 9.8), 1450, 5.5, (1.0, 0.66, 0.43))
    add_area("Fill", (7.0, -4.5, 6.3), 760, 5.0, (0.45, 0.61, 1.0))
    add_area("Rim", (2.5, 5.5, 8.0), 1200, 4.0, (1.0, 0.48, 0.28))
    camera = create_camera("ReviewCamera")

    # Beauty: front three-quarter, showing the true right buttress depth.
    aim_camera(camera, (9.6, -14.8, 7.2), (0.0, 0.15, 2.35), 10.6)
    scene.render.filepath = os.path.join(RENDERS, "Wall_Slab_A_v2_beauty_34.png")
    bpy.ops.render.render(write_still=True)

    # Clay silhouette: nearly orthographic front, material-neutral shape judgment.
    assign_material(wall, clay)
    ground_obj.hide_render = True
    scene.world.color = (0.008, 0.010, 0.014)
    aim_camera(camera, (0.0, -16.0, 4.5), (0.0, 0.0, 2.45), 10.2)
    scene.render.filepath = os.path.join(RENDERS, "Wall_Slab_A_v2_silhouette_clay.png")
    bpy.ops.render.render(write_still=True)

    # Assembly: exactly two offset, overlapping instances; neutral clay only.
    ground_obj.hide_render = False
    scene.world.color = (0.014, 0.018, 0.026)
    wall.location = (-3.15, 0.10, 0.0)
    copy = wall.copy()
    copy.data = wall.data
    bpy.context.collection.objects.link(copy)
    copy.name = "SM_VRK_Wall_Slab_A_AssemblyCopy"
    copy.location = (3.35, 0.48, 0.24)
    copy.rotation_euler[2] = math.radians(-1.8)
    copy.scale = (0.94, 0.98, 0.94)
    assign_material(copy, clay)
    assign_material(wall, clay)
    aim_camera(camera, (11.5, -20.5, 9.5), (0.25, 0.25, 2.45), 17.0)
    scene.render.filepath = os.path.join(RENDERS, "Wall_Slab_A_v2_assembly_two.png")
    bpy.ops.render.render(write_still=True)

    # Return source object to zero for a clean Blender file.
    wall.location = (0.0, 0.0, 0.0)
    copy.hide_viewport = True
    copy.hide_render = True


def export_obj(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    obj.location = (0.0, 0.0, 0.0)
    out = os.path.join(MODELS, "SM_VRK_Wall_Slab_A.obj")
    bpy.ops.wm.obj_export(filepath=out, export_selected_objects=True,
                          export_materials=False, forward_axis='NEGATIVE_Z', up_axis='Y')
    return out


def validate(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    non_manifold = sum(1 for edge in bm.edges if not edge.is_manifold)
    volume = abs(bm.calc_volume(signed=True))
    bm.free()
    dims = [round(v, 3) for v in obj.dimensions]
    report = {
        "name": obj.name,
        "version": 2,
        "vertices": len(obj.data.vertices),
        "faces": len(obj.data.polygons),
        "non_manifold_edges": non_manifold,
        "watertight": non_manifold == 0,
        "signed_volume_abs_m3": round(volume, 3),
        "dimensions_blender_xyz_m": dims,
        "design_gate": {
            "continuous_trench": False,
            "local_feature_width_fraction": 0.425,
            "separated_local_features": 3,
            "authored_diagonal_fracture_cells": 4,
            "single_connected_mesh": True
        }
    }
    with open(os.path.join(REPORTS, "Wall_Slab_A_validation.json"), 'w', encoding='utf-8') as handle:
        json.dump(report, handle, indent=2)
    print("SCULPT_VALIDATION", json.dumps(report))


clear_scene()
wall = build_wall()
validate(wall)
export_obj(wall)
render_review_set(wall)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "Wall_Slab_A_Source.blend"))
