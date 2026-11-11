using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// The automatic turret from the gold balloon, or the Bluebird's own. It sits on the plane, turns toward
    /// the nearest enemy in range and fires fast, aiming where the target will be. Rounds curve a little
    /// toward their target and knock it out of the sky, as a flick would. It lasts a few seconds, blinking
    /// as it runs out.
    /// </summary>
    public class PlaneTurret : MonoBehaviour
    {
        /// <summary>Fired when any turret's shot downs an enemy.</summary>
        public static event Action<PlaneTurret, EnemyPlane> AnyEnemyDowned;

        public EnemyPlane CurrentTarget { get; private set; }
        public int ShotsFired { get; private set; }
        /// <summary>Seconds of flight left before the turret comes off.</summary>
        public float TimeLeft { get; private set; }

        MiniPlane _plane;
        LevelConfig _cfg;
        GameArt _art;
        Transform _swivel;
        Renderer[] _renderers = new Renderer[0];
        float _cooldown;

        public void Setup(MiniPlane plane, LevelConfig cfg, GameArt art, float size)
        {
            _plane = plane;
            _cfg = cfg;
            _art = art;
            _cooldown = 0.15f;
            Refresh();

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
            _renderers = _swivel.GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>Starts the turret's time over.</summary>
        public void Refresh()
        {
            TimeLeft = _cfg != null ? Mathf.Max(0.5f, _cfg.turretDurationSeconds) : 6f;
        }

        void Update()
        {
            if (_plane == null || !_plane.IsFlying || _cfg == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            TimeLeft -= dt;
            if (TimeLeft <= 0f)
            {
                ComicVfx.Play(ComicKind.Poof, _swivel.position, 0.05f);
                _plane.RemoveTurret();
                return;
            }
            bool blink = TimeLeft < 1.2f && Mathf.Repeat(TimeLeft * 8f, 1f) < 0.35f;
            foreach (var r in _renderers) if (r != null && r.enabled == blink) r.enabled = !blink;

            CurrentTarget = FindTarget();
            Vector3 aim = transform.parent.forward;
            if (CurrentTarget != null && Intercept(CurrentTarget, out var dir)) aim = dir;

            // Turn about the plane's up axis only, so the turret stays seated on the deck.
            Vector3 up = transform.parent.up;
            Vector3 flat = aim - up * Vector3.Dot(aim, up);
            if (flat.sqrMagnitude > 1e-8f)
            {
                Quaternion want = Quaternion.LookRotation(flat.normalized, up);
                _swivel.rotation = Quaternion.RotateTowards(_swivel.rotation, want, 900f * dt);
            }

            _cooldown -= dt;
            if (CurrentTarget != null && _cooldown <= 0f)
            {
                _cooldown = Mathf.Max(0.03f, _cfg.turretFireInterval);
                Fire(aim, CurrentTarget);
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

        /// <summary>
        /// The direction that meets the enemy, allowing for its motion and for the plane's own speed, which
        /// every round inherits. False if there is nothing sensible to aim at.
        /// </summary>
        bool Intercept(EnemyPlane enemy, out Vector3 direction)
        {
            float speed = Mathf.Max(0.1f, _cfg.turretShotSpeed);
            Vector3 r = enemy.Position - _swivel.position;
            Vector3 v = enemy.Velocity - _plane.Velocity;
            // Solve |r + v t| = speed t for the earliest positive t.
            float a = Vector3.Dot(v, v) - speed * speed;
            float b = 2f * Vector3.Dot(r, v);
            float c = Vector3.Dot(r, r);
            float t = -1f;
            if (Mathf.Abs(a) < 1e-6f)
            {
                if (Mathf.Abs(b) > 1e-6f) t = -c / b;
            }
            else
            {
                float disc = b * b - 4f * a * c;
                if (disc >= 0f)
                {
                    float sq = Mathf.Sqrt(disc);
                    float t1 = (-b - sq) / (2f * a);
                    float t2 = (-b + sq) / (2f * a);
                    t = Mathf.Min(t1, t2);
                    if (t < 0f) t = Mathf.Max(t1, t2);
                }
            }
            Vector3 meet = t > 0f ? r + v * t : r;
            direction = meet.sqrMagnitude > 1e-8f ? meet.normalized : _plane.Forward;
            return c > 1e-8f;
        }

        void Fire(Vector3 direction, EnemyPlane target)
        {
            ShotsFired++;
            Sfx.Play(SoundKind.TurretShot, _swivel.position);
            var shot = TurretShot.Create(_art != null ? _art.turretShot : null);
            Vector3 start = _swivel.position + direction * 0.012f;
            shot.Fire(start, direction * _cfg.turretShotSpeed + _plane.Velocity, this, target,
                Mathf.Max(0.003f, _cfg.turretHitRadius), _cfg.turretShotHomingDeg);
        }

        internal void ReportDowned(EnemyPlane enemy)
        {
            AnyEnemyDowned?.Invoke(this, enemy);
        }
    }

    /// <summary>
    /// One turret round. Curves gently toward the enemy it was fired at and knocks the first enemy it passes
    /// close to out of the sky.
    /// </summary>
    public class TurretShot : MonoBehaviour
    {
        public static readonly List<TurretShot> Active = new List<TurretShot>();

        [Tooltip("Drawn size of the round, in metres.")]
        public float radius = 0.0035f;
        public float lifetime = 1.2f;

        Vector3 _velocity;
        PlaneTurret _owner;
        EnemyPlane _target;
        float _hitRadius = 0.02f;
        float _homingDeg;
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

        public void Fire(Vector3 position, Vector3 velocity, PlaneTurret owner, EnemyPlane target, float hitRadius, float homingDeg)
        {
            transform.position = position;
            _velocity = velocity;
            _owner = owner;
            _target = target;
            _hitRadius = hitRadius;
            _homingDeg = homingDeg;
        }

        void OnEnable() { Active.Add(this); }

        void OnDisable() { Active.Remove(this); }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _age += dt;
            if (_age > lifetime)
            {
                Destroy(gameObject);
                return;
            }

            if (_target != null && _target.IsAlive && _homingDeg > 0f)
            {
                Vector3 toTarget = _target.Position - transform.position;
                if (toTarget.sqrMagnitude > 1e-8f)
                    _velocity = Vector3.RotateTowards(_velocity, toTarget.normalized * _velocity.magnitude, _homingDeg * Mathf.Deg2Rad * dt, 0f);
            }

            Vector3 from = transform.position;
            Vector3 to = from + _velocity * dt;
            transform.position = to;

            foreach (var e in EnemyPlane.Active)
            {
                if (e == null || !e.IsAlive) continue;
                if (HandRing.DistancePointSegment(e.Position, from, to) < e.radius + _hitRadius)
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
