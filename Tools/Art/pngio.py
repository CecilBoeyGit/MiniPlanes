"""Small numpy image helpers shared by the art scripts (run inside Blender's Python).

read_png  - decode through Blender, returns float32 HxWx4 in 0..1, top row first, raw sRGB values.
write_png - straight-alpha RGBA8 PNG written with zlib, so no Blender colour management touches it.
resize    - separable Lanczos3 with a kernel that widens when downscaling.
"""
import os
import struct
import zlib

import numpy as np


def read_png(path):
    import bpy
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    bpy.data.images.remove(img)
    return buf.reshape(h, w, 4)[::-1].copy()


def write_png(path, rgba):
    a = np.clip(rgba, 0.0, 1.0)
    data = np.round(a * 255.0).astype(np.uint8)
    h, w, c = data.shape
    assert c == 4
    # PNG "Sub" filter per row: small files for images with large flat areas.
    sub = data.astype(np.int16)
    sub[:, 1:] = sub[:, 1:] - sub[:, :-1]
    sub = (sub & 255).astype(np.uint8)
    raw = np.concatenate([np.ones((h, 1), np.uint8), sub.reshape(h, w * 4)], axis=1).tobytes()

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


def _lanczos_axis(img, new_len, axis):
    old_len = img.shape[axis]
    if old_len == new_len:
        return img
    scale = new_len / old_len
    support = 3.0 * max(1.0, 1.0 / scale)
    centres = (np.arange(new_len) + 0.5) / scale - 0.5
    idx0 = np.floor(centres - support).astype(int) + 1
    n = int(np.ceil(2 * support)) + 1
    offs = idx0[:, None] + np.arange(n)[None, :]
    x = (offs - centres[:, None]) * min(1.0, scale)
    w = np.sinc(x) * np.sinc(x / 3.0)
    w[np.abs(x) >= 3.0] = 0.0
    w /= w.sum(axis=1, keepdims=True)
    offs = np.clip(offs, 0, old_len - 1)
    moved = np.moveaxis(img, axis, 0)
    res = np.zeros((new_len,) + moved.shape[1:], dtype=np.float32)
    # Chunked so large upscales stay inside memory.
    extra = (1,) * (moved.ndim - 1)
    for s in range(0, new_len, 256):
        e = min(s + 256, new_len)
        res[s:e] = (moved[offs[s:e]] * w[s:e].reshape(e - s, n, *extra)).sum(axis=1)
    return np.moveaxis(res, 0, axis)


def resize(img, new_h, new_w):
    return _lanczos_axis(_lanczos_axis(img, new_h, 0), new_w, 1)


def over(rgba, bg_rgb):
    """Composite straight-alpha rgba over a solid colour or an image of the same shape."""
    a = rgba[..., 3:4]
    return np.concatenate([rgba[..., :3] * a + np.asarray(bg_rgb, np.float32) * (1 - a),
                           np.ones_like(a)], axis=-1)
