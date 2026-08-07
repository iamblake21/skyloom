"""
render_contact.py -- clay multiview renders + reference-comparison contact sheet.

Renders FRONT / PROFILE / TOP / THREE-QUARTER clay views of the hero prototypes
and composites them next to the matching thumbnail cropped out of the original
reference image, so the comparison is made on pixels rather than on claims.

Run:
  blender --background --python render_contact.py -- <blendfile> <outdir> <tag> <name:refindex> ...
"""

import os
import sys
import math

import bpy
import numpy as np
from mathutils import Vector

REF_SRC = r"C:\Users\slicc\AppData\Local\Temp\codex-clipboard-db984868-8918-4a06-867f-b0eff2430d10.png"
REF_TILES = 11
REF_BAND = 100

VIEWS = ["FRONT", "PROFILE", "TOP", "3-4"]
CELL = 900
BG = (0.118, 0.118, 0.125)


# ------------------------------------------------------------ reference crops

def crop_reference(index, out_path, size=CELL):
    img = bpy.data.images.load(REF_SRC)
    W, H = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)[::-1]
    bpy.data.images.remove(img)
    x0 = int(round(index * W / REF_TILES))
    x1 = int(round((index + 1) * W / REF_TILES))
    crop = px[0:REF_BAND, x0:x1]
    ch, cw = crop.shape[:2]
    s = max(1, int(size / max(ch, cw)))
    up = np.repeat(np.repeat(crop, s, axis=0), s, axis=1)
    uh, uw = up.shape[:2]
    canvas = np.zeros((size, size, 4), dtype=np.float32)
    canvas[..., 3] = 1.0
    canvas[..., 0:3] = 0.04
    oy = max(0, (size - uh) // 2)
    ox = max(0, (size - uw) // 2)
    h = min(uh, size - oy)
    w = min(uw, size - ox)
    canvas[oy:oy + h, ox:ox + w] = up[:h, :w]
    out = bpy.data.images.new("refcrop", width=size, height=size, alpha=True)
    out.pixels = canvas[::-1].ravel().tolist()
    out.filepath_raw = out_path
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


# ------------------------------------------------------------------ rig

def pick_engine(scene):
    try:
        scene.render.engine = 'CYCLES'
        prefs = bpy.context.preferences.addons['cycles'].preferences
        for dev_type in ('OPTIX', 'CUDA', 'HIP', 'ONEAPI'):
            try:
                prefs.compute_device_type = dev_type
                prefs.get_devices()
                gpus = [d for d in prefs.devices if d.type == dev_type]
                if gpus:
                    for d in prefs.devices:
                        d.use = (d.type == dev_type)
                    scene.cycles.device = 'GPU'
                    print(f"[RENDER] Cycles GPU via {dev_type}")
                    return
            except Exception:
                continue
        scene.cycles.device = 'CPU'
        print("[RENDER] Cycles CPU")
    except Exception as e:
        print("[RENDER] falling back to EEVEE:", e)
        for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
            try:
                scene.render.engine = eng
                break
            except TypeError:
                continue


def clay_material(name, color):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.70
    for key, val in (("Specular IOR Level", 0.28), ("Metallic", 0.0)):
        if key in bsdf.inputs:
            bsdf.inputs[key].default_value = val
    return mat


def setup_world(scene):
    world = bpy.data.worlds.new("clay_world")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.052, 0.055, 0.062, 1.0)
    bg.inputs[1].default_value = 1.0


def add_sun(name, az, el, energy, color, angle=0.16):
    d = bpy.data.lights.new(name, type='SUN')
    d.energy = energy
    d.color = color
    d.angle = angle
    ob = bpy.data.objects.new(name, d)
    bpy.context.collection.objects.link(ob)
    a = math.radians(az)
    e = math.radians(el)
    direction = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
    ob.rotation_euler = (-direction).to_track_quat('-Z', 'Y').to_euler()
    return ob


def setup_lights():
    add_sun("key", 128, 52, 3.4, (1.0, 0.95, 0.88), angle=0.20)
    add_sun("fill", -52, 18, 0.85, (0.72, 0.80, 1.0), angle=0.5)
    add_sun("rim", 300, 30, 1.5, (1.0, 0.88, 0.80), angle=0.25)


def bbox_world(ob):
    pts = np.array([list(ob.matrix_world @ Vector(c)) for c in ob.bound_box])
    return pts.min(axis=0), pts.max(axis=0)


def frame_camera(cam, ob, view):
    lo, hi = bbox_world(ob)
    centre = Vector(((lo + hi) * 0.5))
    diag = float(np.linalg.norm(hi - lo))

    if view == "FRONT":
        d = Vector((0, 1, 0)); up = Vector((0, 0, 1)); ortho = True
    elif view == "PROFILE":
        d = Vector((-1, 0, 0)); up = Vector((0, 0, 1)); ortho = True
    elif view == "TOP":
        d = Vector((0, 0, -1)); up = Vector((0, 1, 0)); ortho = True
    else:
        a = math.radians(38.0); e = math.radians(26.0)
        d = -Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
        up = Vector((0, 0, 1)); ortho = False

    d.normalize()
    right = d.cross(up)
    if right.length < 1e-6:
        right = d.cross(Vector((1, 0, 0)))
    right.normalize()
    real_up = right.cross(d).normalized()

    corners = [Vector((x, y, z)) for x in (lo[0], hi[0])
               for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]
    ext_r = max(abs((c - centre).dot(right)) for c in corners) * 2.0
    ext_u = max(abs((c - centre).dot(real_up)) for c in corners) * 2.0
    ext = max(ext_r, ext_u)

    cam.data.type = 'ORTHO' if ortho else 'PERSP'
    if ortho:
        cam.data.ortho_scale = ext * 1.20
        dist = diag * 2.5 + 5.0
    else:
        cam.data.lens = 62.0
        fov = 2.0 * math.atan(18.0 / cam.data.lens)
        dist = (ext * 0.62) / math.tan(fov * 0.5) + diag * 0.35

    cam.location = centre - d * dist
    cam.rotation_euler = (-d).to_track_quat('Z', 'Y').to_euler()
    if view != "TOP":
        # keep world up genuinely up in frame
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    else:
        cam.rotation_euler = (0.0, 0.0, 0.0)
        cam.location = centre + Vector((0, 0, diag * 2.0 + 5.0))


def render_views(blendfile, outdir, tag, targets):
    bpy.ops.wm.open_mainfile(filepath=blendfile)
    scene = bpy.context.scene
    pick_engine(scene)
    if scene.render.engine == 'CYCLES':
        scene.cycles.samples = 128
        scene.cycles.use_denoising = True
    scene.render.resolution_x = CELL
    scene.render.resolution_y = CELL
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = 'PNG'
    try:
        scene.view_settings.view_transform = 'AgX'
        scene.view_settings.look = 'AgX - Medium Contrast'
    except Exception:
        pass

    setup_world(scene)
    setup_lights()

    mat = clay_material("M_Clay", (0.905, 0.615, 0.470))
    meshes = [o for o in scene.objects if o.type == 'MESH']
    for o in meshes:
        o.data.materials.clear()
        o.data.materials.append(mat)

    # ground for the three-quarter view only (reads contact + undercut shadow)
    gm = bpy.data.meshes.new("ground")
    gm.from_pydata([(-40, -40, 0), (40, -40, 0), (40, 40, 0), (-40, 40, 0)], [], [(0, 1, 2, 3)])
    gm.update()
    ground = bpy.data.objects.new("ground", gm)
    ground.location.z = -0.004
    gmat = clay_material("M_Ground", (0.20, 0.19, 0.21))
    gm.materials.append(gmat)
    scene.collection.objects.link(ground)

    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    os.makedirs(outdir, exist_ok=True)
    written = {}
    for name, _refidx in targets:
        ob = scene.objects.get(name)
        if ob is None:
            print("[RENDER] MISSING OBJECT", name)
            continue
        for o in meshes:
            o.hide_render = (o is not ob)
        written[name] = []
        for view in VIEWS:
            ground.hide_render = (view != "3-4")
            frame_camera(cam, ob, view)
            path = os.path.join(outdir, f"{tag}_{name}_{view}.png")
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            written[name].append(path)
            print("[RENDER]", path)
    return written


# ------------------------------------------------------------- contact sheet

def build_sheet(rows, out_path, title):
    """rows: [(label, [imgpath x 5])] -- ref + 4 views."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE', 'CYCLES'):
        try:
            scene.render.engine = eng
            break
        except TypeError:
            continue
    try:
        scene.view_settings.view_transform = 'Standard'
    except Exception:
        pass

    ncol = 5
    nrow = len(rows)
    gap = 0.055
    lab = 0.115
    cw = 1.0
    ch = 1.0 + lab

    total_w = ncol * cw + (ncol - 1) * gap
    total_h = nrow * ch + (nrow - 1) * gap + 0.34

    def plane(x, y, w, h, img_path):
        me = bpy.data.meshes.new("p")
        me.from_pydata([(x, 0, y), (x + w, 0, y), (x + w, 0, y + h), (x, 0, y + h)], [],
                       [(0, 1, 2, 3)])
        me.uv_layers.new(name="UV")
        uvs = [(0, 0), (1, 0), (1, 1), (0, 1)]
        for i, loop in enumerate(me.loops):
            me.uv_layers[0].data[i].uv = uvs[i]
        me.update()
        ob = bpy.data.objects.new("p", me)
        scene.collection.objects.link(ob)
        mat = bpy.data.materials.new("m")
        mat.use_nodes = True
        nt = mat.node_tree
        nt.nodes.clear()
        tex = nt.nodes.new("ShaderNodeTexImage")
        img = bpy.data.images.load(img_path)
        img.colorspace_settings.name = 'sRGB'
        tex.image = img
        tex.interpolation = 'Cubic'
        em = nt.nodes.new("ShaderNodeEmission")
        em.inputs[1].default_value = 1.0
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(tex.outputs[0], em.inputs[0])
        nt.links.new(em.outputs[0], out.inputs[0])
        me.materials.append(mat)
        return ob

    def text(body, x, y, size, align='CENTER', col=(0.82, 0.82, 0.86)):
        cu = bpy.data.curves.new("t", type='FONT')
        cu.body = body
        cu.size = size
        cu.align_x = align
        cu.align_y = 'CENTER'
        ob = bpy.data.objects.new("t", cu)
        ob.location = (x, -0.02, y)
        ob.rotation_euler = (math.pi / 2, 0, 0)
        scene.collection.objects.link(ob)
        mat = bpy.data.materials.new("mt")
        mat.use_nodes = True
        nt = mat.node_tree
        nt.nodes.clear()
        em = nt.nodes.new("ShaderNodeEmission")
        em.inputs[0].default_value = (*col, 1.0)
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(em.outputs[0], out.inputs[0])
        cu.materials.append(mat)
        return ob

    headers = ["REFERENCE (source thumbnail)", "FRONT (ortho)", "PROFILE (ortho)",
               "TOP (ortho)", "THREE-QUARTER"]

    top = total_h - 0.34
    text(title, total_w * 0.5, total_h - 0.16, 0.105)

    for r, (label, paths) in enumerate(rows):
        y = top - (r + 1) * ch - r * gap
        for c in range(ncol):
            x = c * (cw + gap)
            plane(x, y + lab, cw, ch - lab, paths[c])
            cap = headers[c] if r == 0 else ""
            text(f"{label}  |  {headers[c].split(' (')[0]}", x + cw * 0.5,
                 y + lab * 0.45, 0.046,
                 col=(0.92, 0.86, 0.80) if c == 0 else (0.70, 0.72, 0.78))
        del cap

    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = 'ORTHO'
    pad = 0.12
    span = max(total_w, total_h) + pad * 2
    cam_data.ortho_scale = span
    cam = bpy.data.objects.new("cam", cam_data)
    cam.location = (total_w * 0.5, -12.0, total_h * 0.5)
    cam.rotation_euler = (math.pi / 2, 0, 0)
    scene.collection.objects.link(cam)
    scene.camera = cam

    world = bpy.data.worlds.new("w")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (*BG, 1.0)

    res = 4400
    scene.render.resolution_x = res
    scene.render.resolution_y = int(res * (total_h + pad * 2) / span) if span else res
    scene.render.resolution_x = int(res * (total_w + pad * 2) / span)
    scene.render.resolution_y = int(res * (total_h + pad * 2) / span)
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = out_path
    bpy.ops.render.render(write_still=True)
    print("[SHEET]", out_path, scene.render.resolution_x, scene.render.resolution_y)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    blendfile, outdir, tag = argv[0], argv[1], argv[2]
    targets = []
    for spec in argv[3:]:
        n, i = spec.rsplit(":", 1)
        targets.append((n, int(i)))

    refdir = os.path.join(outdir, "_ref")
    os.makedirs(refdir, exist_ok=True)
    refpaths = {}
    for name, idx in targets:
        p = os.path.join(refdir, f"ref_{idx}.png")
        crop_reference(idx, p)
        refpaths[name] = p

    written = render_views(blendfile, outdir, tag, targets)

    rows = []
    for name, _idx in targets:
        if name not in written:
            continue
        short = name.replace("SM_VRKC1_", "")
        rows.append((short, [refpaths[name]] + written[name]))

    build_sheet(rows, os.path.join(outdir, f"{tag}_ContactSheet.png"),
                "Vertical Rock Kit — ClaudeV1 hero prototypes vs reference   (clay, no textures)")


main()
