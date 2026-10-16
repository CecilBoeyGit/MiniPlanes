using System;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>Spawns enemy planes on a sphere around the player while a round is in progress.</summary>
    public class EnemySpawner : MonoBehaviour
    {
        public Transform head;
        public Material enemyBodyMaterial;
        public Material enemyWingMaterial;
        public Material projectileMaterial;
        [Tooltip("Distance from the head at which enemies appear, in metres.")]
        public float spawnRadius = 1.5f;
        public float minElevationDeg = -12f;
        public float maxElevationDeg = 18f;

        public event Action<EnemyPlane> EnemySpawned;

        LevelConfig _cfg;
        MiniPlane _target;
        bool _active;
        float _timer;
        int _seed;

        public void Begin(LevelConfig cfg, MiniPlane target, int seed)
        {
            _cfg = cfg;
            _target = target;
            _seed = seed * 7919;
            _active = cfg != null && cfg.maxEnemies > 0;
            _timer = _active ? cfg.enemySpawnInterval * 0.4f : 0f;
        }

        public void End()
        {
            _active = false;
            EnemyPlane.DestroyAll();
            Projectile.DestroyAll();
        }

        void Update()
        {
            if (!_active || _cfg == null) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = _cfg.enemySpawnInterval;
            if (EnemyPlane.Active.Count >= _cfg.maxEnemies) return;
            Spawn();
        }

        void Spawn()
        {
            _seed++;
            var rng = new System.Random(_seed);
            Vector3 centre = head != null ? head.position : new Vector3(0f, 1.1f, 0f);
            float az = (float)rng.NextDouble() * 360f;
            float el = Mathf.Lerp(minElevationDeg, maxElevationDeg, (float)rng.NextDouble());
            Vector3 dir = Quaternion.Euler(-el, az, 0f) * Vector3.forward;
            Vector3 pos = centre + dir * spawnRadius;

            var go = new GameObject("EnemyPlane");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var enemy = go.AddComponent<EnemyPlane>();
            enemy.Setup(_cfg, _target, head, enemyBodyMaterial, enemyWingMaterial, projectileMaterial, _seed);
            EnemySpawned?.Invoke(enemy);
        }
    }
}
