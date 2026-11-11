using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Difficulty curve of the Infinite mode. Each number eases from its start value toward a hard cap,
    /// so later waves keep getting tougher but never become humanly impossible.
    /// </summary>
    [CreateAssetMenu(fileName = "Infinite", menuName = "MiniPlanes/Infinite Config")]
    public class InfiniteConfig : ScriptableObject
    {
        [Tooltip("Tuning the waves start from: fuel, balloon, bomber and fighter details not listed below.")]
        public LevelConfig baseLevel;

        [Header("Curve")]
        [Tooltip("Waves taken to cover about 63% of the way from start to cap. Larger is gentler.")]
        public float rampWaves = 6f;
        [Tooltip("The path's shape and speed ramp this many times more slowly than the enemies: 2 makes wave 5's path what wave 3's used to be.")]
        public float pathRampStretch = 2f;

        [Header("Wave length")]
        public float waveDurationSeconds = 32f;
        public int ringsRequiredPerWave = 0;

        [Header("Plane")]
        public float startSpeed = 0.25f;
        public float capSpeed = 0.52f;
        public float startRandomness = 0.1f;
        public float capRandomness = 0.85f;
        public float startArcDeg = 105f;
        public float capArcDeg = 165f;

        [Tooltip("Lives at the start of an Infinite run. They carry over from wave to wave.")]
        public int lives = 3;
        [Tooltip("Coins for every wave survived.")]
        public int coinsPerWave = 5;

        [Header("Respawn (bought with coins on the game-over page)")]
        [Tooltip("Coins for the first respawn in a run.")]
        public int respawnBaseCost = 50;
        [Tooltip("Each further respawn in the same run costs this much more.")]
        public int respawnCostStep = 50;
        [Tooltip("Every wave reached adds this much to the price, so a respawn deep into a run costs more.")]
        public int respawnCostPerWave = 10;
        [Tooltip("Lives a respawn gives back.")]
        public int respawnLives = 1;

        /// <summary>
        /// The price of the next respawn: the base, plus a step for each respawn already bought in the run, plus
        /// a little for every wave reached.
        /// </summary>
        public int RespawnCost(int bought, int wave)
        {
            return Mathf.Max(0, respawnBaseCost + respawnCostStep * Mathf.Max(0, bought) + respawnCostPerWave * Mathf.Max(0, wave - 1));
        }

        [Header("Path")]
        public float startTurnRadius = 0.145f;
        public float capTurnRadius = 0.08f;
        [Tooltip("Corkscrews, loops and orbits are allowed from this wave on.")]
        public int loopsFromWave = 8;

        [Header("Fuel and balloons")]
        public int fuelFromWave = 1;
        public int balloonsFromWave = 3;

        [Header("Powerup balloons")]
        [Tooltip("Average seconds between blue shield balloons in a wave.")]
        public float shieldInterval = 14f;
        public int maxShieldsPerWave = 2;
        [Tooltip("The gold turret balloon is rare: one, on every this many waves.")]
        public int turretEveryWaves = 3;
        [Tooltip("The small heart balloon, which gives a life back: one, on every this many waves.")]
        public int lifeEveryWaves = 2;

        [Header("Booster tubes (none in wave 1; waves 2-4: 1, then this many)")]
        public int tubesFromWave = 2;
        public int maxTubesPerWave = 2;

        [Header("Floating islands")]
        public int islandsFromWave = 4;
        public int startIslands = 3;
        public int capIslands = 8;
        [Tooltip("Jamming towers first stand on the islands' second wave; after that their number creeps up every this many waves.")]
        public int towerStepWaves = 3;
        public int capTowers = 3;

        [Header("Zeppelin boss waves")]
        [Tooltip("Every this many waves is a boss wave: 5 makes waves 5, 10, 15 and so on.")]
        public int bossEvery = 5;
        [Tooltip("The boss's missiles start from this boss on. 2 means the second boss wave.")]
        public int bossMissilesFrom = 2;
        public float bossWaveDurationSeconds = 48f;

        [Header("Fighters")]
        public int fightersFromWave = 2;
        public int capFighters = 3;
        public float startFighterInterval = 11f;
        public float capFighterInterval = 5f;
        public float capFighterSpeed = 0.42f;
        public float capBurstInterval = 1.8f;

        [Header("Bombers")]
        public int bombersFromWave = 4;
        public int capBombers = 2;
        public float startBomberInterval = 16f;
        public float capBomberInterval = 9f;

        /// <summary>0 at wave 1, approaching 1 as waves go on.</summary>
        public float Ramp(int wave)
        {
            return 1f - Mathf.Exp(-Mathf.Max(0, wave - 1) / Mathf.Max(0.5f, rampWaves));
        }

        /// <summary>
        /// 0 at wave 1, approaching 1, for the path's speed and shape. It moves more slowly than the enemy
        /// curve, so early waves fly simple, calm paths and the twists build up gradually.
        /// </summary>
        public float PathRamp(int wave)
        {
            float slowed = Mathf.Max(0f, wave - 1) / Mathf.Max(1f, pathRampStretch);
            return 1f - Mathf.Exp(-slowed / Mathf.Max(0.5f, rampWaves));
        }

        /// <summary>Writes wave settings into a runtime level instance.</summary>
        public void Apply(LevelConfig level, int wave)
        {
            float t = Ramp(wave);
            level.levelName = "Wave " + wave;
            level.levelNumber = wave;
            level.introCaption = "";
            level.requiredRingPasses = ringsRequiredPerWave;
            level.hitsToCrash = 1;
            level.preFlightSeconds = wave == 1 ? 3f : 1.5f;
            level.pathDurationSeconds = waveDurationSeconds;
            float pt = PathRamp(wave);
            level.planeSpeed = Mathf.Lerp(startSpeed, capSpeed, pt);
            level.randomness = Mathf.Lerp(startRandomness, capRandomness, pt);
            level.frontArcDeg = Mathf.Lerp(startArcDeg, capArcDeg, pt);
            level.revealMode = PathRevealMode.TimedAtStart;
            level.useFuel = wave >= fuelFromWave;
            level.turnRadius = Mathf.Lerp(startTurnRadius, capTurnRadius, pt);
            level.allowLoops = wave >= loopsFromWave;
            level.useBalloons = wave >= balloonsFromWave;
            level.shieldBalloonInterval = shieldInterval;
            level.maxShieldBalloons = maxShieldsPerWave;
            bool turretWave = level.useBalloons && turretEveryWaves > 0 && wave % turretEveryWaves == 0;
            level.maxTurretBalloons = turretWave ? 1 : 0;
            level.turretBalloonInterval = turretWave ? waveDurationSeconds * 0.5f : 0f;
            bool lifeWave = level.useBalloons && lifeEveryWaves > 0 && wave % lifeEveryWaves == 0;
            level.maxLifeBalloons = lifeWave ? 1 : 0;
            level.lifeBalloonInterval = lifeWave ? waveDurationSeconds * 0.8f : 0f;

            level.maxFighters = wave < fightersFromWave
                ? 0
                : Mathf.Clamp(1 + Mathf.FloorToInt(t * capFighters), 1, capFighters);
            level.fighterSpawnInterval = Mathf.Lerp(startFighterInterval, capFighterInterval, t);
            level.fighterSpeed = Mathf.Lerp(0.3f, capFighterSpeed, t);
            level.burstInterval = Mathf.Lerp(2.6f, capBurstInterval, t);

            level.maxBombers = wave < bombersFromWave
                ? 0
                : Mathf.Clamp(1 + Mathf.FloorToInt(t * (capBombers - 1) * 1.2f), 1, capBombers);
            level.bomberSpawnInterval = Mathf.Lerp(startBomberInterval, capBomberInterval, t);

            level.islandCount = wave < islandsFromWave
                ? 0
                : Mathf.Min(capIslands, startIslands + (wave - islandsFromWave) / 2);
            level.jammingTowers = TowersForWave(wave);

            int boss = BossNumber(wave);
            level.SetBoss(boss, boss >= bossMissilesFrom);
            if (boss > 0)
            {
                // The zeppelin brings its own fighters; nothing else attacks on a boss wave.
                level.levelName = "Boss wave " + wave;
                level.pathDurationSeconds = bossWaveDurationSeconds;
                level.maxFighters = 0;
                level.maxBombers = 0;
            }

            // Tubes are spread evenly through the wave, so the cap is reached but never crowded.
            int tubes = TubesForWave(wave);
            level.maxBoosterTubes = tubes;
            level.boosterTubeInterval = tubes > 0 ? level.pathDurationSeconds / (tubes + 1f) : 0f;
            level.maxTubesAtOnce = 2;
        }

        /// <summary>Booster tubes in a wave: none before tubesFromWave, 1 up to wave 4, then the cap.</summary>
        public int TubesForWave(int wave)
        {
            if (wave < tubesFromWave) return 0;
            if (wave <= 4) return Mathf.Min(1, maxTubesPerWave);
            return maxTubesPerWave;
        }

        /// <summary>
        /// The most jamming towers in a wave: none the first time the islands appear, one the second time, then
        /// slowly more, up to the cap. The islands randomise between this and one fewer.
        /// </summary>
        public int TowersForWave(int wave)
        {
            int appearance = wave - islandsFromWave;
            if (appearance < 1) return 0;
            return Mathf.Min(capTowers, 1 + (appearance - 1) / Mathf.Max(1, towerStepWaves));
        }

        /// <summary>0 for a normal wave, otherwise which boss wave this is: 1 for the first.</summary>
        public int BossNumber(int wave)
        {
            if (bossEvery <= 0 || wave <= 0 || wave % bossEvery != 0) return 0;
            return wave / bossEvery;
        }

        public static InfiniteConfig CreateDefault()
        {
            var cfg = CreateInstance<InfiniteConfig>();
            cfg.baseLevel = CreateInstance<LevelConfig>();
            cfg.baseLevel.ApplyDefaults(6);
            return cfg;
        }
    }
}
