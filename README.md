# MiniPlanes

A seated mixed-reality mini-game for Meta Quest 3 and 3S, built with Unity 6000.3.14f1, URP and the
Meta XR Core SDK on the Unity OpenXR backend. Hands only, no controllers.

A tiny toy plane flies a different random route each flight, in the 180 degrees in front of you. Make a
C with your index finger and thumb and a bubble scoring ring fills inside it, following the shape of your
fingers. The plane scores by flying through it from either side. Your hands are rock: touching the plane is
a hit. Each tutorial level and each Infinite run has 3 lives; after a crash the plane comes back where it fell.
To pause, face a palm toward you and press the Menu button on that wrist with your other hand. Six tutorial
levels introduce the mechanics one at a time, each opening on a page with a Start button, and passing them
all opens the endless Infinite mode. Coins earned in play buy new planes and paint in the Hangar.

## Scoring

| Action | Points | Coins |
|---|---|---|
| Plane through a single-hand C ring | 2 | 2 |
| Plane through the big two-hand O ring | 1 (it is larger and easier) | 1 |
| Flicking a fighter out of the sky | 2 | 1 |
| Flicking a bomber out of the sky | 4 | 1 |
| The turret shooting an enemy down | 1 | 1 |
| Cupping a flashing bomb balloon until it is contained | - | 2 |
| Surviving an Infinite wave | - | 5 |

Points are the score for a level or run; coins are saved and spent in the Hangar (Bluebird 500, Pusher
Racer 1000). All of them are on the `Game` object (`GameManager`), except wave coins on `Infinite.asset`.

## Levels

| # | Name | Introduces | Line preview | Risk |
|---|---|---|---|---|
| 1 | First Flight | Rings, short and slow | On the whole level | 5 hits |
| 2 | Eyes Open | Reacting on the spot | 3 s, then 2.5 s after 3 rings in a row | 4 hits |
| 3 | Running on Fumes | Fuel: three rings fill the tank | as level 2 | 2 hits |
| 4 | Bandits | Fighters with machine guns; block bullets, flick fighters | as level 2 | 1 hit |
| 5 | Fair Winds | Draggable balloons: blue shield, gold turret | as level 2 | 1 hit |
| 6 | Bomb Run | Bombers drop red bomb balloons, then fly off | as level 2 | 1 hit |
| - | Infinite | Waves, high score | 3 s per wave | 1 hit |

Every level has 3 lives (`lives`). Hits are per life: a low-risk level absorbs a few before one crash.

A level passes when the plane finishes its path with enough ring passes (`requiredRingPasses`). The line
only ever shows the part of the path still ahead of the plane.

## Tuning

Everything gameplay-related is exposed in the Inspector:

- **`Assets/MiniPlanes/Levels/Level1..6.asset`**: speed, path length and shape, the arc in front of the
  player, line preview timings (`startRevealSeconds`, `streakToReveal`, `streakRevealSeconds`), fuel
  (`fuelDrainPerSecond`, `ringsToFillTank`), fighters, balloons and bombers.
- **`Assets/MiniPlanes/Levels/Infinite.asset`**: start values and hard caps for the wave curve, and
  `rampWaves` for how quickly it ramps.
- **`HandRing`** on each hand: C detection, fill time, `visualRadiusScale` (the bubble's size), `shapeFollow`
  (how closely it follows the fingers). **`RingMerger`** on `Game/RingMerger`: how close
  the hands must be to merge, merge time, big ring size. **`BubbleRingVisual`**: wobble and float.
- **`FlickDetector`** on each hand: flick speed and reach. **`BalloonGrabber`**: pinch pick-up radius.
- **`CurvedHud`** on `CockpitHUD`: placement, follow speed, layout, colours.
- **`WristMenuButton`** on `WristMenu`: how directly the palm must face you, button size and placement.
  **`PausePanel`** on `PausePanel`: reach, follow speed, button layout, poke depth.
- **`Assets/MiniPlanes/Levels/Planes.asset`**: the Hangar's planes, prices, paint palettes and where the
  turret mounts on each.
- **`GameManager`**: points and coins per action; `devUnlockAll` opens every level and `devUnlockAllPlanes`
  every plane, without saving anything; `devCoinsOnStart` tops up the coins; `devResetProgressOnStart` wipes
  saved progress. All are off in shipped settings.

## UI prefabs

Every piece of UI is a prefab in `Assets/MiniPlanes/Prefabs/UI`, built from ordinary uGUI Images and Texts,
so you can restyle them with your own sprites and fonts:

| Prefab | What it is |
|---|---|
| `MenuPanel` | Start page, level intro with its Start button, result page and Hangar |
| `CockpitHUD` | Progress, score, level, lives and coins, plus the caption line |
| `PausePanel` | Resume, Restart, Main Menu |
| `WristMenuButton` | The palm-up Menu button |
| `ShieldIcon`, `BombIcon`, `TurretIcon` | The icons above the balloons |
| `Crosshair`, `FuelGauge` | The balloon drag target and the plane's fuel bar |

The menu, HUD and pause panel are flat canvases that a `CurvedCanvas` bends around you; anything you add to
them curves too. Their `LazyFollow` sets how they trail your head. Buttons are `PokeButton`s, found by their
`id`, so keep the ids when you restyle. The starter sprites are in `Assets/MiniPlanes/Art/UI`. Rebuilding the
scene never overwrites an existing prefab or sprite: delete one to get the generated version back.

Rebuilding the scene keeps your level edits. Level assets only reset when
`LevelConfig.CurrentDefaultsVersion` is bumped in code.

## Building

`MiniPlanes > 0. Setup Everything` regenerates settings, scene and references.
`MiniPlanes > 3. Build Android APK` writes `Builds/MiniPlanes.apk`. Install with `adb install -r`.

Headless (run one at a time; the project can only be open once):

```
Unity.exe -batchmode -nographics -buildTarget Android -projectPath D:/Unity/MiniPlanes -executeMethod MiniPlanes.Editor.MiniPlanesBootstrap.SetupAndBuild -quit -logFile build.log
```

## Automated checks

`MiniPlanes > 4. Run Autopilot Checks` plays the real scene with simulated hands at a fixed timestep and
checks each mechanic: path limits, unlocks, poking the menu, ring scoring, the O merge, fuel rates,
bullets, flicks, balloons, bomb arming, chaining, covering and Infinite's waves. Results are logged as
`[AUTOPILOT] PASS` or `FAIL`. Headless, leave out `-quit`, and the exit code is 0 only if all pass:

```
Unity.exe -batchmode -nographics -buildTarget Android -projectPath D:/Unity/MiniPlanes -executeMethod MiniPlanes.Editor.MiniPlanesAutopilot.Run -logFile auto.log
```

Add `-mpShotsDir <folder>` and drop `-nographics` to render preview PNGs of the menus, models, rings,
HUD, Hangar, stencil reveal and comic VFX instead.

## How it works

| Script | Role |
|---|---|
| `Core/GameManager.cs` | Menu, levels, unlocks, Infinite waves, scoring. |
| `Core/LevelConfig.cs`, `InfiniteConfig.cs` | All tuning; the Infinite difficulty curve with caps. |
| `Core/FlightPathGenerator.cs` | A virtual pilot flies random manoeuvres within the front arc; `randomness` sets how wild. |
| `Core/CatmullRomPath.cs` | Arc-length spline, so the plane flies at constant speed. |
| `Core/PathRevealer.cs` | The faint line, ahead of the plane only: always on, timed, or brought back by a ring streak. |
| `Core/MiniPlane.cs` | Flight, fuel, shield, turret, hits, crash and respawn, plane model and paint. |
| `Core/PlaneTurret.cs` | The gold balloon's automatic turret and its rounds. |
| `Core/PlaneCatalog.cs`, `PlanePaint.cs` | The Hangar's planes and palettes, and applying paint. |
| `Core/ComicVfx.cs` | Pooled hand-drawn effects: onset, peak, breakup frames. |
| `Core/Progression.cs` | Saved unlocks, high score, coins, owned planes and paint. |
| `Core/GameArt.cs` | References to all materials, models, sprites and fonts. |
| `Hands/HandSource.cs`, `MetaHandSource.cs`, `FakeHandSource.cs` | Hand input: abstract, Meta, simulated. |
| `Hands/HandRing.cs`, `BubbleRingVisual.cs`, `RingMerger.cs` | C detection, bubble portal, big O merge. |
| `Hands/HandHazard.cs`, `FlickDetector.cs`, `BalloonGrabber.cs` | Rock hands, flicks, pinch-drag. |
| `Enemies/EnemyPlane.cs`, `BomberPlane.cs`, `Projectile.cs` | Fighters, bombers, machine-gun rounds. |
| `Enemies/PowerBalloon.cs`, `BombBalloon.cs`, `EnemySpawner.cs` | Balloons and spawning. |
| `UI/CurvedCanvas.cs`, `CurvedGraphic.cs`, `LazyFollow.cs` | Bending uGUI onto a cylinder; smooth follow. |
| `UI/PokeCanvas.cs`, `PokeButton.cs`, `PokeScrollArea.cs` | Fingertip input for any canvas. |
| `UI/CurvedHud.cs`, `UI/MenuPanel.cs` | The HUD, and the menu with its intro, result and Hangar pages. |
| `UI/WristMenuButton.cs`, `UI/PausePanel.cs` | Palm-up wrist button; curved lazy-follow pause menu. |

Shaders in `Assets/MiniPlanes/Shaders`: `Matte` (printed polymer), `BubbleRing` (radial fill, wobble,
peanut merge, finger-shaped outline), `OccludedSilhouette` (solid plane reveal behind hands), `PathLine`,
`ComicSprite`, `ShieldBubble`. `Matte` can also repaint a model's colourful parts, which the Hangar palettes
and the gold balloon use.

The behind-the-hand reveal works in three steps. The hand material writes stencil 1. Aircraft clear it
where they are visible. The silhouette then draws only where it is behind existing depth and the
stencil still says that depth belongs to a hand.

## Art

Style references are in `Docs/ArtBible`, from the Codex art bible session. VFX cuts and fonts are in
`Assets/MiniPlanes/Art`. The models were generated on the Tripo website and baked to about 5,000 triangles: the
three player planes, two enemies and the turret come from the reference sheets in `Docs/ArtBible` and use a
small texture; the balloons use vertex colours. The gold turret balloon is the blue one repainted in game.
`Tools/Tripo/web_generations.md` records the prompts, task IDs and how to rebuild them.
The original Blender versions are kept in `Tools/Art/standins`.

Font: Chakra Petch, SIL Open Font License (`Assets/MiniPlanes/Art/Fonts/OFL.txt`).
