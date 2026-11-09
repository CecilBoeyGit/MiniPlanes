"""Cut the comic VFX sprites out of Docs/ArtBible/comic-effects.png as transparent PNGs.

Run:
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup -P Tools/Art/cut_vfx.py

Writes Assets/MiniPlanes/Art/Vfx/<name>.png and Tools/Art/previews/vfx_contact_sheet.png.

Method: upsample the crop, estimate the local paper colour, then un-mix each pixel from the paper
(colour-to-alpha: the smallest alpha that explains the pixel as foreground over paper). A soft
threshold turns that into a matte, so ink edges carry their real coverage instead of a paper-coloured
fringe, and strongly coloured or cream fills saturate to fully opaque. Components that touch the crop
border and tiny specks are dropped, then colour is bled outward under transparent pixels so bilinear
filtering does not produce halos in the headset.
"""
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from pngio import read_png, write_png, resize, over  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(ROOT, "Docs", "ArtBible", "comic-effects.png")
OUT = os.path.join(ROOT, "Assets", "MiniPlanes", "Art", "Vfx")
PREVIEW = os.path.join(HERE, "previews", "vfx_contact_sheet.png")

# name: (x0, y0, x1, y1) in board pixels, canvas size, and optional boxes to erase before keying.
SPRITES = {
    "plus1":        ((122, 212, 452, 345), 256, []),
    "plus2":        ((1250, 212, 1572, 345), 256, []),
    "ring_peak":    ((6, 335, 545, 668), 512, []),
    "crash_peak":   ((566, 212, 1125, 672), 512, []),
    "flick_peak":   ((1160, 340, 1662, 652), 512, []),
    "ring_onset":   ((28, 724, 146, 812), 256, []),
    "ring_breakup": ((388, 708, 534, 836), 256, []),
    "crash_onset":  ((592, 708, 696, 816), 256, []),
    "crash_breakup": ((986, 704, 1116, 824), 256, []),
    "flick_onset":  ((1170, 734, 1268, 828), 256, []),
    "flick_breakup": ((1516, 720, 1654, 816), 256, []),
}
# The small stage row is the only drawing of onset and breakup. Its peak thumbnails are not used
# because the large key poses above are the same drawings at higher resolution.

SUPERSAMPLE = 4        # work resolution multiple of the board crop
NOISE_FLOOR = 0.050    # colour distance from local paper treated as paper grain
ALPHA_LO = 0.10        # colour-to-alpha value mapped to matte 0
ALPHA_HI = 0.50        # colour-to-alpha value mapped to matte 1
MIN_SPECK_BOARD_PX = 7 # components smaller than this many board pixels are dropped
INK = np.array([0.08, 0.07, 0.07], np.float32)  # contour colour measured from the board


def local_paper(crop):
    """Paper colour per crop: median of the brightest low-chroma cluster around the crop border."""
    border = np.concatenate([crop[:6].reshape(-1, 3), crop[-6:].reshape(-1, 3),
                             crop[:, :6].reshape(-1, 3), crop[:, -6:].reshape(-1, 3)])
    med = np.median(border, axis=0)
    near = border[np.linalg.norm(border - med, axis=1) < 0.05]
    return np.median(near, axis=0) if len(near) else med


def color_to_alpha(rgb, paper):
    up = (rgb - paper) / np.maximum(1.0 - paper, 1e-3)
    down = (paper - rgb) / np.maximum(paper, 1e-3)
    return np.max(np.maximum(up, down), axis=-1)


def key(rgb, paper):
    dist = np.linalg.norm(rgb - paper, axis=-1)
    a0 = color_to_alpha(rgb, paper)
    t = np.clip((a0 - ALPHA_LO) / (ALPHA_HI - ALPHA_LO), 0.0, 1.0)
    matte = t * t * (3.0 - 2.0 * t)
    grain = np.clip((dist - NOISE_FLOOR) / 0.04, 0.0, 1.0)
    matte = matte * grain
    # Un-mix the foreground colour using the final matte.
    m = np.maximum(matte, 1e-3)[..., None]
    fg = np.clip((rgb - (1.0 - m) * paper) / m, 0.0, 1.0)
    # Pixels on the straight line between paper and ink are pure ink coverage: their colour is ink
    # and their alpha is the coverage, otherwise edges turn tan on a dark room behind them.
    d = INK - paper
    v = rgb - paper
    t = np.sum(v * d, axis=-1) / np.sum(d * d)
    residual = np.linalg.norm(v - t[..., None] * d, axis=-1)
    w = np.clip((residual - 0.03) / 0.06, 0.0, 1.0)     # 0 = on the ink line, 1 = a real colour
    c = np.clip((t - 0.25) / 0.5, 0.0, 1.0)
    ink_matte = c * c * (3.0 - 2.0 * c)
    matte = np.where(t > 0, ink_matte * (1 - w) + matte * w, matte)
    blend = np.where(t > 0, w, 1.0)[..., None]
    fg = INK * (1 - blend) + fg * blend
    return fg, matte


def label_components(mask):
    """Connected components (4-neighbour) by union-find over runs; numpy has no labeler."""
    h, w = mask.shape
    labels = np.zeros((h, w), np.int32)
    parent = [0]

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    nxt = 1
    prev_runs = []
    for y in range(h):
        row = mask[y]
        d = np.diff(np.concatenate([[0], row.astype(np.int8), [0]]))
        starts = np.nonzero(d == 1)[0]
        ends = np.nonzero(d == -1)[0]
        runs = []
        pi = 0
        for s, e in zip(starts, ends):
            lab = 0
            while pi < len(prev_runs) and prev_runs[pi][1] <= s:
                pi += 1
            j = pi
            while j < len(prev_runs) and prev_runs[j][0] < e:
                other = find(prev_runs[j][2])
                if lab == 0:
                    lab = other
                elif lab != other:
                    parent[other] = lab
                j += 1
            if lab == 0:
                lab = nxt
                parent.append(nxt)
                nxt += 1
            labels[y, s:e] = lab
            runs.append((s, e, lab))
        prev_runs = runs
    remap = np.arange(nxt, dtype=np.int32)
    for i in range(1, nxt):
        remap[i] = find(i)
    return remap[labels]


def clean(matte, scale):
    """Drop border-touching components and specks; returns the cleaned matte."""
    mask = matte > 0.04
    lab = label_components(mask)
    n = lab.max() + 1
    area = np.bincount(lab.ravel(), minlength=n)
    border = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    keep = area >= MIN_SPECK_BOARD_PX * scale * scale
    keep[0] = False
    keep[border] = False
    return matte * keep[lab]


def bleed(rgb, alpha, iters=24):
    """Spread colour into transparent texels so bilinear sampling cannot pull in black."""
    rgb = rgb.copy()
    known = alpha > 0.5
    for _ in range(iters):
        grow = np.zeros_like(known)
        acc = np.zeros_like(rgb)
        cnt = np.zeros(known.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            k = np.roll(known, (dy, dx), (0, 1))
            acc += np.roll(rgb, (dy, dx), (0, 1)) * k[..., None]
            cnt += k
        grow = (~known) & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][..., None]
        known = known | grow
        if known.all():
            break
    return rgb


def soft_upsample(img, k):
    """Bilinear upsample plus a small blur. Lanczos here overshoots next to ink, and the overshoot
    reads as cream to the key, which is what produced a pale halo around every contour."""
    h, w = img.shape[:2]
    ys = (np.arange(h * k) + 0.5) / k - 0.5
    xs = (np.arange(w * k) + 0.5) / k - 0.5
    y0 = np.clip(np.floor(ys).astype(int), 0, h - 2)
    x0 = np.clip(np.floor(xs).astype(int), 0, w - 2)
    fy = np.clip(ys - y0, 0, 1)[:, None, None]
    fx = np.clip(xs - x0, 0, 1)[None, :, None]
    a = img[y0][:, x0] * (1 - fx) + img[y0][:, x0 + 1] * fx
    b = img[y0 + 1][:, x0] * (1 - fx) + img[y0 + 1][:, x0 + 1] * fx
    out = a * (1 - fy) + b * fy
    # Box blur over the board-pixel footprint, along each axis, using a running sum.
    r = max(1, k // 2)
    for axis in (0, 1):
        pad = [(0, 0)] * 3
        pad[axis] = (r + 1, r)
        c = np.cumsum(np.pad(out, pad, mode="edge"), axis=axis, dtype=np.float64)
        hi = np.take(c, np.arange(2 * r + 1, c.shape[axis]), axis=axis)
        lo = np.take(c, np.arange(0, c.shape[axis] - 2 * r - 1), axis=axis)
        out = (hi - lo) / (2 * r + 1)
    return np.clip(out, 0, 1).astype(np.float32)


def cut(board, name, box, canvas):
    x0, y0, x1, y1 = box
    crop = board[y0:y1, x0:x1, :3]
    paper = local_paper(crop)
    h, w = crop.shape[:2]
    big = soft_upsample(crop, SUPERSAMPLE)
    fg, matte = key(big, paper)
    matte = clean(matte, SUPERSAMPLE)
    ys, xs = np.nonzero(matte > 0.02)
    if len(xs) == 0:
        raise RuntimeError(name + ": nothing left after keying")
    t, b, l, r = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    fg, matte = fg[t:b, l:r], matte[t:b, l:r]
    fit = int(canvas * 0.94)
    s = fit / max(b - t, r - l)
    nh, nw = max(1, round((b - t) * s)), max(1, round((r - l) * s))
    # Premultiplied resize so colour outside the matte does not bleed into the edge.
    pm = np.concatenate([fg * matte[..., None], matte[..., None]], axis=-1).astype(np.float32)
    pm = np.clip(resize(pm, nh, nw), 0, 1)
    a = pm[..., 3]
    col = np.where(a[..., None] > 1e-3, pm[..., :3] / np.maximum(a[..., None], 1e-3), 0.0)
    col = np.clip(col, 0, 1)
    out = np.zeros((canvas, canvas, 4), np.float32)
    oy, ox = (canvas - nh) // 2, (canvas - nw) // 2
    out[oy:oy + nh, ox:ox + nw, :3] = col
    out[oy:oy + nh, ox:ox + nw, 3] = a
    out[..., :3] = bleed(out[..., :3], out[..., 3])
    write_png(os.path.join(OUT, name + ".png"), out)
    print("%-14s board %4dx%-4d paper %s  art %dx%d on %d  scale from board %.2f" % (
        name, r - l, b - t, np.round(paper, 3), nw, nh, canvas, s * SUPERSAMPLE))
    return out


def contact_sheet(sprites):
    cell = 256
    names = list(sprites)
    cols = 6
    bgs = [(0.10, 0.11, 0.13), (0.30, 0.55, 0.85), (0.85, 0.20, 0.45)]
    rows = int(np.ceil(len(names) / cols))
    sheet = np.ones((rows * cell * len(bgs), cols * cell, 4), np.float32)
    for bi, bg in enumerate(bgs):
        for i, n in enumerate(names):
            im = sprites[n]
            if im.shape[0] != cell:
                im = np.clip(resize(im * np.concatenate(
                    [im[..., 3:4]] * 3 + [np.ones_like(im[..., 3:4])], axis=-1), cell, cell), 0, 1)
                a = np.maximum(im[..., 3:4], 1e-3)
                im = np.concatenate([np.clip(im[..., :3] / a, 0, 1), im[..., 3:4]], axis=-1)
            y = (bi * rows + i // cols) * cell
            x = (i % cols) * cell
            sheet[y:y + cell, x:x + cell] = over(im, bg)
    write_png(PREVIEW, sheet)


def main():
    board = read_png(SRC)
    print("board", board.shape)
    out = {}
    for name, (box, canvas, _erase) in SPRITES.items():
        out[name] = cut(board, name, box, canvas)
    contact_sheet(out)
    print("wrote", OUT)


main()
