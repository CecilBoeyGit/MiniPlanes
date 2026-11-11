using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Shows and hides the faint flight-path line according to the level's rules: always on, timed at
    /// the start of a flight, or brought back briefly by a streak of consecutive ring passes. Only the
    /// part of the path still ahead of the plane is drawn.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class PathRevealer : MonoBehaviour
    {
        const int MaxLinePoints = 900;
        const float LinePointSpacing = 0.015f;
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int TrimId = Shader.PropertyToID("_Trim");
        static readonly int TrimFadeId = Shader.PropertyToID("_TrimFade");
        static readonly int ReachId = Shader.PropertyToID("_Reach");
        static readonly int ReachFadeId = Shader.PropertyToID("_ReachFade");

        [Tooltip("Width of the line, in metres.")]
        public float lineWidth = 0.0028f;
        [Tooltip("Gap between the plane and the start of the line, in metres.")]
        public float gapAheadOfPlane = 0.02f;
        [Tooltip("Length over which the start of the line fades in, in metres.")]
        public float startFadeLength = 0.05f;
        [Tooltip("Keep the line on regardless of the level. For testing path shapes.")]
        public bool debugAlwaysShow;

        public int Streak { get; private set; }
        public float CurrentAlpha { get; private set; }
        public bool IsRevealing => _active && (_alwaysOn || Time.time < _visibleUntil || debugAlwaysShow);
        /// <summary>How far along the path (0 to 1) the line is drawn: below 1 while a streak reveal grows.</summary>
        public float Reach { get; private set; } = 1f;
        /// <summary>A streak reveal is playing: growing, holding or fading out.</summary>
        public bool StreakRevealing => _streakAt >= 0f && Time.time < _visibleUntil;

        /// <summary>Distance along the path where the drawn line starts. Everything before it is hidden.</summary>
        public float VisibleFromDistance { get; private set; }

        LineRenderer _line;
        MaterialPropertyBlock _block;
        LevelConfig _cfg;
        MiniPlane _plane;
        float _pathLength = 1f;
        bool _active;
        bool _alwaysOn;
        float _visibleUntil;
        float _lastRingTime;
        float _streakAt = -1f;
        float _streakFrom;

        void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.enabled = false;
            _line.useWorldSpace = true;
            _line.textureMode = LineTextureMode.Stretch;
            _line.widthMultiplier = lineWidth;
            _line.numCapVertices = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _block = new MaterialPropertyBlock();
        }

        /// <summary>Loads the path and shows it through the pre-flight countdown (unless Hidden).</summary>
        public void Begin(CatmullRomPath path, LevelConfig cfg, MiniPlane plane)
        {
            _cfg = cfg;
            _plane = plane;
            _pathLength = Mathf.Max(0.01f, path.Length);
            _active = true;
            Streak = 0;
            _streakAt = -1f;
            Reach = 1f;
            _lastRingTime = Time.time;
            _alwaysOn = cfg.revealMode == PathRevealMode.AlwaysVisible;
            // Stays on until OnTakeOff starts the timed part.
            _visibleUntil = cfg.revealMode == PathRevealMode.Hidden ? 0f : float.PositiveInfinity;

            // Evenly spaced, so the line's texture coordinate along its length matches distance along the path.
            int count = Mathf.Clamp(Mathf.CeilToInt(path.Length / LinePointSpacing) + 1, 2, MaxLinePoints);
            _line.positionCount = count;
            for (int i = 0; i < count; i++)
                _line.SetPosition(i, path.PositionAt(path.Length * i / (count - 1)));
        }

        public void OnTakeOff()
        {
            if (_cfg == null || _cfg.revealMode != PathRevealMode.TimedAtStart) return;
            _visibleUntil = Time.time + _cfg.startRevealSeconds;
        }

        /// <summary>Counts a successful ring pass toward the streak.</summary>
        public void OnRingPass()
        {
            if (_cfg == null) return;
            if (Time.time - _lastRingTime > _cfg.streakResetSeconds) Streak = 0;
            _lastRingTime = Time.time;
            Streak++;

            if (_cfg.revealMode == PathRevealMode.TimedAtStart && _cfg.streakToReveal > 0 && Streak >= _cfg.streakToReveal)
            {
                Streak = 0;
                // Grow from where the plane is now out to the end of the path, hold, then fade away.
                _streakAt = Time.time;
                _streakFrom = _plane != null && _plane.Path != null ? Mathf.Clamp01((_plane.Distance + gapAheadOfPlane) / _pathLength) : 0f;
                _visibleUntil = Time.time + _cfg.streakRevealSeconds;
                Sfx.Play(SoundKind.StreakReveal);
            }
        }

        /// <summary>A crash, hit or similar mishap breaks the streak.</summary>
        public void BreakStreak()
        {
            Streak = 0;
        }

        public void End()
        {
            _active = false;
            _alwaysOn = false;
        }

        void Update()
        {
            if (StreakRevealing && _cfg != null)
            {
                // A streak reveal sets its own brightness: on at once while it grows, then fading to nothing.
                float age = Time.time - _streakAt;
                float grow = Mathf.Clamp01(age / Mathf.Max(0.01f, _cfg.streakGrowSeconds));
                Reach = Mathf.Lerp(_streakFrom, 1f, 1f - (1f - grow) * (1f - grow));
                float left = _visibleUntil - Time.time;
                CurrentAlpha = _cfg.pathAlpha * Mathf.Clamp01(left / Mathf.Max(0.01f, _cfg.streakFadeSeconds));
            }
            else
            {
                if (_streakAt >= 0f)
                {
                    // The streak reveal has run out: gone completely, not easing back.
                    _streakAt = -1f;
                    if (!_alwaysOn) CurrentAlpha = 0f;
                }
                Reach = 1f;
                float target = IsRevealing && _cfg != null ? _cfg.pathAlpha : 0f;
                float fade = _cfg != null ? Mathf.Max(0.01f, _cfg.revealFadeSeconds) : 0.3f;
                float full = _cfg != null ? Mathf.Max(0.01f, _cfg.pathAlpha) : 1f;
                CurrentAlpha = Mathf.MoveTowards(CurrentAlpha, target, full / fade * Time.deltaTime);
            }

            bool show = CurrentAlpha > 0.001f;
            if (_line.enabled != show) _line.enabled = show;
            if (!show) return;

            if (_active && _plane != null && _plane.Path != null)
                VisibleFromDistance = _plane.Distance > 0f ? _plane.Distance + gapAheadOfPlane : 0f;

            _block.SetFloat(AlphaId, CurrentAlpha);
            _block.SetFloat(TrimId, VisibleFromDistance / _pathLength);
            _block.SetFloat(TrimFadeId, Mathf.Max(startFadeLength, 0.001f) / _pathLength);
            _block.SetFloat(ReachId, Mathf.Max(Reach, VisibleFromDistance / _pathLength));
            _block.SetFloat(ReachFadeId, 0.04f / _pathLength);
            _line.SetPropertyBlock(_block);
        }
    }
}
