using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// The background music and how it moves between pieces. Edit at Assets/MiniPlanes/Audio/GameMusic.asset.
    /// The tracks are made by Tools/Audio/synth/make_music.py. Normal, Hostile and Uprise share tempo, length and
    /// chords and play in lock-step, so the game can crossfade between them at any moment.
    /// </summary>
    [CreateAssetMenu(fileName = "GameMusic", menuName = "MiniPlanes/Game Music")]
    public class GameMusic : ScriptableObject
    {
        [Range(0f, 1f)] public float volume = 0.5f;

        [Header("Tracks")]
        [Tooltip("The start page, looped. Also what the game opens with.")]
        public AudioClip startMenu;
        [Tooltip("Played once before the start-page loop, leading straight into it. At boot it takes the place of the title jingle.")]
        public AudioClip startMenuIntro;
        [Tooltip("Every other menu page: Hangar, level intro, results.")]
        public AudioClip menu;
        [Tooltip("In flight, nothing hostile around.")]
        public AudioClip normal;
        [Tooltip("In flight while enemy planes are up. Same tempo and length as Normal.")]
        public AudioClip hostile;
        [Tooltip("The build to the finish line. Same tempo and length as Normal.")]
        public AudioClip uprise;
        [Tooltip("Zeppelin waves.")]
        public AudioClip boss;
        [Tooltip("A rising swell looped under the build; it speeds up as the plane nears the end.")]
        public AudioClip riser;
        [Tooltip("The hit when the plane reaches the end of the path.")]
        public AudioClip climax;

        [Header("Track volumes")]
        [Range(0f, 1f)] public float startMenuVolume = 0.9f;
        [Range(0f, 1f)] public float menuVolume = 0.9f;
        [Range(0f, 1f)] public float normalVolume = 0.85f;
        [Range(0f, 1f)] public float hostileVolume = 0.85f;
        [Range(0f, 1f)] public float upriseVolume = 0.9f;
        [Range(0f, 1f)] public float bossVolume = 0.9f;
        [Range(0f, 1f)] public float riserVolume = 0.35f;
        [Range(0f, 1f)] public float climaxVolume = 0.9f;

        [Header("Transitions, in seconds")]
        [Tooltip("Between the menu pieces, and between the menus and the game.")]
        public float crossfadeSeconds = 1.8f;
        public float hostileInSeconds = 1.5f;
        public float hostileOutSeconds = 3.5f;
        [Tooltip("The hostile music stays this long after the last enemy plane goes, so it doesn't flicker between waves of them.")]
        public float hostileHoldSeconds = 2f;
        public float bossFadeSeconds = 1.5f;
        [Tooltip("After the finish, the build fades back into the normal music over this long.")]
        public float upriseFadeOutSeconds = 3.5f;

        [Header("The build to the finish")]
        [Tooltip("Share of the path flown when the build starts.")]
        [Range(0.3f, 0.95f)] public float upriseStart = 0.7f;
        [Tooltip("Share of the path over which the build fades in.")]
        [Range(0.01f, 0.3f)] public float upriseFadeIn = 0.06f;
        [Tooltip("The build starts muffled and opens up to full brightness at the finish. Starting low-pass cutoff, in Hz.")]
        public float upriseStartCutoff = 900f;
        [Tooltip("Riser playback pitch at the start of the build and at the finish.")]
        public Vector2 riserPitch = new Vector2(0.75f, 1.7f);

        [Header("Ducking")]
        [Tooltip("Music level while the pause menu is open.")]
        [Range(0f, 1f)] public float pauseVolume = 0.35f;
        [Tooltip("Low-pass cutoff while paused, in Hz.")]
        public float pauseCutoff = 1100f;
        [Tooltip("Music level under level and wave jingles.")]
        [Range(0f, 1f)] public float jingleDuck = 0.35f;
        [Tooltip("Music level just after the finish hit.")]
        [Range(0f, 1f)] public float climaxDuck = 0.4f;
    }
}
