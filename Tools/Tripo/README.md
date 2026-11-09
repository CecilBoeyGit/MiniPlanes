# Tripo pipeline (MiniPlanes)

Two routes reach the same result. They use **separate credit balances**.

| Route | Credits | Tool |
| --- | --- | --- |
| Website (studio.tripo3d.ai) | Web credits | Browser, by hand or through an agent the owner has approved |
| API | API credits | `tripo-cli` through `generate.ps1` |

On 2026-11-03 the API balance is 0, so the CLI route exits with code 4 and charges nothing.

## Files

| File | Purpose |
| --- | --- |
| `assets.json` | Manifest: id, reference image, prompt, face limit, target size, style suffix, pricing |
| `generate.ps1` | API route: `-List`, `-Id <name or all>`, `-DryRun`, `-Force` |
| `install_tripo.py` | Converts a download to the model contract, previews it, optionally installs it |
| `refs/` | Square reference images made by `Tools/Art/crop_refs.py` |
| `runs/<AssetName>/` | Downloads and result logs, outside `Assets` so Unity ignores them |

## References

`PlayerPlane.png` and `EnemyFighter.png` are crops of `Docs/ArtBible/aircraft-studies.png`. The bomber and the
balloons have no concept art, so their references are renders of the Blender stand-ins. The manifest prompts
are there for a text-to-model run, or to steer the site if it accepts a prompt with an image.

## API route

Requires Node.js 20+ and `npm install -g tripo-cli`. Sign-in is the account owner's job (`tripo login`); keys
never go into files, chat or commands. `tripo balance --json` shows credits only.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -List
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id all -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id PlayerPlane
```

The dry run is offline and free. List price per asset is 110 credits for P2 generation with standard texture
plus 5 for the FBX convert. `-Force` starts a new paid task. Exit codes: 0 ok, 2 params, 3 auth, 4 credits,
5 content policy, 6 failed or timed out, 7 network, 9 rate limit. Never retry a paid run blindly: on 6 or 7
read `tripo docs --topic common-errors` first.

## Website route

Upload `refs/<AssetName>.png`, generate once, download FBX if offered and GLB otherwise, and save it to
`runs/<AssetName>/`. Record whether a texture came with it.

## After either route

See "Tripo results replacing the stand-ins" in `Tools/Art/README.md`. Results sit in `runs/` until
`install_tripo.py --install` is run, so a bad generation cannot replace a working model by accident.
