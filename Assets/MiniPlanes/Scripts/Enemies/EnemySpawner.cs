using System;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>Spawns fighters, bombers and balloons in front of the player during a flight.</summary>
    public class EnemySpawner : MonoBehaviour
    {
        public Transform head;
        public GameArt art;
        public HandSource[] hands;

        [Tooltip("Distance from the head at which enemies appear, in metres.")]
        public float spawnRadius = 1.4f;
        [Tooltip("Enemies appear within this angle either side of straight ahead, in degrees.")]
        public float spawnHalfArcDeg = 75f;
        public float minElevationDeg = -12f;
        public float maxElevationDeg = 15f;

        [Header("Balloon placement, relative to the head")]
        public float balloonHalfArcDeg = 55f;
        public float balloonMinRadius = 0.32f;
        public float balloonMaxRadius = 0.52f;
        public float balloonMinHeight = -0.25f;
        public float balloonMaxHeight = 0.05f;

        public event Action<EnemyPlane> EnemySpawned;

        LevelConfig _cfg;
        MiniPlane _target;
        Vector3 _forward = Vector3.forward;
        bool _active;
        float _fighterTimer, _bomberTimer, _shieldTimer, _turretTimer;
        int _seed;
        System.Random _rng = new System.Random(1);

        public void Begin(LevelConfig cfg, MiniPlane target, Vector3 forward, int seed)
        {
            _cfg = cfg;
            _target = target;
            _seed = seed * 7919;
            _rng = new System.Random(_seed);
            _forward = forward;
            _forward.y = 0f;
            if (_forward.sqrMagnitude < 1e-4f) _forward = Vector3.forward;
            _forward.Normalize();
            _active = cfg != null;
            if (!_active) return;
            _fighterTimer = cfg.fighterSpawnInterval * 0.4f;
            _bomberTimer = cfg.bomberSpawnInterval * 0.5f;
            _shieldTimer = cfg.shieldBalloonInterval * 0.4f;
            _turretTimer = cfg.turretBalloonInterval * 0.6f;
        }

        public void End()
        {
            _active = false;
            EnemyPlane.DestroyAll();
            Projectile.DestroyAll();
            PowerBalloon.DestroyAll();
            BombBalloon.DestroyAll();
            TurretShot.DestroyAll();
        }

        void Update()
        {
            if (!_active || _cfg == null) return;
            float dt = Time.deltaTime;

            if (_cfg.maxFighters > 0)
            {
                _fighterTimer -= dt;
                if (_fighterTimer <= 0f)
                {
                    _fighterTimer = _cfg.fighterSpawnInterval;
                    if (CountAlive(typeof(EnemyPlane)) < _cfg.maxFighters) SpawnFighter();
                }
            }

            if (_cfg.maxBombers > 0)
            {
                _bomberTimer -= dt;
                if (_bomberTimer <= 0f)
                {
                    _bomberTimer = _cfg.bomberSpawnInterval;
                    if (CountAlive(typeof(BomberPlane)) < _cfg.maxBombers) SpawnBomber();
                }
            }

            if (_cfg.useBalloons)
            {
                _shieldTimer -= dt;
                if (_shieldTimer <= 0f)
                {
                    _shieldTimer = _cfg.shieldBalloonInterval * Jitter();
                    SpawnBalloon(BalloonKind.Shield);
                }
                if (_cfg.turretBalloonInterval > 0f)
                {
                    _turretTimer -= dt;
                    if (_turretTimer <= 0f)
                    {
                        _turretTimer = _cfg.turretBalloonInterval * Jitter();
                        SpawnBalloon(BalloonKind.Turret);
                    }
                }
            }
        }

        float Jitter()
        {
            return Mathf.Lerp(0.75f, 1.25f, (float)_rng.NextDouble());
        }

        int CountAlive(Type exact)
        {
            int n = 0;
            foreach (var e in EnemyPlane.Active)
                if (e != null && e.IsAlive && e.GetType() == exact) n++;
            return n;
        }

        Vector3 EnemySpawnPoint()
        {
            Vector3 centre = head != null ? head.position : new Vector3(0f, 1.1f, 0f);
            float az = Mathf.Lerp(-spawnHalfArcDeg, spawnHalfArcDeg, (float)_rng.NextDouble());
            float el = Mathf.Lerp(minElevationDeg, maxElevationDeg, (float)_rng.NextDouble());
            Vector3 dir = Quaternion.AngleAxis(az, Vector3.up) * _forward;
            return centre + (dir * Mathf.Cos(el * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(el * Mathf.Deg2Rad)) * spawnRadius;
        }

        public EnemyPlane SpawnFighter()
        {
            _seed++;
            var go = new GameObject("Fighter");
            go.transform.SetParent(transform, false);
            go.transform.position = EnemySpawnPoint();
            var enemy = go.AddComponent<EnemyPlane>();
            enemy.Setup(_cfg, _target, head, art, _seed);
            EnemySpawned?.Invoke(enemy);
            return enemy;
        }

        public BomberPlane SpawnBomber()
        {
            _seed++;
            var go = new GameObject("Bomber");
            go.transform.SetParent(transform, false);
            go.transform.position = EnemySpawnPoint();
            var bomber = go.AddComponent<BomberPlane>();
            bomber.Hands = hands;
            bomber.Setup(_cfg, _target, head, art, _seed);
            EnemySpawned?.Invoke(bomber);
            return bomber;
        }

        public PowerBalloon SpawnBalloon(BalloonKind kind = BalloonKind.Shield)
        {
            Vector3 centre = head != null ? head.position : new Vector3(0f, 1.1f, 0f);
            float az = Mathf.Lerp(-balloonHalfArcDeg, balloonHalfArcDeg, (float)_rng.NextDouble());
            float radius = Mathf.Lerp(balloonMinRadius, balloonMaxRadius, (float)_rng.NextDouble());
            float height = Mathf.Lerp(balloonMinHeight, balloonMaxHeight, (float)_rng.NextDouble());
            Vector3 pos = centre + Quaternion.AngleAxis(az, Vector3.up) * _forward * radius + Vector3.up * height;

            var go = new GameObject(kind == BalloonKind.Shield ? "ShieldBalloon" : "TurretBalloon");
            go.transform.SetParent(transform, false);
            var balloon = go.AddComponent<PowerBalloon>();
            balloon.Setup(kind, _cfg, _target, art, pos);
            return balloon;
        }
    }
}
