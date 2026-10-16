# MiniPlanes

A seated mixed-reality mini-game for Meta Quest 3, built with Unity 6000.3.14f1, URP and the
Meta XR Core SDK on the Unity OpenXR backend. Hands only, no controllers.

A tiny procedurally generated aeroplane, about 4 cm across, flies a hidden randomised route that
circles you for several laps at arm's length, drifting in and out, climbing, diving and sometimes
doubling back. Make a C sign with your index finger and thumb and the loop becomes a scoring ring
(a closed OK sign works too). Fly the plane through the C for a point. Touch the plane with any
part of your hand, including the ring's own fingers, and it crashes, because your hands are rock.
From level 3 enemy planes chase and shoot the mini plane; flick them away with a fast hand swipe.

## Opening and building

1. Open `D:\Unity\MiniPlanes` in Unity 6000.3.14f1 with the Android build target.
2. The scene is `Assets/MiniPlanes/Scenes/MiniPlanes.unity`. It is already the only scene in
   Build Settings.
3. If settings or the scene ever need regenerating, use the menu
   `MiniPlanes > 0. Setup Everything` (it runs steps 1 and 2 below).
   - `MiniPlanes > 1. Configure Project Settings`: Android IL2CPP/ARM64/Vulkan, min SDK 32,
     GameActivity entry, XR Plug-in Management with OpenXR, Meta XR feature set, hands-only
     tracking at high frequency, passthrough required, OpenXR hand skeleton.
   - `MiniPlanes > 2. Build Game Scene`: rebuilds the scene, materials and level assets. Level
     assets are reset to the code defaults in `LevelConfig.ApplyDefaults` every time.
4. `MiniPlanes > 3. Build Android APK` writes `Builds/MiniPlanes.apk`. Install with
   `adb install -r Builds/MiniPlanes.apk`.

Everything can also run headless:

```
Unity.exe -batchmode -nographics -buildTarget Android -projectPath D:/Unity/MiniPlanes -executeMethod MiniPlanes.Editor.MiniPlanesBootstrap.SetupAll -quit
Unity.exe -batchmode -nographics -buildTarget Android -projectPath D:/Unity/MiniPlanes -executeMethod MiniPlanes.Editor.MiniPlanesBootstrap.BuildAndroid -quit
Unity.exe -batchmode -nographics -buildTarget Android -projectPath D:/Unity/MiniPlanes -executeMethod MiniPlanes.Editor.MiniPlanesBootstrap.SetupAndBuild -quit
```

## Testing in the editor

Play mode needs a hand-tracking source: Meta Quest Link with hand tracking enabled in the Quest
settings, or the Meta XR Simulator package. Without a headset the game still starts after a
6-second grace period with a fallback head position so the plane and path logic can be checked;
hands simply never track.

Tick `Show Path` on the `Game` object to draw the hidden route as a magenta line.

## How it works

| Script | Role |
|---|---|
| `Core/GameManager.cs` | Round and level state machine, score, lives, restarts. |
| `Core/FlightPathGenerator.cs` | Builds the hidden route: entry outside reach, several laps around the head with radius and height waves and occasional turn-backs, exit. |
| `Core/CatmullRomPath.cs` | Arc-length parameterised spline so the plane flies at constant speed. |
| `Core/MiniPlane.cs` | Path follower with banking and a tumble-to-the-ground crash. Collision radius is a fraction of the wingspan. |
| `Core/ProceduralPlaneMesh.cs` | Flat-shaded fuselage, wings, tail and canopy from a random seed, sized to fit through a C. |
| `Core/LevelConfig.cs` | ScriptableObject with per-level speed, laps, path envelope, enemy counts. Assets in `Levels/`. |
| `Hands/HandSource.cs` | Abstract hand input: joints and bone capsules. |
| `Hands/MetaHandSource.cs` | Adapter over `OVRHand` + `OVRSkeleton` (OpenXR 26-joint skeleton). |
| `Hands/HandRing.cs` | C-sign detection (tip gap, index curl, loop area), finger-polygon scoring and crash band. |
| `Hands/HandHazard.cs` | Bone-capsule collision: hand touches crash the plane and block bullets. |
| `Hands/FlickDetector.cs` | Fast hand motion knocks away enemies within reach. |
| `Enemies/EnemySpawner.cs`, `EnemyPlane.cs`, `Projectile.cs` | Enemy waves, pursuit with stand-off orbit, lead-aimed shots. |
| `UI/ScoreHud.cs` | World-space panel that follows the gaze. |

Collisions are all geometric (point-to-capsule distances, a plane-crossing test and a
point-in-polygon test on the real finger loop) rather than PhysX triggers, which is more reliable
at centimetre scale and frame-rate independent.

## Tuning

- C-sign sensitivity: `HandRing` on each hand. `maxGapEnter` / `maxGapExit` set how wide the
  thumb-index opening may be, `minCurlDeg` / `maxCurlDeg` set how curved the index must be, and
  `minLoopArea` rejects pinches and fists. `fingerRadius` widens or narrows the crash band around
  the loop.
- Plane size: `PlaneShapeParams.Friendly` in `ProceduralPlaneMesh.cs`; the collision radius follows
  from `MiniPlane.collisionRadiusPerSpan`.
- Flick strength: `FlickDetector.flickSpeed` (metres per second) and `reach`.
- Round length and shape: `laps`, `waypointSpacingDeg`, `reversalChance` and the radius and height
  ranges per level in `Assets/MiniPlanes/Levels/Level1..5.asset` (edit `ApplyDefaults` to make
  changes stick across regeneration).
- Path envelope: `FlightPathGenerator` (`entryRadius`, `headClearance`, `maxHeightStep`,
  `reversalOffset`).
