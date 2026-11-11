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

        GameSounds _sounds;
        Transform _head;
        readonly List<AudioSource> _pool = new List<AudioSource>();
        readonly Dictionary<SoundKind, float> _lastPlayed = new Dictionary<SoundKind, float>();
        int _next;

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
            src.Play();
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
