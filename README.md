# MiniPlanes

A seated mixed-reality mini-game for Meta Quest 3 and 3S, built with Unity 6000.3.14f1, URP and the
Meta XR Core SDK on the Unity OpenXR backend. Hands only, no controllers.

A tiny toy plane flies a different random route each flight, in the 180 degrees in front of you. Make a
C with your index finger and thumb and a bubble scoring ring fills inside it, following the shape of your
fingers. The plane scores by flying through it from either side. Your hands are rock: touching the plane is
a hit. Each tutorial level and each Infinite run has 3 lives; after a crash the plane comes back where it fell.
To pause, face a palm toward you and press the Menu button on that wrist with your other hand. Six tutorial
levels introduce the mechanics one at a time, each opening on a page with a Start button, and passing them
all opens the endless Infinite mode and a seventh level for playtesting the zeppelin boss. Coins earned in
Infinite buy new planes and paint in the Hangar.

## Scoring

| Action | Points | Coins (Infinite only) |
|---|---|---|
| Plane through a single-hand C ring | 2 | 2 |
| Plane through the big two-hand O ring | 1 (it is larger and easier) | 1 |
| Flicking a fighter out of the sky | 2 | 1 |
| Flicking a bomber out of the sky | 4 | 1 |
| The turret shooting an enemy down | 1 | 1 |
| Holding a zeppelin missile until it drops | 2 | 1 |
| Cupping a flashing bomb balloon until it is contained | - | 5 |
| Surviving an Infinite wave | - | 5 |

Points are the score for a level or run; coins are saved and spent in the Hangar (Bluebird 500, Pusher
Racer 1000). Tutorial levels pay no coins for play, so they can't be farmed; instead each pays 5 coins the
first time it is passed, and nothing on replays. All of these are on the `Game` object (`GameManager`),
except wave coins on `Infinite.asset`.

## Powerups and planes

- **Blue balloon**: a one-hit shield. **Gold balloon**: an automatic turret for 6 seconds that aims ahead of
  its targets and fires fast. Pinch a balloon's crosshair and drag it onto the path.
- **Booster tubes**: short glass tubes about as wide as a C ring, straight or curved, 6 to 14 cm long. Pinch one and
  it moves and turns with your hand, like a real object. Line it up where you remember the path goes: flying
  in one end and out the other boosts the plane for 3 seconds and uses the tube up. The plane touching the wall
  breaks it with no boost. Hands never break a tube; otherwise it vanishes when its time runs out.
- **In Infinite** at most 2 blue balloons come per wave, the gold turret balloon only once every third wave, and
  booster tubes are capped: none in wave 1, 1 in wave 2, 2 in waves 3 to 5, then 3. Fuel is on from wave 1.
  The path starts simple and calm in wave 1 and its twists build up slowly: the path's ramp runs at half the
  speed of the enemies' (`pathRampStretch`), so wave 5 flies what wave 3 used to.
- **Bluebird** (500): fits its own turret, the same as the gold balloon's, when the first enemy plane of
  each wave shows up. **Pusher Racer** (1000): boosts for 3 seconds at the start of every wave.
  **Supersonic** (2000): fits twin turrets (one on each dome, each picking its own target) when each
  wave's first enemy shows up, and boosts at every wave start. Perks are flags, so a plane can have several.

## Levels

| # | Name | Introduces | Line preview | Risk |
|---|---|---|---|---|
| 1 | First Flight | Rings, short and slow | On the whole level | 5 hits |
| 2 | Eyes Open | Reacting on the spot | 3 s, then 2.5 s after 3 rings in a row | 4 hits |
| 3 | Running on Fumes | Fuel: three rings fill the tank | as level 2 | 2 hits |
| 4 | Bandits | Fighters with machine guns; block bullets, flick fighters | as level 2 | 1 hit |
| 5 | Fair Winds | Draggable balloons: blue shield, gold turret | as level 2 | 1 hit |
| 6 | Bomb Run | Bombers drop red bomb balloons, then fly off | as level 2 | 1 hit |
| 7 | Zeppelin | The boss, with every attack, for playtesting | On the whole level | 2 hits |
| - | Infinite | Waves, high score | 3 s per wave | 1 hit |

Level 5 also hands out booster tubes. Level 7 opens with Infinite, after level 6, and Infinite doesn't need it.

In Infinite, **floating islands** (12 to 24 cm across) appear from wave 4 around you and the path, never on it. The plane
crashes into them. Touching one with a hand shatters it into shards that drift slowly in every direction,
across the path too; a shard hitting the plane also crashes it, and touching a shard turns it to dust.
From the islands' second wave, **jamming towers** stand on some of them (one at first, then slowly more, up to 3, randomised
each wave), under a struck-through wi-fi icon. While any tower stands, the warning that sounds when an enemy plane
arrives is silenced. Towers go with their island, so smack the island (or the tower) to clear the jamming; the
plane crashes into a tower as into an island.

Every 5th wave is a **boss wave**: a giant zeppelin hangs in the distance and can't be destroyed. Between
attacks it cruises a random way, left or right, round the flight path, then takes its next turn at bullet trails, mostly aimed at the plane (block them with your hands), batches of fighters launched
from its decks, and large tracking missiles. Its aimed trails lead the plane along its path and re-aim every
round, so they follow it; one trail per barrage is dead on. Hold a missile with an open palm for 1.5 seconds and it drops
out of the sky; smacking it sets it off, which is only safe far from the plane. The first boss wave has no
missiles; each later one adds trails, fighters and missiles.

Every level has 3 lives (`lives`). Hits are per life: a low-risk level absorbs a few before one crash.

A level passes when the plane finishes its path with enough ring passes (`requiredRingPasses`). The line
only ever shows the part of the path still ahead of the plane.

Tutorial Start pages draw icons in their text: write `{fighter}`, `{bomber}`, `{zeppelin}`, `{missile}`,
`{shield}`, `{turret}`, `{bomb}` or `{tube}` in a level's `introCaption` (`InlineIcons`; the pictures are in
`GameArt.captionIcons` and `Assets/MiniPlanes/Art/UI`).

## Sound

Sound effects are in `Assets/MiniPlanes/Audio`, cut from the Skylooper sound collection by
`Tools/Audio/convert_sfx.sh` (sources and freesound IDs in `Tools/Audio/SOURCES.md`). Clips, volumes and
which sounds are heard from where they happen are in `Assets/MiniPlanes/Audio/GameSounds.asset`; rebuilding
the scene keeps your changes. The plane hums with a looping propeller that rises when boosting. Pausing
pauses world sounds; menu sounds still play.

Ring scores, enemy engines, the enemy explosion and all music are synthesized by `Tools/Audio/synth` (see
`Tools/Audio/SOURCES.md`). Music settings are in `Assets/MiniPlanes/Audio/GameMusic.asset`; `MusicDirector` follows the
game on its own: a start-page theme, other menus, and in flight a normal mix that crossfades to a hostile one while
enemy planes are up, a build from 70% of the path that peaks with a hit at the finish, and the zeppelin's own piece.
The music dims under pause and jingles.

## Tuning

Everything gameplay-related is exposed in the Inspector:

- **`Assets/MiniPlanes/Levels/Level1..7.asset`**: speed, path length and shape, the arc in front of the
  player, line preview timings (`startRevealSeconds`, `streakToReveal`, `streakRevealSeconds`), fuel
  (`fuelDrainPerSecond`, `ringsToFillTank`), fighters, balloons, turret (`turretDurationSeconds`,
  `turretFireInterval`, `turretHitRadius`), booster tubes and boost, islands, bombers, and the zeppelin
  (`bossLevel` and the `boss...` and `missile...` values, such as `missileHoldSeconds`).
- **`Assets/MiniPlanes/Levels/Infinite.asset`**: start values and hard caps for the wave curve, and
  `rampWaves` for how quickly it ramps; when tubes and islands start; `towerStepWaves` and `capTowers` for jamming towers; `bossEvery` and `bossMissilesFrom`.
- **`HandRing`** on each hand: C detection, fill time, `visualRadiusScale` (the bubble's size), `shapeFollow`
  (how closely it follows the fingers). **`RingMerger`** on `Game/RingMerger`: how close
  the hands must be to merge, merge time, big ring size. **`BubbleRingVisual`**: wobble and float.
- **`FlickDetector`** on each hand: flick speed and reach. **`BalloonGrabber`**: pinch pick-up radius.
- **`CurvedHud`** on `CockpitHUD`: placement, follow speed, layout, colours.
- **`WristMenuButton`** on `WristMenu`: how directly the palm must face you, button size and placement.
  **`PausePanel`** on `PausePanel`: reach, follow speed, button layout, poke depth.
- **`Assets/MiniPlanes/Levels/Planes.asset`**: the Hangar's planes, prices, perks, paint palettes and where
  the turret mounts on each.
- **`GameManager`**: points and coins per action; `devUnlockAll` opens every level and `devUnlockAllPlanes`
  every plane, without saving anything; `devCoinsOnStart` tops up the coins; `devResetProgressOnStart` wipes
  saved progress. All are off in shipped settings.

## UI prefabs

Every piece of UI is a prefab in `Assets/MiniPlanes/Prefabs/UI`, built from ordinary uGUI Images and Texts,
so you can restyle them with your own sprites and fonts:

| Prefab | What it is |
|---|---|
| `MenuPanel` | Start page, level intro with its Start button, result page and Hangar |
| `CockpitHUD` | Progress bar with a sliding plane knob and a finish flag, score, level, lives and coins, plus the caption line |
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
bullets, flicks, balloons, the turret, plane perks, booster tubes, islands, bomb arming, chaining, covering,
coins, Infinite's waves and the zeppelin. Results are logged as
`[AUTOPILOT] PASS` or `FAIL`. Headless, leave out `-quit`, and the exit code is 0 only if all pass:

```
Unity.exe -batchmode -nographics -buildTarget Android -projectPath D:/Unity/MiniPlanes -executeMethod MiniPlanes.Editor.MiniPlanesAutopilot.Run -logFile auto.log
```

Add `-mpShotsDir <folder>` and drop `-nographics` to render preview PNGs of the menus, models, rings,
HUD, Hangar, stencil reveal, comic VFX, booster tubes, islands and the zeppelin instead.

## How it works

| Script | Role |
|---|---|
| `Core/GameManager.cs` | Menu, levels, unlocks, Infinite waves, scoring. |
| `Core/LevelConfig.cs`, `InfiniteConfig.cs` | All tuning; the Infinite difficulty curve with caps. |
| `Core/FlightPathGenerator.cs` | A virtual pilot flies random manoeuvres within the front arc; `randomness` sets how wild. |
| `Core/CatmullRomPath.cs` | Arc-length spline, so the plane flies at constant speed. |
| `Core/PathRevealer.cs` | The faint line, ahead of the plane only: always on, timed, or brought back by a ring streak. |
| `Core/MiniPlane.cs` | Flight, fuel, shield, turret, hits, crash and respawn, plane model and paint. |
| `Core/PlaneTurret.cs` | The automatic turret: intercept aiming, homing rounds, 6 s life. |
| `Core/PlaneCatalog.cs`, `PlanePaint.cs` | The Hangar's planes, perks and palettes, and applying paint. |
| `Core/ComicVfx.cs` | Pooled hand-drawn effects: onset, peak, breakup frames. |
| `Core/Progression.cs` | Saved unlocks, high score, coins, owned planes and paint. |
| `Core/GameArt.cs` | References to all materials, models, sprites and fonts. |
| `Hands/HandSource.cs`, `MetaHandSource.cs`, `FakeHandSource.cs` | Hand input: abstract, Meta, simulated. |
| `Hands/HandRing.cs`, `BubbleRingVisual.cs`, `RingMerger.cs` | C detection, bubble portal, big O merge. |
| `Hands/HandHazard.cs`, `FlickDetector.cs`, `BalloonGrabber.cs` | Rock hands, flicks, pinch-drag and tube holding. |
| `Hands/HandMotion.cs` | Shared hand speed, to tell a gentle hold from a smack. |
| `Enemies/EnemyPlane.cs`, `BomberPlane.cs`, `Projectile.cs` | Fighters, bombers, machine-gun rounds. |
| `Enemies/PowerBalloon.cs`, `BombBalloon.cs`, `EnemySpawner.cs` | Balloons and spawning. |
| `Enemies/BoosterTube.cs` | Booster tubes: their shapes, holding, and the clean-pass test. |
| `Enemies/Zeppelin.cs`, `BossMissile.cs` | The boss and its attacks; palm-held missiles. |
| `World/FloatingIslands.cs` | Infinite's islands, their shards and dust. |
| `World/JammingTower.cs` | Towers on the islands that silence the enemy warning. |
| `Audio/GameSounds.cs`, `Audio/Sfx.cs` | The sound list, and the pooled player every event calls. |
| `UI/CurvedCanvas.cs`, `CurvedGraphic.cs`, `LazyFollow.cs` | Bending uGUI onto a cylinder; smooth follow. |
| `UI/PokeCanvas.cs`, `PokeButton.cs`, `PokeScrollArea.cs` | Fingertip input for any canvas. |
| `UI/CurvedHud.cs`, `UI/MenuPanel.cs` | The HUD, and the menu with its intro, result and Hangar pages. |
| `UI/WristMenuButton.cs`, `UI/PausePanel.cs` | Palm-up wrist button; curved lazy-follow pause menu. |

Shaders in `Assets/MiniPlanes/Shaders`: `Matte` (printed polymer), `BubbleRing` (radial fill, wobble,
peanut merge, finger-shaped outline), `OccludedSilhouette` (solid plane reveal behind hands), `PathLine`,
`ComicSprite`, `ShieldBubble` (also the tubes' glass), `Trail` (the boost streak). `UIComic` draws the menu, pause, wrist and splash panels and buttons as solid paint with an ink outline, a top highlight and a bottom shade, read from the `RoundedComic` sprite's channels; `UiComic.Apply` swaps it in at start and adds a `ComicShadow` drop shadow behind each. `Matte` can also repaint a model's colourful parts, which the Hangar palettes
and the gold balloon use.

The behind-the-hand reveal works in three steps. The hand material writes stencil 1. Aircraft clear it
where they are visible. The silhouette then draws only where it is behind existing depth and the
stencil still says that depth belongs to a hand.

## Art

Style references are in `Docs/ArtBible`, from the Codex art bible session. VFX cuts and fonts are in
`Assets/MiniPlanes/Art`. The models were generated on the Tripo website and baked to about 5,000 triangles: the
three player planes, two enemies, the turret and the zeppelin come from the reference sheets in `Docs/ArtBible`
and use a small texture; the balloons use vertex colours. Islands, tubes and missiles are built in code. The gold turret balloon is the blue one repainted in game.
`Tools/Tripo/web_generations.md` records the prompts, task IDs and how to rebuild them.
The original Blender versions are kept in `Tools/Art/standins`.

Font: Chakra Petch, SIL Open Font License (`Assets/MiniPlanes/Art/Fonts/OFL.txt`).
