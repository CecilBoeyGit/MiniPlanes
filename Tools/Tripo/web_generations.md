# Models generated on the Tripo website

These are the models now in `Assets/MiniPlanes/Art/Models`. They were made on studio.tripo3d.ai with web credits, using
HD Model, H3.1, 55 credits each. Exports are free. The API route has its own separate balance.

Image-to-3D keeps the facing of the source picture, so the rotation that puts the nose at -Y differs per model. Check the
top view in the preview. On the website, wait for the file to reach Downloads before closing the Export dialog. If the browser never saves it, the
Export button is only a link to a signed `tripo-data...tripo3d.com/.../tripo_convert_<id>.glb` URL, which curl can fetch.

Total spent on 2026-11-03: 660 credits for 12 generations, including 3 rejected attempts. Balance went from 66,866 to 66,206.
On 2026-11-09: 165 credits for the Turret, Biplane and Pusher Racer (image-to-3D from Docs/ArtBible). The gold turret balloon is the blue one repainted in game, not a model.
Later that day the Zeppelin (image-to-3D from `refs/v2/EnemyZeppelin.png`, a crop of `Docs/ArtBible/enemy-zeppelin-concept.png`) cost nothing: the account's one-time 8K Texture trial made it free.

The three aircraft are image-to-3D, from the hero view of the Codex reference sheets in `Docs/ArtBible/*-model-reference.png`, cropped to `Tools/Tripo/refs/v2`. The balloons are still text-to-3D, from the prompts below. These aircraft raw files are `model_v2.glb`, and they bake to a 512 px texture because vertex colours smear the wing stripes.

| Asset | Task | Bake command options |
|---|---|---|
| PlayerPlane | 3e50dd0c-1132-4863-b38a-687b8b82f1ce | `--rotate 0 0 -90 --cut-front 0.125 --propeller --prop-span 0.4 --texture 512 --smooth 8` |
| EnemyFighter | 338459a6-9f76-4d69-94f3-e6eeae1afe50 | `--rotate 0 0 0 --texture 512 --smooth 8` |
| EnemyBomber | 2fda4b79-b294-419c-9f12-1e58b2ccf438 | `--rotate 0 0 0 --texture 512 --smooth 8` |
| PlayerBiplane | c476f544-e426-4f4d-ba6b-772d582aca6c | `--rotate 0 0 0 --cut-front 0.12 --propeller --prop-span 0.42 --texture 512 --smooth 8` |
| PlayerPusher | f87edfc0-2e3b-4b7f-ae0d-1f4ed5d08f7d | `--rotate 0 0 180 --texture 512 --smooth 8` (rear propeller stays part of the body) |
| Turret | 3c9873e4-9fa8-4abc-9909-b0ab4544929f | `--rotate 0 0 -90 --texture 256 --max-tris 2500 --smooth 6` |
| EnemyZeppelin | f4d23ed5-769d-470f-9f98-aed5090979c0 | `--rotate 0 0 0 --texture 1024 --max-tris 6000 --smooth 6` (exported at 2K) |
| BalloonShield | c4560afe-4dbd-4952-9160-0baa6f49499b | `--max-tris 4500 --smooth 8` |
| BalloonBomb | ed239993-4252-412c-8990-5cfc71366036 | `--max-tris 4500 --smooth 8` |

To re-export one, open `https://studio.tripo3d.ai/workspace/generate/<task>`, choose Export, GLB, and save it as
`Tools/Tripo/runs/<Asset>/model.glb`. That folder is ignored by git because each file is about 60 MB. Then bake:

```
blender -b --factory-startup -P Tools/Tripo/bake_tripo.py -- --asset <Asset> --input Tools/Tripo/runs/<Asset>/model.glb <options> --install
```

`bake_tripo.py` replaces `install_tripo.py` for these models. Decimating the raw download directly opens gaps along UV
seams, so it welds first and smooths away surface noise. It then bakes the 4K texture into a small texture (aircraft) or
vertex colours (balloons).

The original Blender versions are kept in `Tools/Art/standins`. The player's spinning propeller comes from there.

## What worked in the prompts

Plain "toy plane" wording produced realistic aircraft with landing gear, windows and panels. Two things fixed that:
"simple rounded shape like a bath toy", and listing what to leave out ("no wheels, no landing gear, no windows, no panels").
Naming each colour by part stopped Tripo inventing a cream canopy and a bright green propeller.

## Prompts used

**BalloonShield:** Cute toy hot air balloon miniature, 3D printed matte polymer, smooth soft rounded one-piece form, round teardrop envelope with soft shallow vertical gores, small chunky woven-free plain basket on short thick struts. Colour gradient on the envelope from pale sky blue at the top to deep blue at the bottom, with thin cream vertical stripes, cream basket. Muted pastel colours, no decals, no text, no thin ropes, single isolated object, no ground, no base plate

**BalloonBomb:** Cute toy hot air balloon miniature, 3D printed matte polymer, smooth soft rounded one-piece form, round teardrop envelope with soft shallow vertical gores, carrying a chunky round black cartoon bomb with a short stubby cream fuse underneath on short thick struts, instead of a basket. Colour gradient on the envelope from bright red at the top to dark red at the bottom. No decals, no text, no thin ropes, single isolated object, no ground, no base plate

The grey storm balloon (task 45f09ddf-fd34-4dac-8a57-d34d7bb09316) was removed from the game because it was too easily confused with the blue one.
