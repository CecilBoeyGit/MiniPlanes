"""Convert one Tripo download (FBX or GLB) so it matches the model contract, and optionally install it.

Run:
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup -P Tools/Tripo/install_tripo.py -- \
      --asset PlayerPlane --input Tools/Tripo/runs/PlayerPlane/model.glb [--rotate 0 0 0] [--max-tris 6000] [--install]

Without --install it writes Tools/Tripo/runs/<Asset>/<Asset>_converted.fbx and the preview
Tools/Art/previews/<Asset>_tripo.png, and leaves the Blender stand-in alone. Compare the preview with
the concept first. With --install it writes Assets/MiniPlanes/Art/Models/<Asset>.fbx and
Assets/MiniPlanes/Art/Models/Textures/<Asset>_BaseColor.png.

What it does: joins all meshes into one object named "Body", applies --rotate so the model has nose -Y
and up +Z (balloons: basket down), reduces to --max-tris, moves the origin to the bounding box centre,
applies all transforms, saves the base colour image as a PNG, and exports with the contract FBX settings.
Tripo models are one piece, so there is no separate "Propeller" object.
"""
import argparse
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "Tools", "Art"))
import build_models  # noqa: E402  (its main() only runs when it is the script being executed)


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--asset", required=True)
    ap.add_argument("--input", required=True)
    ap.add_argument("--rotate", type=float, nargs=3, default=(0.0, 0.0, 0.0),
                    help="XYZ degrees applied after import to reach nose -Y, up +Z")
    ap.add_argument("--max-tris", type=int, default=6000)
    ap.add_argument("--install", action="store_true")
    ap.add_argument("--out-dir", default=None, help="override the conversion folder (used for tests)")
    return ap.parse_args(argv)


def import_model(path):
    ext = os.path.splitext(path)[1].lower()
    if ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    else:
        raise SystemExit("Unsupported input: " + path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("No mesh in " + path)
    return meshes


def bake_world(meshes):
    """Move every mesh's world transform into its vertices and drop parents, armatures and empties."""
    for o in meshes:
        o.data = o.data.copy()
        o.data.transform(o.matrix_world)
        o.data.update()
    for o in meshes:
        o.parent = None
        o.matrix_world = Matrix.Identity(4)
    for o in list(bpy.context.scene.objects):
        if o.type != "MESH":
            bpy.data.objects.remove(o)


def join(meshes):
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = "Body"
    body.data.name = "Body"
    return body


def tri_count(mesh):
    mesh.calc_loop_triangles()
    return len(mesh.loop_triangles)


def reduce_tris(body, limit):
    n = tri_count(body.data)
    if n <= limit:
        return
    mod = body.modifiers.new("dec", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = limit / n
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier=mod.name)


def center_on_bounds(body):
    v = np.empty(len(body.data.vertices) * 3)
    body.data.vertices.foreach_get("co", v)
    v = v.reshape(-1, 3)
    c = 0.5 * (v.min(axis=0) + v.max(axis=0))
    body.data.transform(Matrix.Translation(Vector(-c)))
    body.data.update()
    return v.max(axis=0) - v.min(axis=0)


def save_base_color(body, asset, out_dir):
    """Save the first material's base colour image; returns its path or None for untextured models."""
    for slot in body.material_slots:
        mat = slot.material
        if not mat or not mat.node_tree:
            continue
        bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None or not bsdf.inputs["Base Color"].is_linked:
            continue
        node = bsdf.inputs["Base Color"].links[0].from_node
        if node.type != "TEX_IMAGE" or node.image is None:
            continue
        os.makedirs(out_dir, exist_ok=True)
        path = os.path.join(out_dir, asset + "_BaseColor.png")
        img = node.image
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        return path
    return None


def main():
    args = parse_args()
    in_path = os.path.abspath(args.input)
    models_dir = os.path.join(ROOT, "Assets", "MiniPlanes", "Art", "Models")
    run_dir = args.out_dir or os.path.join(ROOT, "Tools", "Tripo", "runs", args.asset)
    if args.install:
        fbx_path = os.path.join(models_dir, args.asset + ".fbx")
        tex_dir = os.path.join(models_dir, "Textures")
    else:
        fbx_path = os.path.join(run_dir, args.asset + "_converted.fbx")
        tex_dir = os.path.join(run_dir, "Textures")

    build_models.clear_scene()
    meshes = import_model(in_path)
    bake_world(meshes)
    body = join(meshes)
    rot = Euler([math.radians(a) for a in args.rotate], "XYZ").to_matrix().to_4x4()
    body.data.transform(rot)
    body.data.update()
    reduce_tris(body, args.max_tris)
    size = center_on_bounds(body)
    body.data.shade_smooth()
    tex = save_base_color(body, args.asset, tex_dir)
    bpy.context.view_layer.update()
    os.makedirs(os.path.dirname(fbx_path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    build_models.export_fbx(fbx_path, {"Body": body})
    print("%s: %d tris, size %s, texture %s, wrote %s" % (
        args.asset, tri_count(body.data), np.round(size, 3), tex, fbx_path))
    build_models.render_views(args.asset + "_tripo", size, args.asset.startswith("Balloon"))


main()
