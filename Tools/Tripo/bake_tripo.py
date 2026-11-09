"""Turn one raw Tripo GLB (about 2 million triangles and a 4K texture) into a game model for this project.

Run:
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup -P Tools/Tripo/bake_tripo.py -- \
      --asset PlayerPlane --input Tools/Tripo/runs/PlayerPlane/model.glb --rotate 0 0 90 \
      --cut-front 0.085 --propeller [--install]

What it does, in order:
  1. Applies --rotate, so the model has nose -Y and up +Z (balloons: basket down).
  2. Works on a welded copy with no UVs: a first decimation, then a smoothing pass that removes AI surface lumps too
     small to read on a 4 cm toy, then a final decimation to --max-tris. Welding first stops the UV seams
     opening into visible gaps.
  3. Bakes colour from the original textured mesh into vertex colours, using the median of a small patch around
     each vertex, which removes the atlas gaps. The result needs no texture at all, which suits the game's Matte shader and Quest memory.
     With --texture N it paints an N px texture instead, for detail vertices can't hold, like wing stripes.
     Image-generated models need this. The FBX then has white vertex colours, and the texture sits beside it.
  4. Optionally slices off the model's frozen propeller (--cut-front, as a fraction of its length) and fits the
     spinning "Propeller" object from Tools/Art/standins/PlayerPlane.fbx (--propeller).

Without --install it writes runs/<Asset>/<Asset>_baked.fbx and previews/<Asset>_tripo.png only, so a bad result
cannot replace a working model. With --install it writes Assets/MiniPlanes/Art/Models/<Asset>.fbx (and .png).
"""
import argparse
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "Tools", "Art"))
import build_models  # noqa: E402  (its main() only runs when it is the script being executed)


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--asset", required=True)
    ap.add_argument("--input", required=True)
    ap.add_argument("--rotate", type=float, nargs=3, default=(0.0, 0.0, 0.0))
    ap.add_argument("--max-tris", type=int, default=5000)
    ap.add_argument("--smooth", type=int, default=14, help="smoothing iterations")
    ap.add_argument("--cut-front", type=float, default=0.0, help="slice this fraction of the length off the nose")
    ap.add_argument("--propeller", action="store_true", help="fit the spinning propeller from the stand-in")
    ap.add_argument("--prop-span", type=float, default=0.36, help="propeller span as a fraction of wingspan")
    ap.add_argument("--texture", type=int, default=0, help="bake a colour texture this size instead of vertex colours")
    ap.add_argument("--install", action="store_true")
    return ap.parse_args(argv)


def import_joined(path):
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("No mesh in " + path)
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
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def base_color_pixels(obj):
    for slot in obj.material_slots:
        mat = slot.material
        if not mat or not mat.node_tree:
            continue
        for node in mat.node_tree.nodes:
            if node.type != "BSDF_PRINCIPLED" or not node.inputs["Base Color"].is_linked:
                continue
            tex = node.inputs["Base Color"].links[0].from_node
            if tex.type == "TEX_IMAGE" and tex.image is not None:
                w, h = tex.image.size
                pix = np.empty(w * h * 4, np.float32)
                tex.image.pixels.foreach_get(pix)
                return pix.reshape(h, w, 4)[:, :, :3], w, h
    raise SystemExit("The model has no base colour texture")


def apply_modifier(obj, kind, **props):
    mod = obj.modifiers.new("m", kind)
    for k, v in props.items():
        setattr(mod, k, v)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


def tris(mesh):
    mesh.calc_loop_triangles()
    return len(mesh.loop_triangles)


def decimate_to(obj, limit):
    n = tris(obj.data)
    if n > limit:
        apply_modifier(obj, "DECIMATE", decimate_type="COLLAPSE", ratio=limit / n)


def weld(obj):
    """GLB vertices are split along UV seams, leaving disconnected patches that decimation would pull apart."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    size = max(obj.dimensions)
    before = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=size * 2e-5)
    print("Welded %d vertices down to %d" % (before, len(bm.verts)))
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def cut_nose(obj, fraction):
    """Removes everything in front of a plane near the nose (-Y) and caps the hole. Returns the cut's Y."""
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    ys = [v.co.y for v in bm.verts]
    y0, y1 = min(ys), max(ys)
    cut = y0 + (y1 - y0) * fraction
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    result = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, cut, 0), plane_no=(0, 1, 0), clear_inner=True)
    new_edges = [e for e in result["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    bmesh.ops.holes_fill(bm, edges=new_edges, sides=0)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return cut


def bake_colors(target, source, pixels, w, h):
    """Sets target's corner colours from the texture on source, sampling at vertices and face centres."""
    src = source.data
    src.calc_loop_triangles()
    n_tris = len(src.loop_triangles)
    verts = np.empty(len(src.vertices) * 3, np.float64)
    src.vertices.foreach_get("co", verts)
    verts = verts.reshape(-1, 3)
    tri_verts = np.empty(n_tris * 3, np.int32)
    src.loop_triangles.foreach_get("vertices", tri_verts)
    tri_verts = tri_verts.reshape(-1, 3)
    tri_loops = np.empty(n_tris * 3, np.int32)
    src.loop_triangles.foreach_get("loops", tri_loops)
    tri_loops = tri_loops.reshape(-1, 3)
    uv_attr = src.uv_layers.active.uv
    uvs = np.empty(len(uv_attr) * 2, np.float32)
    uv_attr.foreach_get("vector", uvs)
    uvs = uvs.reshape(-1, 2)

    print("Building the search tree over %d triangles" % n_tris)
    tree = BVHTree.FromPolygons(verts.tolist(), tri_verts.tolist(), all_triangles=True)

    def sample(point):
        loc, _, index, _ = tree.find_nearest(point)
        if index is None:
            return np.array([0.5, 0.5, 0.5])
        a, b, c = verts[tri_verts[index]]
        p = np.array(loc)
        v0, v1, v2 = b - a, c - a, p - a
        d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
        d20, d21 = v2.dot(v0), v2.dot(v1)
        denom = d00 * d11 - d01 * d01
        if abs(denom) < 1e-18:
            bary = np.array([1.0, 0.0, 0.0])
        else:
            v = (d11 * d20 - d01 * d21) / denom
            u = (d00 * d21 - d01 * d20) / denom
            bary = np.array([1.0 - v - u, v, u])
        uv = (uvs[tri_loops[index]] * bary[:, None]).sum(axis=0)
        x = int(np.clip(uv[0] % 1.0, 0, 0.99999) * w)
        y = int(np.clip(uv[1] % 1.0, 0, 0.99999) * h)
        return pixels[y, x]

    mesh = target.data
    mesh.calc_loop_triangles()
    mesh.update()
    co = np.empty(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    radius = 0.014 * float((co.max(axis=0) - co.min(axis=0)).max())
    print("Sampling colours for %d vertices" % len(mesh.vertices))

    # Tripo's atlas has dark and pale gaps between its UV islands. Sampling a small patch around each vertex
    # and taking the median votes those thin lines out.
    colors = np.zeros((len(mesh.vertices), 3))
    for v in mesh.vertices:
        n = v.normal
        t = n.orthogonal().normalized()
        u = n.cross(t).normalized()
        samples = [sample(v.co)]
        for ring, count in ((0.5, 4), (1.0, 8)):
            for k in range(count):
                a = 2.0 * math.pi * k / count
                samples.append(sample(v.co + (t * math.cos(a) + u * math.sin(a)) * (radius * ring)))
        colors[v.index] = np.median(np.array(samples), axis=0)

    # One gentle pass over neighbours, for an even gradient.
    edges = np.empty(len(mesh.edges) * 2, np.int32)
    mesh.edges.foreach_get("vertices", edges)
    edges = edges.reshape(-1, 2)
    total = np.zeros_like(colors)
    count = np.zeros(len(colors))
    np.add.at(total, edges[:, 0], colors[edges[:, 1]])
    np.add.at(total, edges[:, 1], colors[edges[:, 0]])
    np.add.at(count, edges[:, 0], 1)
    np.add.at(count, edges[:, 1], 1)
    colors = 0.5 * colors + 0.5 * total / np.maximum(count, 1)[:, None]

    attr = mesh.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    loop_verts = np.empty(len(mesh.loops), np.int32)
    mesh.loops.foreach_get("vertex_index", loop_verts)
    lin = build_models.to_linear(np.clip(colors, 0, 1))
    rgba = np.concatenate([lin[loop_verts], np.ones((len(loop_verts), 1))], axis=1)
    attr.data.foreach_set("color", rgba.reshape(-1).astype(np.float32))
    mesh.color_attributes.active_color = attr


TEXTURE_RESERVED = 0.04  # A white strip along the top and right of the texture, for parts coloured by vertex.


def bake_texture(target, source, pixels, w, h, size, png_path):
    """Unwraps target and paints a texture from the original textured mesh, for detail too fine for vertex colours."""
    src = source.data
    src.calc_loop_triangles()
    n_tris = len(src.loop_triangles)
    verts = np.empty(len(src.vertices) * 3, np.float64)
    src.vertices.foreach_get("co", verts)
    verts = verts.reshape(-1, 3)
    tri_verts = np.empty(n_tris * 3, np.int32)
    src.loop_triangles.foreach_get("vertices", tri_verts)
    tri_verts = tri_verts.reshape(-1, 3)
    tri_loops = np.empty(n_tris * 3, np.int32)
    src.loop_triangles.foreach_get("loops", tri_loops)
    tri_loops = tri_loops.reshape(-1, 3)
    uv_attr = src.uv_layers.active.uv
    uvs = np.empty(len(uv_attr) * 2, np.float32)
    uv_attr.foreach_get("vector", uvs)
    uvs = uvs.reshape(-1, 2)
    print("Building the search tree over %d triangles" % n_tris)
    tree = BVHTree.FromPolygons(verts.tolist(), tri_verts.tolist(), all_triangles=True)

    # Unwrap the game mesh, keeping clear of the reserved strip.
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(55.0), island_margin=0.012)
    bpy.ops.object.mode_set(mode="OBJECT")
    mesh = target.data
    layer = mesh.uv_layers.active.uv
    low_uv = np.empty(len(layer) * 2, np.float32)
    layer.foreach_get("vector", low_uv)
    low_uv = low_uv.reshape(-1, 2) * (1.0 - TEXTURE_RESERVED)
    layer.foreach_set("vector", low_uv.reshape(-1))
    mesh.update()

    co = np.empty(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)

    image = np.ones((size, size, 3), np.float32)
    filled = np.zeros((size, size), bool)
    mesh.calc_loop_triangles()
    print("Painting a %d px texture over %d triangles" % (size, len(mesh.loop_triangles)))
    painted = 0
    for tri in mesh.loop_triangles:
        a, b, c = (low_uv[l] * size for l in tri.loops)
        pa, pb, pc = (co[v] for v in tri.vertices)
        denom = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(denom) < 1e-9:
            continue
        x0, x1 = int(max(0, math.floor(min(a[0], b[0], c[0]) - 1))), int(min(size - 1, math.ceil(max(a[0], b[0], c[0]) + 1)))
        y0, y1 = int(max(0, math.floor(min(a[1], b[1], c[1]) - 1))), int(min(size - 1, math.ceil(max(a[1], b[1], c[1]) + 1)))
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                px, py = x + 0.5, y + 0.5
                l0 = ((b[1] - c[1]) * (px - c[0]) + (c[0] - b[0]) * (py - c[1])) / denom
                l1 = ((c[1] - a[1]) * (px - c[0]) + (a[0] - c[0]) * (py - c[1])) / denom
                l2 = 1.0 - l0 - l1
                if l0 < -0.02 or l1 < -0.02 or l2 < -0.02 or filled[y, x]:
                    continue
                point = pa * l0 + pb * l1 + pc * l2
                loc, _, index, _ = tree.find_nearest(point)
                if index is None:
                    continue
                t0, t1, t2 = verts[tri_verts[index]]
                p = np.array(loc)
                v0, v1, v2 = t1 - t0, t2 - t0, p - t0
                d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
                d20, d21 = v2.dot(v0), v2.dot(v1)
                d = d00 * d11 - d01 * d01
                if abs(d) < 1e-18:
                    bary = np.array([1.0, 0.0, 0.0])
                else:
                    v = (d11 * d20 - d01 * d21) / d
                    u = (d00 * d21 - d01 * d20) / d
                    bary = np.array([1.0 - v - u, v, u])
                uv = (uvs[tri_loops[index]] * bary[:, None]).sum(axis=0)
                sx = int(np.clip(uv[0] % 1.0, 0, 0.99999) * w)
                sy = int(np.clip(uv[1] % 1.0, 0, 0.99999) * h)
                image[y, x] = pixels[sy, sx]
                filled[y, x] = True
                painted += 1
    print("Painted %d texels" % painted)

    # Grow each island outward a few texels, so mip levels and seams don't pick up the white background.
    painted_mask = filled.copy()
    limit = int(size * (1.0 - TEXTURE_RESERVED))
    for _ in range(6):
        total = np.zeros_like(image)
        count = np.zeros((size, size), np.float32)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            total += np.roll(np.roll(image * filled[:, :, None], dy, axis=0), dx, axis=1)
            count += np.roll(np.roll(filled.astype(np.float32), dy, axis=0), dx, axis=1)
        grow = (~filled) & (count > 0)
        grow[limit:, :] = False
        grow[:, limit:] = False
        image[grow] = total[grow] / count[grow][:, None]
        filled |= grow

    # Tripo's atlas has thin pale and dark gaps between its islands. A 3x3 median removes those hairlines,
    # and is too small to hurt wing stripes or the canopy frame.
    stack = [np.roll(np.roll(image, dy, axis=0), dx, axis=1) for dy in (-1, 0, 1) for dx in (-1, 0, 1)]
    image[painted_mask] = np.median(np.stack(stack), axis=0)[painted_mask]

    rgba = np.ones((size, size, 4), np.float32)
    rgba[:, :, :3] = np.clip(image, 0.0, 1.0)
    os.makedirs(os.path.dirname(png_path), exist_ok=True)
    build_models.write_png(png_path, rgba[::-1])  # Blender's rows run bottom-up; PNG's run top-down.

    # White vertex colours, so the shared Matte shader shows the texture unchanged.
    attr = mesh.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    attr.data.foreach_set("color", np.ones(len(mesh.loops) * 4, np.float32))
    mesh.color_attributes.active_color = attr


def textured_material(png_path):
    """Only for the preview render. In the game the material comes from the scene builder."""
    mat = bpy.data.materials.new("M_Textured")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(png_path, check_existing=False)
    col = nt.nodes.new("ShaderNodeVertexColor")
    col.layer_name = "Col"
    mix = nt.nodes.new("ShaderNodeVectorMath")
    mix.operation = "MULTIPLY"
    nt.links.new(tex.outputs["Color"], mix.inputs[0])
    nt.links.new(col.outputs["Color"], mix.inputs[1])
    nt.links.new(mix.outputs["Vector"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.62
    return mat


def point_uvs_at_reserved(obj):
    """This part is coloured by its vertices, so all its UVs sit on the texture's white strip."""
    mesh = obj.data
    while mesh.uv_layers:
        mesh.uv_layers.remove(mesh.uv_layers[0])
    layer = mesh.uv_layers.new(name="UVMap")
    spot = 1.0 - TEXTURE_RESERVED * 0.5
    layer.uv.foreach_set("vector", np.full(len(mesh.loops) * 2, spot, np.float32))
    mesh.update()


def fit_propeller(body, span_fraction):
    """Brings in the spinning propeller from the stand-in, sized to this body and placed at its nose."""
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, "Tools", "Art", "standins", "PlayerPlane.fbx"))
    new = [o for o in bpy.context.scene.objects if o not in before]
    prop = next((o for o in new if o.name.startswith("Propeller")), None)
    if prop is None:
        raise SystemExit("The stand-in has no Propeller object")

    prop.parent = None
    world = prop.matrix_world.copy()
    prop.data = prop.data.copy()
    prop.data.transform(world)
    for o in new:
        if o is not prop:
            bpy.data.objects.remove(o)

    co = np.empty(len(prop.data.vertices) * 3)
    prop.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    centre = 0.5 * (co.min(axis=0) + co.max(axis=0))
    old_span = max(co[:, 0].max() - co[:, 0].min(), co[:, 2].max() - co[:, 2].min())

    b = np.empty(len(body.data.vertices) * 3)
    body.data.vertices.foreach_get("co", b)
    b = b.reshape(-1, 3)
    wingspan = b[:, 0].max() - b[:, 0].min()
    y_min = b[:, 1].min()
    nose = b[b[:, 1] < y_min + (b[:, 1].max() - y_min) * 0.01]
    hub = np.array([nose[:, 0].mean(), y_min, nose[:, 2].mean()])

    scale = span_fraction * wingspan / max(old_span, 1e-6)
    prop.data.transform(Matrix.Translation(Vector(-centre)))
    prop.data.transform(Matrix.Scale(scale, 4))
    prop.data.update()
    depth = (co[:, 1].max() - co[:, 1].min()) * scale
    prop.matrix_world = Matrix.Translation(Vector(hub - np.array([0, depth * 0.3, 0])))
    prop.name = "Propeller"
    prop.data.name = "Propeller"
    if not prop.data.materials:
        prop.data.materials.append(build_models.get_material())
    else:
        prop.data.materials[0] = build_models.get_material()
    if prop.data.color_attributes:
        prop.data.color_attributes[0].name = "Col"
    prop.parent = body
    prop.matrix_parent_inverse = Matrix.Identity(4)
    return prop


def main():
    args = parse_args()
    run_dir = os.path.join(ROOT, "Tools", "Tripo", "runs", args.asset)
    models_dir = os.path.join(ROOT, "Assets", "MiniPlanes", "Art", "Models")
    fbx_path = os.path.join(models_dir, args.asset + ".fbx") if args.install else os.path.join(run_dir, args.asset + "_baked.fbx")

    build_models.clear_scene()
    # Claim the material's name now, or the propeller's FBX import takes it and the preview loses vertex colours.
    build_models.get_material()
    source = import_joined(os.path.abspath(args.input))
    rot = Euler([math.radians(a) for a in args.rotate], "XYZ").to_matrix().to_4x4()
    source.data.transform(rot)
    source.data.update()

    body = source.copy()
    body.data = source.data.copy()
    bpy.context.scene.collection.objects.link(body)
    body.name = "Body"
    body.data.name = "Body"
    body.data.materials.clear()
    while body.data.uv_layers:
        body.data.uv_layers.remove(body.data.uv_layers[0])
    for attr in list(body.data.color_attributes):
        body.data.color_attributes.remove(attr)

    pixels, w, h = base_color_pixels(source)

    weld(body)
    decimate_to(body, 80000)
    if args.cut_front > 0.0:
        cut_nose(body, args.cut_front)
    if args.smooth > 0:
        apply_modifier(body, "SMOOTH", factor=0.6, iterations=args.smooth)
    decimate_to(body, args.max_tris)

    png_path = os.path.splitext(fbx_path)[0] + ".png"
    if args.texture > 0:
        bake_texture(body, source, pixels, w, h, args.texture, png_path)
    else:
        bake_colors(body, source, pixels, w, h)

    bpy.data.objects.remove(source)

    # Centre on the bounds, then smooth shade.
    co = np.empty(len(body.data.vertices) * 3)
    body.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    body.data.transform(Matrix.Translation(Vector(-0.5 * (co.min(axis=0) + co.max(axis=0)))))
    body.data.materials.append(textured_material(png_path) if args.texture > 0 else build_models.get_material())
    body.data.shade_smooth()
    body.data.update()
    size = co.max(axis=0) - co.min(axis=0)

    objs = {"Body": body}
    if args.propeller:
        objs["Propeller"] = fit_propeller(body, args.prop_span)
        if args.texture > 0:
            point_uvs_at_reserved(objs["Propeller"])

    bpy.context.view_layer.update()
    build_models.export_fbx(fbx_path, objs)
    total = sum(tris(o.data) for o in objs.values())
    print("%s: %d tris, size %s, wrote %s" % (args.asset, total, np.round(size, 3), fbx_path))
    build_models.render_views(args.asset + "_tripo", size, args.asset.startswith("Balloon"))


main()
