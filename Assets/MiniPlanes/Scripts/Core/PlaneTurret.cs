using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// The automatic turret from the gold balloon. It sits on the plane, turns toward the nearest enemy in
    /// range and fires, leading the target. A hit knocks the enemy out of the sky, as a flick would.
    /// </summary>
    public class PlaneTurret : MonoBehaviour
    {
        /// <summary>Fired when any turret's shot downs an enemy.</summary>
        public static event Action<PlaneTurret, EnemyPlane> AnyEnemyDowned;

        public EnemyPlane CurrentTarget { get; private set; }
        public int ShotsFired { get; private set; }

        MiniPlane _plane;
        LevelConfig _cfg;
        GameArt _art;
        Transform _swivel;
        float _cooldown;

        public void Setup(MiniPlane plane, LevelConfig cfg, GameArt art, float size)
        {
            _plane = plane;
            _cfg = cfg;
            _art = art;
            _cooldown = 0.3f;

            _swivel = new GameObject("Swivel").transform;
            _swivel.SetParent(transform, false);
            if (art != null && art.turret != null)
            {
                ModelUtil.Attach(_swivel, art.turret, art.TurretMaterial, size, ModelUtil.FitAxis.Width);
            }
            else
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(_swivel, false);
                body.transform.localScale = new Vector3(size, size * 0.45f, size);
                var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(barrel.GetComponent<Collider>());
                barrel.transform.SetParent(_swivel, false);
                barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                barrel.transform.localPosition = new Vector3(0f, 0f, size * 0.5f);
                barrel.transform.localScale = new Vector3(size * 0.22f, size * 0.4f, size * 0.22f);
                foreach (var r in _swivel.GetComponentsInChildren<MeshRenderer>())
                    if (art != null) r.sharedMaterial = art.turretShot != null ? art.turretShot : art.planeMatte;
            }
            if (art != null) ModelUtil.AddSilhouette(_swivel.gameObject, art.silhouette);
        }

        void Update()
        {
            if (_plane == null || !_plane.IsFlying || _cfg == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            CurrentTarget = FindTarget();
            Vector3 aim = CurrentTarget != null ? LeadPoint(CurrentTarget) - _swivel.position : transform.parent.forward;

            // Turn about the plane's up axis only, so the turret stays seated on the deck.
            Vector3 up = transform.parent.up;
            Vector3 flat = aim - up * Vector3.Dot(aim, up);
            if (flat.sqrMagnitude > 1e-8f)
            {
                Quaternion want = Quaternion.LookRotation(flat.normalized, up);
                _swivel.rotation = Quaternion.RotateTowards(_swivel.rotation, want, 540f * dt);
            }

            _cooldown -= dt;
            if (CurrentTarget != null && _cooldown <= 0f)
            {
                _cooldown = Mathf.Max(0.05f, _cfg.turretFireInterval);
                Fire(aim.normalized);
            }
        }

        EnemyPlane FindTarget()
        {
            EnemyPlane best = null;
            float bestDistance = Mathf.Max(0.05f, _cfg.turretRange);
            foreach (var e in EnemyPlane.Active)
            {
                if (e == null || !e.IsAlive) continue;
                float d = Vector3.Distance(e.Position, _plane.Position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = e;
                }
            }
            return best;
        }

        Vector3 LeadPoint(EnemyPlane enemy)
        {
            float flight = Vector3.Distance(enemy.Position, _swivel.position) / Mathf.Max(0.1f, _cfg.turretShotSpeed);
            return enemy.Position + enemy.Velocity * flight;
        }

        void Fire(Vector3 direction)
        {
            ShotsFired++;
            var shot = TurretShot.Create(_art != null ? _art.turretShot : null);
            Vector3 start = _swivel.position + direction * 0.012f;
            shot.Fire(start, direction * _cfg.turretShotSpeed + _plane.Velocity, this);
        }

        internal void ReportDowned(EnemyPlane enemy)
        {
            AnyEnemyDowned?.Invoke(this, enemy);
        }
    }

    /// <summary>One turret round. Flies straight and knocks the first enemy it touches out of the sky.</summary>
    public class TurretShot : MonoBehaviour
    {
        public static readonly List<TurretShot> Active = new List<TurretShot>();

        public float radius = 0.003f;
        public float lifetime = 1.5f;

        Vector3 _velocity;
        PlaneTurret _owner;
        float _age;

        public static TurretShot Create(Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "TurretShot";
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            if (material != null) r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var shot = go.AddComponent<TurretShot>();
            go.transform.localScale = Vector3.one * (shot.radius * 2f);
            return shot;
        }

        public void Fire(Vector3 position, Vector3 velocity, PlaneTurret owner)
        {
            transform.position = position;
            _velocity = velocity;
            _owner = owner;
        }

        void OnEnable() { Active.Add(this); }

        void OnDisable() { Active.Remove(this); }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age > lifetime)
            {
                Destroy(gameObject);
                return;
            }
            Vector3 from = transform.position;
            Vector3 to = from + _velocity * dt;
            transform.position = to;

            foreach (var e in EnemyPlane.Active)
            {
                if (e == null || !e.IsAlive) continue;
                if (HandRing.DistancePointSegment(e.Position, from, to) < e.radius + radius)
                {
                    e.Flick(_velocity.normalized * 0.8f);
                    if (_owner != null) _owner.ReportDowned(e);
                    Destroy(gameObject);
                    return;
                }
            }
        }

        public static void DestroyAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i] != null) Destroy(Active[i].gameObject);
            Active.Clear();
        }
    }
}
