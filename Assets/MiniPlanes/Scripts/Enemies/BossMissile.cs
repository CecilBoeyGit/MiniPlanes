using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// The zeppelin's large tracking missile. It chases the mini plane and blows up on reaching it. Hold it
    /// still with an open palm for a moment and its motor dies, so it drops out of the sky. Smacking it, or
    /// any fast hand, sets it off on the spot: harmless far from the plane, dangerous close to it.
    /// </summary>
    public class BossMissile : MonoBehaviour
    {
        public enum State { Flying, Held, Falling, Done }

        public static readonly List<BossMissile> Active = new List<BossMissile>();
        /// <summary>A palm held a missile until it dropped.</summary>
        public static event Action<BossMissile> AnyDefused;
        /// <summary>A missile went off, wherever it was.</summary>
        public static event Action<BossMissile> AnyExploded;

        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

        public float length = 0.075f;
        [Tooltip("Touch radius for hands and the plane, in metres.")]
        public float radius = 0.014f;
        [Tooltip("A palm this close to the missile's middle holds it, in metres.")]
        public float holdRadius = 0.06f;
        [Tooltip("Seconds a palm can slip off before the hold starts over, for tracking hiccups.")]
        public float holdSlipSeconds = 0.2f;
        [Tooltip("The missile goes off when it passes this close to the plane, in metres, so a near miss still counts.")]
        public float proximityFuse = 0.045f;

        public State CurrentState { get; private set; } = State.Flying;
        public bool IsAlive => CurrentState == State.Flying || CurrentState == State.Held;
        public bool IsHeld => CurrentState == State.Held;
        /// <summary>0 to 1: how far through the hold that drops it.</summary>
        public float HoldProgress => _cfg != null ? Mathf.Clamp01(_held / Mathf.Max(0.05f, _cfg.missileHoldSeconds)) : 0f;
        public bool WasSmacked { get; private set; }
        public Vector3 Position => transform.position;
        public Vector3 Forward => _forward;

        LevelConfig _cfg;
        MiniPlane _plane;
        HandSource[] _hands;
        Vector3 _forward;
        float _age;
        float _held;
        float _slip;
        Vector3 _fallVelocity;
        Vector3 _spin;
        Transform _flame;
        Renderer[] _renderers = new Renderer[0];
        MaterialPropertyBlock _block;
        readonly List<BoneSegment> _segments = new List<BoneSegment>(32);

        public void Setup(LevelConfig cfg, MiniPlane plane, HandSource[] hands, GameArt art, Vector3 position, Vector3 direction)
        {
            _cfg = cfg;
            _plane = plane;
            _hands = hands;
            _forward = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(_forward, Vector3.up));
            BuildModel(art);
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
        }

        void BuildModel(GameArt art)
        {
            var mat = art != null ? art.planeMatte : null;
            var cream = new Color(0.96f, 0.9f, 0.78f);
            var coral = new Color(0.9f, 0.4f, 0.3f);
            var plum = new Color(0.42f, 0.24f, 0.3f);
            float d = radius * 1.15f;
            var x90 = Quaternion.Euler(90f, 0f, 0f);
            ModelUtil.Primitive(PrimitiveType.Cylinder, transform, Vector3.zero, new Vector3(d, length * 0.36f, d), x90, mat, cream);
            ModelUtil.Primitive(PrimitiveType.Sphere, transform, Vector3.forward * length * 0.36f, new Vector3(d, d, length * 0.34f), Quaternion.identity, mat, coral);
            ModelUtil.Primitive(PrimitiveType.Cylinder, transform, Vector3.forward * length * 0.12f, new Vector3(d * 1.04f, length * 0.04f, d * 1.04f), x90, mat, coral);
            for (int i = 0; i < 4; i++)
            {
                var rot = Quaternion.Euler(0f, 0f, 45f + i * 90f);
                ModelUtil.Primitive(PrimitiveType.Cube, transform, rot * (Vector3.up * d * 0.75f) + Vector3.back * length * 0.3f,
                    new Vector3(d * 0.12f, d * 0.9f, length * 0.22f), rot, mat, coral);
            }
            ModelUtil.Primitive(PrimitiveType.Cylinder, transform, Vector3.back * length * 0.38f, new Vector3(d * 0.7f, length * 0.04f, d * 0.7f), x90, mat, plum);
            _flame = ModelUtil.Primitive(PrimitiveType.Sphere, transform, Vector3.back * length * 0.47f, new Vector3(d * 0.6f, d * 0.6f, d * 1.1f),
                Quaternion.identity, mat, new Color(1f, 0.82f, 0.3f)).transform;
            if (art != null) ModelUtil.AddSilhouette(gameObject, art.silhouette);
        }

        void OnEnable() { Active.Add(this); }

        void OnDisable() { Active.Remove(this); }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || CurrentState == State.Done) return;

            if (CurrentState == State.Falling)
            {
                _age += dt;
                _fallVelocity += Physics.gravity * 0.35f * dt;
                transform.position += _fallVelocity * dt;
                transform.Rotate(_spin * dt, Space.Self);
                transform.localScale = Vector3.one * Mathf.Clamp01(1.6f - _age * 0.8f);
                if (_age > 2f) Destroy(gameObject);
                return;
            }

            _age += dt;
            if (_age > _cfg.missileLifetime)
            {
                Explode();
                return;
            }

            if (!GameManager.IsPaused && CheckHands(dt)) return;

            if (CurrentState == State.Flying)
            {
                if (_plane != null && _plane.IsFlying)
                {
                    Vector3 to = _plane.Position - transform.position;
                    if (to.sqrMagnitude > 1e-8f)
                        _forward = Vector3.RotateTowards(_forward, to.normalized, _cfg.missileTurnRateDeg * Mathf.Deg2Rad * dt, 0f);
                }
                transform.position += _forward * (_cfg.missileSpeed * dt);
                transform.rotation = Quaternion.LookRotation(_forward, Vector3.up) * Quaternion.Euler(0f, 0f, _age * 200f);
                if (_flame != null) _flame.localScale = new Vector3(1f, 1f, 1f + 0.4f * Mathf.Sin(_age * 60f)) * (radius * 0.7f);

                if (_plane != null && _plane.IsFlying && Vector3.Distance(_plane.Position, transform.position) < Mathf.Max(proximityFuse, radius + _plane.Radius))
                {
                    Explode();
                    return;
                }
            }
            else
            {
                // Held: the motor strains against the palm, shaking, and flashes faster as it gives out.
                Vector3 shake = UnityEngine.Random.insideUnitSphere * 0.0015f;
                transform.rotation = Quaternion.LookRotation(_forward, Vector3.up) * Quaternion.Euler(shake * 4000f);
                SetFlash(Mathf.Repeat(Time.time * Mathf.Lerp(4f, 14f, HoldProgress), 1f) < 0.5f ? 0.7f : 0f);
            }
        }

        /// <summary>Handles palms holding the missile and hands smacking it. True if the missile is gone.</summary>
        bool CheckHands(float dt)
        {
            bool palmOn = false;
            if (_hands != null)
            {
                foreach (var h in _hands)
                {
                    if (h == null || !h.IsTracked) continue;
                    float speed = HandMotion.Speed(h);
                    if (!h.GetBoneSegments(_segments)) continue;
                    bool touching = HandSource.MinSurfaceDistance(_segments, transform.position) < radius;
                    bool palmClose = h.TryGetJoint(HandJoint.Palm, out var palm) && Vector3.Distance(palm, transform.position) < holdRadius;
                    if (!touching && !palmClose) continue;
                    if (speed > _cfg.missileSmackSpeed)
                    {
                        WasSmacked = true;
                        Explode();
                        return true;
                    }
                    if (palmClose) palmOn = true;
                }
            }

            if (palmOn)
            {
                _slip = 0f;
                _held += dt;
                if (CurrentState == State.Flying) CurrentState = State.Held;
                if (_held >= _cfg.missileHoldSeconds)
                {
                    Defuse();
                    return true;
                }
            }
            else if (CurrentState == State.Held)
            {
                _slip += dt;
                if (_slip > holdSlipSeconds)
                {
                    _held = 0f;
                    CurrentState = State.Flying;
                    SetFlash(0f);
                }
            }
            return false;
        }

        void SetFlash(float amount)
        {
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                // Keep each part's own colour, set when it was built.
                r.GetPropertyBlock(_block);
                _block.SetFloat(FlashId, amount);
                _block.SetColor(FlashColorId, Color.white);
                r.SetPropertyBlock(_block);
            }
        }

        /// <summary>Puts the missile somewhere, heading a given way. For tests and scripted moments.</summary>
        public void SetCourse(Vector3 position, Vector3 direction)
        {
            transform.position = position;
            if (direction.sqrMagnitude > 1e-6f) _forward = direction.normalized;
        }

        /// <summary>The motor dies and the missile tumbles out of the sky.</summary>
        public void Defuse()
        {
            if (!IsAlive) return;
            CurrentState = State.Falling;
            _age = 0f;
            SetFlash(0f);
            if (_flame != null) _flame.gameObject.SetActive(false);
            _fallVelocity = _forward * 0.03f;
            _spin = new Vector3(UnityEngine.Random.Range(-200f, 200f), UnityEngine.Random.Range(-90f, 90f), UnityEngine.Random.Range(-300f, 300f));
            ComicVfx.Play(ComicKind.Flick, transform.position, 0.09f);
            Sfx.Play(SoundKind.Defused, transform.position);
            AnyDefused?.Invoke(this);
        }

        /// <summary>Goes off now. The plane inside the blast takes damage.</summary>
        public void Explode()
        {
            if (!IsAlive) return;
            CurrentState = State.Done;
            ComicVfx.Play(ComicKind.Blast, transform.position, _cfg.missileBlastRadius * 1.3f);
            if (_plane != null && _plane.IsFlying && Vector3.Distance(transform.position, _plane.Position) < _cfg.missileBlastRadius)
                _plane.Damage("caught in a missile blast");
            Sfx.Play(WasSmacked ? SoundKind.SmallBlast : SoundKind.Explosion, transform.position);
            AnyExploded?.Invoke(this);
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
