# MiniPlanes art tools

Everything here runs in Blender's Python, which has numpy. Run from the project root, with Unity's project
lock not involved: these scripts only write files.

```
set BLENDER="C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
```

| Command | Output |
| --- | --- |
| `%BLENDER% -b --factory-startup -P Tools/Art/build_models.py` | All six `Assets/MiniPlanes/Art/Models/*.fbx`, previews in `Tools/Art/previews/` |
| `... -P Tools/Art/build_models.py -- PlayerPlane BalloonBomb` | Only the named models |
| `... -P Tools/Art/verify_fbx.py` | Reads the FBX files back and prints axes, transforms, triangles, bounds and colours |
| `... -P Tools/Art/cut_vfx.py` | `Assets/MiniPlanes/Art/Vfx/*.png`, contact sheet `Tools/Art/previews/vfx_contact_sheet.png` |
| `... -P Tools/Art/crop_refs.py` | `Tools/Tripo/refs/*.png`, needs the previews to exist |

A full model rebuild takes under a minute.

## Models

Aircraft are signed distance fields in `build_models.py`: smooth unions give the moulded fillets. Each is
meshed with surface nets on a fine grid, decimated to its triangle budget, snapped back onto the field and
shaded with normals from the field. Balloon envelopes are lathed, so stripes follow gore columns exactly and
stay crisp; the basket is a small field mesh joined in.

- **Axes while modelling:** nose -Y, up +Z. The export maps that to nose +Z, up +Y in the FBX. Unity negates
  X on import, so Blender +X becomes Unity -X. The planes are symmetric, so this only mirrors triangle layout.
- **Pivot:** bounding box centre of the whole model. `Propeller` is a child of `Body` with its origin on the
  hub and its spin axis along its local Z in the FBX.
- **Colour:** per-corner vertex colours, stored as sRGB bytes in the FBX (`colors_type='SRGB'`). No textures.
  The shader decides whether to convert them to linear.
- **Material:** one placeholder slot, `M_VertexColor`.

Adjust shapes in the `*_sdf` functions, palettes in the `*_STOPS` constants and the `build_*` functions. The
script prints open and multi-face edge counts per object; both should be 0.

## VFX sprites

`cut_vfx.py` crops each sprite by the board pixel box in `SPRITES`, upsamples, removes the paper by un-mixing
each pixel from the local paper colour, unmixes ink edges to pure ink plus coverage, drops border-touching
components and specks, and bleeds colour under transparent pixels. Peak sprites come from the large key poses
(512 canvas, near board resolution). Onset and breakup only exist as small thumbnails on the board (256
canvas, upscaled 2 to 3 times from the board), so they are softer. Every sprite is scaled to fit its canvas,
so sprites do not share a scale: size them per stage in code. The numerals are `plus1` and `plus2`, with their
ink ticks.

## Tripo results replacing the stand-ins

The Blender models stay at the contract paths until a Tripo result is clearly better.

1. Put the download in `Tools/Tripo/runs/<AssetName>/` (see `Tools/Tripo/README.md`).
2. Convert without installing:
   `%BLENDER% -b --factory-startup -P Tools/Tripo/install_tripo.py -- --asset PlayerPlane --input Tools/Tripo/runs/PlayerPlane/model.glb`
3. Compare `Tools/Art/previews/PlayerPlane_tripo.png` with the concept. If the nose is not at the lower left
   of the hero view, add `--rotate X Y Z` degrees and repeat.
4. Add `--install` to overwrite `Assets/MiniPlanes/Art/Models/<AssetName>.fbx` and write
   `Assets/MiniPlanes/Art/Models/Textures/<AssetName>_BaseColor.png`.

Differences a game must allow for after a swap: a Tripo model has a texture rather than vertex colours, and
no separate `Propeller` object, so nothing spins. To return to the stand-in, rerun `build_models.py` for that
model.

The fonts in `Assets/MiniPlanes/Art/Fonts/` are Chakra Petch SemiBold and Bold from the `google/fonts`
repository, under the SIL Open Font License in `OFL.txt`.
