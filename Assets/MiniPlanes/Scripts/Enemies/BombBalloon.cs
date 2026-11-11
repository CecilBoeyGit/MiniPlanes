using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// The bomber's red hot air balloon. It arms and flashes when the plane comes close, then explodes
    /// when its fuse runs out. A hand touching it sets it off at once. While armed, cupping it with both
    /// hands contains the blast, so it cannot hurt the plane.
    /// </summary>
    public class BombBalloon : MonoBehaviour
    {
        public enum State { Idle, Armed, Done }

        public static readonly List<BombBalloon> Active = new List<BombBalloon>();

        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

        public float size = 0.07f;
        [Tooltip("Touch radius for hands, in metres.")]
        public float radius = 0.028f;

        [Tooltip("Height of the bomb icon above the balloon's centre, in metres.")]
        public float iconHeight = 0.058f;

        /// <summary>Any bomb balloon was contained by the player's hands.</summary>
        public static event Action<BombBalloon> AnyContained;

        public State CurrentState { get; private set; } = State.Idle;
        public bool IsAlive => CurrentState != State.Done;
        public bool WasContained { get; private set; }
        public Vector3 Position => transform.position;

        LevelConfig _cfg;
        MiniPlane _plane;
        HandSource[] _hands;
        Renderer[] _renderers = new Renderer[0];
        MaterialPropertyBlock _block;
        Vector3 _anchor;
        float _life;
        float _fuse;
        float _coverTime;
        float _bobPhase;
        Transform _icon;
        float _passAt = -1f;
        float _passMiss = float.PositiveInfinity;

        public void Setup(LevelConfig cfg, MiniPlane plane, HandSource[] hands, GameArt art, Vector3 position)
        {
            _cfg = cfg;
            _plane = plane;
            _hands = hands;
            _anchor = position;
            transform.position = position;
            _life = cfg.bombLifetime;
            _bobPhase = UnityEngine.Random.value * 6.28f;
            _block = new MaterialPropertyBlock();

            if (art != null && art.balloonBomb != null)
            {
                ModelUtil.Attach(transform, art.balloonBomb, art.planeMatte, size, ModelUtil.FitAxis.Height);
            }
            else
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = sphere.GetComponent<Collider>();
                if (col != null) Destroy(col);
                sphere.transform.SetParent(transform, false);
                sphere.transform.localScale = new Vector3(size * 0.75f, size, size * 0.75f);
                var r = sphere.GetComponent<MeshRenderer>();
                if (art != null) r.sharedMaterial = art.planeMatte;
                var b = new MaterialPropertyBlock();
                b.SetFloat("_UseVertexColor", 0f);
                b.SetColor(BaseColorId, new Color(0.9f, 0.25f, 0.2f));
                r.SetPropertyBlock(b);
            }
            _renderers = GetComponentsInChildren<Renderer>(true);

            // Where along its path the plane comes closest, so the fuse can be timed to that moment.
            if (plane != null && plane.Path != null)
            {
                var samples = plane.Path.Samples;
                float best = float.PositiveInfinity;
                int bestIndex = -1;
                for (int i = 0; i < samples.Count; i += 2)
                {
                    float d = (samples[i] - position).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        bestIndex = i;
                    }
                }
                if (bestIndex >= 0)
                {
                    _passMiss = Mathf.Sqrt(best);
                    _passAt = plane.Path.Length * bestIndex / Mathf.Max(1f, samples.Count - 1f);
                }
            }

            if (art != null && art.bombIcon != null)
            {
                _icon = Instantiate(art.bombIcon, transform, false).transform;
                _icon.localPosition = Vector3.up * iconHeight;
            }
        }

        void OnEnable() { Active.Add(this); }

        void OnDisable() { Active.Remove(this); }

        void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            transform.position = _anchor + Vector3.up * (Mathf.Sin(Time.time * 1.1f + _bobPhase) * 0.004f);
            if (_icon != null) _icon.position = transform.position + Vector3.up * iconHeight;

            if (CurrentState == State.Idle)
            {
                _life -= dt;
                if (_life <= 0f)
                {
                    CurrentState = State.Done;
                    ComicVfx.Play(ComicKind.Poof, transform.position);
                    Destroy(gameObject);
                    return;
                }
                if (_plane != null && _plane.IsFlying && ShouldArm()) Arm();
                return;
            }

            // Armed: flash faster and faster as the fuse burns.
            _fuse -= dt;
            float urgency = 1f - Mathf.Clamp01(_fuse / Mathf.Max(0.05f, _cfg.bombFuseSeconds));
            float rate = Mathf.Lerp(4f, 16f, urgency);
            SetFlash(Mathf.Repeat(Time.time * rate, 1f) < 0.5f ? 0.85f : 0f);

            if (BothHandsCovering())
            {
                _coverTime += dt;
                if (_coverTime >= _cfg.bombCoverHoldSeconds)
                {
                    Contain();
                    return;
                }
            }
            else
            {
                _coverTime = 0f;
            }

            if (_fuse <= 0f) Detonate();
        }

        /// <summary>
        /// Arms when the plane is one fuse-length away from passing it, so the full warning flash plays and
        /// the blast comes as the plane goes by. A plane that wanders close by some other way arms it too.
        /// </summary>
        bool ShouldArm()
        {
            if (Vector3.Distance(transform.position, _plane.Position) < _cfg.bombArmProximity) return true;
            if (_passAt < 0f || _passMiss > _cfg.bombBlastRadius) return false;
            float ahead = _passAt - _plane.Distance;
            return ahead > 0f && ahead <= _plane.Speed * _cfg.bombFuseSeconds;
        }

        public void Arm()
        {
            if (CurrentState != State.Idle) return;
            CurrentState = State.Armed;
            _fuse = _cfg.bombFuseSeconds;
            Sfx.Play(SoundKind.BombArmed, transform.position);
        }

        void SetFlash(float amount)
        {
            _block.SetFloat(FlashId, amount);
            _block.SetColor(FlashColorId, new Color(1f, 0.08f, 0.05f));
            foreach (var r in _renderers) if (r != null) r.SetPropertyBlock(_block);
        }

        /// <summary>Both palms close by, on roughly opposite sides of the balloon.</summary>
        public bool BothHandsCovering()
        {
            if (_hands == null || _hands.Length < 2 || _hands[0] == null || _hands[1] == null) return false;
            if (!_hands[0].IsTracked || !_hands[1].IsTracked) return false;
            if (!_hands[0].TryGetJoint(HandJoint.Palm, out var a) || !_hands[1].TryGetJoint(HandJoint.Palm, out var b)) return false;
            Vector3 p = transform.position;
            Vector3 da = a - p;
            Vector3 db = b - p;
            float reach = _cfg.bombCoverDistance;
            if (da.sqrMagnitude > reach * reach || db.sqrMagnitude > reach * reach) return false;
            return Vector3.Dot(da.normalized, db.normalized) < -0.2f;
        }

        /// <summary>Called when a hand's bone capsules touch the balloon.</summary>
        public void TouchedByHand()
        {
            if (!IsAlive) return;
            // Cupping hands naturally brush the balloon: that is containment, not an accident.
            if (CurrentState == State.Armed && BothHandsCovering()) return;
            Detonate();
        }

        void Contain()
        {
            CurrentState = State.Done;
            WasContained = true;
            ComicVfx.Play(ComicKind.Poof, transform.position);
            Sfx.Play(SoundKind.Defused, transform.position);
            AnyContained?.Invoke(this);
            Destroy(gameObject);
        }

        /// <summary>Explodes now. Anything within the blast radius takes damage.</summary>
        public void Detonate()
        {
            if (!IsAlive) return;
            CurrentState = State.Done;
            ComicVfx.Play(ComicKind.Blast, transform.position, _cfg.bombBlastRadius * 1.3f);
            Sfx.Play(SoundKind.Explosion, transform.position);
            if (_plane != null && _plane.IsFlying &&
                Vector3.Distance(transform.position, _plane.Position) < _cfg.bombBlastRadius)
            {
                _plane.Damage("caught in a bomb blast");
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
