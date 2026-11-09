using UnityEngine;

namespace MiniPlanes
{
    public enum ComicKind { RingPass, Crash, Flick, Blast, Poof, Spark, ShieldPop, Plus1, Plus2 }

    /// <summary>
    /// Pooled hand-drawn comic effects from the art bible: each plays its onset, peak and breakup frames
    /// on a billboard in the room. Score numbers float up and fade.
    /// </summary>
    public class ComicVfx : MonoBehaviour
    {
        public static ComicVfx Instance { get; private set; }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        class Sprite
        {
            public MeshRenderer renderer;
            public Transform transform;
            public Texture2D[] frames;
            public float[] durations;
            public float age;
            public float total;
            public float size;
            public Color tint;
            public Vector3 position;
            public Vector3 drift;
            public bool active;
            public bool number;
        }

        const int PoolSize = 24;
        static readonly float[] BurstTiming = { 0.07f, 0.24f, 0.2f };
        static readonly float[] NumberTiming = { 0.85f };

        GameArt _art;
        Transform _head;
        Sprite[] _pool;

        /// <summary>Which head the billboards face. Set once at start, or by tests that swap the head.</summary>
        public void SetHead(Transform head) { _head = head; }

        MaterialPropertyBlock _block;

        public void Init(GameArt art, Transform head)
        {
            Instance = this;
            _art = art;
            _head = head;
            _block = new MaterialPropertyBlock();
            var quad = ModelUtil.QuadMesh();
            _pool = new Sprite[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Comic" + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = art != null ? art.comicSprite : null;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;
                _pool[i] = new Sprite { renderer = r, transform = go.transform };
            }
        }

        public static void Play(ComicKind kind, Vector3 position, float size = 0f)
        {
            if (Instance != null) Instance.PlayInternal(kind, position, size);
        }

        void PlayInternal(ComicKind kind, Vector3 position, float size)
        {
            if (_art == null || _art.comicSprite == null) return;
            Texture2D[] frames;
            Color tint = Color.white;
            float defaultSize;
            bool number = false;
            switch (kind)
            {
                case ComicKind.RingPass:
                    frames = new[] { _art.ringOnset, _art.ringPeak, _art.ringBreakup };
                    defaultSize = 0.11f;
                    break;
                case ComicKind.Crash:
                    frames = new[] { _art.crashOnset, _art.crashPeak, _art.crashBreakup };
                    defaultSize = 0.13f;
                    break;
                case ComicKind.Flick:
                    frames = new[] { _art.flickOnset, _art.flickPeak, _art.flickBreakup };
                    defaultSize = 0.11f;
                    break;
                case ComicKind.Blast:
                    frames = new[] { _art.crashOnset, _art.crashPeak, _art.crashBreakup };
                    defaultSize = 0.3f;
                    break;
                case ComicKind.Poof:
                    frames = new[] { _art.crashOnset, _art.crashBreakup };
                    tint = new Color(0.8f, 0.8f, 0.82f);
                    defaultSize = 0.09f;
                    break;
                case ComicKind.ShieldPop:
                    frames = new[] { _art.ringOnset, _art.ringPeak, _art.ringBreakup };
                    tint = new Color(0.55f, 0.75f, 1f);
                    defaultSize = 0.1f;
                    break;
                case ComicKind.Spark:
                    frames = new[] { _art.flickOnset };
                    defaultSize = 0.05f;
                    break;
                case ComicKind.Plus1:
                    frames = new[] { _art.plus1 };
                    defaultSize = 0.06f;
                    number = true;
                    break;
                default:
                    frames = new[] { _art.plus2 };
                    defaultSize = 0.06f;
                    number = true;
                    break;
            }

            var s = NextFree();
            s.frames = frames;
            s.durations = number ? NumberTiming : BurstTiming;
            s.total = 0f;
            for (int i = 0; i < frames.Length && i < s.durations.Length; i++) s.total += s.durations[i];
            s.age = 0f;
            s.size = size > 0f ? size : defaultSize;
            s.tint = tint;
            s.position = position;
            s.drift = number ? Vector3.up * 0.07f : Vector3.zero;
            s.number = number;
            s.active = true;
            s.renderer.enabled = true;
            Apply(s);
        }

        Sprite NextFree()
        {
            Sprite oldest = _pool[0];
            foreach (var s in _pool)
            {
                if (!s.active) return s;
                if (s.age / Mathf.Max(0.01f, s.total) > oldest.age / Mathf.Max(0.01f, oldest.total)) oldest = s;
            }
            return oldest;
        }

        void LateUpdate()
        {
            if (_pool == null) return;
            foreach (var s in _pool)
            {
                if (!s.active) continue;
                s.age += Time.deltaTime;
                if (s.age >= s.total)
                {
                    s.active = false;
                    s.renderer.enabled = false;
                    continue;
                }
                Apply(s);
            }
        }

        void Apply(Sprite s)
        {
            // Pick the frame.
            int frame = 0;
            float t = s.age;
            for (int i = 0; i < s.frames.Length && i < s.durations.Length; i++)
            {
                frame = i;
                if (t < s.durations[i]) break;
                t -= s.durations[i];
            }
            float frameT = s.durations[frame] > 0f ? Mathf.Clamp01(t / s.durations[frame]) : 1f;

            float life = Mathf.Clamp01(s.age / s.total);
            float scale;
            float alpha = 1f;
            if (s.number)
            {
                // Punch in, hold, float away.
                scale = life < 0.15f ? Mathf.Lerp(0.4f, 1.15f, life / 0.15f) : Mathf.Lerp(1.15f, 1f, (life - 0.15f) / 0.2f);
                alpha = life > 0.7f ? 1f - (life - 0.7f) / 0.3f : 1f;
            }
            else
            {
                scale = frame == 0
                    ? Mathf.Lerp(0.45f, 0.8f, frameT)
                    : Mathf.Lerp(0.95f, 1.1f, life);
                if (life > 0.8f) alpha = 1f - (life - 0.8f) / 0.2f;
            }

            s.transform.position = s.position + s.drift * life;
            s.transform.localScale = Vector3.one * (s.size * scale);
            if (_head != null)
            {
                Vector3 look = s.transform.position - _head.position;
                if (look.sqrMagnitude > 1e-6f) s.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            }

            var tex = s.frames[frame];
            s.renderer.enabled = tex != null;
            if (tex == null) return;
            _block.Clear();
            _block.SetTexture(BaseMapId, tex);
            _block.SetColor(BaseColorId, s.tint);
            _block.SetFloat(AlphaId, alpha);
            s.renderer.SetPropertyBlock(_block);
        }
    }
}
