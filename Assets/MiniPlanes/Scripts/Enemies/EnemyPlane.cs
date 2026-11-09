using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Level 1 enemy: a fighter that chases the mini plane, circles it at a stand-off distance and
    /// fires machine-gun bursts. A hand flick sends it tumbling out of the sky.
    /// </summary>
    public class EnemyPlane : MonoBehaviour
    {
        public static readonly List<EnemyPlane> Active = new List<EnemyPlane>();

        public float wingspan = 0.038f;
        public float radius = 0.012f;
        public float turnRateDeg = 140f;
        [Tooltip("Distance at which the fighter stops closing in and starts circling, in metres.")]
        public float standoff = 0.18f;
        public float projectileRadius = 0.0035f;
        [Tooltip("Despawn when farther than this from the player's head, in metres.")]
        public float maxDistanceFromHead = 3f;
        public float tumbleDuration = 1.6f;

        public bool IsAlive { get; protected set; }
        public Vector3 Position => transform.position;
        public Vector3 Velocity => IsAlive ? Forward * Speed : Vector3.zero;

        public event Action<EnemyPlane> Died;

        protected LevelConfig Cfg;
        protected MiniPlane Target;
        protected Transform Head;
        protected GameArt Art;
        protected Vector3 Forward = Vector3.forward;
        protected float Speed;

        float _bank;
        int _orbitSign = 1;
        float _fireTimer;
        float _burstTimer;
        int _burstLeft;

        public void Setup(LevelConfig cfg, MiniPlane target, Transform head, GameArt art, int seed)
        {
            Cfg = cfg;
            Target = target;
            Head = head;
            Art = art;
            var rng = new System.Random(seed);

            BuildModel(rng);

            Speed = StartSpeed();
            _fireTimer = cfg.burstInterval * (0.5f + 0.5f * (float)rng.NextDouble());
            _orbitSign = rng.NextDouble() < 0.5 ? -1 : 1;

            Vector3 toTarget = target != null ? target.Position - transform.position : Vector3.forward;
            Forward = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(Forward, Vector3.up);
            IsAlive = true;
            OnSetup(rng);
        }

        protected virtual void BuildModel(System.Random rng)
        {
            if (Art != null && Art.enemyFighter != null)
                ModelUtil.Attach(transform, Art.enemyFighter, Art.FighterMaterial, wingspan, ModelUtil.FitAxis.Width);
            else
                ModelUtil.AttachProcedural(transform, PlaneShapeParams.Enemy(rng), Art != null ? Art.planeMatte : null);
        }

        protected virtual float StartSpeed() { return Cfg.fighterSpeed; }

        protected virtual void OnSetup(System.Random rng) { }

        void OnEnable()
        {
            Active.Add(this);
        }

        void OnDisable()
        {
            Active.Remove(this);
        }

        void Update()
        {
            if (!IsAlive) return;
            Tick(Time.deltaTime);
            if (IsAlive && Head != null && Vector3.Distance(transform.position, Head.position) > maxDistanceFromHead)
                Destroy(gameObject);
        }

        protected virtual void Tick(float dt)
        {
            bool hasTarget = Target != null && Target.IsFlying;
            Vector3 desired = Forward;
            float dist = float.PositiveInfinity;
            if (hasTarget)
            {
                Vector3 to = Target.Position - transform.position;
                dist = to.magnitude;
                Vector3 toDir = dist > 1e-5f ? to / dist : Forward;
                if (dist > standoff)
                {
                    desired = toDir;
                }
                else
                {
                    // Circle the target rather than ramming it every time.
                    Vector3 tangent = Vector3.Cross(Vector3.up, toDir) * _orbitSign;
                    desired = (tangent + toDir * 0.15f).normalized;
                }
            }
            Steer(desired, dt);

            if (!hasTarget) return;

            if (dist < radius + Target.Radius)
            {
                Target.Damage("rammed by a fighter");
                ComicVfx.Play(ComicKind.Crash, transform.position, 0.09f);
                Die(Vector3.down * 0.5f);
                return;
            }

            _fireTimer -= dt;
            if (_burstLeft > 0)
            {
                _burstTimer -= dt;
                if (_burstTimer <= 0f)
                {
                    FireAt(Target);
                    _burstLeft--;
                    _burstTimer = Cfg.burstSpacing;
                }
            }
            else if (_fireTimer <= 0f && dist < Cfg.fireRange &&
                     Vector3.Dot(Forward, (Target.Position - transform.position).normalized) > 0.5f)
            {
                _burstLeft = Mathf.Max(1, Cfg.burstCount);
                _burstTimer = 0f;
                _fireTimer = Cfg.burstInterval;
            }
        }

        protected void Steer(Vector3 desired, float dt)
        {
            Vector3 newForward = Vector3.RotateTowards(Forward, desired, turnRateDeg * Mathf.Deg2Rad * dt, 0f);
            float yawRate = Vector3.SignedAngle(new Vector3(Forward.x, 0f, Forward.z), new Vector3(newForward.x, 0f, newForward.z), Vector3.up) / Mathf.Max(dt, 1e-4f);
            Forward = newForward;
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-yawRate * 0.4f, -60f, 60f), 1f - Mathf.Exp(-8f * dt));

            transform.position += Forward * (Speed * dt);
            transform.rotation = Quaternion.LookRotation(Forward, Vector3.up) * Quaternion.Euler(0f, 0f, _bank);
        }

        void FireAt(MiniPlane target)
        {
            float dist = Vector3.Distance(transform.position, target.Position);
            float flight = dist / Mathf.Max(Cfg.projectileSpeed, 0.05f);
            Vector3 lead = target.Position + target.Velocity * flight;
            Vector3 dir = (lead - transform.position).normalized;
            var p = Projectile.Create(Art != null ? Art.projectile : null, projectileRadius);
            p.Fire(transform.position + dir * 0.025f, dir * Cfg.projectileSpeed, target);
        }

        /// <summary>Knock the enemy away with the given velocity, in metres per second.</summary>
        public void Flick(Vector3 velocity)
        {
            if (!IsAlive) return;
            ComicVfx.Play(ComicKind.Flick, transform.position);
            OnFlicked();
            Die(Vector3.ClampMagnitude(velocity * 0.5f, 1.5f));
        }

        protected virtual void OnFlicked() { }

        protected void Die(Vector3 initialVelocity)
        {
            IsAlive = false;
            Died?.Invoke(this);
            StartCoroutine(Tumble(initialVelocity));
        }

        IEnumerator Tumble(Vector3 v)
        {
            Vector3 spin = new Vector3(UnityEngine.Random.Range(-540f, 540f), UnityEngine.Random.Range(-360f, 360f), UnityEngine.Random.Range(-900f, 900f));
            float t = 0f;
            while (t < tumbleDuration)
            {
                float dt = Time.deltaTime;
                t += dt;
                v += Physics.gravity * 0.35f * dt;
                transform.position += v * dt;
                transform.Rotate(spin * dt, Space.Self);
                float k = t / tumbleDuration;
                transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.2f, k * k);
                yield return null;
            }
            Destroy(gameObject);
        }

        public static void DestroyAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i] != null) Destroy(Active[i].gameObject);
            Active.Clear();
        }
    }
}
