"""
rockkit_lib.py -- original geometry library for the stylized vertical rock kit (ClaudeV1).

Design intent
-------------
The rejected earlier kit failed because every shape was a 2D silhouette pushed
through a constant depth. Nothing in this library is capable of producing that:

* `build_loft` sweeps a *3D* centreline (which bends in all three axes) and
  evaluates a cross-section that changes half-width, half-depth, squareness,
  centroid offset, roll and concavity at every station. Front and back surfaces
  are therefore never parallel and the depth axis meanders.
* The flat-top / concave-underside terms are resolved against **world up**
  projected into the section plane, not against a fixed local axis, so a form
  that bends from vertical to horizontal keeps a flat sky-facing top and a
  scooped underside all the way round the bend.
* End sections are closed with irregular non-planar dome caps (or shallow
  dished soles at ground contact), never with a flat n-gon fan.
* `build_boulder` builds a smoothed convex polytope (broad, non-parallel,
  differently sized facets with rounded edges) and then displaces its mass
  centre with directional bulges and broad scoops, so it cannot land on a
  rounded cube.

Everything is authored macro -> meso. The only high-frequency term available is
a deliberately tiny value-noise used to break perfect smoothness; it is never a
substitute for sculpture.

Blender coordinates (Z up). Exported to Unity with forward=-Z, up=Y.
"""

import math
import numpy as np

UP = np.array([0.0, 0.0, 1.0])


# ---------------------------------------------------------------- utilities

def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def hermite(keys, t):
    """Catmull-Rom / cubic-Hermite interpolation of scalar keyframes.

    keys: [(t0, v0), (t1, v1), ...] with strictly increasing t.
    Produces C1-continuous parameter curves so lofted widths never kink.
    """
    ts = np.asarray([k[0] for k in keys], dtype=float)
    vs = np.asarray([k[1] for k in keys], dtype=float)
    n = len(ts)
    if n == 1:
        return np.full_like(np.asarray(t, dtype=float), vs[0])
    m = np.zeros(n)
    for i in range(n):
        if i == 0:
            m[i] = (vs[1] - vs[0]) / (ts[1] - ts[0])
        elif i == n - 1:
            m[i] = (vs[-1] - vs[-2]) / (ts[-1] - ts[-2])
        else:
            m[i] = (vs[i + 1] - vs[i - 1]) / (ts[i + 1] - ts[i - 1])
    t = np.clip(np.asarray(t, dtype=float), ts[0], ts[-1])
    idx = np.clip(np.searchsorted(ts, t, side='right') - 1, 0, n - 2)
    h = ts[idx + 1] - ts[idx]
    s = (t - ts[idx]) / h
    s2 = s * s
    s3 = s2 * s
    h00 = 2 * s3 - 3 * s2 + 1
    h10 = s3 - 2 * s2 + s
    h01 = -2 * s3 + 3 * s2
    h11 = s3 - s2
    return h00 * vs[idx] + h10 * h * m[idx] + h01 * vs[idx + 1] + h11 * h * m[idx + 1]


def spline3(control_points, samples=600):
    """Smooth 3D curve through control points (Catmull-Rom per component)."""
    pts = np.asarray(control_points, dtype=float)
    ts = np.linspace(0.0, 1.0, len(pts))
    t = np.linspace(0.0, 1.0, samples)
    return np.stack([hermite(list(zip(ts, pts[:, k])), t) for k in range(3)], axis=1)


def resample_by_arclength(curve, stations):
    d = np.linalg.norm(np.diff(curve, axis=0), axis=1)
    s = np.concatenate([[0.0], np.cumsum(d)])
    total = s[-1]
    s = s / total
    target = np.linspace(0.0, 1.0, stations)
    out = np.stack([np.interp(target, s, curve[:, k]) for k in range(3)], axis=1)
    return out, total


def parallel_transport_frames(P):
    """Rotation-minimising frames. Avoids the twist popping that a naive
    up-vector frame produces when the centreline swings through vertical."""
    S = len(P)
    T = np.zeros_like(P)
    T[1:-1] = P[2:] - P[:-2]
    T[0] = P[1] - P[0]
    T[-1] = P[-1] - P[-2]
    T /= np.linalg.norm(T, axis=1, keepdims=True)

    U = np.zeros_like(P)
    V = np.zeros_like(P)
    ref = np.array([0.0, 1.0, 0.0])
    if abs(float(np.dot(ref, T[0]))) > 0.9:
        ref = np.array([1.0, 0.0, 0.0])
    u = ref - np.dot(ref, T[0]) * T[0]
    u /= np.linalg.norm(u)
    U[0] = u
    V[0] = np.cross(T[0], u)

    for i in range(1, S):
        a, b = T[i - 1], T[i]
        axis = np.cross(a, b)
        na = np.linalg.norm(axis)
        if na < 1e-9:
            u = U[i - 1]
        else:
            k = axis / na
            th = math.atan2(na, float(np.dot(a, b)))
            up = U[i - 1]
            u = (up * math.cos(th) + np.cross(k, up) * math.sin(th)
                 + k * float(np.dot(k, up)) * (1.0 - math.cos(th)))
        u = u - np.dot(u, T[i]) * T[i]
        u /= np.linalg.norm(u)
        U[i] = u
        V[i] = np.cross(T[i], u)
    return T, U, V


def _ang_delta(a, b):
    """Signed shortest angular difference a-b, wrapped to [-pi, pi]."""
    d = a - b
    return (d + math.pi) % (2.0 * math.pi) - math.pi


# ------------------------------------------------------------------ meshes

class Mesh:
    """Vertex list + polygon list. Faces are CCW seen from outside."""

    def __init__(self):
        self.verts = []
        self.faces = []

    def add_verts(self, arr):
        base = len(self.verts)
        for v in np.asarray(arr, dtype=float):
            self.verts.append((float(v[0]), float(v[1]), float(v[2])))
        return base

    def add_face(self, idx):
        self.faces.append(tuple(int(i) for i in idx))

    def np_verts(self):
        return np.asarray(self.verts, dtype=float)

    def set_verts(self, arr):
        self.verts = [(float(v[0]), float(v[1]), float(v[2])) for v in np.asarray(arr, dtype=float)]

    def translate(self, t):
        v = self.np_verts() + np.asarray(t, dtype=float)
        self.set_verts(v)

    def sit_on_ground(self, centre_xy=True):
        v = self.np_verts()
        v[:, 2] -= v[:, 2].min()
        if centre_xy:
            lo = v.min(axis=0)
            hi = v.max(axis=0)
            mid = (lo + hi) * 0.5
            v[:, 0] -= mid[0]
            v[:, 1] -= mid[1]
        self.set_verts(v)

    def bounds(self):
        v = self.np_verts()
        return v.min(axis=0), v.max(axis=0)


# ------------------------------------------------------------------- lofts

class LoftSpec:
    """Keyframed description of a swept volume.

    All width/offset keys are lists of (t, value) with t the normalised
    arc-length position along the centreline.

      wu        half-extent along frame U  (for an upright start this is depth)
      wv        half-extent along frame V
      cu, cv    centroid offset inside the section plane -> the depth axis and
                the width axis wander independently of the centreline
      roll      section roll about the tangent
      e_up      superellipse exponent toward world up   (high = flat plateau)
      e_down    superellipse exponent toward world down
      e_side    superellipse exponent sideways
      conc      strength of the world-down concave scoop (undercut)
      conc_sigma / conc_offset  width and asymmetry of that scoop
      harmonics [(freq, [(t, amp)...], phase0, phase_drift), ...]
                low-frequency lobes around the section whose phase drifts along
                the sweep, producing broad folds with directional flow
    """

    def __init__(self, control_points, stations=40, n_theta=32):
        self.control_points = control_points
        self.stations = stations
        self.n_theta = n_theta
        self.wu = [(0.0, 0.5), (1.0, 0.5)]
        self.wv = [(0.0, 0.5), (1.0, 0.5)]
        self.cu = [(0.0, 0.0), (1.0, 0.0)]
        self.cv = [(0.0, 0.0), (1.0, 0.0)]
        self.roll = [(0.0, 0.0), (1.0, 0.0)]
        self.e_up = [(0.0, 3.2), (1.0, 3.2)]
        self.e_down = [(0.0, 3.0), (1.0, 3.0)]
        self.e_side = [(0.0, 3.2), (1.0, 3.2)]
        self.conc = [(0.0, 0.0), (1.0, 0.0)]
        self.conc_sigma = [(0.0, 0.8), (1.0, 0.8)]
        self.conc_offset = [(0.0, 0.0), (1.0, 0.0)]
        self.harmonics = []
        self.start_cap = dict(height=-0.03, rings=3, flare=1.0, tilt=(0.0, 0.0), wobble=0.0)
        self.end_cap = dict(height=0.12, rings=3, flare=1.0, tilt=(0.0, 0.0), wobble=0.02)
        self.seed = 7

        # ---- faceting -------------------------------------------------
        # The cross-section is a *smoothed convex polygon*, not an ellipse.
        # Broad planar facets with defined creases are what makes the kit read
        # as chiselled stylized rock instead of soft plasticine. Facet angles
        # drift and facet offsets swell/pinch along the sweep, so no facet ever
        # runs straight down the form -- that is what stops it becoming a prism.
        self.facets = 9               # 0 -> fall back to the plain superellipse
        self.facet_jitter = 0.42      # fraction of the even spacing
        self.facet_drift = 0.55       # radians of rotation across the whole run
        self.facet_gain = (0.90, 1.06)   # min/max facet offset multiplier
        self.facet_wave = (1.2, 3.4)     # swell/pinch cycles along the run
        self.sharp = [(0.0, 11.0), (1.0, 11.0)]   # crease crispness
        self.top_gain = [(0.0, 0.94), (1.0, 0.94)]   # <1 -> world-up plane cuts a plateau
        self.bot_gain = [(0.0, 1.10), (1.0, 1.10)]   # >1 -> underside stays rounded


def build_loft(spec):
    S = spec.stations
    N = spec.n_theta
    curve = spline3(spec.control_points, samples=max(600, S * 20))
    P, _length = resample_by_arclength(curve, S)
    T, U, V = parallel_transport_frames(P)

    t = np.linspace(0.0, 1.0, S)
    wu = hermite(spec.wu, t)
    wv = hermite(spec.wv, t)
    cu = hermite(spec.cu, t)
    cv = hermite(spec.cv, t)
    roll = hermite(spec.roll, t)
    e_up = hermite(spec.e_up, t)
    e_dn = hermite(spec.e_down, t)
    e_sd = hermite(spec.e_side, t)
    conc = hermite(spec.conc, t)
    conc_s = hermite(spec.conc_sigma, t)
    conc_o = hermite(spec.conc_offset, t)

    sharp = hermite(spec.sharp, t)
    top_g = hermite(spec.top_gain, t)
    bot_g = hermite(spec.bot_gain, t)

    harm = []
    for (freq, amp_keys, ph0, drift) in spec.harmonics:
        harm.append((freq, hermite(amp_keys, t), ph0, drift))

    theta = np.linspace(0.0, 2.0 * math.pi, N, endpoint=False)
    ca, sa = np.cos(theta), np.sin(theta)

    # irregular facet plane directions; jittered, never evenly spaced
    frng = np.random.default_rng(spec.seed * 977 + 13)
    K = int(spec.facets)
    if K > 0:
        step = 2.0 * math.pi / K
        base_ang = (np.arange(K) * step
                    + frng.uniform(-spec.facet_jitter, spec.facet_jitter, K) * step)
        drift_k = frng.uniform(-1.0, 1.0, K) * spec.facet_drift
        gmin, gmax = spec.facet_gain
        gmid = 0.5 * (gmin + gmax)
        gamp = 0.5 * (gmax - gmin)
        wave_k = frng.uniform(spec.facet_wave[0], spec.facet_wave[1], K)
        wphase = frng.uniform(0.0, 2.0 * math.pi, K)
        gbias = frng.uniform(-0.5, 0.5, K) * gamp

    mesh = Mesh()
    rings = []
    centres = []

    for i in range(S):
        # roll the frame
        cr, sr = math.cos(roll[i]), math.sin(roll[i])
        Ui = U[i] * cr + V[i] * sr
        Vi = -U[i] * sr + V[i] * cr

        # world up expressed inside the section plane; k fades the world-aligned
        # terms out when the section is (nearly) horizontal, where "up" is the cap
        proj = UP - float(np.dot(UP, T[i])) * T[i]
        m = float(np.linalg.norm(proj))
        if m > 1e-6:
            pdir = proj / m
            th_up = math.atan2(float(np.dot(pdir, Vi)), float(np.dot(pdir, Ui)))
        else:
            th_up = 0.0
        k = float(smoothstep(0.18, 0.55, m))

        psi = theta - th_up
        cp = np.cos(psi)
        w_up = np.maximum(cp, 0.0) ** 2
        w_dn = np.maximum(-cp, 0.0) ** 2
        w_sd = np.sin(psi) ** 2
        e_dir = e_up[i] * w_up + e_dn[i] * w_dn + e_sd[i] * w_sd
        e = e_sd[i] + k * (e_dir - e_sd[i])
        e = np.maximum(e, 2.0)

        if K > 0:
            # --- smoothed convex polygon section -----------------------
            # support(a) is the distance from the section centre to a tangent
            # plane of the underlying (wu, wv) ellipse in direction a, so the
            # facet ring inherits the sweep's anisotropy for free.
            def support(a):
                return np.sqrt((wu[i] * np.cos(a)) ** 2 + (wv[i] * np.sin(a)) ** 2)

            ang = base_ang + drift_k * t[i]
            gain = 1.0 + gbias + gamp * np.cos(wave_k * 2.0 * math.pi * t[i] + wphase)
            h = support(ang) * gain

            p = sharp[i]
            acc = np.zeros_like(theta)
            for kk in range(K):
                acc += (np.maximum(np.cos(theta - ang[kk]), 0.0) / h[kk]) ** p

            # world-aligned plateau and underside planes; the gain relaxes to
            # 3.0 as the section turns horizontal so they stop cutting there
            tg = top_g[i] + (1.0 - k) * (3.0 - top_g[i])
            bg = bot_g[i] + (1.0 - k) * (3.0 - bot_g[i])
            h_top = float(support(np.asarray(th_up))) * tg
            h_bot = float(support(np.asarray(th_up + math.pi))) * bg
            acc += (np.maximum(np.cos(theta - th_up), 0.0) / h_top) ** p
            acc += (np.maximum(np.cos(theta - th_up - math.pi), 0.0) / h_bot) ** p

            r = acc ** (-1.0 / p)
        else:
            r = (np.abs(ca / wu[i]) ** e + np.abs(sa / wv[i]) ** e) ** (-1.0 / e)

        for (freq, amps, ph0, drift) in harm:
            r = r * (1.0 + amps[i] * np.cos(freq * theta + ph0 + drift * t[i]))

        if conc[i] > 1e-6 and k > 1e-6:
            th_dn = th_up + math.pi + conc_o[i]
            d = _ang_delta(theta, th_dn)
            r = r * (1.0 - k * conc[i] * np.exp(-(d / conc_s[i]) ** 2))

        x = cu[i] + r * ca
        y = cv[i] + r * sa
        pts = P[i][None, :] + x[:, None] * Ui[None, :] + y[:, None] * Vi[None, :]
        rings.append(pts)
        centres.append((P[i] + cu[i] * Ui + cv[i] * Vi, T[i], Ui, Vi))

    # ---- ring vertices + side quads
    ring_base = []
    for pts in rings:
        ring_base.append(mesh.add_verts(pts))
    for i in range(S - 1):
        a, b = ring_base[i], ring_base[i + 1]
        for j in range(N):
            j2 = (j + 1) % N
            mesh.add_face([a + j, a + j2, b + j2, b + j])

    rng = np.random.default_rng(spec.seed)

    def make_cap(ring_pts, ring_start, centre, out_dir, u_ax, v_ax, cfg, reverse):
        h = cfg.get('height', 0.1)
        R = int(cfg.get('rings', 3))
        flare = cfg.get('flare', 1.0)
        tilt = cfg.get('tilt', (0.0, 0.0))
        wob = cfg.get('wobble', 0.0)
        phase = rng.uniform(0.0, 2.0 * math.pi, 3)
        prev_start = ring_start
        prev_n = N
        for s in range(1, R):
            a = (s / R) * (math.pi * 0.5)
            shrink = math.cos(a) ** (1.0 / max(flare, 1e-3))
            lift = math.sin(a)
            # non-axisymmetric shrink so the cap is not a dome of revolution
            ang = theta + phase[0]
            mod = (1.0 + 0.14 * np.cos(2 * ang) + 0.09 * np.cos(3 * ang + phase[1]))
            mod = mod / mod.max()   # guarantees a monotone inward march: no cap self-intersection
            pts = centre[None, :] + (ring_pts - centre[None, :]) * (shrink * mod)[:, None]
            pts = pts + out_dir[None, :] * (h * lift)
            pts = pts + u_ax[None, :] * (tilt[0] * lift) + v_ax[None, :] * (tilt[1] * lift)
            if wob > 0.0:
                w = wob * np.cos(3 * theta + phase[2]) * lift
                pts = pts + out_dir[None, :] * w[:, None]
            start = mesh.add_verts(pts)
            for j in range(prev_n):
                j2 = (j + 1) % prev_n
                if reverse:
                    mesh.add_face([prev_start + j, start + j, start + j2, prev_start + j2])
                else:
                    mesh.add_face([prev_start + j, prev_start + j2, start + j2, start + j])
            prev_start = start
        pole = centre + out_dir * h + u_ax * tilt[0] + v_ax * tilt[1]
        pi_ = mesh.add_verts(pole[None, :])
        for j in range(prev_n):
            j2 = (j + 1) % prev_n
            if reverse:
                mesh.add_face([prev_start + j, pi_, prev_start + j2])
            else:
                mesh.add_face([prev_start + j, prev_start + j2, pi_])

    c0, t0, u0, v0 = centres[0]
    make_cap(rings[0], ring_base[0], c0, -t0, u0, v0, spec.start_cap, reverse=True)
    c1, t1, u1, v1 = centres[-1]
    make_cap(rings[-1], ring_base[-1], c1, t1, u1, v1, spec.end_cap, reverse=False)

    return mesh


# --------------------------------------------------------------- boulders

def _cube_sphere(n):
    """Watertight quad sphere, pole-free, evenly distributed."""
    verts = {}
    order = []

    def key(p):
        return (round(p[0], 6), round(p[1], 6), round(p[2], 6))

    def vid(p):
        k = key(p)
        if k not in verts:
            verts[k] = len(order)
            order.append(np.asarray(p, dtype=float))
        return verts[k]

    faces = []
    axes = [
        (np.array([1, 0, 0.]), np.array([0, 1, 0.]), np.array([0, 0, 1.])),
        (np.array([-1, 0, 0.]), np.array([0, 0, 1.]), np.array([0, 1, 0.])),
        (np.array([0, 1, 0.]), np.array([0, 0, 1.]), np.array([1, 0, 0.])),
        (np.array([0, -1, 0.]), np.array([1, 0, 0.]), np.array([0, 0, 1.])),
        (np.array([0, 0, 1.]), np.array([1, 0, 0.]), np.array([0, 1, 0.])),
        (np.array([0, 0, -1.]), np.array([0, 1, 0.]), np.array([1, 0, 0.])),
    ]
    for (nrm, ax, ay) in axes:
        grid = []
        for iy in range(n + 1):
            row = []
            for ix in range(n + 1):
                u = ix / n * 2.0 - 1.0
                v = iy / n * 2.0 - 1.0
                p = nrm + ax * u + ay * v
                p = p / np.linalg.norm(p)
                row.append(vid(p))
            grid.append(row)
        for iy in range(n):
            for ix in range(n):
                faces.append((grid[iy][ix], grid[iy][ix + 1],
                              grid[iy + 1][ix + 1], grid[iy + 1][ix]))
    return np.asarray(order, dtype=float), faces


def _value_noise_dirs(dirs, seed, octaves):
    """Very low-frequency directional noise. Deliberately weak: this exists to
    break perfect smoothness, never to stand in for form."""
    rng = np.random.default_rng(seed)
    acc = np.zeros(len(dirs))
    for (freq, amp) in octaves:
        for _ in range(3):
            ax = rng.normal(size=3)
            ax /= np.linalg.norm(ax)
            ph = rng.uniform(0.0, 2.0 * math.pi)
            acc += amp * np.sin(freq * (dirs @ ax) * math.pi + ph)
    return acc


class BoulderSpec:
    """Smoothed convex polytope + directional mass displacement.

    planes    [(normal(3), offset), ...] -- offsets differ so facets differ in
              size; normals are deliberately non-parallel and non-orthogonal
    sharp     power-mean exponent: high = crisper facet edges, low = softer
    bulges    [(direction(3), amplitude, exponent), ...]; negative amplitude
              carves a broad concave scoop
    affine    3x3 applied after the radial evaluation (anisotropy + shear),
              which also skews the facets away from any orthogonal set
    """

    def __init__(self):
        self.res = 14
        self.planes = []
        self.sharp = 7.0
        self.bulges = []
        self.affine = np.eye(3)
        self.noise = [(1.4, 0.012), (2.6, 0.006)]
        self.seed = 3
        # hard-surface path (boulder_object)
        self.bevel = 0.085
        self.bevel_wide = 3.2        # multiplier for the wide-chamfer edge group
        self.bevel_wide_frac = 0.38  # fraction of edges that get the wide chamfer
        self.bevel_segments = 3
        self.densify = 2


def build_boulder(spec):
    dirs, faces = _cube_sphere(spec.res)
    normals = np.asarray([p[0] for p in spec.planes], dtype=float)
    normals = normals / np.linalg.norm(normals, axis=1, keepdims=True)
    offs = np.asarray([p[1] for p in spec.planes], dtype=float)

    p = spec.sharp
    dots = np.maximum(dirs @ normals.T, 0.0)          # (V, K)
    acc = np.sum((dots / offs[None, :]) ** p, axis=1)
    r = acc ** (-1.0 / p)

    for (bdir, amp, bexp) in spec.bulges:
        b = np.asarray(bdir, dtype=float)
        b /= np.linalg.norm(b)
        w = np.maximum(dirs @ b, 0.0) ** bexp
        r = r * (1.0 + amp * w)

    if spec.noise:
        r = r * (1.0 + _value_noise_dirs(dirs, spec.seed, spec.noise))

    pos = dirs * r[:, None]
    pos = pos @ np.asarray(spec.affine, dtype=float).T

    mesh = Mesh()
    mesh.add_verts(pos)
    for f in faces:
        mesh.add_face(f)
    return mesh


def convex_polytope(planes, tol=1e-6):
    """Exact intersection of half-spaces n.x <= h (origin must be inside).

    Building the boulder as a *real* polyhedron and then beveling its edges is
    what finally gives broad, dead-flat facets meeting in crisp-but-soft creases.
    Evaluating a smoothed distance field on a sphere cannot: a tight crease falls
    inside a single quad and the silhouette staircases along the grid.
    """
    N = np.asarray([p[0] for p in planes], dtype=float)
    N = N / np.linalg.norm(N, axis=1, keepdims=True)
    H = np.asarray([p[1] for p in planes], dtype=float)
    K = len(planes)

    cand = []
    for i in range(K):
        for j in range(i + 1, K):
            for k in range(j + 1, K):
                A = N[[i, j, k]]
                if abs(np.linalg.det(A)) < 1e-8:
                    continue
                x = np.linalg.solve(A, H[[i, j, k]])
                if np.all(N @ x <= H + tol):
                    cand.append(x)

    verts = []
    for x in cand:
        if not any(np.linalg.norm(x - y) < 1e-5 for y in verts):
            verts.append(x)
    P = np.asarray(verts, dtype=float)

    faces = []
    for k in range(K):
        on = np.where(np.abs(P @ N[k] - H[k]) < 1e-5)[0]
        if len(on) < 3:
            continue
        c = P[on].mean(axis=0)
        e1 = P[on[0]] - c
        e1 = e1 - np.dot(e1, N[k]) * N[k]
        e1 /= np.linalg.norm(e1)
        e2 = np.cross(N[k], e1)
        rel = P[on] - c
        order = on[np.argsort(np.arctan2(rel @ e2, rel @ e1))]
        faces.append(tuple(int(t) for t in order))
    return P, faces


def euler_mat(e):
    """XYZ euler (radians) -> 3x3 rotation matrix."""
    cx, cy, cz = np.cos(e)
    sx, sy, sz = np.sin(e)
    rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return rz @ ry @ rx


def block_mass(size, euler=(0.0, 0.0, 0.0), centre=(0.0, 0.0, 0.0),
               jitter=7.0, cuts=(), chamfers=0, chamfer_range=(0.58, 0.76), seed=0):
    """One irregular convex planar mass, as (verts, faces).

    Six slightly non-orthogonal face planes plus any number of corner-chamfer
    planes. Nothing here is axis-aligned or equal: normals are jittered, offsets
    are scaled unevenly, and the whole block is then rotated. Several of these
    unioned together is what gives re-entrant plan outlines, unequal lobes and
    contours that never run straight -- none of which a single swept envelope
    can produce.
    """
    rng = np.random.default_rng(seed * 7919 + 17)
    half = np.asarray(size, dtype=float) * 0.5
    axes = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)]
    planes = []
    for i, a in enumerate(axes):
        n = np.asarray(a, dtype=float) + rng.normal(scale=math.radians(jitter) * 1.6, size=3)
        n /= np.linalg.norm(n)
        planes.append((n, float(half[i // 2] * rng.uniform(0.93, 1.07))))
    for (az, el, frac) in cuts:
        planes.append((azel(az, el), float(half.max() * frac)))

    # Auto corner chamfers. Unioning raw boxes reads as a stack of tilted
    # slabs no matter how the boxes are posed; knocking every corner off first
    # turns each mass into a rounded polyhedron, and their union then reads as
    # one lumpy rock instead of intersecting plates.
    if chamfers:
        lo, hi = chamfer_range
        for i in range(chamfers):
            d = rng.normal(size=3)
            d /= np.linalg.norm(d)
            planes.append((d, float(half.max() * rng.uniform(lo, hi))))

    P, F = convex_polytope(planes)
    P = P @ euler_mat(np.asarray(euler, dtype=float)).T + np.asarray(centre, dtype=float)
    return P, F


def mass_to_object(name, verts, faces):
    import bpy
    import bmesh
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(p) for p in verts], [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bm.calc_volume(signed=True) < 0.0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()
    return ob


def soften(ob, bevel=0.12, wide=2.2, wide_frac=0.40, segments=4,
           densify=1, smooth_iters=0, smooth_factor=0.5,
           bulges=(), noise=None, seed=0):
    """Cozy finish: generous, deliberately uneven chamfers on every edge.

    The structure is already carved, so this only decides how hard the creases
    read. A large offset relative to facet size, in two passes with different
    radii, keeps broad planar facet centres while rounding the edges enough that
    the result reads as weathered stone rather than a cut gem.
    """
    import bmesh
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    def _bevel(edges, off):
        if not edges:
            return
        try:
            bmesh.ops.bevel(bm, geom=edges, offset=off, offset_type='OFFSET',
                            segments=segments, profile=0.5, affect='EDGES',
                            clamp_overlap=True)
        except TypeError:
            bmesh.ops.bevel(bm, geom=edges, offset=off, segments=segments,
                            profile=0.5, clamp_overlap=True)

    rng = np.random.default_rng(seed * 31 + 7)
    sharp = [e for e in bm.edges
             if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(12.0)]
    mask = rng.random(len(sharp)) < wide_frac
    _bevel([e for e, m in zip(sharp, mask) if m], bevel * wide)
    _bevel([e for e in bm.edges
            if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(22.0)],
           bevel)

    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    for _ in range(densify):
        bmesh.ops.subdivide_edges(bm, edges=list(bm.edges), cuts=1, use_grid_fill=True)

    # Densify then relax. This is the step that turns carved booleans into cozy
    # stone: a Laplacian pass pulls every hard seam into a rounded transition
    # while the broad facet centres, having no local curvature to lose, stay
    # flat. Bevelling alone cannot do it -- clamp_overlap shrinks the chamfer to
    # nothing exactly where the boolean left small faces, which is where the
    # seams are.
    if smooth_iters:
        # Taubin, not plain Laplacian. A straight Laplacian relax shrinks the
        # form badly -- nine passes at 0.55 took one of these pieces from 3.92
        # to 0.47 cubic units. Alternating a positive pass with a slightly
        # larger negative one relaxes the seams while holding the volume.
        lam = smooth_factor
        mu = -(smooth_factor + 0.03)
        verts = list(bm.verts)
        for _ in range(smooth_iters):
            bmesh.ops.smooth_vert(bm, verts=verts, factor=lam,
                                  use_axis_x=True, use_axis_y=True, use_axis_z=True)
            bmesh.ops.smooth_vert(bm, verts=verts, factor=mu,
                                  use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()

    if bulges or noise:
        v = np.asarray([list(x.co) for x in me.vertices], dtype=float)
        c = v.mean(axis=0)
        d = v - c
        r = np.linalg.norm(d, axis=1, keepdims=True)
        dirs = d / np.maximum(r, 1e-9)
        scale = np.ones(len(v))
        for (bdir, amp, bexp) in bulges:
            b = np.asarray(bdir, dtype=float)
            b /= np.linalg.norm(b)
            scale *= (1.0 + amp * np.maximum(dirs @ b, 0.0) ** bexp)
        if noise:
            scale *= (1.0 + _value_noise_dirs(dirs, seed, noise))
        v = c + d * scale[:, None]
        for i, x in enumerate(me.vertices):
            x.co = tuple(v[i])
        me.update()
    return ob


def soft_slice(mesh, origin, normal, k=0.10, undulate=None):
    """Smooth-min against a world-space plane: everything on the +normal side is
    pressed onto the plane, with a fillet of radius ~k/2 at the boundary.

    This is the "quarried plateau" operator. Stylized rock in this style reads
    as a soft volume sliced by a couple of broad planes -- a flat sky-facing top
    and a flat ground contact -- and that slice is what produces the crisp
    silhouette and the strong value break the reference has. `undulate` bends
    the cut plane very gently so the plateau is never a machined surface.

    Only vertex positions move; topology and manifoldness are untouched, and the
    cut is along the plane normal so no two vertices are ever merged.
    """
    v = mesh.np_verts()
    n = np.asarray(normal, dtype=float)
    n = n / np.linalg.norm(n)
    o = np.asarray(origin, dtype=float)
    d = (v - o) @ n
    if undulate:
        amp, fx, fy, p1, p2 = undulate
        d = d - amp * np.sin(fx * v[:, 0] + p1) * np.cos(fy * v[:, 1] + p2)
    dn = 0.5 * (d - np.sqrt(d * d + k * k))
    mesh.set_verts(v + (dn - d)[:, None] * n)
    return mesh


def soft_floor(mesh, z_target, k=0.10, undulate=None):
    """Ground-contact slice: flattens everything below z_target onto it."""
    return soft_slice(mesh, (0.0, 0.0, z_target), (0.0, 0.0, -1.0), k, undulate)


def azel(az_deg, el_deg):
    a = math.radians(az_deg)
    e = math.radians(el_deg)
    return np.array([math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)])


# ------------------------------------------------------- blender interop

def to_blender(mesh, name):
    import bpy
    import bmesh
    me = bpy.data.meshes.new(name)
    me.from_pydata(mesh.verts, [], mesh.faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)

    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # drop any zero-area slivers introduced by the cap collapse
    degen = [f for f in bm.faces if f.calc_area() < 1e-10]
    if degen:
        bmesh.ops.delete(bm, geom=degen, context='FACES')
        bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bm.calc_volume(signed=True) < 0.0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()
    return ob


def boulder_object(name, spec):
    """Hard-surface boulder: exact polytope -> bevel -> densify -> broad
    directional bulges and scoops. Facets stay planar, creases stay crisp,
    and the mass centre is displaced off the geometric centre."""
    import bpy
    import bmesh

    P, faces = convex_polytope(spec.planes)
    P = P @ np.asarray(spec.affine, dtype=float).T

    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(p) for p in P], [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)

    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    # Two bevel passes with different offsets on a seeded split of the edges.
    # A single uniform chamfer on every edge is exactly what makes a polytope
    # read as a machined die instead of a weathered cobble.
    def _bevel(edges, off):
        if not edges:
            return
        try:
            bmesh.ops.bevel(bm, geom=edges, offset=off, offset_type='OFFSET',
                            segments=spec.bevel_segments, profile=0.5,
                            affect='EDGES', clamp_overlap=True)
        except TypeError:
            bmesh.ops.bevel(bm, geom=edges, offset=off,
                            segments=spec.bevel_segments, profile=0.5,
                            clamp_overlap=True)

    brng = np.random.default_rng(spec.seed * 31 + 7)
    all_edges = list(bm.edges)
    mask = brng.random(len(all_edges)) < spec.bevel_wide_frac
    _bevel([e for e, m in zip(all_edges, mask) if m], spec.bevel * spec.bevel_wide)
    # The first pass rebuilds geometry, so the second group cannot be carried
    # across as stale references. Whatever is still sharp is what pass one did
    # not touch; those get the narrow chamfer.
    thr = math.radians(24.0)
    _bevel([e for e in bm.edges
            if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > thr],
           spec.bevel)

    bmesh.ops.triangulate(bm, faces=bm.faces)
    for _ in range(spec.densify):
        bmesh.ops.subdivide_edges(bm, edges=list(bm.edges), cuts=1,
                                  use_grid_fill=True)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()

    # broad, low-frequency mass displacement: bulges and scoops that break the
    # convexity without touching the facet language
    v = np.asarray([list(x.co) for x in me.vertices], dtype=float)
    c = v.mean(axis=0)
    d = v - c
    r = np.linalg.norm(d, axis=1, keepdims=True)
    dirs = d / np.maximum(r, 1e-9)
    scale = np.ones(len(v))
    for (bdir, amp, bexp) in spec.bulges:
        b = np.asarray(bdir, dtype=float)
        b /= np.linalg.norm(b)
        scale *= (1.0 + amp * np.maximum(dirs @ b, 0.0) ** bexp)
    if spec.noise:
        scale *= (1.0 + _value_noise_dirs(dirs, spec.seed, spec.noise))
    v = c + d * scale[:, None]
    for i, x in enumerate(me.vertices):
        x.co = tuple(v[i])
    me.update()
    return ob


def plane_cut(ob, co, no, bevel=0.05, segments=2):
    """True geometric bisect: quarries a genuinely flat plateau or sole.

    Clamping vertices onto a plane leaves a sawtooth silhouette wherever the
    quad rows cross it, so the cut is done for real -- bisect, discard the
    material on the +normal side, cap the loop, then bevel the new rim so the
    plateau meets the wall with the soft chamfer the reference style has
    instead of a razor edge. Stays watertight throughout.
    """
    import bmesh
    from mathutils import Vector

    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)

    n = Vector(no).normalized()
    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6,
                           plane_co=Vector(co), plane_no=n,
                           clear_outer=True, clear_inner=False)

    boundary = [e for e in bm.edges if e.is_boundary]
    if boundary:
        bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
    # A partial cut necessarily leaves the surface tangentially where it ends,
    # producing razor slivers of cap. Left in place the bevel inflates them into
    # visible fins, so they are dissolved and never beveled.
    bmesh.ops.dissolve_degenerate(bm, dist=2e-4, edges=list(bm.edges))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    if bevel > 0.0:
        rim = [e for e in bm.edges
               if len(e.link_faces) == 2
               and e.calc_length() > bevel * 1.2
               and abs(e.link_faces[0].normal.dot(n) - e.link_faces[1].normal.dot(n)) > 0.25
               and max(f.normal.dot(n) for f in e.link_faces) > 0.9]
        if rim:
            try:
                bmesh.ops.bevel(bm, geom=rim, offset=bevel, offset_type='OFFSET',
                                segments=segments, profile=0.5, affect='EDGES',
                                clamp_overlap=True)
            except TypeError:
                bmesh.ops.bevel(bm, geom=rim, offset=bevel, segments=segments,
                                profile=0.5, clamp_overlap=True)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)

    bm.to_mesh(me)
    bm.free()
    me.update()
    return ob


def make_ellipsoid(name, loc, scale, rot=(0.0, 0.0, 0.0), subdiv=4):
    """Smooth cutter volume for carving undercuts."""
    import bpy
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv, radius=1.0, location=loc)
    ob = bpy.context.active_object
    ob.name = name
    ob.scale = scale
    ob.rotation_euler = rot
    return ob


def boolean_cut(ob, cutter, operation='DIFFERENCE',
                solvers=('MANIFOLD', 'EXACT', 'FLOAT')):
    """Carve `cutter` out of `ob`, then delete the cutter.

    Used for doorways and undercuts. Sculpting a deep concavity into a swept
    cross-section field folds the surface back on itself; subtracting a real
    volume cannot.

    Solvers are tried in order and the result is checked: on these dense swept
    meshes the EXACT solver silently returns an *empty* mesh rather than
    raising, so a bare `modifier_apply` is not enough to know it worked.
    """
    import bpy
    base = ob.data.copy()
    try:
        for solver in solvers:
            ob.data = base.copy()
            md = ob.modifiers.new("bool", 'BOOLEAN')
            md.operation = operation
            md.object = cutter
            try:
                md.solver = solver
            except (TypeError, AttributeError):
                ob.modifiers.remove(md)
                continue
            bpy.ops.object.select_all(action='DESELECT')
            bpy.context.view_layer.objects.active = ob
            ob.select_set(True)
            try:
                bpy.ops.object.modifier_apply(modifier=md.name)
            except RuntimeError:
                if md.name in ob.modifiers:
                    ob.modifiers.remove(md)
                continue
            if len(ob.data.vertices) > 0:
                bpy.data.objects.remove(cutter, do_unlink=True)
                return ob
        raise RuntimeError(
            f"boolean {operation} on {ob.name} produced an empty mesh with every solver")
    finally:
        if base.users == 0:
            bpy.data.meshes.remove(base)


def bevel_sharp_edges(ob, angle_deg=50.0, offset=0.035, segments=2):
    """Soften only genuinely sharp creases -- e.g. a boolean seam -- and leave
    the authored facet creases alone."""
    import bmesh
    import math as _m
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    thr = _m.radians(angle_deg)
    edges = [e for e in bm.edges
             if len(e.link_faces) == 2
             and e.calc_face_angle(0.0) > thr
             and e.calc_length() > offset * 1.5]
    if edges:
        try:
            bmesh.ops.bevel(bm, geom=edges, offset=offset, offset_type='OFFSET',
                            segments=segments, profile=0.5, affect='EDGES',
                            clamp_overlap=True)
        except TypeError:
            bmesh.ops.bevel(bm, geom=edges, offset=offset, segments=segments,
                            profile=0.5, clamp_overlap=True)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)
    bm.to_mesh(me)
    bm.free()
    me.update()
    return ob


def cleanup(ob, merge=2e-4, min_area=1e-7, keep_largest=True):
    """Restore manifoldness after booleans and bevels.

    Boolean solvers leave a handful of near-duplicate vertices and zero-area
    slivers along the seam; left alone they show up as non-manifold edges even
    though nothing is visibly wrong.
    """
    import bmesh
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=merge)
    bmesh.ops.dissolve_degenerate(bm, dist=merge * 5.0, edges=list(bm.edges))
    # Booleans leave back-to-back sliver pairs -- two faces of ~5e-9 area with
    # opposing normals sharing a sub-millimetre edge. They are far above a 1e-10
    # threshold, so that threshold silently passed them through and left the edge
    # carrying four faces.
    degen = [f for f in bm.faces if f.calc_area() < min_area]
    if degen:
        bmesh.ops.delete(bm, geom=degen, context='FACES')

    # Duplicate faces: two faces spanning the identical vertex set. remove_doubles
    # will not touch these, and one of them is enough to leave an edge carrying
    # three faces. Booleans emit them occasionally along a seam.
    bm.verts.index_update()
    seen = set()
    dupes = []
    for f in bm.faces:
        key = tuple(sorted(v.index for v in f.verts))
        if key in seen:
            dupes.append(f)
        else:
            seen.add(key)
    if dupes:
        bmesh.ops.delete(bm, geom=dupes, context='FACES')

    # Repair any edge left carrying three or more faces: drop the smallest
    # offender and re-close the hole. Booleans occasionally leave one of these
    # behind and a single one is enough to make the mesh non-watertight.
    for _ in range(4):
        bad = [e for e in bm.edges if len(e.link_faces) > 2]
        if not bad:
            break
        victims = set()
        for e in bad:
            faces = sorted(e.link_faces, key=lambda f: f.calc_area())
            for f in faces[:len(faces) - 2]:
                victims.add(f)
        if not victims:
            break
        bmesh.ops.delete(bm, geom=list(victims), context='FACES')
        boundary = [e for e in bm.edges if e.is_boundary]
        if boundary:
            bmesh.ops.holes_fill(bm, edges=boundary, sides=0)

    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')

    if keep_largest:
        # Chains of unions and subtractions can strand a shard: a cutter shaves
        # a block down until it no longer touches its neighbour. Keep only the
        # biggest connected shell so a stray island never ships.
        bm.faces.index_update()
        seen = {}
        comps = []
        for f in bm.faces:
            if f.index in seen:
                continue
            group = []
            stack = [f]
            seen[f.index] = len(comps)
            while stack:
                cur = stack.pop()
                group.append(cur)
                for e in cur.edges:
                    for nf in e.link_faces:
                        if nf.index not in seen:
                            seen[nf.index] = len(comps)
                            stack.append(nf)
            comps.append(group)
        if len(comps) > 1:
            comps.sort(key=lambda g: sum(f.calc_area() for f in g), reverse=True)
            drop = [f for g in comps[1:] for f in g]
            bmesh.ops.delete(bm, geom=drop, context='FACES')
            loose = [v for v in bm.verts if not v.link_faces]
            if loose:
                bmesh.ops.delete(bm, geom=loose, context='VERTS')

    for _ in range(3):
        boundary = [e for e in bm.edges if e.is_boundary]
        if not boundary:
            break
        try:
            bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
        except RuntimeError:
            pass
        boundary = [e for e in bm.edges if e.is_boundary]
        if boundary:
            try:
                bmesh.ops.triangle_fill(bm, edges=boundary, use_beauty=True)
            except (RuntimeError, TypeError):
                break
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bm.calc_volume(signed=True) < 0.0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)
    bm.to_mesh(me)
    bm.free()
    me.update()
    return ob


def validate(ob):
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(ob.data)

    non_manifold_e = sum(1 for e in bm.edges if not e.is_manifold)
    boundary_e = sum(1 for e in bm.edges if e.is_boundary)
    loose_v = sum(1 for v in bm.verts if not v.link_edges)
    wire_e = sum(1 for e in bm.edges if not e.link_faces)
    non_manifold_v = sum(1 for v in bm.verts if not v.is_manifold)
    volume = bm.calc_volume(signed=True)

    # connected components over faces
    seen = set()
    comps = 0
    for f in bm.faces:
        if f.index in seen:
            continue
        comps += 1
        stack = [f]
        seen.add(f.index)
        while stack:
            cur = stack.pop()
            for e in cur.edges:
                for nf in e.link_faces:
                    if nf.index not in seen:
                        seen.add(nf.index)
                        stack.append(nf)

    verts = np.asarray([v.co[:] for v in bm.verts], dtype=float)
    lo, hi = verts.min(axis=0), verts.max(axis=0)
    tris = sum(len(f.verts) - 2 for f in bm.faces)

    # depth-variation metric: how much the Y extent changes across X slices.
    # A constant-depth extrusion scores ~0 here.
    xs = verts[:, 0]
    bins = np.linspace(xs.min(), xs.max(), 13)
    depths = []
    for i in range(len(bins) - 1):
        sel = (xs >= bins[i]) & (xs <= bins[i + 1])
        if sel.sum() > 3:
            depths.append(verts[sel, 1].max() - verts[sel, 1].min())
    depths = np.asarray(depths) if depths else np.asarray([0.0])
    depth_var = float(depths.std() / max(depths.mean(), 1e-6))

    res = dict(
        name=ob.name,
        verts=len(bm.verts), faces=len(bm.faces), tris=tris,
        components=comps,
        non_manifold_edges=non_manifold_e,
        non_manifold_verts=non_manifold_v,
        boundary_edges=boundary_e,
        wire_edges=wire_e,
        loose_verts=loose_v,
        signed_volume=float(volume),
        watertight=bool(non_manifold_e == 0 and boundary_e == 0
                        and wire_e == 0 and loose_v == 0 and comps == 1),
        bbox_min=[float(x) for x in lo],
        bbox_max=[float(x) for x in hi],
        size=[float(x) for x in (hi - lo)],
        depth_variation=depth_var,
    )
    bm.free()
    return res
