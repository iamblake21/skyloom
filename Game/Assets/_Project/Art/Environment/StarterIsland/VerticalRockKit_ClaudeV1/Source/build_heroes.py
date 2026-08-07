"""
build_heroes.py -- the three hero prototypes (Arch, Overhang, Stone B) for the
stylized vertical rock kit.

Approach
--------
Every piece is CARVED, not swept. Each is a union of several deliberately
unequal, non-parallel convex planar masses, then cut by further masses and
planes, then finished with generous uneven chamfers.

The earlier swept-loft version (kept as build_heroes.py.sweep_backup) was
abandoned because a single smoothed envelope cannot avoid the tells: its
aperture came out a parallel-sided slot with mirror-matched jambs, its plateaus
came out as one flat plane spanning most of the plan, and its contours ran
ruled-straight over half their length. Unioned planar masses give re-entrant
outlines, stepped jambs, unequal lobes and contours that change direction
structurally -- not as a tuning exercise.

The finish is deliberately soft: broad planar facet centres with generously
rounded creases, so it reads as cozy weathered stone rather than a cut gem.

Run:
  blender --background --python build_heroes.py
"""

import os
import sys
import json
import math

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(bpy.data.filepath or __file__))
if not HERE or not os.path.isdir(HERE):
    HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import rockkit_lib as RK          # noqa: E402
import importlib                  # noqa: E402
importlib.reload(RK)

ROOT = os.path.abspath(os.path.join(HERE, os.pardir))
MODELS = os.path.join(ROOT, "Models")
REPORTS = os.path.join(ROOT, "Reports")
for d in (MODELS, REPORTS):
    os.makedirs(d, exist_ok=True)


# --------------------------------------------------------------- helpers

def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def obj_bounds(ob):
    v = np.array([list(x.co) for x in ob.data.vertices])
    return v.min(axis=0), v.max(axis=0)


def ground(ob):
    """Bottom-centre pivot: base on z=0, centred on the XY bounding box."""
    me = ob.data
    v = np.array([list(x.co) for x in me.vertices])
    lo, hi = v.min(axis=0), v.max(axis=0)
    off = np.array([(lo[0] + hi[0]) * 0.5, (lo[1] + hi[1]) * 0.5, lo[2]])
    for x in me.vertices:
        x.co = (x.co[0] - off[0], x.co[1] - off[1], x.co[2] - off[2])
    me.update()
    return ob


def shade(ob, angle_deg=35.0):
    """Wide enough that only authored facet creases split; a tight angle creases
    along the tessellation itself and shows up as shading terraces."""
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.shade_smooth()
    try:
        bpy.ops.object.shade_auto_smooth(angle=math.radians(angle_deg))
    except Exception:
        pass


def export_obj(ob, path):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.wm.obj_export(
        filepath=path, export_selected_objects=True,
        forward_axis='NEGATIVE_Z', up_axis='Y',
        export_materials=False, export_uv=False,
        export_normals=True, export_triangulated_mesh=False,
        apply_modifiers=False)


def build_union(name, blocks):
    """blocks: list of kwargs for RK.block_mass. The first becomes the body.

    keep_largest MUST stay off while the union is being assembled: mid-sequence
    the mesh legitimately has several shells, and discarding the smaller ones
    between steps eats the piece one block at a time. It is only safe once every
    mass has been merged.
    """
    ob = RK.mass_to_object(name, *RK.block_mass(**blocks[0]))
    for b in blocks[1:]:
        piece = RK.mass_to_object(name + "_u", *RK.block_mass(**b))
        RK.boolean_cut(ob, piece, operation='UNION')
        RK.cleanup(ob, keep_largest=False)
    return ob


def subtract(ob, blocks):
    for b in blocks:
        cutter = RK.mass_to_object("cut", *RK.block_mass(**b))
        RK.boolean_cut(ob, cutter, operation='DIFFERENCE')
        RK.cleanup(ob, keep_largest=False)
    return ob


# =========================================================== ARCH
# Reference: a wide, low, table-like mass. Two waisted legs -- broad at the
# shoulder, pinched at mid-height, splaying into foot pads -- carrying a thick
# lintel that cantilevers past both. The aperture is a keyhole: narrow at the
# soffit, widest around 55-60% height, roughly 29% of the total width, sitting
# left of the mass centre.

ARCH_BODY = [
    # Left leg: three stacked, differently rotated masses. Where they meet, the
    # union leaves re-entrant corners -- that is the waist, and neither a convex
    # block nor a smooth sweep can produce it.
    dict(size=(1.02, 1.16, 0.62), centre=(-0.78, 0.02, 0.30), euler=(0.015, 0.035, 0.050),
         cuts=((205, -34, 0.86), (25, 22, 0.92)), chamfers=9, seed=11),
    dict(size=(0.78, 0.96, 0.86), centre=(-0.70, -0.04, 0.88), euler=(-0.020, -0.030, -0.045),
         cuts=((120, 18, 0.88), (300, -12, 0.94)), chamfers=9, seed=12),
    dict(size=(0.94, 1.10, 0.62), centre=(-0.74, 0.03, 1.45), euler=(0.010, 0.025, 0.035),
         cuts=((60, 30, 0.90),), chamfers=9, seed=13),
    # Right leg: narrower, waisted higher up, smaller foot.
    dict(size=(0.86, 1.04, 0.52), centre=(0.74, -0.03, 0.26), euler=(-0.010, -0.030, -0.060),
         cuts=((150, -28, 0.88), (330, 20, 0.93)), chamfers=9, seed=14),
    dict(size=(0.66, 0.88, 0.94), centre=(0.68, 0.03, 0.86), euler=(0.015, 0.035, 0.040),
         cuts=((250, 16, 0.90),), chamfers=9, seed=15),
    dict(size=(0.80, 1.00, 0.58), centre=(0.70, -0.02, 1.44), euler=(-0.010, -0.020, -0.025),
         cuts=((15, 26, 0.91),), chamfers=9, seed=16),
    # Lintel, cantilevered past both legs and tilted off level.
    dict(size=(2.62, 1.18, 0.70), centre=(-0.04, 0.01, 1.80), euler=(0.013, -0.019, 0.010),
         cuts=((95, 34, 0.52), (275, -30, 0.55)), chamfers=9, seed=17),
]

# Keyhole aperture: three stacked cutter masses, each offset and rotated, so the
# jambs step instead of running as two matched arcs, and the soffit is tilted so
# crown thickness varies across the span.
ARCH_VOID = [
    dict(size=(0.60, 1.90, 0.80), centre=(-0.08, 0.0, 0.28), euler=(0.000, 0.010, 0.030),
         jitter=5.0, seed=31),
    dict(size=(0.79, 1.90, 0.64), centre=(-0.02, 0.0, 0.86), euler=(0.000, -0.015, -0.025),
         jitter=5.0, seed=32),
    dict(size=(0.54, 1.90, 0.60), centre=(0.05, 0.0, 1.30), euler=(0.000, 0.055, 0.040),
         jitter=5.0, seed=33),
]


def build_arch():
    ob = build_union("SM_VRKC1_Arch_A", ARCH_BODY)
    subtract(ob, ARCH_VOID)
    # Two non-coplanar top shelves meeting at a rim break, rather than one flat
    # plane spanning the plan.
    RK.plane_cut(ob, (0, 0, 2.13), (0.06, -0.07, 1.0), bevel=0.07)
    RK.plane_cut(ob, (-0.62, 0, 2.05), (-0.34, 0.11, 1.0), bevel=0.07)
    RK.plane_cut(ob, (0, 0, 0.0), (0.0, 0.0, -1.0), bevel=0.05)
    RK.cleanup(ob)
    RK.soften(ob, bevel=0.115, wide=2.2, wide_frac=0.45, segments=4, densify=1, smooth_iters=0,
              bulges=[(RK.azel(310, -18), 0.030, 2.2),
                      (RK.azel(120, 34), -0.026, 2.6)],
              noise=[(1.6, 0.005), (2.9, 0.0025)], seed=11)
    RK.cleanup(ob)
    ground(ob)
    shade(ob)
    return ob


# =========================================================== OVERHANG
# Reference: a cantilever. A bulky planted root block carrying a broad tilted
# top plateau with a crisp rim, sweeping forward and down to a keel point, with
# a strongly scooped belly and a hanging knuckle partway along.

OVERHANG_BODY = [
    # Root: wide footprint, planted.
    dict(size=(0.92, 1.06, 1.30), centre=(-0.52, -0.16, 0.62), euler=(0.025, 0.020, 0.050),
         cuts=((200, -26, 0.82), (30, 34, 0.88)), chamfers=9, seed=21),
    # Arm blocks, each offset laterally so the plan axis genuinely curves, and
    # each pitched differently so the top steps instead of ruling straight.
    dict(size=(0.94, 1.16, 0.92), centre=(0.02, 0.10, 0.74), euler=(-0.020, 0.050, -0.030),
         cuts=((140, 28, 0.84), (320, -20, 0.90)), chamfers=9, seed=22),
    dict(size=(0.80, 0.92, 0.62), centre=(0.56, 0.16, 0.60), euler=(0.015, 0.100, 0.045),
         cuts=((70, 24, 0.86),), chamfers=9, seed=23),
    dict(size=(0.56, 0.54, 0.34), centre=(0.98, 0.02, 0.44), euler=(-0.010, 0.150, -0.050),
         cuts=((250, -18, 0.88),), chamfers=9, seed=24),
    # Keel: converges in height and width together. Sits clear of the undercut
    # cutter below -- any lower and the scoop shaves it loose as its own island.
    dict(size=(0.34, 0.26, 0.20), centre=(1.20, -0.12, 0.50), euler=(0.000, 0.170, -0.080),
         cuts=((180, 12, 0.84),), chamfers=9, seed=25),
    # Hanging knuckle on the belly, so the underside is not a single arc.
    dict(size=(0.34, 0.40, 0.36), centre=(0.14, 0.02, 0.30), euler=(0.040, -0.060, 0.070),
         cuts=((90, -30, 0.86),), chamfers=9, seed=26),
]

OVERHANG_CUTS = [
    # The undercut: a broad tilted mass scooped out from below, biased to one
    # side of the axis so the void is asymmetric.
    dict(size=(1.70, 1.30, 0.78), centre=(0.56, -0.12, -0.16), euler=(0.050, -0.100, 0.080),
         jitter=6.0, seed=41),
    # A second, smaller bite behind the knuckle.
    dict(size=(0.62, 0.86, 0.44), centre=(-0.06, 0.22, 0.24), euler=(-0.040, 0.070, -0.100),
         jitter=6.0, seed=42),
]


def build_overhang():
    ob = build_union("SM_VRKC1_Overhang_A", OVERHANG_BODY)
    subtract(ob, OVERHANG_CUTS)
    # Top plateau over the root, breaking to the flank at a rim.
    RK.plane_cut(ob, (0, 0, 1.22), (0.30, -0.08, 1.0), bevel=0.06)
    RK.plane_cut(ob, (0.62, 0, 1.02), (0.62, 0.06, 1.0), bevel=0.055)
    # Anchored back face, two unequal facets.
    RK.plane_cut(ob, (-0.94, 0.0, 0.55), (-1.0, 0.24, 0.26), bevel=0.055)
    RK.plane_cut(ob, (-0.80, -0.58, 0.30), (-0.55, -1.0, 0.14), bevel=0.05)
    RK.plane_cut(ob, (0, 0, 0.0), (0.0, 0.0, -1.0), bevel=0.045)
    RK.cleanup(ob)
    RK.soften(ob, bevel=0.105, wide=2.2, wide_frac=0.45, segments=4, densify=1, smooth_iters=0,
              bulges=[(RK.azel(35, -22), 0.032, 2.2),
                      (RK.azel(215, 30), -0.028, 2.6)],
              noise=[(1.6, 0.005), (2.9, 0.0025)], seed=23)
    RK.cleanup(ob)
    ground(ob)
    shade(ob)
    return ob


# =========================================================== STONE B
# Reference: a chunky boulder, roughly as tall as wide. A small tilted top
# plateau offset off-centre and smaller than the body's girth; the body bulges
# widest below it; one shoulder cut by a large chamfer, the opposite shoulder
# left rounded; base tucked under, flat only where it meets ground.

STONE_B_BODY = [
    # Dominant lobe, carrying most of the volume.
    dict(size=(1.06, 0.94, 0.92), centre=(-0.10, 0.04, 0.48), euler=(0.030, -0.045, 0.110),
         cuts=((40, 42, 0.72), (215, -30, 0.80), (128, 8, 0.84), (300, 20, 0.78)), chamfers=9, seed=51),
    # Secondary lobe, offset so the mass centre is displaced and the plan gets a
    # re-entrant notch where the two meet.
    dict(size=(0.66, 0.72, 0.62), centre=(0.44, -0.20, 0.36), euler=(-0.070, 0.055, -0.130),
         cuts=((330, 26, 0.76), (150, -22, 0.82)), chamfers=9, seed=52),
    # Low shoulder pad.
    dict(size=(0.60, 0.56, 0.40), centre=(-0.34, -0.24, 0.22), euler=(0.050, 0.040, 0.175),
         cuts=((80, -26, 0.80),), chamfers=9, seed=53),
]

STONE_B_CUTS = [
    # Broad chamfer taking one shoulder only.
    dict(size=(0.96, 0.90, 0.64), centre=(0.42, 0.52, 1.02), euler=(0.170, -0.110, 0.090),
         jitter=6.0, seed=61),
    # Undercut bite in a lower flank.
    dict(size=(0.52, 0.58, 0.44), centre=(-0.62, 0.34, 0.30), euler=(-0.090, 0.120, 0.150),
         jitter=6.0, seed=62),
]


def build_stone_b():
    ob = build_union("SM_VRKC1_Stone_B", STONE_B_BODY)
    subtract(ob, STONE_B_CUTS)
    # Small tilted top plateau, well off horizontal and smaller than the girth.
    RK.plane_cut(ob, (0, 0, 0.88), (0.20, -0.15, 1.0), bevel=0.06)
    RK.plane_cut(ob, (0, 0, 0.06), (0.0, 0.0, -1.0), bevel=0.05)
    RK.cleanup(ob)
    RK.soften(ob, bevel=0.120, wide=2.3, wide_frac=0.46, segments=4, densify=1, smooth_iters=0,
              bulges=[(RK.azel(300, -20), 0.038, 2.2),
                      (RK.azel(135, 40), -0.030, 2.6)],
              noise=[(1.5, 0.006), (2.8, 0.003)], seed=51)
    RK.cleanup(ob)
    ground(ob)
    shade(ob, 32.0)
    return ob


# =========================================================== driver

def main():
    clear_scene()
    report = {}
    for label, fn, fname in (
            ("Arch_A", build_arch, "SM_VRKC1_Arch_A.obj"),
            ("Overhang_A", build_overhang, "SM_VRKC1_Overhang_A.obj"),
            ("Stone_B", build_stone_b, "SM_VRKC1_Stone_B.obj")):
        ob = fn()
        report[label] = RK.validate(ob)
        export_obj(ob, os.path.join(MODELS, fname))

    with open(os.path.join(REPORTS, "heroes_validation.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
    bpy.ops.wm.save_as_mainfile(
        filepath=os.path.join(ROOT, "VerticalRockKit_ClaudeV1_Heroes.blend"))

    for k, v in report.items():
        print(f"[VALIDATE] {k}: watertight={v['watertight']} tris={v['tris']} "
              f"comps={v['components']} nm_e={v['non_manifold_edges']} "
              f"nm_v={v['non_manifold_verts']} wire={v['wire_edges']} "
              f"bnd={v['boundary_edges']} vol={v['signed_volume']:.4f} "
              f"size={['%.2f' % x for x in v['size']]} depthvar={v['depth_variation']:.3f}")


main()
