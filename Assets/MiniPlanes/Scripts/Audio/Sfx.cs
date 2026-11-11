using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Plays the game's sounds from a small pool of audio sources: world sounds from where they happen,
    /// menu and level sounds in the player's head. Anything can call <see cref="Play"/>; with no player in
    /// the scene it does nothing.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public static Sfx Instance { get; private set; }

        [Tooltip("Sounds that can play at once.")]
        public int voices = 20;
        [Tooltip("World sounds keep full volume within this distance, in metres.")]
        public float minDistance = 0.6f;
        public float maxDistance = 8f;
        [Tooltip("How much world sounds are placed in space: 1 fully, 0 not at all.")]
        [Range(0f, 1f)] public float spatialBlend = 0.75f;
        [Tooltip("Enemy engines keep full volume within this distance, in metres.")]
        public float engineMinDistance = 1.3f;

        GameSounds _sounds;
        Transform _head;
        readonly List<AudioSource> _pool = new List<AudioSource>();
        readonly Dictionary<SoundKind, float> _lastPlayed = new Dictionary<SoundKind, float>();
        int _next;

        /// <summary>What each voice is playing and when it starts (a queued voice starts later).</summary>
        struct VoiceInfo { public SoundKind kind; public float startAt; public float length; }
        readonly Dictionary<AudioSource, VoiceInfo> _info = new Dictionary<AudioSource, VoiceInfo>();

        /// <summary>Sounds started within this many seconds of each other count as the same moment.</summary>
        const float SameMoment = 0.15f;

        /// <summary>
        /// One action, one sound. When the key plays, these stop if they started at the same moment, and are
        /// skipped if they come just after it. A button that pauses plays the pause sound, not a click as well; a
        /// clipped ring that hurts the plane plays the hit; the three-in-a-row reveal replaces that ring's chime; a
        /// new best replaces the game-over jingle.
        /// </summary>
        static readonly Dictionary<SoundKind, SoundKind[]> Replaces = new Dictionary<SoundKind, SoundKind[]>
        {
            { SoundKind.Pause, new[] { SoundKind.UiClick } },
            { SoundKind.Unpause, new[] { SoundKind.UiClick } },
            { SoundKind.Purchase, new[] { SoundKind.UiClick } },
            { SoundKind.PlaneHit, new[] { SoundKind.RingClip } },
            { SoundKind.ShieldBreak, new[] { SoundKind.RingClip } },
            { SoundKind.PlaneCrash, new[] { SoundKind.RingClip, SoundKind.PlaneHit } },
            { SoundKind.StreakReveal, new[] { SoundKind.RingPass, SoundKind.RingBigO } },
            { SoundKind.Reward, new[] { SoundKind.LevelFailed } },
        };

        /// <summary>When the key plays while one of these is sounding, it waits for it to end instead of overlapping.</summary>
        static readonly Dictionary<SoundKind, SoundKind[]> WaitsFor = new Dictionary<SoundKind, SoundKind[]>
        {
            { SoundKind.LevelFailed, new[] { SoundKind.PlaneCrash } },
            { SoundKind.Reward, new[] { SoundKind.PlaneCrash, SoundKind.LevelComplete } },
            { SoundKind.LifeUp, new[] { SoundKind.LevelComplete, SoundKind.NewWave } },
        };

        /// <summary>Jingles that also wait for the music's finish hit to ring out.</summary>
        static bool WaitsForClimax(SoundKind kind)
        {
            return kind == SoundKind.NewWave || kind == SoundKind.LevelComplete || kind == SoundKind.LevelFailed;
        }

        /// <summary>How many times each sound has started, for tests.</summary>
        public static readonly Dictionary<SoundKind, int> Counts = new Dictionary<SoundKind, int>();

        public void Init(GameSounds sounds, Transform head)
        {
            Instance = this;
            _sounds = sounds;
            _head = head;
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = minDistance;
                src.maxDistance = maxDistance;
                src.dopplerLevel = 0f;
                _pool.Add(src);
            }
            if (sounds != null && sounds.music != null)
            {
                var music = new GameObject("Music").AddComponent<MusicDirector>();
                music.transform.SetParent(transform, false);
                music.Init(sounds.music);
            }
        }

        public void SetHead(Transform head) { _head = head; }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Plays a sound where something happened. Menu sounds ignore the position.</summary>
        public static void Play(SoundKind kind, Vector3 position, float volumeScale = 1f)
        {
            if (Instance != null) Instance.PlayInternal(kind, position, volumeScale, true);
        }

        /// <summary>Plays a sound in the player's head, wherever its entry says it belongs.</summary>
        public static void Play(SoundKind kind, float volumeScale = 1f)
        {
            if (Instance != null) Instance.PlayInternal(kind, Vector3.zero, volumeScale, false);
        }

        void PlayInternal(SoundKind kind, Vector3 position, float volumeScale, bool hasPosition)
        {
            var entry = _sounds != null ? _sounds.Get(kind) : null;
            if (entry == null || entry.clip == null) return;
            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(kind, out var last) && now - last < entry.minInterval) return;
            _lastPlayed[kind] = now;
            Counts[kind] = Counts.TryGetValue(kind, out var c) ? c + 1 : 1;

            // The title theme opens the start-page music; when that music has its intro, it plays the cue instead.
            if (kind == SoundKind.Title && MusicDirector.Instance != null && MusicDirector.Instance.PlayTitle()) return;

            // Skip this if something that replaces it has just started; stop what this replaces.
            foreach (var pair in _info)
            {
                if (!pair.Key.isPlaying || Mathf.Abs(pair.Value.startAt - now) > SameMoment) continue;
                if (Replaces.TryGetValue(pair.Value.kind, out var over) && System.Array.IndexOf(over, kind) >= 0) return;
            }
            if (Replaces.TryGetValue(kind, out var replaced))
                foreach (var pair in _info)
                    if (pair.Key.isPlaying && System.Array.IndexOf(replaced, pair.Value.kind) >= 0 &&
                        Mathf.Abs(pair.Value.startAt - now) <= SameMoment)
                        pair.Key.Stop();

            // Jingles queue behind what they would talk over.
            float delay = 0f;
            if (WaitsFor.TryGetValue(kind, out var waits))
                foreach (var pair in _info)
                    if (pair.Key.isPlaying && System.Array.IndexOf(waits, pair.Value.kind) >= 0)
                        delay = Mathf.Max(delay, pair.Value.startAt + pair.Value.length - now);
            if (WaitsForClimax(kind) && MusicDirector.Instance != null)
                delay = Mathf.Max(delay, MusicDirector.Instance.ClimaxRemaining);
            delay = Mathf.Min(delay, 2.5f);

            var src = NextVoice();
            bool spatial = entry.spatial && hasPosition;
            src.transform.position = spatial ? position : (_head != null ? _head.position : Vector3.zero);
            src.spatialBlend = spatial ? spatialBlend : 0f;
            src.clip = entry.clip;
            src.volume = entry.volume * volumeScale * _sounds.masterVolume;
            src.pitch = 1f + Random.Range(-entry.pitchJitter, entry.pitchJitter);
            src.loop = false;
            // Menu sounds also play during the pause, when the game clock is stopped.
            src.ignoreListenerPause = !spatial;
            if (delay > 0.01f) src.PlayDelayed(delay);
            else src.Play();
            float length = entry.clip.length / Mathf.Max(0.1f, src.pitch);
            _info[src] = new VoiceInfo { kind = kind, startAt = now + Mathf.Max(0f, delay), length = length };
            float duck = DuckLevel(kind);
            if (duck < 1f && MusicDirector.Instance != null)
                MusicDirector.Instance.Duck(delay + length, duck, delay);
        }

        /// <summary>
        /// How far the music dips under a sound (1 = not at all). The title jingle plays alone, so the start-page
        /// music comes in after it; result jingles nearly silence it, since they are in a different key.
        /// </summary>
        static float DuckLevel(SoundKind kind)
        {
            switch (kind)
            {
                case SoundKind.Title: return 0f;
                case SoundKind.LevelComplete:
                case SoundKind.LevelFailed:
                case SoundKind.Reward: return 0.12f;
                case SoundKind.NewWave:
                case SoundKind.LifeUp: return 0.3f;
                case SoundKind.BossAlarm:
                case SoundKind.EnemyWarning: return 0.45f;
                case SoundKind.StreakReveal: return 0.6f;
                default: return 1f;
            }
        }

        /// <summary>
        /// Starts a looping engine sound on something, at a random point in the loop and a slightly different
        /// pitch, so several of the same don't drone in unison. Null if there is no such sound.
        /// </summary>
        public static AudioSource StartEngine(SoundKind kind, Transform parent, float pitchSpread = 0.06f)
        {
            var src = CreateLoop(kind, parent);
            if (src == null) return null;
            src.pitch = 1f + Random.Range(-pitchSpread, pitchSpread);
            // Enemies fly a metre or more away; keep their engines at full level out to there.
            src.minDistance = Mathf.Max(src.minDistance, Instance.engineMinDistance);
            src.time = Random.Range(0f, src.clip.length * 0.95f);
            src.Play();
            return src;
        }

        /// <summary>Fades a looping sound down in pitch and volume, then removes it: an engine dying.</summary>
        public static void WindDown(AudioSource src, float seconds = 0.7f)
        {
            if (src == null) return;
            if (Instance == null || !src.isActiveAndEnabled)
            {
                Destroy(src.gameObject);
                return;
            }
            Instance.StartCoroutine(Instance.WindDownRoutine(src, seconds));
        }

        System.Collections.IEnumerator WindDownRoutine(AudioSource src, float seconds)
        {
            float v0 = src.volume, p0 = src.pitch, t = 0f;
            while (src != null && t < seconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / seconds);
                src.volume = v0 * (1f - k) * (1f - k);
                src.pitch = p0 * Mathf.Lerp(1f, 0.55f, k);
                yield return null;
            }
            if (src != null) Destroy(src.gameObject);
        }

        AudioSource NextVoice()
        {
            // Prefer a free voice; otherwise steal the oldest.
            for (int i = 0; i < _pool.Count; i++)
            {
                var s = _pool[(_next + i) % _pool.Count];
                if (!s.isPlaying)
                {
                    _next = (_next + i + 1) % _pool.Count;
                    return s;
                }
            }
            var steal = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            return steal;
        }

        /// <summary>A looping sound attached to something, such as the plane's engine. Null if there is none.</summary>
        public static AudioSource CreateLoop(SoundKind kind, Transform parent)
        {
            var inst = Instance;
            var entry = inst != null && inst._sounds != null ? inst._sounds.Get(kind) : null;
            if (entry == null || entry.clip == null) return null;
            var go = new GameObject(kind + "Loop");
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = entry.clip;
            src.loop = true;
            src.playOnAwake = false;
            src.volume = entry.volume * inst._sounds.masterVolume;
            src.spatialBlend = entry.spatial ? inst.spatialBlend : 0f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = inst.minDistance;
            src.maxDistance = inst.maxDistance;
            src.dopplerLevel = 0f;
            return src;
        }

        public static float BaseVolume(SoundKind kind)
        {
            var inst = Instance;
            var entry = inst != null && inst._sounds != null ? inst._sounds.Get(kind) : null;
            return entry != null ? entry.volume * inst._sounds.masterVolume : 0f;
        }
    }
}
