using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    public enum BalloonKind { Shield, Turret, Life }

    /// <summary>
    /// A draggable powerup balloon. A circular crosshair can be pinched and moved anywhere, and the balloon
    /// drifts slowly to it. Blue gives the plane a one-hit shield when it flies through; gold fits an
    /// automatic turret for the rest of the flight. An icon above each says which is which.
    /// </summary>
    public class PowerBalloon : MonoBehaviour
    {
        public static readonly List<PowerBalloon> Active = new List<PowerBalloon>();
        /// <summary>The plane flew through a heart balloon. GameManager adds the life, if there is room.</summary>
        public static event System.Action<PowerBalloon> AnyLifeCollected;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Balloon height in metres.")]
        public float size = 0.07f;
        [Tooltip("The plane within this distance of the balloon's centre flies through it, in metres.")]
        public float catchRadius = 0.04f;
        [Tooltip("Height of the icon above the balloon's centre, in metres.")]
        public float iconHeight = 0.058f;
        [Tooltip("Without its own model, the gold balloon is the blue one repainted with this.")]
        public PlanePalette goldPaint = new PlanePalette { name = "Gold", hueShift = -0.47f, saturation = 2f, brightness = 1.05f, onlyHue = 0.62f };
        [Tooltip("The heart balloon is the blue one repainted with this.")]
        public PlanePalette heartPaint = new PlanePalette { name = "Heart", hueShift = 0.36f, saturation = 1.3f, brightness = 1.08f, onlyHue = 0.62f };
        [Tooltip("The heart balloon's size and catch radius, as a share of the others'. It is small and harder to catch.")]
        [Range(0.3f, 1f)] public float heartScale = 0.6f;

        /// <summary>This balloon's height, in metres.</summary>
        public float Size => Kind == BalloonKind.Life ? size * heartScale : size;
        /// <summary>How close the plane must pass to catch this balloon, in metres.</summary>
        public float CatchRadius => Kind == BalloonKind.Life ? catchRadius * heartScale : catchRadius;
        float IconHeight => Kind == BalloonKind.Life ? iconHeight * heartScale : iconHeight;

        public BalloonKind Kind { get; private set; }
        public bool IsAlive { get; private set; }
        public bool IsHeld => _holder != null;
        public Vector3 CrosshairPosition { get; private set; }
        public Vector3 Position => transform.position;

        LevelConfig _cfg;
        MiniPlane _plane;
        BalloonGrabber _holder;
        Vector3 _anchor;
        float _life;
        float _bobPhase;
        Transform _crosshair;
        CanvasGroup _crosshairFade;
        Transform _icon;
        LineRenderer _tether;
        Color _tint;

        public static Color TintOf(BalloonKind kind)
        {
            switch (kind)
            {
                case BalloonKind.Shield: return new Color(0.35f, 0.68f, 1f);
                case BalloonKind.Life: return new Color(1f, 0.42f, 0.52f);
                default: return new Color(1f, 0.78f, 0.2f);
            }
        }

        public void Setup(BalloonKind kind, LevelConfig cfg, MiniPlane plane, GameArt art, Vector3 position)
        {
            Kind = kind;
            _cfg = cfg;
            _plane = plane;
            _anchor = position;
            CrosshairPosition = position;
            transform.position = position;
            _life = cfg.balloonLifetime;
            _bobPhase = Random.value * 6.28f;
            _tint = TintOf(kind);

            bool repaint = kind == BalloonKind.Life || (kind == BalloonKind.Turret && (art == null || art.balloonTurret == null));
            var modelPrefab = art == null ? null : kind == BalloonKind.Turret && art.balloonTurret != null ? art.balloonTurret : art.balloonShield;
            if (modelPrefab != null)
            {
                var model = ModelUtil.Attach(transform, modelPrefab, art.planeMatte, Size, ModelUtil.FitAxis.Height);
                if (repaint) PlanePaint.Apply(model, kind == BalloonKind.Life ? heartPaint : goldPaint);
            }
            else BuildFallback(art);

            var iconPrefab = art == null ? null : kind == BalloonKind.Shield ? art.shieldIcon : kind == BalloonKind.Life ? art.lifeIcon : art.turretIcon;
            if (iconPrefab != null)
            {
                _icon = Instantiate(iconPrefab, transform, false).transform;
                _icon.localPosition = Vector3.up * IconHeight;
                if (kind == BalloonKind.Life) _icon.localScale = Vector3.one * Mathf.Lerp(heartScale, 1f, 0.4f);
            }

            if (art != null && art.crosshair != null)
            {
                _crosshair = Instantiate(art.crosshair, transform.parent, false).transform;
                _crosshair.name = "Crosshair";
                _crosshairFade = _crosshair.GetComponentInChildren<CanvasGroup>();
                foreach (var g in _crosshair.GetComponentsInChildren<Graphic>()) g.color = new Color(_tint.r, _tint.g, _tint.b, g.color.a);
            }
            if (art != null && art.pathLine != null)
            {
                var line = new GameObject("Tether");
                line.transform.SetParent(transform, false);
                _tether = line.AddComponent<LineRenderer>();
                _tether.sharedMaterial = art.pathLine;
                _tether.positionCount = 2;
                _tether.widthMultiplier = 0.0015f;
                _tether.useWorldSpace = true;
                _tether.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _tether.receiveShadows = false;
                _tether.enabled = false;
            }
            IsAlive = true;
        }

        void BuildFallback(GameArt art)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = new Vector3(size * 0.75f, size, size * 0.75f);
            var r = sphere.GetComponent<MeshRenderer>();
            if (art != null) r.sharedMaterial = art.planeMatte;
            var block = new MaterialPropertyBlock();
            block.SetFloat("_UseVertexColor", 0f);
            block.SetColor(BaseColorId, _tint);
            r.SetPropertyBlock(block);
        }

        void OnEnable() { Active.Add(this); }

        void OnDisable() { Active.Remove(this); }

        void OnDestroy()
        {
            if (_crosshair != null) Destroy(_crosshair.gameObject);
        }

        public void Grab(BalloonGrabber who) { _holder = who; }

        public void Release(BalloonGrabber who)
        {
            if (_holder == who) _holder = null;
        }

        public void DragTo(Vector3 point) { CrosshairPosition = point; }

        /// <summary>Moves the balloon and its crosshair somewhere at once, with no drift.</summary>
        public void MoveTo(Vector3 point)
        {
            CrosshairPosition = point;
            _anchor = point;
            transform.position = point;
        }

        void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;

            float follow = Mathf.Max(0.05f, _cfg.balloonFollowSeconds);
            _anchor = Vector3.Lerp(_anchor, CrosshairPosition, 1f - Mathf.Exp(-3f * dt / follow));
            float bob = Mathf.Sin(Time.time * 1.3f + _bobPhase) * 0.004f;
            transform.position = _anchor + Vector3.up * bob;
            // The model is upright; keep the icon level above it.
            if (_icon != null) _icon.position = transform.position + Vector3.up * IconHeight;

            // Holding keeps it alive, so a balloon cannot vanish out of the player's fingers.
            if (!IsHeld) _life -= dt;
            if (_life <= 0f)
            {
                Pop(ComicKind.Poof);
                return;
            }

            if (_plane != null && _plane.IsFlying &&
                Vector3.Distance(transform.position, _plane.Position) < CatchRadius + _plane.Radius)
            {
                if (Kind == BalloonKind.Shield) _plane.GiveShield();
                else if (Kind == BalloonKind.Life) AnyLifeCollected?.Invoke(this);
                else _plane.EquipTurret(_cfg);
                Pop(ComicKind.ShieldPop);
                return;
            }

            UpdateCrosshair();
        }

        void Pop(ComicKind vfx)
        {
            IsAlive = false;
            ComicVfx.Play(vfx, transform.position);
            Destroy(gameObject);
        }

        void UpdateCrosshair()
        {
            if (_crosshair != null)
            {
                _crosshair.position = CrosshairPosition;
                float pulse = IsHeld ? 1.2f : 1f + 0.08f * Mathf.Sin(Time.time * 4f);
                _crosshair.localScale = Vector3.one * pulse;
                if (_crosshairFade != null) _crosshairFade.alpha = Mathf.Clamp01(_life / 2f);
            }

            if (_tether != null)
            {
                bool show = Vector3.Distance(transform.position, CrosshairPosition) > 0.03f;
                _tether.enabled = show;
                if (show)
                {
                    _tether.SetPosition(0, transform.position);
                    _tether.SetPosition(1, CrosshairPosition);
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
