using System;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>Every sound the game plays. Each maps to one entry in <see cref="GameSounds"/>.</summary>
    public enum SoundKind
    {
        RingPass, RingBigO, RingClip,
        PlaneHit, PlaneCrash, EngineLoop, Boost,
        Flick, GunBurst, BulletBlock,
        TurretOn, TurretShot,
        ShieldOn, ShieldBreak, BalloonPop,
        BombArmed, Explosion, SmallBlast, Defused,
        TubeBreak, IslandShatter, Dust,
        BossAlarm, MissileLock, MissileLaunch,
        UiClick, Pause, Unpause, Title, LevelComplete, NewWave, LevelFailed, Purchase, Reward
    }

    /// <summary>One sound: its clip and how it plays.</summary>
    [Serializable]
    public class SoundEntry
    {
        public SoundKind kind;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("Random pitch change either way, so repeats don't sound identical.")]
        [Range(0f, 0.3f)] public float pitchJitter = 0.05f;
        [Tooltip("Heard from where it happens. Off for menus and level jingles, which play in your head.")]
        public bool spatial = true;
        [Tooltip("The same sound won't start again within this many seconds, so bursts don't pile up.")]
        public float minInterval = 0.04f;
    }

    /// <summary>
    /// The game's sound list. Edit clips and volumes here: Assets/MiniPlanes/Audio/GameSounds.asset.
    /// The clips are made from the Skylooper sound collection by Tools/Audio/convert_sfx.sh.
    /// </summary>
    [CreateAssetMenu(fileName = "GameSounds", menuName = "MiniPlanes/Game Sounds")]
    public class GameSounds : ScriptableObject
    {
        [Range(0f, 1f)] public float masterVolume = 1f;
        public SoundEntry[] sounds = new SoundEntry[0];

        public SoundEntry Get(SoundKind kind)
        {
            if (sounds == null) return null;
            foreach (var s in sounds)
                if (s != null && s.kind == kind) return s;
            return null;
        }
    }
}
