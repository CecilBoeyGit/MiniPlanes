# MiniPlanes art bible

Reference images and prompts copied from the Codex session `01a1a394-9c49-7690-b4e4-0a8531d17925`.

| File | What it shows |
| --- | --- |
| `aircraft-studies.png` | Rounded prop trainer in solid teal and in the cream, seafoam, teal gradient; coral swept-wing enemy |
| `comic-effects.png` | Hand-inked VFX: RING PASS (+1), CRASH, ENEMY FLICK (+2), each with onset, peak and breakup |
| `gameplay-concept.png` | Three gameplay moments in a living room |
| `scoring-ring-storyboard.png` | How the scoring ring forms with one hand and with two |
| `generation-prompts.md`, `scoring-ring-prompt.md` | The image prompts behind those boards |
| `codex-missing-concepts-prompt.txt` | Prompt for the concepts that do not exist yet: bomber and the three balloons. Not yet run, see below |

## Style rules

- **3D:** a 3D-printed miniature. Chunky soft one-piece forms, matte polymer, very subtle layer lines,
  no decals, no panel lines. Plain colours or elegant gradients only.
- **VFX:** 2D hand-drawn comic. Variable-weight dark ink contours, flat fills, hard cream highlights,
  no 3D shading, glow or gradients.

## Palette

| Asset | Colours |
| --- | --- |
| Player plane | Cream `#F3E5B8` nose and propeller, seafoam `#8FD0AE` middle, teal `#3E9389` tail |
| Enemy fighter | Coral `#F07A62` |
| Enemy bomber | Darker coral to brick `#C9503F` |
| Shield balloon | `#BFE3FF` to `#2F7FD6`, subtle cream stripes |
| Bomb balloon | `#E5483B` to `#7A1F1A`, round bomb-like basket |
| Storm balloon | Grey-violet `#8A84A8` to `#4B4668` |

## Missing concepts

There is no concept art for the bomber or the balloons. Resuming the Codex session failed on 2026-11-03
because the session has an active writer (the Codex app holds it open). To draw them, close that session
and run, from this folder:

```
codex exec resume --skip-git-repo-check 01a1a394-9c49-7690-b4e4-0a8531d17925 - < codex-missing-concepts-prompt.txt
```
