"""Cut the frozen propellers out of a baked twin-engine model so the game can spin them.

Tripo bakes propellers into the body mesh. This finds the two propeller discs in front of the engine
nacelles, separates each into its own object named PropellerL / PropellerR with its pivot on the hub, and
writes the model back. Any object whose name starts with "Propeller" spins in the game.

Run:
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup -P Tools/Tripo/split_propellers.py -- \
      --asset EnemyBomber [--depth 0.05] [--radius 0.09] [--install]

Without --install it writes runs/<Asset>/<Asset>_props.fbx so the result can be checked first.
"""
import argparse
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "Tools", "Art"))
import build_models  # noqa: E402


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--asset", required=True)
    ap.add_argument("--depth", type=float, default=0.05, help="propeller slice depth, as a fraction of the model's length")
    ap.add_argument("--radius", type=float, default=0.09, help="propeller radius, as a fraction of the wingspan")
    ap.add_argument("--side-min", type=float, default=0.12, help="ignore the fuselage: |x| below this fraction of the span")
    ap.add_argument("--hub-radius", type=float, default=0.025, help="around the hub, take everything (the spinner), as a fraction of the span")
    ap.add_argument("--min-green", type=float, default=0.55, help="beyond the hub, take only texels this green or more: the cream blades")
    ap.add_argument("--install", action="store_true")
    return ap.parse_args(argv)


def main():
    args = parse()
    models = os.path.join(ROOT, "Assets", "MiniPlanes", "Art", "Models")
    src = os.path.join(models, args.asset + ".fbx")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if any(o.name.startswith("Propeller") for o in meshes):
        raise SystemExit("%s already has propeller objects" % args.asset)
    body = max(meshes, key=lambda o: len(o.data.vertices))
    body.name = "Body"

    # Work in the bake's own space: nose -Y, up +Z, no object transform.
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    co = np.array([tuple(v.co) for v in body.data.vertices])
    mn, mx = co.min(0), co.max(0)
    span, length = mx[0] - mn[0], mx[1] - mn[1]
    side = co[np.abs(co[:, 0]) > args.side_min * span]
    front_y = side[:, 1].min()
    slice_back = front_y + args.depth * length
    radius = args.radius * span

    hubs = {}
    for name, sign in (("PropellerL", -1), ("PropellerR", 1)):
        f = side[(np.sign(side[:, 0]) == sign) & (side[:, 1] < slice_back)]
        if len(f) == 0:
            raise SystemExit("no propeller found on side %d" % sign)
        hubs[name] = Vector(((f[:, 0].min() + f[:, 0].max()) / 2, f[:, 1].mean(), (f[:, 2].min() + f[:, 2].max()) / 2))
        print("%s hub %s" % (name, tuple(round(v, 4) for v in hubs[name])))

    texture = None
    tex_path = os.path.join(models, args.asset + ".png")
    if os.path.exists(tex_path):
        img = bpy.data.images.load(tex_path)
        tw, th = img.size
        texture = np.array(img.pixels[:], dtype=np.float32).reshape(th, tw, 4)

    objs = {"Body": body}
    for name, hub in hubs.items():
        bpy.ops.object.select_all(action="DESELECT")
        body.select_set(True)
        bpy.context.view_layer.objects.active = body
        bpy.ops.object.mode_set(mode="EDIT")
        bm = bmesh.from_edit_mesh(body.data)
        count = 0
        uv = bm.loops.layers.uv.active
        for face in bm.faces:
            c = face.calc_center_median()
            radial = ((c.x - hub.x) ** 2 + (c.z - hub.z) ** 2) ** 0.5
            inside = c.y < slice_back and radial < radius and abs(c.x) > args.side_min * 0.75 * span
            if inside and texture is not None and uv is not None and radial > args.hub_radius * span:
                # Away from the hub, take only the cream blades, not the coral nacelle behind them.
                u = sum(l[uv].uv.x for l in face.loops) / len(face.loops)
                v = sum(l[uv].uv.y for l in face.loops) / len(face.loops)
                px = texture[min(th - 1, max(0, int(v * th))), min(tw - 1, max(0, int(u * tw)))]
                inside = px[1] > args.min_green
            face.select = bool(inside)
            count += face.select
        bmesh.update_edit_mesh(body.data)
        bpy.ops.mesh.separate(type="SELECTED")
        bpy.ops.object.mode_set(mode="OBJECT")
        part = next(o for o in bpy.context.selected_objects if o is not body)
        part.name = name
        part.data.name = name
        bpy.context.scene.cursor.location = hub
        bpy.ops.object.select_all(action="DESELECT")
        part.select_set(True)
        bpy.context.view_layer.objects.active = part
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
        objs[name] = part
        print("%s: %d faces" % (name, count))

    out = src if args.install else os.path.join(ROOT, "Tools", "Tripo", "runs", args.asset, args.asset + "_props.fbx")
    build_models.export_fbx(out, objs)
    print("wrote", out)


main()
