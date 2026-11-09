"""Crop clean reference images for Tripo image-to-model out of the art bible boards.

Run:
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup -P Tools/Art/crop_refs.py

Writes Tools/Tripo/refs/<AssetName>.png. Aircraft come from aircraft-studies.png. The bomber and the
three balloons have no concept art, so their references are the stand-in hero renders in
Tools/Art/previews/, which must already exist (build_models.py makes them).
"""
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from pngio import read_png, write_png  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
BIBLE = os.path.join(ROOT, "Docs", "ArtBible")
REFS = os.path.join(ROOT, "Tools", "Tripo", "refs")
PREVIEWS = os.path.join(HERE, "previews")


def square(img, x0, y0, x1, y1):
    """Crop, then pad to a square with the crop's own corner colour so nothing else shows."""
    c = img[y0:y1, x0:x1, :3]
    h, w = c.shape[:2]
    side = max(h, w)
    paper = np.median(np.concatenate([c[:6].reshape(-1, 3), c[-6:].reshape(-1, 3)]), axis=0)
    out = np.ones((side, side, 4), np.float32)
    out[..., :3] = paper
    oy, ox = (side - h) // 2, (side - w) // 2
    out[oy:oy + h, ox:ox + w, :3] = c
    return out


board = read_png(os.path.join(BIBLE, "aircraft-studies.png"))
# Board pixel boxes around the gradient trainer and the coral enemy, clear of labels and neighbours.
write_png(os.path.join(REFS, "PlayerPlane.png"), square(board, 600, 190, 1090, 600))
write_png(os.path.join(REFS, "EnemyFighter.png"), square(board, 1130, 180, 1625, 575))

# The preview sheet's hero render fills the left 1100 x 800 pixels.
for name in ("EnemyBomber", "BalloonShield", "BalloonBomb", "BalloonStorm"):
    sheet = read_png(os.path.join(PREVIEWS, name + ".png"))
    write_png(os.path.join(REFS, name + ".png"), square(sheet, 150, 0, 1050, 800))
print("wrote", REFS, sorted(os.listdir(REFS)))
