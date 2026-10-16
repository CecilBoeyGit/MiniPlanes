using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Hostile plane that chases the mini plane, circles it at a stand-off distance and shoots.
    /// A hand flick sends it tumbling out of the sky.
    /// </summary>
    public class EnemyPlane : MonoBehaviour
    {
        public static readonly List<EnemyPlane> Active = new List<EnemyPlane>();

        public float radius = 0.01f;
        public float speed = 0.35f;
        public float turnRateDeg = 140f;
        [Tooltip("Distance at which the enemy stops closing in and starts circling, in metres.")]
        public float standoff = 0.18f;
        public float fireInterval = 2.5f;
        public float fireRange = 0.6f;
        public float projectileSpeed = 0.9f;
        public float projectileRadius = 0.0035f;
        [Tooltip("Despawn when farther than this from the player's head, in metres.")]
        public float maxDistanceFromHead = 3f;
        public float tumbleDuration = 1.6f;

        public bool IsAlive { get; private set; }
        public Vector3 Position => transform.position;

        public event Action<EnemyPlane> Died;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        MiniPlane _target;
        Transform _head;
        Material _projectileMaterial;
        Vector3 _forward = Vector3.forward;
        float _fireTimer;
        float _bank;
        int _orbitSign = 1;

        public void Setup(LevelConfig cfg, MiniPlane target, Transform head, Material body, Material wing, Material projectile, int seed)
        {
            var rng = new System.Random(seed);
            var shape = PlaneShapeParams.Enemy(rng);

            var filter = gameObject.AddComponent<MeshFilter>();
            var renderer = gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = ProceduralPlaneMesh.Build(shape);
            radius = Mathf.Clamp(shape.wingSpan * 0.3f, 0.004f, 0.02f);
            renderer.sharedMaterials = new[] { body, wing };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, shape.bodyColor);
            renderer.SetPropertyBlock(block, 0);
            block.SetColor(BaseColorId, shape.wingColor);
            renderer.SetPropertyBlock(block, 1);

            _target = target;
            _head = head;
            _projectileMaterial = projectile;
            speed = cfg.enemySpeed;
            fireInterval = cfg.enemyFireInterval;
            fireRange = cfg.enemyFireRange;
            projectileSpeed = cfg.projectileSpeed;
            _fireTimer = fireInterval * (0.5f + 0.5f * (float)rng.NextDouble());
            _orbitSign = rng.NextDouble() < 0.5 ? -1 : 1;

            Vector3 toTarget = target != null ? target.Position - transform.position : Vector3.forward;
            _forward = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(_forward, Vector3.up);
            IsAlive = true;
        }

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
            float dt = Time.deltaTime;

            Vector3 desired = _forward;
            bool hasTarget = _target != null && _target.IsFlying;
            float dist = float.PositiveInfinity;
            if (hasTarget)
            {
                Vector3 to = _target.Position - transform.position;
                dist = to.magnitude;
                Vector3 toDir = dist > 1e-5f ? to / dist : _forward;
                if (dist > standoff)
                {
                    desired = toDir;
                }
                else
                {
                    // Circle the target instead of ramming it every time.
                    Vector3 tangent = Vector3.Cross(Vector3.up, toDir) * _orbitSign;
                    desired = (tangent + toDir * 0.15f).normalized;
                }
            }

            Vector3 newForward = Vector3.RotateTowards(_forward, desired, turnRateDeg * Mathf.Deg2Rad * dt, 0f);
            float yawRate = Vector3.SignedAngle(new Vector3(_forward.x, 0f, _forward.z), new Vector3(newForward.x, 0f, newForward.z), Vector3.up) / Mathf.Max(dt, 1e-4f);
            _forward = newForward;
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-yawRate * 0.4f, -60f, 60f), 1f - Mathf.Exp(-8f * dt));

            transform.position += _forward * speed * dt;
            transform.rotation = Quaternion.LookRotation(_forward, Vector3.up) * Quaternion.Euler(0f, 0f, _bank);

            if (hasTarget)
            {
                if (dist < radius + _target.radius)
                {
                    _target.Crash("rammed by an enemy");
                    Die(Vector3.down * 0.5f);
                    return;
                }

                _fireTimer -= dt;
                Vector3 toDir = (_target.Position - transform.position).normalized;
                if (_fireTimer <= 0f && dist < fireRange && Vector3.Dot(_forward, toDir) > 0.6f)
                {
                    FireAt(_target, dist);
                    _fireTimer = fireInterval;
                }
            }

            if (_head != null && Vector3.Distance(transform.position, _head.position) > maxDistanceFromHead)
                Destroy(gameObject);
        }

        void FireAt(MiniPlane target, float dist)
        {
            float flight = dist / Mathf.Max(projectileSpeed, 0.05f);
            Vector3 lead = target.Position + target.Velocity * flight;
            Vector3 dir = (lead - transform.position).normalized;
            var p = Projectile.Create(_projectileMaterial, projectileRadius);
            p.Fire(transform.position + dir * 0.035f, dir * projectileSpeed, target);
        }

        /// <summary>Knock the enemy away with the given velocity (metres per second).</summary>
        public void Flick(Vector3 velocity)
        {
            if (!IsAlive) return;
            Die(Vector3.ClampMagnitude(velocity * 0.5f, 1.5f));
        }

        void Die(Vector3 initialVelocity)
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
