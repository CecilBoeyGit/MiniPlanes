using UnityEngine;

namespace MiniPlanes
{
    public enum PathRevealMode
    {
        [Tooltip("The faint line stays on for the whole level.")] AlwaysVisible,
        [Tooltip("The line shows at the start for a set time, and again after a streak of ring passes.")] TimedAtStart,
        [Tooltip("The line is never shown.")] Hidden
    }

    /// <summary>
    /// Every gameplay number for one level. Create via Assets > Create > MiniPlanes > Level Config.
    /// The six tutorial assets live in Assets/MiniPlanes/Levels and are safe to edit: rebuilding the
    /// scene keeps your values unless <see cref="CurrentDefaultsVersion"/> is bumped in code.
    /// </summary>
    [CreateAssetMenu(fileName = "Level", menuName = "MiniPlanes/Level Config")]
    public class LevelConfig : ScriptableObject
    {
        public const int CurrentDefaultsVersion = 9;
        [HideInInspector] public int defaultsVersion;

        [Header("Identity")]
        public string levelName = "Level";
        public int levelNumber = 1;
        [TextArea] public string introCaption = "";

        [Header("Passing the level")]
        [Tooltip("Ring passes needed by the time the plane finishes its path. 0 means simply surviving passes.")]
        public int requiredRingPasses = 2;
        [Tooltip("Hits the plane can take before it crashes. 1 crashes on the first hit; more makes a level low risk.")]
        public int hitsToCrash = 1;
        [Tooltip("Crashes allowed before the level is lost. After each crash the plane comes back where it fell.")]
        public int lives = 3;

        [Header("Flight")]
        [Tooltip("Flight speed in metres per second.")]
        public float planeSpeed = 0.22f;
        [Tooltip("Roughly how long the plane takes to fly the whole path, in seconds.")]
        public float pathDurationSeconds = 22f;
        [Tooltip("Pause before take-off, in seconds, while the plane waits and the path can be studied.")]
        public float preFlightSeconds = 3f;

        [Header("Path shape (relative to the player's head when the level starts)")]
        [Tooltip("Total width of the arc in front of the player the path stays inside, in degrees. Max 156.")]
        public float frontArcDeg = 110f;
        [Range(0f, 1f), Tooltip("0 is wide, gentle sweeps. Higher picks targets more erratically and turns tighter.")]
        public float randomness = 0.2f;
        [Tooltip("The plane's usual turning radius, in metres. Bigger is calmer and easier to follow. 0 works it out from randomness.")]
        public float turnRadius = 0f;
        [Tooltip("Allows corkscrews, loops and orbits. They make the path dense, so the tutorial levels leave them off.")]
        public bool allowLoops = false;
        public float minRadius = 0.36f;
        public float maxRadius = 0.56f;
        [Tooltip("Vertical range relative to eye height, in metres.")]
        public float minHeight = -0.32f;
        public float maxHeight = 0.06f;

        [Header("Flight path preview")]
        public PathRevealMode revealMode = PathRevealMode.AlwaysVisible;
        [Tooltip("Seconds the line stays visible after take-off in TimedAtStart mode.")]
        public float startRevealSeconds = 3f;
        [Tooltip("Consecutive ring passes that bring the line back. 0 turns this off.")]
        public int streakToReveal = 3;
        [Tooltip("Seconds the line stays visible after a streak.")]
        public float streakRevealSeconds = 2.5f;
        [Tooltip("A streak resets if no ring pass happens for this many seconds.")]
        public float streakResetSeconds = 12f;
        [Range(0f, 1f), Tooltip("How visible the line is. Lower is fainter.")]
        public float pathAlpha = 0.4f;
        public float revealFadeSeconds = 0.4f;

        [Header("Fuel")]
        public bool useFuel = false;
        public float fuelMax = 100f;
        [Tooltip("Fuel lost per second, at a constant rate. 5 of 100 gives a 20 second tank.")]
        public float fuelDrainPerSecond = 5f;
        [Tooltip("Ring passes that fill the tank from empty to full. Each pass adds max / this.")]
        public int ringsToFillTank = 3;
        [Range(0f, 1f)] public float startFuelFraction = 1f;
        [Range(0f, 1f), Tooltip("Below this the gauge flashes.")]
        public float lowFuelFraction = 0.3f;

        [Header("Fighters (level 1 enemies, machine guns)")]
        public int maxFighters = 0;
        public float fighterSpawnInterval = 7f;
        public float fighterSpeed = 0.3f;
        [Tooltip("Rounds per burst.")]
        public int burstCount = 3;
        [Tooltip("Seconds between rounds within a burst.")]
        public float burstSpacing = 0.14f;
        [Tooltip("Seconds between bursts.")]
        public float burstInterval = 2.6f;
        public float projectileSpeed = 0.7f;
        [Tooltip("Fighters open fire only when closer than this to the plane, in metres.")]
        public float fireRange = 0.55f;

        [Header("Balloons")]
        public bool useBalloons = false;
        [Tooltip("Average seconds between blue shield balloons.")]
        public float shieldBalloonInterval = 11f;
        public float balloonLifetime = 16f;
        [Tooltip("How slowly a balloon drifts to its crosshair: seconds to cover most of the distance.")]
        public float balloonFollowSeconds = 1.6f;
        [Tooltip("Seconds of immunity after the shield absorbs a hit.")]
        public float shieldGraceSeconds = 1f;
        [Tooltip("Average seconds between gold turret balloons. 0 turns them off.")]
        public float turretBalloonInterval = 0f;
        [Tooltip("Most blue balloons in one flight. -1 means no limit.")]
        public int maxShieldBalloons = -1;
        [Tooltip("Most gold balloons in one flight. -1 means no limit.")]
        public int maxTurretBalloons = -1;

        [Header("Turret (from the gold balloon, or the Bluebird's own)")]
        [Tooltip("How long a fitted turret lasts, in seconds.")]
        public float turretDurationSeconds = 6f;
        [Tooltip("The turret fires at enemies closer than this to the plane, in metres.")]
        public float turretRange = 0.75f;
        [Tooltip("Seconds between turret shots.")]
        public float turretFireInterval = 0.12f;
        public float turretShotSpeed = 1.6f;
        [Tooltip("A turret round downs an enemy whose centre passes within this distance, in metres.")]
        public float turretHitRadius = 0.024f;
        [Tooltip("How hard a round curves toward its target, in degrees per second.")]
        public float turretShotHomingDeg = 300f;

        [Header("Booster tubes")]
        [Tooltip("Average seconds between booster tubes. 0 turns them off.")]
        public float boosterTubeInterval = 0f;
        [Tooltip("Most booster tubes in one flight. -1 means no limit.")]
        public int maxBoosterTubes = -1;
        [Tooltip("Seconds a booster tube lasts. Holding it keeps it.")]
        public float boosterTubeLifetime = 24f;
        [Tooltip("Speed multiplier after flying cleanly through a tube.")]
        public float boostSpeedMultiplier = 1.7f;
        public float boostSeconds = 3f;

        [Header("Floating islands")]
        [Tooltip("Islands floating around the path. 0 turns them off.")]
        public int islandCount = 0;
        public float islandMinSize = 0.06f;
        public float islandMaxSize = 0.12f;
        [Tooltip("Gap kept between an island and the flight path, in metres.")]
        public float islandPathClearance = 0.06f;

        [Header("Zeppelin boss")]
        [Tooltip("0 means no boss. 1 is the first, gentlest zeppelin; higher numbers are tougher.")]
        public int bossLevel = 0;
        [Tooltip("Whether the zeppelin fires tracking missiles.")]
        public bool bossMissiles = false;
        [Tooltip("Bullet trails in each barrage.")]
        public int bossTrails = 4;
        [Tooltip("Rounds in each trail.")]
        public int bossTrailRounds = 6;
        public float bossRoundSpacing = 0.14f;
        public float bossBarrageSeconds = 5f;
        public float bossBulletSpeed = 0.75f;
        [Tooltip("How far aimed trails may be off a perfect shot, in degrees. The first trail of a barrage is always dead on; one wanders.")]
        public float bossAimSpreadDeg = 14f;
        [Tooltip("Fighters launched from the zeppelin's decks at a time.")]
        public int bossFighterBatch = 2;
        public int bossMissileCount = 1;
        [Tooltip("Pause between the zeppelin's attacks, in seconds.")]
        public float bossRestSeconds = 3f;
        public float missileSpeed = 0.28f;
        public float missileTurnRateDeg = 70f;
        [Tooltip("How long a palm must hold a missile to make it drop, in seconds.")]
        public float missileHoldSeconds = 1.5f;
        [Tooltip("A hand moving faster than this sets a missile off instead of holding it, in metres per second.")]
        public float missileSmackSpeed = 0.9f;
        [Tooltip("The plane within this distance of a missile blast takes damage, in metres.")]
        public float missileBlastRadius = 0.14f;
        public float missileLifetime = 16f;

        [Header("Bombers (level 2 enemies)")]
        public int maxBombers = 0;
        public float bomberSpawnInterval = 14f;
        public float bomberSpeed = 0.3f;
        [Tooltip("Speed multiplier once the bomber has dropped its balloon and flies away.")]
        public float bomberExitMultiplier = 1.4f;
        [Tooltip("Lead time, in seconds, between the balloon appearing and the plane reaching it.")]
        public float bombLeadSeconds = 3f;
        [Tooltip("Sideways distance of the balloon from the flight path, in metres.")]
        public float bombPathOffset = 0.07f;
        [Tooltip("A bomber destroyed closer than this to its balloon sets the balloon off, in metres.")]
        public float explosionRange = 0.3f;
        [Tooltip("The plane within this distance arms the balloon, which starts flashing, in metres.")]
        public float bombArmProximity = 0.18f;
        [Tooltip("Seconds from arming to detonation.")]
        public float bombFuseSeconds = 2f;
        [Tooltip("The plane within this distance of a balloon blast takes damage, in metres.")]
        public float bombBlastRadius = 0.3f;
        [Tooltip("Both palms must be within this distance, on opposite sides of the balloon, to contain it.")]
        public float bombCoverDistance = 0.13f;
        [Tooltip("How long both hands must hold the cover, in seconds.")]
        public float bombCoverHoldSeconds = 0.2f;
        [Tooltip("An unarmed balloon disappears after this many seconds.")]
        public float bombLifetime = 18f;

        /// <summary>The fuel one ring pass adds.</summary>
        public float FuelPerRing => fuelMax / Mathf.Max(1, ringsToFillTank);

        /// <summary>
        /// Sets the zeppelin's tuning for a boss of the given strength: 1 is the first boss. Each step adds
        /// trails, rounds, fighters and missiles, up to caps.
        /// </summary>
        public void SetBoss(int strength, bool missiles)
        {
            bossLevel = Mathf.Max(0, strength);
            bossMissiles = missiles;
            if (bossLevel == 0) return;
            int k = bossLevel;
            bossTrails = Mathf.Min(3 + k, 8);
            bossTrailRounds = Mathf.Min(5 + k, 10);
            bossRoundSpacing = Mathf.Max(0.09f, 0.15f - 0.01f * k);
            bossBarrageSeconds = 5f;
            bossBulletSpeed = Mathf.Min(0.7f + 0.05f * k, 1f);
            bossAimSpreadDeg = Mathf.Max(1f, 3f - 0.5f * k);
            bossFighterBatch = Mathf.Min(1 + k, 4);
            bossMissileCount = Mathf.Clamp(k - 1, 1, 3);
            bossRestSeconds = Mathf.Max(1.4f, 3.2f - 0.3f * k);
            missileSpeed = Mathf.Min(0.26f + 0.02f * k, 0.38f);
            missileTurnRateDeg = Mathf.Min(60f + 8f * k, 110f);
        }

        /// <summary>Fills in the shipped tuning for a tutorial level, 1 to 7.</summary>
        public void ApplyDefaults(int level)
        {
            defaultsVersion = CurrentDefaultsVersion;
            levelNumber = level;
            switch (level)
            {
                case 1:
                    levelName = "First Flight";
                    introCaption = "Follow the faint line.\nMake a C with finger and thumb, and let the plane fly through.";
                    requiredRingPasses = 2;
                    hitsToCrash = 5;
                    planeSpeed = 0.2f;
                    pathDurationSeconds = 20f;
                    preFlightSeconds = 5f;
                    frontArcDeg = 100f;
                    randomness = 0.1f;
                    turnRadius = 0.14f;
                    revealMode = PathRevealMode.AlwaysVisible;
                    break;
                case 2:
                    levelName = "Eyes Open";
                    introCaption = "Memorise the line. It fades soon after take-off.\nThree rings in a row bring it back.";
                    requiredRingPasses = 3;
                    hitsToCrash = 4;
                    planeSpeed = 0.25f;
                    pathDurationSeconds = 26f;
                    frontArcDeg = 110f;
                    randomness = 0.12f;
                    turnRadius = 0.135f;
                    revealMode = PathRevealMode.TimedAtStart;
                    break;
                case 3:
                    levelName = "Running on Fumes";
                    introCaption = "The plane burns fuel.\nEvery ring tops it up. Three rings fill the tank.";
                    requiredRingPasses = 3;
                    hitsToCrash = 2;
                    planeSpeed = 0.27f;
                    pathDurationSeconds = 32f;
                    frontArcDeg = 120f;
                    randomness = 0.12f;
                    turnRadius = 0.13f;
                    revealMode = PathRevealMode.TimedAtStart;
                    useFuel = true;
                    break;
                case 4:
                    levelName = "Bandits";
                    introCaption = "Enemy fighters shoot at your plane.\nBlock bullets with your hands. Flick fighters to destroy them.";
                    requiredRingPasses = 3;
                    hitsToCrash = 1;
                    planeSpeed = 0.28f;
                    pathDurationSeconds = 34f;
                    frontArcDeg = 125f;
                    randomness = 0.14f;
                    turnRadius = 0.13f;
                    revealMode = PathRevealMode.TimedAtStart;
                    useFuel = true;
                    maxFighters = 2;
                    break;
                case 5:
                    levelName = "Fair Winds";
                    introCaption = "Pinch a powerup and drag it onto the flight path.\nBlue shields one hit, gold fits a turret. Line a tube up with the path for a speed boost.";
                    requiredRingPasses = 3;
                    hitsToCrash = 1;
                    planeSpeed = 0.3f;
                    pathDurationSeconds = 36f;
                    frontArcDeg = 130f;
                    randomness = 0.15f;
                    turnRadius = 0.13f;
                    revealMode = PathRevealMode.TimedAtStart;
                    useFuel = true;
                    maxFighters = 1;
                    fighterSpawnInterval = 11f;
                    useBalloons = true;
                    turretBalloonInterval = 18f;
                    boosterTubeInterval = 12f;
                    break;
                case 7:
                    levelName = "Zeppelin";
                    introCaption = "A giant zeppelin attacks from afar. It can't be destroyed.\nBlock its bullets, flick its fighters, and hold a missile in your palm until it drops.";
                    requiredRingPasses = 2;
                    hitsToCrash = 2;
                    planeSpeed = 0.26f;
                    pathDurationSeconds = 55f;
                    frontArcDeg = 120f;
                    randomness = 0.12f;
                    turnRadius = 0.135f;
                    revealMode = PathRevealMode.AlwaysVisible;
                    useBalloons = true;
                    shieldBalloonInterval = 14f;
                    SetBoss(1, true);
                    break;
                default:
                    levelName = "Bomb Run";
                    introCaption = "Bombers drop red balloons near your path, then fly off.\nFlick a bomber for extra points. Cup a flashing balloon with both hands.";
                    requiredRingPasses = 4;
                    hitsToCrash = 1;
                    planeSpeed = 0.32f;
                    pathDurationSeconds = 40f;
                    frontArcDeg = 135f;
                    randomness = 0.16f;
                    turnRadius = 0.125f;
                    revealMode = PathRevealMode.TimedAtStart;
                    useFuel = true;
                    maxFighters = 1;
                    fighterSpawnInterval = 13f;
                    useBalloons = true;
                    turretBalloonInterval = 16f;
                    maxBombers = 1;
                    break;
            }
        }
    }
}
