"""Read the exported FBX files back at the file level and print what Unity will receive.

Run:
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup -P Tools/Art/verify_fbx.py

Uses Blender's FBX parser only, without importing into a scene, so no axis conversion hides anything.
FBX is right handed: nose should be +Z, up +Y. Unity negates X on import, so the FBX -X side becomes
Unity's +X (right wing).
"""
import array
import os

import bpy  # noqa: F401
import numpy as np
from io_scene_fbx import parse_fbx

HERE = os.path.dirname(os.path.abspath(__file__))
MODELS = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "MiniPlanes", "Art", "Models"))


def child(elem, name):
    for e in elem.elems:
        if e.id == name.encode():
            return e
    return None


def children(elem, name):
    return [e for e in elem.elems if e.id == name.encode()]


def arr(elem):
    a = elem.props[0]
    return np.array(a) if isinstance(a, array.array) else np.array(list(a))


def props70(elem):
    out = {}
    p70 = child(elem, "Properties70")
    if p70 is None:
        return out
    for p in p70.elems:
        out[p.props[0].decode()] = p.props[4:]
    return out


def report(path):
    root, _version = parse_fbx.parse(path)
    gs = props70(child(root, "GlobalSettings"))
    print("%s (%d KB)" % (os.path.basename(path), os.path.getsize(path) // 1024))
    print("  axes up=%s front=%s coord=%s unit=%s" % (
        gs.get("UpAxis"), gs.get("FrontAxis"), gs.get("CoordAxis"), gs.get("UnitScaleFactor")))
    objects = child(root, "Objects")
    geoms = {g.props[0]: g for g in children(objects, "Geometry")}
    models = children(objects, "Model")
    conns = [c.props for c in child(root, "Connections").elems]
    names = {m.props[0]: m.props[1].split(b"\x00")[0].decode() for m in models}
    for m in models:
        uid = m.props[0]
        mp = props70(m)
        parent = [c[2] for c in conns if c[1] == uid and c[2] in names]
        print("  object %-10s parent=%-5s T=%s R=%s S=%s" % (
            names[uid], names[parent[0]] if parent else "root",
            np.round(mp.get("Lcl Translation", (0, 0, 0)), 4).tolist(),
            np.round(mp.get("Lcl Rotation", (0, 0, 0)), 3).tolist(),
            np.round(mp.get("Lcl Scaling", (1, 1, 1)), 4).tolist()))
        gid = [c[1] for c in conns if c[2] == uid and c[1] in geoms]
        if not gid:
            continue
        g = geoms[gid[0]]
        v = arr(child(g, "Vertices")).reshape(-1, 3)
        idx = arr(child(g, "PolygonVertexIndex"))
        tris = int(np.count_nonzero(idx < 0))
        lo, hi = v.min(axis=0), v.max(axis=0)
        col = child(g, "LayerElementColor")
        info = "no colours"
        if col is not None:
            c = arr(child(col, "Colors")).reshape(-1, 4)
            info = "colours %d, sRGB range %.3f..%.3f, map %s" % (
                len(c), c[:, :3].min(), c[:, :3].max(),
                child(col, "MappingInformationType").props[0].decode())
        normals = child(g, "LayerElementNormal")
        # Which end carries the bulk? A simple check that the nose (narrow plan end) is +Z.
        print("    tris %d, bounds x[%.3f %.3f] y[%.3f %.3f] z[%.3f %.3f], normals %s, %s" % (
            tris, lo[0], hi[0], lo[1], hi[1], lo[2], hi[2], "yes" if normals is not None else "NO", info))


for name in ("PlayerPlane", "EnemyFighter", "EnemyBomber", "BalloonShield", "BalloonBomb", "BalloonStorm"):
    report(os.path.join(MODELS, name + ".fbx"))
