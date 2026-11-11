using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// The boss: a giant zeppelin hanging in the distance, broadside on. It can't be destroyed. Between attacks
    /// it cruises a random way, left or right, round the flight path to a new spot, and only then starts its
    /// next attack: bullet trails that mostly aim at the plane, batches of fighters launched from its decks,
    /// and, when the level allows, large tracking missiles.
    /// </summary>
    public class Zeppelin : MonoBehaviour
    {
        public enum Attack { Arriving, Rest, Barrage, Fighters, Missiles }

        public static Zeppelin Current { get; private set; }

        [Tooltip("Length of the airship, in metres.")]
        public float length = 1.1f;
        [Tooltip("Distance from the head, in metres.")]
        public float distance = 2.0f;
        [Tooltip("Height above the eyes, in metres.")]
        public float heightAboveEyes = 0.35f;
        [Tooltip("Each move between attacks swings it this far round the path, in degrees.")]
        public float minMoveDeg = 25f;
        public float maxMoveDeg = 55f;
        [Tooltip("It stays within this angle either side of straight ahead, in degrees.")]
        public float maxAzimuthDeg = 65f;
        [Tooltip("Cruising speed round the path, in degrees per second.")]
        public float cruiseDegPerSecond = 14f;
        public float arriveSeconds = 3f;
        public float bulletRadius = 0.006f;

        public Attack CurrentAttack { get; private set; } = Attack.Arriving;
        public int BarragesFired { get; private set; }
        public int FighterBatches { get; private set; }
        public int MissilesLaunched { get; private set; }
        public int RoundsFired { get; private set; }
        /// <summary>Where it sits round the path, in degrees from straight ahead; positive is to the right.</summary>
        public float Azimuth => _azimuth;
        public int Moves { get; private set; }
        public bool IsMoving => !Mathf.Approximately(_azimuth, _targetAzimuth);
        public Vector3 Position => transform.position;
        /// <summary>A new attack began. GameManager shows its warning.</summary>
        public event Action<Zeppelin, Attack> AttackStarted;

        LevelConfig _cfg;
        MiniPlane _plane;
        Transform _head;
        HandSource[] _hands;
        GameArt _art;
        EnemySpawner _spawner;
        Vector3 _home;
        Vector3 _side;
        Vector3 _centre;
        Vector3 _forward;
        float _time;
        float _arrive;
        float _azimuth;
        float _targetAzimuth;
        float _facing = -1f;
        System.Random _rng;
        readonly List<EnemyPlane> _launched = new List<EnemyPlane>();

        // Points on the airship, in its own space as fractions of its length: x right, y up, z toward the nose.
        static readonly Vector3[] GunPorts =
        {
            new Vector3(0f, 0f, 0.5f), new Vector3(0f, -0.2f, 0.05f), new Vector3(0f, -0.2f, -0.08f),
            new Vector3(0.25f, 0.06f, 0.09f), new Vector3(-0.25f, 0.06f, 0.09f), new Vector3(0f, 0.05f, -0.42f)
        };
        // The four parked gunships on the model's two shoulder decks, near side first.
        static readonly Vector3[] Docks =
        {
            new Vector3(-0.25f, 0.06f, 0.17f), new Vector3(-0.25f, 0.06f, 0.0f),
            new Vector3(0.25f, 0.06f, 0.17f), new Vector3(0.25f, 0.06f, 0.0f)
        };
        static readonly Vector3 MissileBay = new Vector3(0f, -0.24f, 0f);

        public void Setup(LevelConfig cfg, MiniPlane plane, Transform head, HandSource[] hands, GameArt art, EnemySpawner spawner,
            Vector3 headPos, Vector3 forward, int seed)
        {
            _cfg = cfg;
            _plane = plane;
            _head = head;
            _hands = hands;
            _art = art;
            _spawner = spawner;
            _rng = new System.Random(seed);
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            _side = Vector3.Cross(Vector3.up, forward).normalized;
            _forward = forward;
            _centre = headPos;
            _home = headPos + forward * distance + Vector3.up * heightAboveEyes;
            // Broadside on, nose to the player's left, so the docks on its near side face the player.
            transform.rotation = Quaternion.LookRotation(-_side, Vector3.up);
            transform.position = _home + forward * 1.2f;
            BuildModel();
            Current = this;
            StartCoroutine(Run());
            Sfx.Play(SoundKind.BossAlarm);
        }

        void BuildModel()
        {
            var holder = new GameObject("Model").transform;
            holder.SetParent(transform, false);
            if (_art != null && _art.zeppelin != null)
            {
                ModelUtil.Attach(holder, _art.zeppelin, _art.ZeppelinMaterial, length, ModelUtil.FitAxis.Length);
            }
            else
            {
                var m = _art != null ? _art.planeMatte : null;
                var coral = new Color(0.86f, 0.42f, 0.33f);
                var ivory = new Color(0.97f, 0.9f, 0.78f);
                var plum = new Color(0.4f, 0.22f, 0.28f);
                float L = length;
                var x90 = Quaternion.Euler(90f, 0f, 0f);
                ModelUtil.Primitive(PrimitiveType.Sphere, holder, Vector3.zero, new Vector3(0.27f, 0.27f, 1f) * L, Quaternion.identity, m, coral);
                ModelUtil.Primitive(PrimitiveType.Cylinder, holder, Vector3.back * 0.3f * L, new Vector3(0.24f, 0.012f, 0.24f) * L, x90, m, ivory);
                ModelUtil.Primitive(PrimitiveType.Cylinder, holder, Vector3.back * 0.35f * L, new Vector3(0.21f, 0.012f, 0.21f) * L, x90, m, ivory);
                ModelUtil.Primitive(PrimitiveType.Cube, holder, new Vector3(0f, 0.16f, -0.42f) * L, new Vector3(0.015f, 0.14f, 0.12f) * L, Quaternion.identity, m, coral);
                ModelUtil.Primitive(PrimitiveType.Cube, holder, new Vector3(0f, 0f, -0.42f) * L, new Vector3(0.3f, 0.015f, 0.1f) * L, Quaternion.identity, m, coral);
                ModelUtil.Primitive(PrimitiveType.Capsule, holder, new Vector3(0f, -0.17f, 0f) * L, new Vector3(0.09f, 0.11f, 0.09f) * L, x90, m, ivory);
                ModelUtil.Primitive(PrimitiveType.Cube, holder, new Vector3(0f, -0.17f, 0f) * L, new Vector3(0.092f, 0.025f, 0.16f) * L, Quaternion.identity, m, plum);
                for (int s = -1; s <= 1; s += 2)
                {
                    ModelUtil.Primitive(PrimitiveType.Cylinder, holder, new Vector3(0.11f * s, -0.13f, -0.02f) * L, new Vector3(0.05f, 0.04f, 0.05f) * L, x90, m, ivory);
                    ModelUtil.Primitive(PrimitiveType.Cube, holder, new Vector3(0.22f * s, 0.05f, 0.08f) * L, new Vector3(0.1f, 0.01f, 0.3f) * L, Quaternion.identity, m, ivory);
                }
                if (_art != null && _art.enemyFighter != null)
                {
                    foreach (var dock in Docks)
                    {
                        var parked = new GameObject("ParkedFighter").transform;
                        parked.SetParent(holder, false);
                        parked.localPosition = dock * L;
                        ModelUtil.Attach(parked, _art.enemyFighter, _art.FighterMaterial, 0.06f * L, ModelUtil.FitAxis.Width);
                    }
                }
            }
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _time += dt;
            _arrive = Mathf.Clamp01(_arrive + dt / Mathf.Max(0.1f, arriveSeconds));
            float ease = 1f - (1f - _arrive) * (1f - _arrive);
            // Cruise round the path towards the chosen spot, easing in and out.
            float left = _targetAzimuth - _azimuth;
            if (Mathf.Abs(left) > 1e-3f)
            {
                float step = cruiseDegPerSecond * dt * Mathf.Clamp(Mathf.Abs(left) / 8f, 0.25f, 1f);
                _azimuth = Mathf.Abs(left) <= step ? _targetAzimuth : _azimuth + Mathf.Sign(left) * step;
                _facing = Mathf.Sign(left);
            }
            else _azimuth = _targetAzimuth;

            Vector3 radial = Quaternion.AngleAxis(_azimuth, Vector3.up) * _forward;
            Vector3 spot = _centre + radial * distance + Vector3.up * heightAboveEyes;
            _home = spot;
            Vector3 bob = Vector3.up * (Mathf.Sin(_time * 0.6f) * 0.03f);
            transform.position = Vector3.Lerp(spot + radial * 1.2f, spot, ease) + bob * ease;
            // Broadside to the player, nose leading the way it last moved.
            Vector3 tangent = Vector3.Cross(Vector3.up, radial).normalized * _facing;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(tangent, Vector3.up), 1f - Mathf.Exp(-1.5f * dt));
        }

        Vector3 Point(Vector3 fraction)
        {
            return transform.TransformPoint(fraction * length);
        }

        IEnumerator Run()
        {
            yield return Wait(arriveSeconds + 0.5f);
            while (true)
            {
                yield return Barrage();
                yield return Rest();
                yield return LaunchFighters();
                yield return Rest();
                yield return Barrage();
                yield return Rest();
                if (_cfg.bossMissiles)
                {
                    yield return LaunchMissiles();
                    yield return Rest();
                }
            }
        }

        IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        IEnumerator Rest()
        {
            CurrentAttack = Attack.Rest;
            ChooseNextSpot();
            float t = 0f;
            while (t < _cfg.bossRestSeconds || IsMoving)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>Picks a new spot round the path, randomly left or right, within the front arc.</summary>
        void ChooseNextSpot()
        {
            float swing = Rand(minMoveDeg, maxMoveDeg);
            float dir = _rng.NextDouble() < 0.5 ? -1f : 1f;
            float target = _azimuth + dir * swing;
            // Turn back rather than leave the front arc.
            if (Mathf.Abs(target) > maxAzimuthDeg) target = _azimuth - dir * swing;
            _targetAzimuth = Mathf.Clamp(target, -maxAzimuthDeg, maxAzimuthDeg);
            Moves++;
        }

        void Begin(Attack attack)
        {
            CurrentAttack = attack;
            if (attack == Attack.Missiles) Sfx.Play(SoundKind.MissileLock);
            AttackStarted?.Invoke(this, attack);
        }

        // ---------------------------------------------------------------- bullet trails

        IEnumerator Barrage()
        {
            Begin(Attack.Barrage);
            BarragesFired++;
            int trails = Mathf.Max(1, _cfg.bossTrails);
            float window = _cfg.bossBarrageSeconds * 0.6f;
            for (int i = 0; i < trails; i++)
            {
                bool wander = i == trails - 1 && trails > 2;
                StartCoroutine(Trail(GunPorts[_rng.Next(GunPorts.Length)], wander, i == 0));
                yield return Wait(window / trails);
            }
            yield return Wait(_cfg.bossBarrageSeconds - window);
        }

        /// <summary>
        /// One stream of rounds from one gun port. An aimed trail re-aims every round at where the plane will be
        /// when the round arrives, keeping a small fixed error, so the stream follows the plane. A wandering
        /// trail sprays across the player's space instead.
        /// </summary>
        IEnumerator Trail(Vector3 port, bool wander, bool deadOn)
        {
            Sfx.Play(SoundKind.GunBurst, Point(port));
            float spread = deadOn ? 0f : _cfg.bossAimSpreadDeg;
            float yaw = Rand(-spread, spread);
            float pitch = Rand(-spread * 0.6f, spread * 0.6f);
            Vector3 wanderDir = WanderDirection(Point(port));
            int rounds = Mathf.Max(1, _cfg.bossTrailRounds);
            for (int r = 0; r < rounds; r++)
            {
                Vector3 from = Point(port);
                Vector3 dir = wander || _plane == null || !_plane.IsFlying ? wanderDir : AimedDirection(from, yaw, pitch);
                FireRound(from, dir);
                yield return Wait(_cfg.bossRoundSpacing);
            }
        }

        Vector3 WanderDirection(Vector3 from)
        {
            Vector3 centre = _head != null ? _head.position : from;
            Vector3 target = centre + (_home - centre).normalized * 0.45f + UnityEngine.Random.insideUnitSphere * 0.4f + Vector3.down * 0.15f;
            return (target - from).normalized;
        }

        Vector3 AimedDirection(Vector3 from, float yawDeg, float pitchDeg)
        {
            Vector3 target = PredictPlane(from);
            Vector3 dir = (target - from).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            return Quaternion.AngleAxis(yawDeg, Vector3.up) * Quaternion.AngleAxis(pitchDeg, side.normalized) * dir;
        }

        /// <summary>
        /// Where the plane will be when a round fired now reaches it. The plane follows a known path at a known
        /// speed, so this looks ahead along the path rather than guessing from its current heading.
        /// </summary>
        public Vector3 PredictPlane(Vector3 from)
        {
            float speed = Mathf.Max(0.1f, _cfg.bossBulletSpeed);
            Vector3 target = _plane.Position;
            for (int i = 0; i < 4; i++)
            {
                float t = Vector3.Distance(from, target) / speed;
                target = _plane.Path != null
                    ? _plane.Path.PositionAt(Mathf.Min(_plane.Distance + _plane.Speed * t, _plane.Path.Length))
                    : _plane.Position + _plane.Velocity * t;
            }
            return target;
        }

        float Rand(float a, float b)
        {
            return Mathf.Lerp(a, b, (float)_rng.NextDouble());
        }

        void FireRound(Vector3 from, Vector3 dir)
        {
            RoundsFired++;
            var mat = _art != null ? (_art.bossBullet != null ? _art.bossBullet : _art.projectile) : null;
            var p = Projectile.Create(mat, bulletRadius);
            p.lifetime = (distance + 1.5f) / Mathf.Max(0.1f, _cfg.bossBulletSpeed);
            p.Fire(from, dir * _cfg.bossBulletSpeed, _plane);
        }

        // ---------------------------------------------------------------- fighters

        IEnumerator LaunchFighters()
        {
            Begin(Attack.Fighters);
            FighterBatches++;
            _launched.Clear();
            int batch = Mathf.Max(1, _cfg.bossFighterBatch);
            for (int i = 0; i < batch; i++)
            {
                if (_spawner != null)
                {
                    var f = _spawner.SpawnFighterAt(Point(Docks[i % Docks.Length]));
                    if (f != null) _launched.Add(f);
                }
                yield return Wait(0.5f);
            }
            float t = 0f;
            while (t < 14f && _launched.Exists(f => f != null && f.IsAlive))
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        // ---------------------------------------------------------------- missiles

        IEnumerator LaunchMissiles()
        {
            Begin(Attack.Missiles);
            var mine = new List<BossMissile>();
            int count = Mathf.Max(1, _cfg.bossMissileCount);
            for (int i = 0; i < count; i++)
            {
                mine.Add(LaunchMissile());
                yield return Wait(1.4f);
            }
            float t = 0f;
            while (t < 16f && mine.Exists(m => m != null && m.IsAlive))
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        public BossMissile LaunchMissile()
        {
            MissilesLaunched++;
            Sfx.Play(SoundKind.MissileLaunch, Point(MissileBay));
            Vector3 from = Point(MissileBay);
            Vector3 toward = _plane != null ? _plane.Position - from : (_head != null ? _head.position - from : Vector3.back);
            var go = new GameObject("BossMissile");
            go.transform.SetParent(transform.parent, false);
            var missile = go.AddComponent<BossMissile>();
            missile.Setup(_cfg, _plane, _hands, _art, from, (toward.normalized + Vector3.down * 0.3f).normalized);
            return missile;
        }

        public static void DestroyAll()
        {
            if (Current != null) Destroy(Current.gameObject);
            Current = null;
            BossMissile.DestroyAll();
        }
    }
}
