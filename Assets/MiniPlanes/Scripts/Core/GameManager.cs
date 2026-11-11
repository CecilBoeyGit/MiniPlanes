using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Top-level game flow: start panel, the tutorial levels with their intro page and unlocks, the Infinite
    /// wave mode with its islands and boss waves, lives and respawns, pause, plane perks, and the scoring and
    /// coins that tie rings, flicks, turrets, bomb covers, missiles, fuel and the HUD together. Coins are only
    /// earned in Infinite, plus a one-off reward for clearing each tutorial level the first time.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class GameManager : MonoBehaviour
    {
        public enum Flow { Menu, Intro, PreFlight, Flying, Result }

        /// <summary>The pause menu is open and the game clock is stopped. Hands must not affect the game.</summary>
        public static bool IsPaused { get; private set; }

        [Header("Scene references")]
        public Transform head;
        public GameArt art;
        public GameSounds sounds;
        public PlaneCatalog catalog;
        public CurvedHud hud;
        public MenuPanel menu;
        public PausePanel pausePanel;
        [Tooltip("The opening logo and Start button, shown once when the game starts.")]
        public SplashPanel splash;
        public WristMenuButton wristMenu;
        public PathRevealer revealer;
        public EnemySpawner spawner;
        public HandRing[] rings;
        public RingMerger merger;
        public HandHazard[] hazards;
        public FlickDetector[] flickers;

        [Header("Levels")]
        public List<LevelConfig> levels = new List<LevelConfig>();
        public InfiniteConfig infinite;

        [Header("Score points")]
        [Tooltip("Points for flying through a single-hand C ring. It is small, so it pays more.")]
        public int singleRingPoints = 2;
        [Tooltip("Points for flying through the big two-hand O ring. It is large and easy, so it pays less.")]
        public int mergedRingPoints = 1;
        [Tooltip("Points for flicking a fighter out of the sky.")]
        public int flickPoints = 2;
        [Tooltip("Points for flicking a bomber out of the sky. Bombers are worth extra.")]
        public int bomberFlickPoints = 4;
        [Tooltip("Points when the turret shoots an enemy down.")]
        public int turretKillPoints = 1;
        [Tooltip("Points for holding a zeppelin missile until it drops.")]
        public int missileDefusePoints = 2;

        [Header("Coins (saved, spent in the Hangar; earned in Infinite only)")]
        [Tooltip("Paid once per tutorial level, the first time it is passed. Tutorials pay nothing else, so replaying them can't farm coins.")]
        public int tutorialFirstClearCoins = 5;
        public int singleRingCoins = 2;
        public int mergedRingCoins = 1;
        [Tooltip("For any enemy plane knocked out of the sky, by hand or turret.")]
        public int enemyCoins = 1;
        [Tooltip("For cupping a flashing bomb balloon until it is contained.")]
        public int bombCoverCoins = 5;
        [Tooltip("For holding a zeppelin missile until it drops.")]
        public int missileCoins = 1;

        [Header("Timing")]
        [Tooltip("Seconds between a flight ending and the result panel appearing.")]
        public float resultDelaySeconds = 1.6f;
        [Tooltip("How long the start menu takes to fade in after the splash screen, in seconds.")]
        public float menuFadeInSeconds = 0.6f;
        [Tooltip("Countdown after Start is pressed, in seconds.")]
        public float startCountdownSeconds = 2f;

        [Header("Playtesting")]
        [Tooltip("Opens every level and Infinite without saving anything. Turn off to test the real unlock flow.")]
        public bool devUnlockAll = false;
        [Tooltip("Lets every plane in the Hangar be flown without buying it, without saving anything.")]
        public bool devUnlockAllPlanes = false;
        [Tooltip("Tops the saved coins up to at least this many when the game starts. 0 does nothing.")]
        public int devCoinsOnStart = 0;
        [Tooltip("Wipes saved unlocks, the high score, coins and planes when the game starts.")]
        public bool devResetProgressOnStart = false;

        public Flow CurrentFlow { get; private set; } = Flow.Menu;
        public int Score { get; private set; }
        public int RingsThisFlight { get; private set; }
        public int LevelIndex { get; private set; }
        public bool InInfinite { get; private set; }
        public int Wave { get; private set; }
        public bool LastFlightPassed { get; private set; }
        public int Lives { get; private set; }
        public int LivesTotal { get; private set; }
        /// <summary>Coins earned since the level or Infinite run began.</summary>
        public int CoinsThisRun { get; private set; }
        /// <summary>Respawns bought in the current Infinite run. Each one costs more.</summary>
        public int RespawnsBought { get; private set; }
        /// <summary>The price of the next respawn in this Infinite run.</summary>
        public int RespawnCost => infinite != null ? infinite.RespawnCost(RespawnsBought, Wave) : 0;
        /// <summary>The game-over page is offering a respawn.</summary>
        public bool OfferingRespawn { get; private set; }
        public MiniPlane Plane => _plane;
        public LevelConfig CurrentConfig => _cfg;
        public IslandField Islands => _islands;
        /// <summary>The coins the last tutorial pass paid for a first clear, or 0.</summary>
        public int LastFirstClearCoins { get; private set; }

        MiniPlane _plane;
        LevelConfig _cfg;
        LevelConfig _waveConfig;
        int _seed;
        Coroutine _flow;
        bool _startPressed;
        IslandField _islands;
        bool _perkTurretPending;
        bool _titlePlayed;
        bool _respawnBought;

        void Awake()
        {
            IsPaused = false;
            Time.timeScale = 1f;
            Progression.UnlockAllOverride = devUnlockAll;
            Progression.AllPlanesOverride = devUnlockAllPlanes;
            if (devResetProgressOnStart) Progression.ResetAll(catalog);
            if (devCoinsOnStart > Progression.Coins) Progression.AddCoins(devCoinsOnStart - Progression.Coins);
            Billboard.Head = head;

            if (levels == null || levels.Count == 0)
            {
                levels = new List<LevelConfig>();
                for (int i = 1; i <= Progression.TutorialLevelCount; i++)
                {
                    var cfg = ScriptableObject.CreateInstance<LevelConfig>();
                    cfg.ApplyDefaults(i);
                    levels.Add(cfg);
                }
            }
            if (infinite == null) infinite = InfiniteConfig.CreateDefault();

            var vfx = new GameObject("ComicVfx").AddComponent<ComicVfx>();
            vfx.transform.SetParent(transform, false);
            vfx.Init(art, head);

            var sfx = new GameObject("Sfx").AddComponent<Sfx>();
            sfx.transform.SetParent(transform, false);
            sfx.Init(sounds, head);

            var planeGo = new GameObject("MiniPlane");
            planeGo.transform.SetParent(transform, false);
            _plane = planeGo.AddComponent<MiniPlane>();
            _plane.Init(art, head);
            _plane.Crashed += OnPlaneCrashed;
            _plane.Hit += OnPlaneHit;
            _plane.ShieldLost += OnShieldLost;
            ApplyPlaneSelection();

            if (rings != null)
                foreach (var r in rings)
                {
                    if (r == null) continue;
                    r.Scored += OnSingleRingScored;
                    r.PlaneHitRing += ring => ClipRing();
                    r.Track(_plane);
                }
            if (merger != null)
            {
                merger.Scored += OnMergedRingScored;
                merger.PlaneHitRing += m => ClipRing();
                merger.Track(_plane);
            }
            if (hazards != null)
                foreach (var h in hazards)
                {
                    if (h == null) continue;
                    h.PlaneHit += (hazard, plane) => DamagePlane("hit your hand");
                    h.Track(_plane);
                }
            if (flickers != null)
                foreach (var f in flickers)
                    if (f != null) f.Flicked += OnEnemyFlicked;
            PlaneTurret.AnyEnemyDowned += OnTurretKill;
            BombBalloon.AnyContained += OnBombContained;
            BossMissile.AnyDefused += OnMissileDefused;
            PowerBalloon.AnyLifeCollected += OnLifeCollected;
            if (spawner != null)
            {
                spawner.EnemySpawned += OnEnemySpawned;
                spawner.ZeppelinSpawned += z => z.AttackStarted += OnBossAttack;
            }

            if (art != null)
            {
                UiComic.Apply(menu, art.uiComic, art.uiComicSprite, art.uiShadowSprite);
                UiComic.Apply(pausePanel, art.uiComic, art.uiComicSprite, art.uiShadowSprite);
                UiComic.Apply(wristMenu, art.uiComic, art.uiComicSprite, art.uiShadowSprite);
                UiComic.Apply(splash, art.uiComic, art.uiComicSprite, art.uiShadowSprite);
            }

            _islands = new GameObject("Islands").AddComponent<IslandField>();
            _islands.transform.SetParent(transform, false);
            _islands.Init(art, spawner != null ? spawner.hands : null, _plane);

            if (menu != null)
            {
                menu.LevelChosen += StartLevel;
                menu.InfiniteChosen += StartInfinite;
                menu.RetryChosen += Retry;
                menu.NextChosen += Next;
                menu.MenuChosen += ShowMenu;
                menu.RespawnChosen += BuyRespawn;
                menu.StartChosen += ConfirmStart;
                menu.BackChosen += ShowMenu;
                menu.PlaneChanged += ApplyPlaneSelection;
            }
            if (wristMenu != null) wristMenu.Pressed += Pause;
            if (pausePanel != null)
            {
                pausePanel.ResumeChosen += Resume;
                pausePanel.RestartChosen += Retry;
                pausePanel.MainMenuChosen += ShowMenu;
            }

            _seed = (int)(System.DateTime.Now.Ticks & 0x7ffffff);
        }

        void OnDestroy()
        {
            PlaneTurret.AnyEnemyDowned -= OnTurretKill;
            BombBalloon.AnyContained -= OnBombContained;
            BossMissile.AnyDefused -= OnMissileDefused;
            PowerBalloon.AnyLifeCollected -= OnLifeCollected;
            if (IsPaused) Time.timeScale = 1f;
            IsPaused = false;
        }

        void Start()
        {
            Switch(SplashRoutine());
        }

        /// <summary>The opening: the logo fades in, then Start, and after it the start menu fades in.</summary>
        IEnumerator SplashRoutine()
        {
            CurrentFlow = Flow.Menu;
            InInfinite = false;
            SetGameplayInput(false);
            Clean();
            _plane.Hide();
            HideHud();
            if (menu != null) menu.Close();
            if (splash == null)
            {
                yield return MenuRoutine(false);
                yield break;
            }
            yield return WaitForHead();
            _titlePlayed = true;
            Sfx.Play(SoundKind.Title);
            bool started = false;
            System.Action onStart = () => started = true;
            splash.StartChosen += onStart;
            splash.Show();
            while (!started) yield return null;
            splash.StartChosen -= onStart;
            yield return MenuRoutine(true);
        }

        IEnumerator WaitForHead()
        {
            float waited = 0f;
            while (!HeadReady() && waited < 3f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            // Give tracking a moment to settle before placing the panel.
            yield return null;
        }

        void Update()
        {
            if (hud != null && (CurrentFlow == Flow.Flying || CurrentFlow == Flow.PreFlight))
                hud.SetProgress(_plane.Progress);
        }

        bool HeadReady()
        {
            return head != null && head.position.y > 0.3f;
        }

        /// <summary>Puts the plane chosen in the Hangar, with its paint, on the mini plane.</summary>
        public void ApplyPlaneSelection()
        {
            if (_plane == null || catalog == null || catalog.Count == 0) return;
            int index = catalog.IndexOf(Progression.SelectedPlane);
            if (!catalog.IsOwned(index)) index = 0;
            var def = catalog.Get(index);
            int palette = Progression.PaletteFor(def.id);
            var paint = def.palettes != null && palette < def.palettes.Length ? def.palettes[palette] : null;
            _plane.SetAppearance(def, paint);
        }

        // ---------------------------------------------------------------- flow control

        public void ShowMenu()
        {
            OfferingRespawn = false;
            Switch(MenuRoutine(false));
        }

        public void StartLevel(int zeroBasedLevel)
        {
            if (zeroBasedLevel < 0 || zeroBasedLevel >= levels.Count) return;
            if (!Progression.IsLevelUnlocked(zeroBasedLevel)) return;
            Switch(LevelRoutine(zeroBasedLevel));
        }

        public void StartInfinite()
        {
            if (!Progression.InfiniteUnlocked) return;
            Switch(InfiniteRoutine());
        }

        /// <summary>
        /// The Respawn button on Infinite's game-over page: spends the coins and carries on with the run,
        /// replaying the wave the plane went down in, with the score kept.
        /// </summary>
        public void BuyRespawn()
        {
            if (!OfferingRespawn || _respawnBought) return;
            if (!Progression.SpendCoins(RespawnCost)) return;
            RespawnsBought++;
            _respawnBought = true;
            Sfx.Play(SoundKind.Purchase);
        }

        /// <summary>For automated tests: sets the lives left in the current level or run.</summary>
        public void DebugSetLives(int lives)
        {
            Lives = Mathf.Clamp(lives, 0, Mathf.Max(lives, LivesTotal));
            if (hud != null) hud.SetLives(Lives, LivesTotal);
        }

        /// <summary>The Start button on a tutorial level's intro page.</summary>
        public void ConfirmStart()
        {
            if (CurrentFlow == Flow.Intro) _startPressed = true;
        }

        void Retry()
        {
            if (InInfinite) StartInfinite();
            else StartLevel(LevelIndex);
        }

        void Next()
        {
            if (InInfinite || LevelIndex + 1 >= levels.Count) ShowMenu();
            else StartLevel(LevelIndex + 1);
        }

        void Switch(IEnumerator routine)
        {
            EndPause();
            OfferingRespawn = false;
            if (_flow != null) StopCoroutine(_flow);
            _flow = StartCoroutine(routine);
        }

        /// <summary>Stops the game clock and opens the pause menu. Only works during a flight.</summary>
        public void Pause()
        {
            if (IsPaused || (CurrentFlow != Flow.Flying && CurrentFlow != Flow.PreFlight)) return;
            IsPaused = true;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Sfx.Play(SoundKind.Pause);
            SetGameplayInput(false);
            if (hud != null) hud.gameObject.SetActive(false);
            if (pausePanel != null) pausePanel.Open();
        }

        /// <summary>Closes the pause menu and carries on with the flight.</summary>
        public void Resume()
        {
            if (!IsPaused) return;
            EndPause();
            Sfx.Play(SoundKind.Unpause);
            SetGameplayInput(true);
        }

        void EndPause()
        {
            if (!IsPaused) return;
            IsPaused = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (hud != null) hud.gameObject.SetActive(true);
            if (pausePanel != null) pausePanel.Close();
        }

        void SetGameplayInput(bool enabled)
        {
            if (rings != null)
                foreach (var r in rings)
                    if (r != null) r.GameplayEnabled = enabled;
        }

        void Clean()
        {
            if (spawner != null) spawner.End();
            if (revealer != null) revealer.End();
            if (_islands != null) _islands.Clear();
            _plane.RemoveTurret();
        }

        void HideHud()
        {
            if (hud == null) return;
            hud.SetVisible(false);
            hud.SetCaption("");
        }

        IEnumerator MenuRoutine(bool fadeIn)
        {
            if (splash != null && !fadeIn) splash.Close();
            CurrentFlow = Flow.Menu;
            InInfinite = false;
            SetGameplayInput(false);
            Clean();
            _plane.Hide();
            HideHud();

            float waited = 0f;
            while (!HeadReady() && waited < 3f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            // Give tracking a moment to settle before placing the panel.
            yield return null;
            if (menu != null)
            {
                menu.ShowStart(levels);
                if (fadeIn) menu.FadeIn(menuFadeInSeconds);
            }
            if (!_titlePlayed)
            {
                _titlePlayed = true;
                Sfx.Play(SoundKind.Title);
            }
        }

        IEnumerator LevelRoutine(int index)
        {
            LevelIndex = index;
            InInfinite = false;
            Score = 0;
            CoinsThisRun = 0;
            _cfg = levels[index];
            LivesTotal = Mathf.Max(1, _cfg.lives);
            Lives = LivesTotal;

            yield return Flight(_cfg, "LV " + _cfg.levelNumber, _cfg.levelName + "\n" + _cfg.introCaption, false, true);

            bool finished = _plane.CurrentState == MiniPlane.State.Finished;
            bool enoughRings = RingsThisFlight >= _cfg.requiredRingPasses;
            LastFlightPassed = finished && enoughRings;

            LastFirstClearCoins = 0;
            if (LastFlightPassed)
            {
                if (!Progression.HasPassed(index))
                {
                    LastFirstClearCoins = tutorialFirstClearCoins;
                    GrantCoins(tutorialFirstClearCoins);
                }
                Progression.MarkPassed(index);
            }

            yield return new WaitForSeconds(resultDelaySeconds);
            Clean();
            HideHud();
            SetGameplayInput(false);
            CurrentFlow = Flow.Result;

            string detail;
            bool hasNext = index + 1 < levels.Count;
            string nextLabel = hasNext ? "NEXT" : "MENU";
            if (LastFlightPassed)
            {
                detail = index + 1 == Progression.LevelsForInfinite
                    ? "Infinite mode is unlocked, and so is the Zeppelin level."
                    : !hasNext
                        ? "You beat the zeppelin!"
                        : "Level " + (index + 2) + " is unlocked.";
                if (LastFirstClearCoins > 0) detail += "\nFirst clear: +" + LastFirstClearCoins + " coins.";
            }
            else if (!finished)
            {
                detail = "Out of lives. Your plane " + _plane.LastCrashReason + ".";
            }
            else
            {
                detail = "The plane made it, but needed " + _cfg.requiredRingPasses + " ring passes. You got " + RingsThisFlight + ".";
            }
            if (menu != null)
                menu.ShowResult(LastFlightPassed ? "LEVEL PASSED" : "TRY AGAIN", detail, "SCORE  " + Score, LastFlightPassed, LastFlightPassed, nextLabel, CoinsThisRun);
            Sfx.Play(LastFlightPassed ? SoundKind.LevelComplete : SoundKind.LevelFailed);
            if (LastFirstClearCoins > 0) Sfx.Play(SoundKind.Reward, 0.7f);
        }

        IEnumerator InfiniteRoutine()
        {
            InInfinite = true;
            Score = 0;
            Wave = 0;
            CoinsThisRun = 0;
            RespawnsBought = 0;
            LivesTotal = Mathf.Max(1, infinite.lives);
            Lives = LivesTotal;
            if (_waveConfig != null) Destroy(_waveConfig);
            _waveConfig = infinite.baseLevel != null
                ? Instantiate(infinite.baseLevel)
                : ScriptableObject.CreateInstance<LevelConfig>();
            _waveConfig.hideFlags = HideFlags.DontSave;
            _cfg = _waveConfig;

            while (true)
            {
            bool alive = true;
            while (alive)
            {
                Wave++;
                infinite.Apply(_waveConfig, Wave);
                if (Wave > 1) Sfx.Play(SoundKind.NewWave);
                string waveIntro = _waveConfig.bossLevel > 0 ? "BOSS WAVE " + Wave + "\nThe zeppelin can't be destroyed. Survive it!" : "WAVE " + Wave;
                yield return Flight(_waveConfig, "W " + Wave, waveIntro, Wave > 1, false);
                alive = _plane.CurrentState == MiniPlane.State.Finished;
                if (alive) AddCoins(infinite.coinsPerWave);
                if (spawner != null) spawner.End();
            }

            LastFlightPassed = false;
            bool best = Progression.ReportScore(Score);
            Sfx.Play(SoundKind.LevelFailed);
            if (best) Sfx.Play(SoundKind.Reward, 0.7f);
            yield return new WaitForSeconds(resultDelaySeconds);
            Clean();
            HideHud();
            SetGameplayInput(false);
            CurrentFlow = Flow.Result;
            if (menu != null)
            {
                int cost = RespawnCost;
                bool affordable = Progression.Coins >= cost;
                menu.ShowResult(best ? "NEW BEST" : "GAME OVER",
                    "You reached wave " + Wave + ". Your plane " + _plane.LastCrashReason + ".\n" +
                    (affordable ? "Respawn to carry on from wave " + Wave + "." : "Respawn costs " + cost + " coins. You have " + Progression.Coins + "."),
                    "SCORE  " + Score, best, false, "MENU", CoinsThisRun);
                menu.OfferRespawn(cost, affordable);
            }

            // Wait on the page: Retry and Menu leave this routine; Respawn carries on with the run.
            _respawnBought = false;
            OfferingRespawn = true;
            while (!_respawnBought) yield return null;
            OfferingRespawn = false;
            if (menu != null) menu.Close();
            if (hud != null) hud.SetCoins(Progression.Coins);
            LivesTotal = Mathf.Max(1, infinite.lives);
            Lives = Mathf.Clamp(infinite.respawnLives, 1, LivesTotal);
            // Replay the wave the plane went down in.
            Wave--;
            }
        }

        /// <summary>
        /// One flight along one path: the intro page or a countdown, take-off, then flying until the path
        /// ends or the lives run out. Each crash with lives left brings the plane back where it fell.
        /// </summary>
        IEnumerator Flight(LevelConfig cfg, string levelLabel, string intro, bool keepShield, bool needsStart)
        {
            CurrentFlow = needsStart ? Flow.Intro : Flow.PreFlight;
            if (menu != null && !needsStart) menu.Close();
            Clean();
            SetGameplayInput(true);
            RingsThisFlight = 0;
            _cfg = cfg;
            _seed++;

            Vector3 headPos = HeadReady() ? head.position : new Vector3(0f, 1.1f, 0f);
            Vector3 headFwd = head != null ? head.forward : Vector3.forward;

            var path = FlightPathGenerator.Build(cfg, headPos, headFwd, _seed);
            _plane.PrepareAtStart(path, cfg, keepShield);
            if (revealer != null) revealer.Begin(path, cfg, _plane);
            if (_islands != null) _islands.Build(cfg, path, headPos, headFwd, _seed);
            _perkTurretPending = false;

            if (hud != null)
            {
                // During a tutorial intro the HUD stays hidden behind the Start page; it appears on Start.
                hud.SetVisible(!needsStart);
                hud.SetLevelLabel(levelLabel);
                hud.SetScore(Score);
                hud.SetProgress(0f);
                hud.SetCoins(Progression.Coins);
                hud.SetLives(Lives, LivesTotal);
                hud.SetCaption("");
            }

            float countdown;
            if (needsStart)
            {
                _startPressed = false;
                if (menu != null) menu.ShowIntro(cfg.levelNumber, cfg.levelName, cfg.introCaption, LivesTotal);
                while (!_startPressed) yield return null;
                if (menu != null) menu.Close();
                if (hud != null) hud.SetVisible(true);
                CurrentFlow = Flow.PreFlight;
                countdown = Mathf.Max(0.5f, startCountdownSeconds);
                intro = "";
            }
            else
            {
                countdown = Mathf.Max(0.5f, cfg.preFlightSeconds);
            }

            float t = countdown;
            while (t > 0f)
            {
                if (hud != null)
                {
                    string count = t <= 3f ? "Take-off in " + Mathf.CeilToInt(t) : "";
                    hud.SetCaption(string.IsNullOrEmpty(intro) ? count : intro + "\n" + count);
                }
                t -= Time.deltaTime;
                yield return null;
            }
            if (hud != null) hud.SetCaption("");

            _plane.TakeOff();
            if (revealer != null) revealer.OnTakeOff();
            CurrentFlow = Flow.Flying;
            var def = _plane.Definition;
            _perkTurretPending = def != null && def.Has(PlanePerk.WaveTurret);
            if (def != null && def.Has(PlanePerk.WaveBoost))
            {
                _plane.Boost(def.perkBoostSeconds, def.perkBoostMultiplier);
                if (hud != null) hud.SetCaption("BOOST!", 1.2f);
            }
            if (spawner != null) spawner.Begin(cfg, _plane, headFwd, _seed);

            while (true)
            {
                while (_plane.IsFlying) yield return null;
                if (_plane.CurrentState != MiniPlane.State.Crashed) break;

                Lives = Mathf.Max(0, Lives - 1);
                if (hud != null) hud.SetLives(Lives, LivesTotal);
                if (Lives <= 0) break;

                // Let the crash play out, clear any bullets in the air, then carry on from where it fell.
                float waited = 0f;
                while (!_plane.CrashSettled && waited < 3f)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
                Projectile.DestroyAll();
                if (hud != null) hud.SetCaption(Lives == 1 ? "LAST LIFE" : Lives + " LIVES LEFT", 1.5f);
                _plane.Respawn();
            }

            if (spawner != null) spawner.End();
            if (revealer != null) revealer.End();
            _plane.RemoveTurret();
            if (hud != null && _plane.CurrentState == MiniPlane.State.Finished) hud.SetProgress(1f);
        }

        // ---------------------------------------------------------------- gameplay events

        void DamagePlane(string reason)
        {
            if (CurrentFlow == Flow.Flying) _plane.Damage(reason);
        }

        void OnSingleRingScored(HandRing ring, Vector3 point)
        {
            RegisterRing(singleRingPoints, singleRingCoins, point);
        }

        void OnMergedRingScored(RingMerger m, Vector3 point)
        {
            RegisterRing(mergedRingPoints, mergedRingCoins, point);
        }

        void ClipRing()
        {
            if (CurrentFlow == Flow.Flying) Sfx.Play(SoundKind.RingClip, _plane.Position);
            DamagePlane("clipped the ring");
        }

        void RegisterRing(int points, int coins, Vector3 point)
        {
            if (CurrentFlow != Flow.Flying) return;
            RingsThisFlight++;
            AddScore(points, point);
            AddCoins(coins);
            ComicVfx.Play(ComicKind.RingPass, point);
            Sfx.Play(points == mergedRingPoints && points != singleRingPoints ? SoundKind.RingBigO : SoundKind.RingPass, point);
            if (_cfg != null) _plane.AddFuel(_cfg.FuelPerRing);
            if (revealer != null) revealer.OnRingPass();
        }

        void OnEnemyFlicked(FlickDetector detector, EnemyPlane enemy)
        {
            if (CurrentFlow != Flow.Flying) return;
            AddScore(enemy is BomberPlane ? bomberFlickPoints : flickPoints, enemy.Position);
            AddCoins(enemyCoins);
        }

        void OnTurretKill(PlaneTurret turret, EnemyPlane enemy)
        {
            if (CurrentFlow != Flow.Flying) return;
            AddScore(turretKillPoints, enemy.Position);
            AddCoins(enemyCoins);
        }

        void OnBombContained(BombBalloon bomb)
        {
            if (CurrentFlow != Flow.Flying) return;
            AddCoins(bombCoverCoins);
        }

        /// <summary>A heart balloon: one life back, up to the level's full count. At full lives it does nothing.</summary>
        void OnLifeCollected(PowerBalloon balloon)
        {
            if (CurrentFlow != Flow.Flying) return;
            if (Lives >= LivesTotal)
            {
                if (hud != null) hud.SetCaption("LIVES FULL", 1f);
                return;
            }
            Lives++;
            if (hud != null)
            {
                hud.SetLives(Lives, LivesTotal);
                hud.SetCaption("+1 LIFE", 1.2f);
            }
            Sfx.Play(SoundKind.LifeUp);
        }

        void OnMissileDefused(BossMissile missile)
        {
            if (CurrentFlow != Flow.Flying) return;
            AddScore(missileDefusePoints, missile.Position);
            AddCoins(missileCoins);
        }

        /// <summary>The Bluebird fits its own turret when the first enemy plane of the wave appears.</summary>
        void OnEnemySpawned(EnemyPlane enemy)
        {
            if (!_perkTurretPending || CurrentFlow != Flow.Flying || !_plane.IsFlying) return;
            _perkTurretPending = false;
            _plane.EquipTurret(_cfg);
            if (hud != null) hud.SetCaption("TURRET READY", 1.2f);
        }

        void OnBossAttack(Zeppelin zeppelin, Zeppelin.Attack attack)
        {
            if (hud == null || CurrentFlow != Flow.Flying) return;
            switch (attack)
            {
                case Zeppelin.Attack.Barrage: hud.SetCaption("BULLET TRAILS! Block them with your hands", 2f); break;
                case Zeppelin.Attack.Fighters: hud.SetCaption("FIGHTERS LAUNCHING! Flick them away", 2f); break;
                case Zeppelin.Attack.Missiles: hud.SetCaption("MISSILES! Hold one in your palm until it drops", 2.5f); break;
            }
        }

        void AddScore(int points, Vector3 at)
        {
            Score += points;
            if (hud != null) hud.SetScore(Score);
            if (points == 1) ComicVfx.Play(ComicKind.Plus1, at + Vector3.up * 0.05f);
            else if (points > 1) ComicVfx.Play(ComicKind.Plus2, at + Vector3.up * 0.05f);
        }

        /// <summary>Coins for something done in play. Only Infinite pays them, so tutorials can't be farmed.</summary>
        void AddCoins(int coins)
        {
            if (!InInfinite) return;
            GrantCoins(coins);
        }

        void GrantCoins(int coins)
        {
            if (coins <= 0) return;
            Progression.AddCoins(coins);
            CoinsThisRun += coins;
            if (hud != null) hud.SetCoins(Progression.Coins);
        }

        void OnPlaneHit(MiniPlane plane, string reason)
        {
            ComicVfx.Play(ComicKind.Spark, plane.Position, 0.07f);
            Sfx.Play(SoundKind.PlaneHit, plane.Position);
            if (revealer != null) revealer.BreakStreak();
        }

        void OnShieldLost(MiniPlane plane)
        {
            ComicVfx.Play(ComicKind.ShieldPop, plane.Position);
            Sfx.Play(SoundKind.ShieldBreak, plane.Position);
        }

        void OnPlaneCrashed(MiniPlane plane, string reason)
        {
            ComicVfx.Play(ComicKind.Crash, plane.Position);
            Sfx.Play(SoundKind.PlaneCrash, plane.Position);
            if (revealer != null) revealer.BreakStreak();
        }
    }
}
