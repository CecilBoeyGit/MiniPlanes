"""Build the MiniPlanes stand-in models in Blender and export them as FBX.

Run (all models, or name some after --):
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup -P Tools/Art/build_models.py
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup -P Tools/Art/build_models.py -- PlayerPlane BalloonBomb

Writes Assets/MiniPlanes/Art/Models/<Name>.fbx and Tools/Art/previews/<Name>.png.

Aircraft are signed distance fields: smooth unions give the moulded one-piece fillets of the concept.
The field is meshed with surface nets, decimated, snapped back onto the surface and shaded with
normals taken from the field. Balloon envelopes are lathed so that stripes follow the gore creases.
Colour is per-corner vertex colour; hex values below are sRGB.

Axes while modelling: nose -Y, up +Z. The export settings map that to nose +Z, up +Y in the FBX.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from pngio import read_png, write_png, resize  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "MiniPlanes", "Art", "Models")
PREVIEW_DIR = os.path.join(HERE, "previews")


# ------------------------------------------------------------------------------- colour helpers
def hex_srgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float64)


def to_linear(c):
    c = np.asarray(c, np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def ramp(t, stops):
    """Colour ramp in sRGB space. stops = [(position, '#hex'), ...] with smooth blending."""
    t = np.asarray(t, np.float64)
    out = np.zeros(t.shape + (3,))
    out[...] = hex_srgb(stops[0][1])
    for (p0, c0), (p1, c1) in zip(stops[:-1], stops[1:]):
        k = smoothstep(p0, p1, t)[..., None]
        out = out * (1 - k) + hex_srgb(c1) * k
    return out


# --------------------------------------------------------------------------------- SDF primitives
def smin(a, b, k):
    if k <= 0:
        return np.minimum(a, b)
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1 - h) + a * h - k * h * (1 - h)


def smax(a, b, k):
    return -smin(-a, -b, k)


def sphere(p, c, r):
    return np.linalg.norm(p - np.asarray(c, np.float64), axis=-1) - r


def ellipsoid(p, c, radii):
    q = p - np.asarray(c, np.float64)
    r = np.asarray(radii, np.float64)
    k0 = np.linalg.norm(q / r, axis=-1)
    k1 = np.linalg.norm(q / (r * r), axis=-1)
    return k0 * (k0 - 1.0) / np.maximum(k1, 1e-9)


def capsule(p, a, b, r0, r1=None):
    """Sphere-swept segment with linearly varying radius (a good enough metric for small tapers)."""
    r1 = r0 if r1 is None else r1
    a = np.asarray(a, np.float64)
    b = np.asarray(b, np.float64)
    pa = p - a
    ba = b - a
    h = np.clip(np.sum(pa * ba, axis=-1) / np.dot(ba, ba), 0.0, 1.0)
    return np.linalg.norm(pa - h[..., None] * ba, axis=-1) - (r0 + (r1 - r0) * h)


def torus_y(p, c, major, minor):
    q = p - np.asarray(c, np.float64)
    return np.hypot(np.hypot(q[..., 0], q[..., 2]) - major, q[..., 1]) - minor


def rounded_box(p, c, half, r):
    q = np.abs(p - np.asarray(c, np.float64)) - (np.asarray(half, np.float64) - r)
    return (np.linalg.norm(np.maximum(q, 0.0), axis=-1)
            + np.minimum(np.max(q, axis=-1), 0.0) - r)


def polygon2d(u, v, pts):
    """Exact signed distance to a closed polygon (negative inside)."""
    pts = np.asarray(pts, np.float64)
    d = np.full(u.shape, np.inf)
    s = np.ones(u.shape)
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % n]
        ex, ey = bx - ax, by - ay
        wx, wy = u - ax, v - ay
        t = np.clip((wx * ex + wy * ey) / (ex * ex + ey * ey), 0.0, 1.0)
        dx, dy = wx - ex * t, wy - ey * t
        d = np.minimum(d, dx * dx + dy * dy)
        c1 = v >= ay
        c2 = v < by
        c3 = ex * wy > ey * wx
        flip = (c1 & c2 & c3) | (~c1 & ~c2 & ~c3)
        s = np.where(flip, -s, s)
    return s * np.sqrt(d)


def plate(u, v, n, pts, corner_r, t, pillow=0.6, pillow_w=0.10, taper_axis=None, taper=(1.0, 1.0)):
    """Extruded polygon with fully rounded edges and a soft pillow bulge (a moulded toy wing).

    u, v are plan coordinates, n the normal coordinate. t is half thickness at the rim.
    """
    d2 = polygon2d(u, v, pts) - corner_r
    bulge = 1.0 + pillow * smoothstep(0.0, pillow_w, -d2)
    tt = t
    if taper_axis is not None:
        lo, hi = taper_axis
        k = np.clip((np.abs(u) - lo) / (hi - lo), 0.0, 1.0)
        tt = t * (taper[0] + (taper[1] - taper[0]) * k)
    q = np.abs(n) / (bulge * tt / t) if np.ndim(tt) or pillow else np.abs(n)
    a = d2 + t
    b = q - t + t
    return (np.minimum(np.maximum(a, b), 0.0)
            + np.hypot(np.maximum(a, 0.0), np.maximum(b, 0.0)) - t)


def profile(stations):
    """Monotone cubic through (y, value) stations, evaluated by dense table lookup."""
    ys = np.array([s[0] for s in stations], np.float64)
    vs = np.array([s[1] for s in stations], np.float64)
    dense = np.linspace(ys[0], ys[-1], 800)
    h = np.diff(ys)
    delta = np.diff(vs) / h
    m = np.zeros_like(vs)
    m[0], m[-1] = delta[0], delta[-1]
    for i in range(1, len(vs) - 1):
        if delta[i - 1] * delta[i] > 0:
            w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i])
    idx = np.clip(np.searchsorted(ys, dense) - 1, 0, len(h) - 1)
    t = (dense - ys[idx]) / h[idx]
    val = ((2 * t ** 3 - 3 * t ** 2 + 1) * vs[idx] + (t ** 3 - 2 * t ** 2 + t) * h[idx] * m[idx]
           + (-2 * t ** 3 + 3 * t ** 2) * vs[idx + 1] + (t ** 3 - t ** 2) * h[idx] * m[idx + 1])
    return lambda y: np.interp(y, dense, val)


def fuselage(p, y0, y1, width, height, center, nose_power=3.0, tail_power=2.2):
    """Elliptical section body along Y. width/height/center are callables of y; ends close round."""
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    mid = 0.5 * (y0 + y1)
    half = 0.5 * (y1 - y0)
    s = (y - mid) / half
    power = np.where(s < 0, nose_power, tail_power)
    close = np.power(np.maximum(1.0 - np.power(np.abs(s), power), 0.0), 1.0 / power)
    yc = np.clip(y, y0, y1)
    w = np.maximum(width(yc) * close, 1e-3)
    h = np.maximum(height(yc) * close, 1e-3)
    q = np.sqrt((x / w) ** 2 + ((z - center(yc)) / h) ** 2)
    return (q - 1.0) * np.minimum(w, h) + np.maximum(np.abs(s) - 1.0, 0.0) * half


# --------------------------------------------------------------------------------- SDF to mesh
def surface_nets(field, lo, h):
    nx, ny, nz = field.shape
    inside = field < 0.0
    shape = (nx - 1, ny - 1, nz - 1)
    acc = np.zeros(shape + (3,), np.float64)
    count = np.zeros(shape, np.int32)
    lo = np.asarray(lo, np.float64)
    edges = []
    for a in range(3):
        sl0 = [slice(None)] * 3
        sl1 = [slice(None)] * 3
        sl0[a] = slice(0, -1)
        sl1[a] = slice(1, None)
        crossing = inside[tuple(sl0)] != inside[tuple(sl1)]
        idx = np.argwhere(crossing)
        f0 = field[tuple(sl0)][crossing]
        f1 = field[tuple(sl1)][crossing]
        t = f0 / (f0 - f1)
        pos = idx.astype(np.float64)
        pos[:, a] += t
        b, c = (a + 1) % 3, (a + 2) % 3
        for db in (0, -1):
            for dc in (0, -1):
                cell = idx.copy()
                cell[:, b] += db
                cell[:, c] += dc
                ok = np.all(cell >= 0, axis=1) & np.all(cell < np.array(shape), axis=1)
                ci = cell[ok]
                np.add.at(acc, (ci[:, 0], ci[:, 1], ci[:, 2]), pos[ok])
                np.add.at(count, (ci[:, 0], ci[:, 1], ci[:, 2]), 1)
        edges.append((a, idx, inside[tuple(sl0)][crossing]))
    active = count > 0
    index = -np.ones(shape, np.int64)
    index[active] = np.arange(np.count_nonzero(active))
    verts = acc[active] / count[active][:, None] * h + lo
    quads = []
    for a, idx, low_inside in edges:
        b, c = (a + 1) % 3, (a + 2) % 3
        ok = (idx[:, b] >= 1) & (idx[:, c] >= 1) & (idx[:, b] < shape[b]) & (idx[:, c] < shape[c])
        ok &= idx[:, a] < shape[a]
        e = idx[ok]
        li = low_inside[ok]

        def at(db, dc):
            cell = e.copy()
            cell[:, b] += db
            cell[:, c] += dc
            return index[cell[:, 0], cell[:, 1], cell[:, 2]]

        q = np.stack([at(-1, -1), at(0, -1), at(0, 0), at(-1, 0)], axis=1)
        q[~li] = q[~li][:, ::-1]
        quads.append(q)
    quads = np.concatenate(quads)
    tris = np.concatenate([quads[:, [0, 1, 2]], quads[:, [0, 2, 3]]])
    return verts, tris


def gradient(f, p, eps=5e-4):
    g = np.empty_like(p)
    for i in range(3):
        d = np.zeros(3)
        d[i] = eps
        g[:, i] = (f(p + d) - f(p - d)) / (2 * eps)
    return g


def project(f, p, iters=4):
    for _ in range(iters):
        g = gradient(f, p)
        n2 = np.maximum(np.sum(g * g, axis=1, keepdims=True), 1e-4)
        step = f(p)[:, None] * g / n2
        # The fields are only approximate distances; a weak gradient must not fling a vertex away.
        length = np.maximum(np.linalg.norm(step, axis=1, keepdims=True), 1e-12)
        p = p - step * np.minimum(1.0, 0.006 / length)
    return p


def decimate(verts, tris, target):
    mesh = bpy.data.meshes.new("tmp")
    mesh.from_pydata(verts.tolist(), [], tris.tolist())
    mesh.update()
    obj = bpy.data.objects.new("tmp", mesh)
    bpy.context.scene.collection.objects.link(obj)
    mod = obj.modifiers.new("dec", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = min(1.0, target / len(tris))
    mod.use_symmetry = False  # symmetric collapse leaves open and multi-face edges on the seam
    dg = bpy.context.evaluated_depsgraph_get()
    dg.update()
    res = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    bm = bmesh.new()
    bm.from_mesh(res)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    # Collapse can leave doubled faces at the mirror seam; drop them and close any hole it leaves.
    bm.verts.index_update()
    seen = set()
    dupes = []
    for face in bm.faces:
        key = tuple(sorted(v.index for v in face.verts))
        if key in seen:
            dupes.append(face)
        seen.add(key)
    if dupes:
        bmesh.ops.delete(bm, geom=dupes, context="FACES_ONLY")
    wire = [e for e in bm.edges if not e.link_faces]
    if wire:
        bmesh.ops.delete(bm, geom=wire, context="EDGES")
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=12)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.verts.ensure_lookup_table()
    bm.verts.index_update()
    v = np.array([x.co[:] for x in bm.verts], np.float64)
    t = np.array([[x.index for x in f.verts] for f in bm.faces], np.int64)
    bm.free()
    bpy.data.objects.remove(obj)
    bpy.data.meshes.remove(mesh)
    bpy.data.meshes.remove(res)
    return v, t


def edge_check(verts, tris):
    e = np.sort(np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]]), axis=1)
    _, c = np.unique(e, axis=0, return_counts=True)
    return "open %d multi %d" % (np.count_nonzero(c == 1), np.count_nonzero(c > 2))


def relax_folds(f, verts, tris, iters=8):
    """Slide vertices of folded triangles towards their neighbours, staying on the surface.

    Collapse decimation leaves a few triangles that face away from the field normal; with smooth
    normals those render as small dark chips.
    """
    for it in range(iters):
        a, b, c = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
        fn = np.cross(b - a, c - a)
        area = np.linalg.norm(fn, axis=1)
        fn = fn / np.maximum(area, 1e-12)[:, None]
        g = gradient(f, (a + b + c) / 3.0)
        g = g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-9)
        bad = np.sum(fn * g, axis=1) < 0.05
        if not bad.any():
            break
        move = np.zeros(len(verts), bool)
        move[tris[bad].ravel()] = True
        acc = np.zeros_like(verts)
        cnt = np.zeros(len(verts))
        for i, j in ((0, 1), (1, 2), (2, 0)):
            np.add.at(acc, tris[:, i], verts[tris[:, j]])
            np.add.at(acc, tris[:, j], verts[tris[:, i]])
            np.add.at(cnt, tris[:, i], 1)
            np.add.at(cnt, tris[:, j], 1)
        avg = acc / np.maximum(cnt, 1)[:, None]
        new = verts.copy()
        new[move] = verts[move] * 0.4 + avg[move] * 0.6
        # Keep the mirror seam on the mirror plane.
        seam = np.abs(verts[:, 0]) < 1e-5
        new[seam, 0] = 0.0
        verts = new.copy()
        verts[move] = project(f, new[move], 2)
    return verts


def mesh_sdf(f, lo, hi, cell, target):
    """SDF callable (Nx3 -> N) to a decimated triangle mesh with field normals."""
    lo = np.asarray(lo, np.float64)
    hi = np.asarray(hi, np.float64)
    # A grid symmetric about x = 0 keeps the nets and the decimation symmetric.
    half_nx = int(np.ceil(max(abs(lo[0]), abs(hi[0])) / cell))
    lo[0] = -half_nx * cell
    ny = int(np.ceil((hi[1] - lo[1]) / cell)) + 1
    nz = int(np.ceil((hi[2] - lo[2]) / cell)) + 1
    nx = 2 * half_nx + 1
    xs = lo[0] + np.arange(nx) * cell
    ys = lo[1] + np.arange(ny) * cell
    zs = lo[2] + np.arange(nz) * cell
    field = np.empty((nx, ny, nz), np.float32)
    # Evaluated slab by slab to bound memory.
    yy, zz = np.meshgrid(ys, zs, indexing="ij")
    for i, x in enumerate(xs):
        pts = np.stack([np.full(yy.shape, x), yy, zz], axis=-1).reshape(-1, 3)
        field[i] = f(pts).reshape(ny, nz)
    verts, tris = surface_nets(field.astype(np.float64), lo, cell)
    verts = project(f, verts, 2)
    verts, tris = decimate(verts, tris, target)
    verts = project(f, verts, 3)
    verts = relax_folds(f, verts, tris)
    g = gradient(f, verts)
    normals = g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-9)
    return verts, tris, normals


class Part:
    """Triangle soup piece: verts Nx3, tris Mx3, normals Nx3, corner colours Mx3x3 in sRGB."""

    def __init__(self, verts, tris, normals, colors):
        self.verts, self.tris, self.normals, self.colors = verts, tris, normals, colors


def vertex_colored(verts, tris, normals, color_fn):
    c = color_fn(verts)
    return Part(verts, tris, normals, c[tris])


def concat(parts):
    vs, ts, ns, cs = [], [], [], []
    base = 0
    for p in parts:
        vs.append(p.verts)
        ts.append(p.tris + base)
        ns.append(p.normals)
        cs.append(p.colors)
        base += len(p.verts)
    return Part(np.concatenate(vs), np.concatenate(ts), np.concatenate(ns), np.concatenate(cs))


# ---------------------------------------------------------------------------- aircraft definitions
def mirror_x(p):
    q = p.copy()
    q[..., 0] = np.abs(q[..., 0])
    return q


def player_body_sdf(p):
    w = profile([(-0.47, 0.150), (-0.38, 0.155), (-0.22, 0.158), (-0.05, 0.150), (0.15, 0.108),
                 (0.33, 0.070), (0.48, 0.050)])
    h = profile([(-0.47, 0.150), (-0.38, 0.155), (-0.22, 0.160), (-0.05, 0.150), (0.15, 0.110),
                 (0.33, 0.075), (0.48, 0.055)])
    c = profile([(-0.47, 0.0), (-0.1, 0.0), (0.2, 0.018), (0.48, 0.045)])
    d = fuselage(p, -0.47, 0.48, w, h, c, nose_power=3.5, tail_power=2.4)
    # Soft cowling step: a shallow groove behind the nose ring.
    d = smax(d, -torus_y(p, (0, -0.325, 0), 0.142, 0.0085), 0.006)

    m = mirror_x(p)
    # Wings: broad, straight, full rounded tips, slight dihedral, set low on the body.
    wz = p[..., 2] + 0.055 - 0.10 * np.abs(p[..., 0])
    wing = plate(m[..., 0], p[..., 1],
                 wz,
                 [(-0.02, -0.155), (0.365, -0.080), (0.365, -0.045), (-0.02, 0.030)],
                 0.072, 0.021, pillow=0.75, pillow_w=0.09)
    d = smin(d, wing, 0.045)

    # Tailplane, rounded paddles.
    tz = p[..., 2] - 0.045
    tail = plate(m[..., 0], p[..., 1], tz,
                 [(-0.02, 0.365), (0.175, 0.385), (0.175, 0.41), (-0.02, 0.425)],
                 0.040, 0.016, pillow=0.6, pillow_w=0.05)
    d = smin(d, tail, 0.025)

    # Fin: swept, thick, rounded top.
    fin = plate(p[..., 1], p[..., 2], p[..., 0],
                [(0.29, 0.06), (0.385, 0.225), (0.405, 0.225), (0.42, 0.06)],
                0.045, 0.017, pillow=0.6, pillow_w=0.05)
    d = smin(d, fin, 0.028)

    # Big soft canopy with a crisp seam where it meets the body.
    canopy = ellipsoid(p, (0, -0.07, 0.118), (0.105, 0.185, 0.112))
    return smin(d, canopy, 0.005)


def player_prop_sdf(p):
    """Local space: hub at the origin, spin axis Y, nose -Y."""
    d = ellipsoid(p, (0, -0.012, 0), (0.054, 0.058, 0.054))
    angle = np.arctan2(p[..., 2], p[..., 0])
    sector = np.round(angle / (2 * np.pi / 3)) * (2 * np.pi / 3)
    ca, sa = np.cos(sector), np.sin(sector)
    # Rotate each 120 degree sector onto +U.
    u = p[..., 0] * ca + p[..., 2] * sa
    v = -p[..., 0] * sa + p[..., 2] * ca
    y = p[..., 1] - 0.005
    # Fixed blade pitch.
    pitch = math.radians(24.0)
    bv = v * math.cos(pitch) - y * math.sin(pitch)
    bn = v * math.sin(pitch) + y * math.cos(pitch)
    blade = plate(u, bv, bn,
                  [(0.04, -0.006), (0.135, -0.012), (0.135, 0.012), (0.04, 0.006)],
                  0.029, 0.0105, pillow=0.5, pillow_w=0.03)
    return smin(d, blade, 0.012)


def fighter_sdf(p):
    w = profile([(-0.5, 0.118), (-0.34, 0.135), (-0.12, 0.14), (0.12, 0.112), (0.34, 0.075), (0.5, 0.056)])
    h = profile([(-0.5, 0.112), (-0.34, 0.125), (-0.12, 0.128), (0.12, 0.10), (0.34, 0.07), (0.5, 0.052)])
    c = profile([(-0.5, -0.012), (-0.2, 0.0), (0.2, 0.01), (0.5, 0.03)])
    d = fuselage(p, -0.5, 0.5, w, h, c, nose_power=2.6, tail_power=3.0)

    m = mirror_x(p)
    wz = p[..., 2] + 0.022 + 0.035 * np.abs(p[..., 0])
    wing = plate(m[..., 0], p[..., 1], wz,
                 [(-0.02, -0.165), (0.405, 0.235), (0.405, 0.29), (-0.02, 0.29)],
                 0.050, 0.020, pillow=0.85, pillow_w=0.10)
    d = smin(d, wing, 0.05)

    tz = p[..., 2] - 0.012
    tail = plate(m[..., 0], p[..., 1], tz,
                 [(-0.02, 0.335), (0.17, 0.43), (0.17, 0.46), (-0.02, 0.46)],
                 0.032, 0.014, pillow=0.6, pillow_w=0.04)
    d = smin(d, tail, 0.022)

    fin = plate(p[..., 1], p[..., 2], p[..., 0],
                [(0.18, 0.06), (0.395, 0.285), (0.43, 0.285), (0.44, 0.06)],
                0.036, 0.016, pillow=0.6, pillow_w=0.05)
    d = smin(d, fin, 0.028)

    canopy = ellipsoid(p, (0, -0.135, 0.088), (0.086, 0.185, 0.092))
    return smin(d, canopy, 0.005)


def bomber_sdf(p):
    w = profile([(-0.5, 0.150), (-0.36, 0.170), (-0.12, 0.178), (0.12, 0.152), (0.32, 0.098), (0.5, 0.06)])
    h = profile([(-0.5, 0.140), (-0.36, 0.158), (-0.12, 0.165), (0.12, 0.142), (0.32, 0.095), (0.5, 0.058)])
    c = profile([(-0.5, -0.012), (-0.2, 0.0), (0.2, 0.02), (0.5, 0.05)])
    d = fuselage(p, -0.5, 0.5, w, h, c, nose_power=3.6, tail_power=2.6)

    m = mirror_x(p)
    # Wide heavy wing: mild sweep, thick, long span.
    wz = p[..., 2] - 0.022 - 0.035 * np.abs(p[..., 0])
    wing = plate(m[..., 0], p[..., 1], wz,
                 [(-0.02, -0.165), (0.25, -0.115), (0.585, -0.005), (0.585, 0.03), (0.25, 0.035), (-0.02, 0.06)],
                 0.055, 0.028, pillow=0.8, pillow_w=0.12)
    d = smin(d, wing, 0.055)

    # Twin engine nacelles with a spinner and short moulded blades.
    nx = 0.315
    wing_z = 0.022 + 0.035 * nx
    nac = capsule(np.stack([m[..., 0], p[..., 1], p[..., 2]], axis=-1),
                  (nx, -0.215, wing_z), (nx, 0.135, wing_z + 0.005), 0.098, 0.05)
    d = smin(d, nac, 0.03)
    spinner = ellipsoid(np.stack([m[..., 0], p[..., 1], p[..., 2]], axis=-1),
                        (nx, -0.325, wing_z), (0.046, 0.062, 0.046))
    d = smin(d, spinner, 0.012)
    # Three short moulded blades radiate from the spinner in the plane of the disc.
    bx = m[..., 0] - nx
    bz = p[..., 2] - wing_z
    by = p[..., 1] + 0.300
    ang = np.arctan2(bz, bx) - math.radians(90.0)
    sector = np.round(ang / (2 * np.pi / 3)) * (2 * np.pi / 3) + math.radians(90.0)
    u = bx * np.cos(sector) + bz * np.sin(sector)
    v = -bx * np.sin(sector) + bz * np.cos(sector)
    blade = plate(u, v, by,
                  [(0.03, -0.004), (0.098, -0.008), (0.098, 0.008), (0.03, 0.004)],
                  0.024, 0.014, pillow=0.3, pillow_w=0.02)
    d = smin(d, blade, 0.018)

    # H tail: broad tailplane with an end-plate fin on each tip.
    tz = p[..., 2] - 0.06
    tail = plate(m[..., 0], p[..., 1], tz,
                 [(-0.02, 0.34), (0.26, 0.375), (0.26, 0.41), (-0.02, 0.44)],
                 0.040, 0.02, pillow=0.6, pillow_w=0.05)
    d = smin(d, tail, 0.028)
    fin = plate(p[..., 1], p[..., 2], m[..., 0] - 0.262,
                [(0.335, -0.02), (0.395, 0.15), (0.425, 0.15), (0.435, -0.02)],
                0.042, 0.018, pillow=0.5, pillow_w=0.04)
    d = smin(d, fin, 0.026)

    # Small blister canopy far forward; the bomber reads through its bulk, not the bubble.
    canopy = ellipsoid(p, (0, -0.215, 0.125), (0.092, 0.125, 0.082))
    return smin(d, canopy, 0.005)


def gradient_y(stops, y0, y1):
    def fn(v):
        return ramp((v[:, 1] - y0) / (y1 - y0), stops)
    return fn


PLAYER_STOPS = [(0.0, "#F3E5B8"), (0.09, "#F3E5B8"), (0.29, "#8FD0AE"), (0.52, "#7FC6A8"), (0.78, "#3E9389"), (1.0, "#3E9389")]
FIGHTER_STOPS = [(0.0, "#F4866E"), (0.5, "#F07A62"), (1.0, "#E66D56")]
BOMBER_STOPS = [(0.0, "#DE6A54"), (0.55, "#CC5442"), (1.0, "#BE4938")]


def build_player():
    body = mesh_sdf(player_body_sdf, (-0.5, -0.52, -0.21), (0.5, 0.52, 0.34), 0.0042, 4300)
    body_part = vertex_colored(*body, gradient_y(PLAYER_STOPS, -0.47, 0.48))
    hub_y = -0.50
    prop = mesh_sdf(player_prop_sdf, (-0.2, -0.09, -0.2), (0.2, 0.07, 0.2), 0.0035, 700)
    prop_part = vertex_colored(*prop, lambda v: np.tile(hex_srgb("#F3E5B8"), (len(v), 1)))
    return [("Body", body_part, None), ("Propeller", prop_part, (0.0, hub_y, 0.0))]


def build_fighter():
    v = mesh_sdf(fighter_sdf, (-0.55, -0.54, -0.2), (0.55, 0.54, 0.4), 0.0042, 4200)
    return [("Body", vertex_colored(*v, gradient_y(FIGHTER_STOPS, -0.5, 0.5)), None)]


def build_bomber():
    v = mesh_sdf(bomber_sdf, (-0.72, -0.54, -0.23), (0.72, 0.54, 0.34), 0.0046, 4900)
    return [("Body", vertex_colored(*v, gradient_y(BOMBER_STOPS, -0.5, 0.5)), None)]


# --------------------------------------------------------------------------------- balloons
GORES = 12
SEGS_PER_GORE = 5
ENVELOPE_PROFILE = [
    # (radius, height) from the crown down to the mouth, classic inverted teardrop.
    (0.000, 1.000), (0.075, 0.994), (0.150, 0.975), (0.225, 0.945), (0.290, 0.905), (0.345, 0.855),
    (0.385, 0.800), (0.410, 0.740), (0.418, 0.680), (0.410, 0.620), (0.385, 0.555), (0.345, 0.490),
    (0.295, 0.430), (0.240, 0.375), (0.185, 0.325), (0.135, 0.285), (0.100, 0.258), (0.082, 0.240),
    (0.084, 0.220), (0.060, 0.215), (0.000, 0.215),
]


def resample_profile(pts, n_sub=2):
    """Catmull-Rom through the profile rows, for a rounder lathe outline."""
    pts = np.asarray(pts, np.float64)
    out = []
    for i in range(len(pts) - 1):
        p0, p1 = pts[max(i - 1, 0)], pts[i]
        p2, p3 = pts[i + 1], pts[min(i + 2, len(pts) - 1)]
        for k in range(n_sub):
            t = k / n_sub
            out.append(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                              + (3 * p1 - p0 - 3 * p2 + p3) * t ** 3))
    out.append(pts[-1])
    out = np.array(out)
    out[:, 0] = np.maximum(out[:, 0], 0.0)
    return out


def lathe_envelope(color_at):
    """Lathed envelope with scalloped gores. color_at(gore_index, height_01) -> sRGB array."""
    prof = resample_profile(ENVELOPE_PROFILE, 2)
    # Keep the crown, belly and mouth rows; thin the long straight run so the budget goes on curve.
    keep = [i for i in range(len(prof)) if i < 14 or i % 2 == 0 or i >= len(prof) - 6]
    prof = prof[keep]
    n_lon = GORES * SEGS_PER_GORE
    rows = len(prof)
    verts = []
    # Row 0 and the last row are single pole vertices.
    pole_top = 0
    verts.append([0.0, 0.0, prof[0, 1]])
    grid = np.zeros((rows - 2, n_lon), np.int64)
    idx = 1
    for r in range(1, rows - 1):
        radius, z = prof[r]
        # Puff is strongest around the belly and fades to the crown and mouth.
        fade = smoothstep(0.26, 0.42, z) * (1.0 - smoothstep(0.90, 0.995, z))
        for j in range(n_lon):
            th = 2 * math.pi * j / n_lon
            phase = (j % SEGS_PER_GORE) / SEGS_PER_GORE
            puff = math.sin(math.pi * phase) ** 0.7
            rr = radius * (1.0 - 0.075 * fade * (1.0 - puff))
            verts.append([rr * math.sin(th), rr * math.cos(th), z])
            grid[r - 1, j] = idx
            idx += 1
    pole_bot = idx
    verts.append([0.0, 0.0, prof[-1, 1]])
    verts = np.array(verts, np.float64)

    tris = []
    gore = []
    for j in range(n_lon):
        j1 = (j + 1) % n_lon
        g = j / SEGS_PER_GORE     # fractional: integer part is the gore, fraction the column in it
        tris.append([pole_top, grid[0, j], grid[0, j1]])
        gore.append(g)
        for r in range(rows - 3):
            a, b = grid[r, j], grid[r, j1]
            c, d = grid[r + 1, j], grid[r + 1, j1]
            tris.append([a, c, d])
            gore.append(g)
            tris.append([a, d, b])
            gore.append(g)
        tris.append([grid[rows - 3, j], pole_bot, grid[rows - 3, j1]])
        gore.append(g)
    tris = np.array(tris, np.int64)
    gore = np.array(gore, np.float64)

    # Area weighted vertex normals. Facing is fixed afterwards.
    a, b, c = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
    fn = np.cross(b - a, c - a)
    center = np.array([0.0, 0.0, 0.62])
    if np.mean(np.sum(fn * ((a + b + c) / 3 - center), axis=1)) < 0:
        tris = tris[:, ::-1]
        fn = -fn
    normals = np.zeros_like(verts)
    for k in range(3):
        np.add.at(normals, tris[:, k], fn)
    normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-9)

    zc = verts[tris][:, :, 2]
    colors = np.zeros(tris.shape + (3,))
    for k in range(3):
        colors[:, k] = color_at(gore, zc[:, k])
    return Part(verts, tris, normals, colors)


def strut_sdf(p, top_r=0.075, bottom_r=0.085, top_z=0.232, bottom_z=0.115, s=0.75):
    m = mirror_x(p)
    q = np.stack([m[..., 0], np.abs(p[..., 1]), p[..., 2]], axis=-1)
    k = top_r * s
    return capsule(q, (k, k, top_z), (bottom_r * s, bottom_r * s, bottom_z), 0.017, 0.019)


def basket_sdf(p):
    d = rounded_box(p, (0, 0, 0.065), (0.092, 0.092, 0.066), 0.034)
    rim = rounded_box(p, (0, 0, 0.122), (0.102, 0.102, 0.016), 0.015)
    d = smin(d, rim, 0.005)
    return smin(d, strut_sdf(p), 0.006)


def basket_color(rim_hex, body_hex, strut_hex):
    # A smooth ramp: hard thresholds on a decimated mesh leave a jagged band.
    return lambda v: ramp(v[:, 2], [(0.095, body_hex), (0.112, rim_hex), (0.136, rim_hex), (0.152, strut_hex)])


def bomb_sdf(p):
    # A big round bomb: the dominant read of this balloon at a glance.
    d = sphere(p, (0, 0, 0.092), 0.108)
    collar = capsule(p, (0, 0, 0.19), (0, 0, 0.205), 0.042)
    d = smin(d, collar, 0.012)
    band = np.maximum(np.abs(p[..., 2] - 0.092) - 0.016, sphere(p, (0, 0, 0.092), 0.116))
    d = smin(d, band, 0.004)
    # Stubby fuse leaning out towards the front, so the round basket reads as a bomb.
    fuse = capsule(p, (0, -0.075, 0.155), (0, -0.128, 0.205), 0.017, 0.015)
    d = smin(d, fuse, 0.008)
    return smin(d, strut_sdf(p, top_r=0.075, bottom_r=0.055, bottom_z=0.17, s=0.8), 0.008)


def bomb_color(v):
    c = np.tile(hex_srgb("#3F100E"), (len(v), 1))
    band = (1.0 - smoothstep(0.013, 0.021, np.abs(v[:, 2] - 0.092)))[:, None]
    c = c * (1 - band) + hex_srgb("#E5483B") * band
    tip = np.linalg.norm(v - np.array([0.0, -0.128, 0.205]), axis=1)
    k = (1.0 - smoothstep(0.022, 0.034, tip))[:, None]
    return c * (1 - k) + hex_srgb("#F3E5B8") * k


def build_balloon(stops, stripes=None, bomb=False, alt=None, basket=None):
    """stops: envelope ramp from the mouth (0) to the crown (1). stripes: (every, hex, amount)."""
    def env_color(gore, z):
        t = np.clip((z - 0.215) / (1.0 - 0.215), 0.0, 1.0)
        c = ramp(t, stops)
        whole = np.floor(gore + 1e-6)
        frac = gore - whole
        if alt is not None:
            mask = (whole % 2 == 1)[..., None]
            c = np.where(mask, c * alt, c)
        if stripes is not None:
            # A stripe runs down the middle three columns of every n-th gore, with crisp edges.
            every, col, amt = stripes
            mask = ((whole % every == 0) & (frac > 0.15) & (frac < 0.75))[..., None]
            c = np.where(mask, c * (1 - amt) + hex_srgb(col) * amt, c)
        return c

    env = lathe_envelope(env_color)
    if bomb:
        v = mesh_sdf(bomb_sdf, (-0.14, -0.17, -0.04), (0.14, 0.14, 0.25), 0.0034, 1400)
        low = vertex_colored(*v, bomb_color)
    else:
        v = mesh_sdf(basket_sdf, (-0.13, -0.13, -0.03), (0.13, 0.13, 0.25), 0.0035, 1000)
        low = vertex_colored(*v, basket_color(*basket))
    return [("Body", concat([env, low]), None)]


def build_shield():
    return build_balloon([(0.0, "#2F7FD6"), (0.45, "#5E9FE4"), (1.0, "#BFE3FF")],
                         stripes=(2, "#FBF2DC", 0.30),
                         basket=("#D8B98A", "#F0DDB6", "#F0DDB6"))


def build_bomb():
    return build_balloon([(0.0, "#7A1F1A"), (0.55, "#B9352B"), (1.0, "#E5483B")],
                         bomb=True, alt=0.90)


def build_storm():
    return build_balloon([(0.0, "#4B4668"), (0.55, "#696388"), (1.0, "#8A84A8")],
                         alt=0.90, basket=("#3A3652", "#5A5578", "#5A5578"))


BUILDERS = {
    "PlayerPlane": build_player,
    "EnemyFighter": build_fighter,
    "EnemyBomber": build_bomber,
    "BalloonShield": build_shield,
    "BalloonBomb": build_bomb,
    "BalloonStorm": build_storm,
}


# ----------------------------------------------------------------------------- scene, export, QA
def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights, bpy.data.worlds):
        for x in list(coll):
            coll.remove(x)


def get_material():
    mat = bpy.data.materials.get("M_VertexColor")
    if mat:
        return mat
    mat = bpy.data.materials.new("M_VertexColor")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    col = nt.nodes.new("ShaderNodeVertexColor")
    col.layer_name = "Col"
    nt.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.62
    return mat


def part_to_object(name, part, origin):
    mesh = bpy.data.meshes.new(name)
    verts = part.verts - origin
    mesh.from_pydata(verts.tolist(), [], part.tris.tolist())
    mesh.update()
    mesh.shade_smooth()
    # Normals from the field (or area weighted, for lathes), stored as custom normals.
    mesh.normals_split_custom_set_from_vertices(part.normals.tolist())
    attr = mesh.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    lin = to_linear(np.clip(part.colors, 0, 1))
    rgba = np.concatenate([lin, np.ones(lin.shape[:2] + (1,))], axis=2)
    # from_pydata keeps triangle and corner order: 3 corners per face in face order.
    attr.data.foreach_set("color", rgba.reshape(-1).astype(np.float32))
    mesh.materials.append(get_material())
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def mesh_report(part):
    m = bpy.data.meshes.new("chk")
    m.from_pydata(part.verts.tolist(), [], part.tris.tolist())
    bm = bmesh.new()
    bm.from_mesh(m)
    boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    multi = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bm.free()
    bpy.data.meshes.remove(m)
    return "%d open, %d multi-face" % (boundary, multi)


def assemble(defs):
    """Pivot at the bounding box centre of the whole model; children keep their own hub origin."""
    allv = np.concatenate([d[1].verts + (0 if d[2] is None else np.array(d[2])) for d in defs])
    center = 0.5 * (allv.min(axis=0) + allv.max(axis=0))
    objs = {}
    root = None
    for name, part, hub in defs:
        if hub is None:
            obj = part_to_object(name, part, center)
            obj.location = (0, 0, 0)
            root = obj
        else:
            obj = part_to_object(name, part, np.zeros(3))
            obj.parent = root
            obj.location = tuple(np.array(hub) - center)
        objs[name] = obj
    return root, objs, allv.max(axis=0) - allv.min(axis=0)


def export_fbx(path, objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs.values():
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs["Body"]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        apply_scale_options="FBX_SCALE_ALL",
        colors_type="SRGB",
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
        use_selection=True,
    )


def setup_render(res=(1100, 800)):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.86, 0.84, 0.82, 1.0)
    bg.inputs[1].default_value = 0.55
    scene.world = world
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 1.9
    sun.data.angle = math.radians(28.0)
    sun.data.color = (1.0, 0.96, 0.9)
    sun.rotation_euler = (math.radians(38), math.radians(8), math.radians(-30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


def add_floor(z):
    mat = bpy.data.materials.new("paper")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (0.93, 0.87, 0.80, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.9
    mesh = bpy.data.meshes.new("floor")
    s = 40.0
    mesh.from_pydata([(-s, -s, z), (s, -s, z), (s, s, z), (-s, s, z)], [], [(0, 1, 2, 3)])
    mesh.materials.append(mat)
    obj = bpy.data.objects.new("floor", mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def aim(cam, loc, target, ortho=None):
    from mathutils import Vector
    cam.location = loc
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    cam.data.type = "ORTHO" if ortho else "PERSP"
    if ortho:
        cam.data.ortho_scale = ortho
    else:
        cam.data.lens = 105


def render_views(name, size, is_balloon):
    cam = setup_render()
    scene = bpy.context.scene
    floor_z = -size[2] / 2 - (0.06 if not is_balloon else 0.02)
    floor = add_floor(floor_z)
    tmp = os.path.join(PREVIEW_DIR, "_tmp_%s_%%s.png" % name)
    span = float(max(size))
    views = {}
    # Same 3/4 front-left view as aircraft-studies.png: nose to the lower left of the frame.
    if is_balloon:
        loc = (0.9 * span * 1.9, -2.2 * span * 1.9, 0.55 * span * 1.9)
    else:
        loc = (1.15 * span * 1.75, -1.55 * span * 1.75, 1.15 * span * 1.75)
    aim(cam, loc, (0, 0, -0.01))
    scene.render.filepath = tmp % "a"
    bpy.ops.render.render(write_still=True)
    views["a"] = read_png(tmp % "a")
    if is_balloon:
        aim(cam, (0, -5, 0), (0, 0, 0), ortho=span * 1.25)
    else:
        aim(cam, (0, 0, 5), (0, 0, 0), ortho=span * 1.25)
    floor.hide_render = not is_balloon and False
    scene.render.filepath = tmp % "b"
    bpy.ops.render.render(write_still=True)
    views["b"] = read_png(tmp % "b")
    if is_balloon:
        aim(cam, (0, 0, -5), (0, 0, 0), ortho=span * 1.25)
    else:
        aim(cam, (0, -5, 0.4), (0, 0, 0), ortho=span * 1.25)
    floor.hide_render = True
    scene.render.filepath = tmp % "c"
    bpy.ops.render.render(write_still=True)
    views["c"] = read_png(tmp % "c")
    for k in "abc":
        os.remove(tmp % k)

    # Contact sheet: hero left, plan and front right, then the hero at 96, 48 and 24 px on a mid tone.
    a = views["a"]
    H, W = a.shape[:2]
    small_h = H // 2
    small_w = int(W * 0.5)
    b = np.clip(resize(views["b"], small_h, small_w), 0, 1)
    c = np.clip(resize(views["c"], small_h, small_w), 0, 1)
    sheet = np.ones((H + 110, W + small_w, 4), np.float32)
    sheet[:, :] = (0.32, 0.30, 0.29, 1.0)
    sheet[:H, :W] = a
    sheet[:small_h, W:] = b
    sheet[small_h:small_h * 2, W:] = c
    x = 12
    for px in (96, 48, 24):
        crop = a[H // 2 - min(H, W) // 2:H // 2 + min(H, W) // 2,
                 W // 2 - min(H, W) // 2:W // 2 + min(H, W) // 2]
        th = np.clip(resize(crop, px, px), 0, 1)
        sheet[H + 7:H + 7 + px, x:x + px] = th
        x += px + 16
    write_png(os.path.join(PREVIEW_DIR, name + ".png"), sheet)


def build_one(name):
    clear_scene()
    defs = BUILDERS[name]()
    tri_total = 0
    for pname, part, _hub in defs:
        bad = mesh_report(part)
        tri_total += len(part.tris)
        print("  %s/%s: %d verts, %d tris, edges: %s" % (
            name, pname, len(part.verts), len(part.tris), bad))
    root, objs, size = assemble(defs)
    bpy.context.view_layer.update()
    path = os.path.join(OUT_DIR, name + ".fbx")
    export_fbx(path, objs)
    print("%s: %d tris total, size %s, wrote %s" % (name, tri_total, np.round(size, 3), path))
    render_views(name, size, name.startswith("Balloon"))


def main():
    argv = sys.argv
    names = argv[argv.index("--") + 1:] if "--" in argv else list(BUILDERS)
    for n in names:
        build_one(n)


if __name__ == "__main__":
    main()
