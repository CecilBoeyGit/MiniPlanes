using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Round and level state machine. Spawns the mini plane on a fresh hidden path each round,
    /// counts ring passes as points, hand contact as crashes, and advances levels.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class GameManager : MonoBehaviour
    {
        public enum Phase { WaitingForTracking, Countdown, Flying, RoundOver, GameOver }

        [Header("Scene references")]
        public Transform head;
        public FlightPathGenerator pathGenerator;
        public EnemySpawner spawner;
        public ScoreHud hud;
        public HandRing[] rings;
        public HandHazard[] hazards;
        public FlickDetector[] flickers;
        [Tooltip("Optional particle system played where the plane crashes.")]
        public ParticleSystem crashSmoke;

        [Header("Levels")]
        public List<LevelConfig> levels = new List<LevelConfig>();
        public int startingLives = 3;

        [Header("Plane materials")]
        public Material planeBodyMaterial;
        public Material planeWingMaterial;

        [Header("Timing")]
        public float countdownSeconds = 3f;
        public float roundEndDelay = 1.6f;
        public float gameOverDelay = 4f;

        [Header("Debug")]
        [Tooltip("Show the hidden flight path (editor testing only).")]
        public bool showPath = false;

        public Phase CurrentPhase { get; private set; } = Phase.WaitingForTracking;
        public int Score { get; private set; }
        public int Lives { get; private set; }
        public int LevelIndex { get; private set; }
        public int PointsThisLevel { get; private set; }
        public MiniPlane Plane => _plane;

        MiniPlane _plane;
        int _seed;
        bool _roundOver;
        string _roundMessage;

        LevelConfig CurrentLevel => levels != null && levels.Count > 0 ? levels[Mathf.Clamp(LevelIndex, 0, levels.Count - 1)] : null;

        void Awake()
        {
            if (levels == null || levels.Count == 0)
            {
                levels = new List<LevelConfig>();
                for (int i = 1; i <= 5; i++)
                {
                    var cfg = ScriptableObject.CreateInstance<LevelConfig>();
                    cfg.ApplyDefaults(i);
                    levels.Add(cfg);
                }
            }

            var planeGo = new GameObject("MiniPlane");
            planeGo.transform.SetParent(transform, false);
            _plane = planeGo.AddComponent<MiniPlane>();
            if (crashSmoke != null)
            {
                crashSmoke.transform.SetParent(planeGo.transform, false);
                crashSmoke.transform.localPosition = Vector3.zero;
            }
            _plane.Finished += OnPlaneFinished;
            _plane.Crashed += OnPlaneCrashed;

            if (rings != null)
                foreach (var r in rings)
                {
                    if (r == null) continue;
                    r.Scored += OnRingScored;
                    r.PlaneHitRing += OnRingHit;
                }
            if (hazards != null)
                foreach (var h in hazards)
                    if (h != null) h.PlaneHit += OnHazardHit;

            _seed = (int)(System.DateTime.Now.Ticks & 0x7fffffff);
        }

        void Start()
        {
            if (pathGenerator != null) pathGenerator.SetDebugDraw(showPath);
            StartCoroutine(GameLoop());
        }

        IEnumerator GameLoop()
        {
            ResetGame();
            while (true)
            {
                CurrentPhase = Phase.WaitingForTracking;
                hud?.SetMessage("Look straight ahead and make a C with your index finger and thumb\nto turn your hand into a ring.");
                float waited = 0f;
                while (!HeadReady() && waited < 6f)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                CurrentPhase = Phase.Countdown;
                float t = countdownSeconds;
                while (t > 0f)
                {
                    hud?.SetMessage($"Round starting in {Mathf.CeilToInt(t)}");
                    t -= Time.deltaTime;
                    yield return null;
                }

                StartRound();
                CurrentPhase = Phase.Flying;
                while (!_roundOver) yield return null;

                CurrentPhase = Phase.RoundOver;
                spawner?.End();
                hud?.SetMessage(_roundMessage);
                yield return new WaitForSeconds(roundEndDelay);

                if (Lives <= 0)
                {
                    CurrentPhase = Phase.GameOver;
                    hud?.SetMessage($"Game over!  Final score {Score}\nRestarting...");
                    yield return new WaitForSeconds(gameOverDelay);
                    ResetGame();
                }
            }
        }

        bool HeadReady()
        {
            return head != null && head.position.y > 0.3f;
        }

        Vector3 HeadPosition => HeadReady() ? head.position : new Vector3(0f, 1.1f, 0f);
        Vector3 HeadForward => head != null ? head.forward : Vector3.forward;

        void ResetGame()
        {
            Score = 0;
            Lives = startingLives;
            LevelIndex = 0;
            PointsThisLevel = 0;
            _plane.Hide();
            spawner?.End();
            RefreshStatus();
        }

        void StartRound()
        {
            var cfg = CurrentLevel;
            _seed++;
            var rng = new System.Random(_seed);

            var path = pathGenerator.Generate(cfg, HeadPosition, HeadForward, _seed);
            _plane.Build(PlaneShapeParams.Friendly(rng), planeBodyMaterial, planeWingMaterial);
            _plane.Launch(path, cfg.planeSpeed);

            if (rings != null) foreach (var r in rings) if (r != null) r.Track(_plane);
            if (hazards != null) foreach (var h in hazards) if (h != null) h.Track(_plane);
            spawner?.Begin(cfg, _plane, _seed);

            _roundOver = false;
            _roundMessage = string.Empty;
            hud?.ClearMessage();
            RefreshStatus();
        }

        void RefreshStatus()
        {
            hud?.SetStatus(Score, Lives, CurrentLevel != null ? CurrentLevel.levelName : string.Empty);
        }

        void OnPlaneFinished(MiniPlane plane)
        {
            if (_roundOver) return;
            _roundOver = true;
            _roundMessage = PointsThisLevel > 0 || Score > 0 ? "The plane got away." : "Missed! Put a ring on its path.";
        }

        void OnPlaneCrashed(MiniPlane plane, string reason)
        {
            if (_roundOver) return;
            _roundOver = true;
            Lives = Mathf.Max(0, Lives - 1);
            _roundMessage = $"Crashed: {reason}.";
            RefreshStatus();
        }

        void OnRingScored(HandRing ring)
        {
            if (_roundOver) return;
            Score++;
            PointsThisLevel++;
            hud?.SetMessage("+1", 0.6f);

            var cfg = CurrentLevel;
            if (cfg != null && PointsThisLevel >= cfg.pointsToAdvance && LevelIndex < levels.Count - 1)
            {
                LevelIndex++;
                PointsThisLevel = 0;
                hud?.SetMessage($"Level up!  {CurrentLevel.levelName}", 2f);
            }
            RefreshStatus();
        }

        void OnRingHit(HandRing ring)
        {
            _plane.Crash("clipped the ring");
        }

        void OnHazardHit(HandHazard hazard, MiniPlane plane)
        {
            plane.Crash("hit your hand");
        }
    }
}
