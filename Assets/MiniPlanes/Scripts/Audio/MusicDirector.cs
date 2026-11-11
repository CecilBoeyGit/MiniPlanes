using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Plays the background music and follows the game: the start-page theme, the menu music, and in flight a
    /// normal, a hostile (enemy planes up) and a build-up mix that play in lock-step and crossfade, plus the
    /// zeppelin's own piece. Past <see cref="GameMusic.upriseStart"/> of the path the build fades in, opens up
    /// and rises with a swell that peaks on the finish, where a hit plays and the build fades back to normal.
    /// It only reads the game's state, so nothing else has to call it. Created by <see cref="Sfx"/>.
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        public static MusicDirector Instance { get; private set; }

        public enum Piece { None, StartMenu, Menu, Game, Boss }

        [Tooltip("Logs each change of music, for checking what plays when.")]
        public bool logChanges = true;

        /// <summary>What is playing now, for tests and debugging.</summary>
        public Piece Current { get; private set; } = Piece.None;
        public bool Hostile { get; private set; }
        public float UpriseLevel => _uprise.level;
        public int ClimaxCount { get; private set; }

        class Track
        {
            public AudioSource src;
            public AudioLowPassFilter filter;
            /// <summary>Optional lead-in played once before the loop.</summary>
            public AudioSource intro;
            public AudioLowPassFilter introFilter;
            public float level;
            public float trackVolume;
            public bool playing;
        }

        GameMusic _m;
        GameManager _game;
        Track _start, _menu, _normal, _hostile, _uprise, _boss;
        Track[] _all;
        AudioSource _riser, _climax;

        float _hostileHold;
        float _afterFinish = -1f;
        bool _wasFinished;
        float _duck = 1f, _duckUntil, _duckFrom, _duckTo = 1f;
        float _pause = 1f;
        float _lastProgress;
        float _riserLevel;
        float _buildAtFinish;
        bool _gamePlaying;
        // At boot the start-page music waits for the title cue, so it opens with its intro at the moment the
        // menu appears (see PlayTitle), or after a few seconds if no cue comes.
        bool _awaitTitle;
        float _awaitUntil;

        public void Init(GameMusic music)
        {
            Instance = this;
            _m = music;
            _start = MakeTrack("StartMenu", music.startMenu, music.startMenuVolume, music.startMenuIntro);
            _awaitTitle = _start.intro != null;
            _awaitUntil = Time.unscaledTime + 6f;
            _menu = MakeTrack("Menu", music.menu, music.menuVolume);
            _normal = MakeTrack("Normal", music.normal, music.normalVolume);
            _hostile = MakeTrack("Hostile", music.hostile, music.hostileVolume);
            _uprise = MakeTrack("Uprise", music.uprise, music.upriseVolume);
            _boss = MakeTrack("Boss", music.boss, music.bossVolume);
            _all = new[] { _start, _menu, _normal, _hostile, _uprise, _boss };
            _riser = MakeSource("Riser", music.riser, true);
            _climax = MakeSource("Climax", music.climax, false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        AudioSource MakeSource(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = loop;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.priority = 0;
            src.volume = 0f;
            src.bypassReverbZones = true;
            // The music carries on, dimmed, while the game is paused.
            src.ignoreListenerPause = true;
            return src;
        }

        Track MakeTrack(string name, AudioClip clip, float volume, AudioClip intro = null)
        {
            var src = MakeSource(name, clip, true);
            var t = new Track { src = src, filter = AddFilter(src), trackVolume = volume };
            if (intro != null && clip != null)
            {
                t.intro = MakeSource(name + "Intro", intro, false);
                t.introFilter = AddFilter(t.intro);
            }
            return t;
        }

        static AudioLowPassFilter AddFilter(AudioSource src)
        {
            var filter = src.gameObject.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = 22000f;
            filter.lowpassResonanceQ = 1f;
            return filter;
        }

        /// <summary>
        /// The title cue at boot: starts the start-page music from its intro at full level, now. Returns false
        /// when there is no intro, and the title jingle should play as a sound instead.
        /// </summary>
        public bool PlayTitle()
        {
            if (_start == null || _start.intro == null) return false;
            _awaitTitle = false;
            Stop(_start);
            _start.level = 1f;
            StartIfNeeded(_start);
            Log("music: title");
            return true;
        }

        /// <summary>Dips the music for a while, under a jingle.</summary>
        public void Duck(float seconds, float level = -1f, float startIn = 0f)
        {
            float to = level >= 0f ? level : _m != null ? _m.jingleDuck : 0.35f;
            float now = Time.unscaledTime;
            bool active = _duckUntil > now;
            _duckTo = Mathf.Min(to, active ? _duckTo : 1f);
            _duckFrom = active ? Mathf.Min(_duckFrom, now + startIn) : now + startIn;
            _duckUntil = Mathf.Max(_duckUntil, now + seconds);
        }

        /// <summary>Seconds until the finish hit has rung out; jingles wait for it.</summary>
        public float ClimaxRemaining =>
            _climax != null && _climax.isPlaying && _climax.clip != null ? Mathf.Max(0f, _climax.clip.length - _climax.time - 0.25f) : 0f;

        void Update()
        {
            if (_m == null) return;
            if (_game == null) _game = GetComponentInParent<GameManager>();
            if (_game == null) _game = FindAnyObjectByType<GameManager>();
            if (_game == null) return;
            float dt = Time.unscaledDeltaTime;

            Piece piece = PieceFor(_game);
            if (piece != Current) Log("music: " + Current + " -> " + piece);
            Current = piece;

            var plane = _game.Plane;
            bool inFlight = piece == Piece.Game || piece == Piece.Boss;
            bool finished = inFlight && plane != null && plane.CurrentState == MiniPlane.State.Finished;
            float progress = inFlight && plane != null ? plane.Progress : 0f;
            if (inFlight && plane != null && (plane.IsFlying || finished)) _lastProgress = progress;

            // The finish: one hit, then the build fades back into the normal mix.
            if (finished && !_wasFinished) OnFinish();
            _wasFinished = finished;
            if (!inFlight) _afterFinish = -1f;
            else if (_afterFinish >= 0f) _afterFinish += dt;
            // A new flight (next wave) starts the build over.
            if (inFlight && plane != null && plane.IsFlying && progress < _m.upriseStart) _afterFinish = -1f;

            // Enemy planes up: hostile, held a moment after the last one goes.
            bool enemies = piece == Piece.Game && AnyEnemyPlane();
            _hostileHold = enemies ? _m.hostileHoldSeconds : Mathf.Max(0f, _hostileHold - dt);
            bool hostile = piece == Piece.Game && (enemies || _hostileHold > 0f);
            if (hostile != Hostile) Log(hostile ? "music: hostile in" : "music: hostile out");
            Hostile = hostile;

            // Build level from how far along the path the plane is.
            float build = 0f;
            if (piece == Piece.Game)
            {
                if (_afterFinish >= 0f)
                    build = _buildAtFinish * (1f - Mathf.Clamp01(_afterFinish / Mathf.Max(0.1f, _m.upriseFadeOutSeconds)));
                else if (plane != null && (plane.IsFlying || plane.CurrentState == MiniPlane.State.Crashed))
                    build = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_m.upriseStart, _m.upriseStart + _m.upriseFadeIn, _lastProgress));
            }

            // Targets for each track.
            float fade = _m.crossfadeSeconds;
            if (_awaitTitle && (piece != Piece.StartMenu || Time.unscaledTime > _awaitUntil)) _awaitTitle = false;
            Approach(_start, piece == Piece.StartMenu && !_awaitTitle ? 1f : 0f, fade, fade, dt);
            Approach(_menu, piece == Piece.Menu ? 1f : 0f, fade, fade, dt);
            Approach(_boss, piece == Piece.Boss ? 1f : 0f, _m.bossFadeSeconds, _m.bossFadeSeconds, dt);
            bool game = piece == Piece.Game;
            float gameFadeIn = Current == Piece.Game && _boss.level > 0f ? _m.bossFadeSeconds : fade;
            Approach(_hostile, game && hostile ? 1f - build : 0f, _m.hostileInSeconds, _m.hostileOutSeconds, dt);
            // The build follows the plane closely; the fade-in share of the path already smooths it.
            Approach(_uprise, game ? build : 0f, 0.25f, game ? 0.25f : fade, dt);
            float rest = Mathf.Clamp01(1f - _uprise.level - _hostile.level);
            Approach(_normal, game ? rest : 0f, gameFadeIn, fade, dt, snap: game);

            // The build opens from muffled to bright as the finish nears.
            float open = 1f;
            if (game && _afterFinish < 0f)
            {
                float k = Mathf.InverseLerp(_m.upriseStart, 1f, _lastProgress);
                open = k * k;
            }
            float upriseCutoff = Mathf.Lerp(_m.upriseStartCutoff, 22000f, open);

            // Pause and jingle ducking.
            bool paused = GameManager.IsPaused;
            _pause = Mathf.MoveTowards(_pause, paused ? _m.pauseVolume : 1f, dt * 2.5f);
            float duckTarget = Time.unscaledTime < _duckUntil && Time.unscaledTime >= _duckFrom - 0.2f ? _duckTo : 1f;
            _duck = Mathf.MoveTowards(_duck, duckTarget, dt * (duckTarget < _duck ? 4f : 1.2f));
            if (Time.unscaledTime >= _duckUntil) _duckTo = 1f;
            float master = _m.volume * _pause * _duck;
            float pauseCut = paused ? _m.pauseCutoff : 22000f;

            foreach (var t in _all)
            {
                t.src.volume = master * t.trackVolume * t.level;
                t.filter.cutoffFrequency = Mathf.Min(pauseCut, t == _uprise ? upriseCutoff : 22000f);
                if (t.intro != null)
                {
                    t.intro.volume = t.src.volume;
                    t.introFilter.cutoffFrequency = t.filter.cutoffFrequency;
                }
            }

            ManagePlayback(piece);
            UpdateRiser(game || piece == Piece.Boss, plane, master, dt);
        }

        static Piece PieceFor(GameManager g)
        {
            switch (g.CurrentFlow)
            {
                case GameManager.Flow.Menu:
                    return g.menu != null && g.menu.IsOpen && !g.menu.IsStartPage ? Piece.Menu : Piece.StartMenu;
                case GameManager.Flow.Intro:
                case GameManager.Flow.Result:
                    return Piece.Menu;
                default:
                    return Zeppelin.Current != null ? Piece.Boss : Piece.Game;
            }
        }

        static bool AnyEnemyPlane()
        {
            foreach (var e in EnemyPlane.Active)
                if (e != null && e.IsAlive) return true;
            return false;
        }

        static void Approach(Track t, float target, float inSeconds, float outSeconds, float dt, bool snap = false)
        {
            float secs = target > t.level ? inSeconds : outSeconds;
            // The in-flight mix shares out one whole: while the normal part only takes up what the others leave,
            // let it follow at once, so the sum never dips.
            if (snap && t.level > 0.01f) secs = Mathf.Min(secs, 0.25f);
            t.level = Mathf.MoveTowards(t.level, target, dt / Mathf.Max(0.01f, secs));
        }

        void ManagePlayback(Piece piece)
        {
            StartIfNeeded(_start);
            StartIfNeeded(_menu);
            StartIfNeeded(_boss);
            // Normal, hostile and build play together from the same instant so their bars line up.
            bool gameWanted = piece == Piece.Game || _normal.level > 0f || _hostile.level > 0f || _uprise.level > 0f;
            if (!_gamePlaying && piece != Piece.Game) gameWanted = false;
            if (gameWanted && !_gamePlaying && _normal.src.clip != null)
            {
                _gamePlaying = true;
                double at = AudioSettings.dspTime + 0.08;
                foreach (var t in new[] { _normal, _hostile, _uprise })
                {
                    if (t.src.clip == null) continue;
                    t.src.timeSamples = 0;
                    t.src.PlayScheduled(at);
                }
            }
            else if (!gameWanted && _gamePlaying)
            {
                // Silent now (menus, or the boss): stop, and start fresh from the first bar next time.
                _gamePlaying = false;
                _normal.src.Stop();
                _hostile.src.Stop();
                _uprise.src.Stop();
            }
            StopIfSilent(_start);
            StopIfSilent(_menu);
            StopIfSilent(_boss);
        }

        static void StartIfNeeded(Track t)
        {
            if (t.level <= 0f || t.playing || t.src.clip == null) return;
            t.playing = true;
            t.src.timeSamples = 0;
            if (t.intro != null && t.intro.clip != null)
            {
                // Intro once, then the loop from the sample the intro ends on.
                double at = AudioSettings.dspTime + 0.05;
                t.intro.PlayScheduled(at);
                t.src.PlayScheduled(at + (double)t.intro.clip.samples / t.intro.clip.frequency);
            }
            else
            {
                t.src.Play();
            }
        }

        static void StopIfSilent(Track t)
        {
            if (t.level <= 0f && t.playing) Stop(t);
        }

        static void Stop(Track t)
        {
            t.playing = false;
            t.src.Stop();
            if (t.intro != null) t.intro.Stop();
        }

        void UpdateRiser(bool inFlight, MiniPlane plane, float master, float dt)
        {
            if (_riser.clip == null) return;
            float target = 0f, pitch = _m.riserPitch.x;
            if (inFlight && plane != null && plane.IsFlying && _afterFinish < 0f && _lastProgress > _m.upriseStart)
            {
                float k = Mathf.InverseLerp(_m.upriseStart, 1f, _lastProgress);
                target = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.25f, k)) * Mathf.Lerp(0.4f, 1f, k);
                pitch = Mathf.Lerp(_m.riserPitch.x, _m.riserPitch.y, k);
            }
            _riserLevel = Mathf.MoveTowards(_riserLevel, target, dt / 0.3f);
            _riser.volume = master * _m.riserVolume * _riserLevel;
            _riser.pitch = pitch;
            if (_riserLevel > 0f && !_riser.isPlaying) _riser.Play();
            else if (_riserLevel <= 0f && _riser.isPlaying) _riser.Stop();
        }

        void OnFinish()
        {
            _afterFinish = 0f;
            _buildAtFinish = _uprise.level;
            ClimaxCount++;
            Log("music: finish hit");
            _riserLevel = 0f;
            if (_riser.isPlaying) _riser.Stop();
            if (_climax.clip != null)
            {
                _climax.volume = _m.volume * _m.climaxVolume;
                _climax.Play();
            }
            Duck(0.9f, _m.climaxDuck);
        }

        void Log(string message)
        {
            if (logChanges) Debug.Log("[MiniPlanes] " + message);
        }
    }
}
