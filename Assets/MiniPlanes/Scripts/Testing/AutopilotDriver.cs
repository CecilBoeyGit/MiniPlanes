#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEditor;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// Plays the real scene with simulated hands at a fixed timestep and checks each mechanic the design
    /// asks for. Every result is logged as [AUTOPILOT] PASS or FAIL. Editor only.
    /// </summary>
    public class AutopilotDriver : MonoBehaviour
    {
        public const string SessionFlag = "MiniPlanes.Autopilot";

        const float Dt = 1f / 72f;
        const float RealTimeLimitSeconds = 900f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!SessionState.GetBool(SessionFlag, false)) return;
            SessionState.SetBool(SessionFlag, false);
            new GameObject("Autopilot").AddComponent<AutopilotDriver>();
        }

        GameManager _gm;
        FakeHandSource _left, _right;
        Transform _head;
        int _pass, _fail;
        float _startReal;

        void Start()
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            _startReal = Time.realtimeSinceStartup;
            Time.captureDeltaTime = Dt;
            DontDestroyOnLoad(gameObject);
            StartCoroutine(Main());
        }

        void Update()
        {
            if (Time.realtimeSinceStartup - _startReal > RealTimeLimitSeconds)
            {
                Check("finished inside the real-time limit", false);
                Finish();
            }
        }

        void Check(string what, bool ok, string detail = "")
        {
            if (ok) _pass++;
            else _fail++;
            Debug.Log("[AUTOPILOT] " + (ok ? "PASS " : "FAIL ") + what + (string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")"));
        }

        void Note(string text)
        {
            Debug.Log("[AUTOPILOT] --- " + text);
        }

        void Finish()
        {
            Debug.Log("[AUTOPILOT] RESULT pass=" + _pass + " fail=" + _fail);
            Progression.ResetAll(_gm != null ? _gm.catalog : null);
            Time.captureDeltaTime = 0f;
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        IEnumerator Main()
        {
            yield return null;
            Rig();
            yield return null;

            string shotsDir = ShotsDirFromArgs();
            if (shotsDir != null)
            {
                yield return TryStep(Shots(shotsDir));
                Finish();
                yield break;
            }

            yield return TryStep(Splash());
            CheckPaths();
            CheckDifficultyCurve();

            Progression.ResetAll(_gm != null ? _gm.catalog : null);
            Progression.UnlockAllOverride = false;
            yield return TryStep(MenuAndUnlocks());
            yield return TryStep(Level1());
            yield return TryStep(Level2());
            yield return TryStep(Level3());
            yield return TryStep(MergedRing());
            yield return TryStep(PauseMenu());
            yield return TryStep(LivesAndCoins());
            yield return TryStep(TutorialReplayPaysNothing());
            yield return TryStep(Hangar());
            yield return TryStep(IntroIcons());
            yield return TryStep(PlanePerks());
            yield return TryStep(Level4());
            yield return TryStep(Level5());
            yield return TryStep(BoosterTubes());
            yield return TryStep(HeartBalloon());
            yield return TryStep(Islands());
            yield return TryStep(JammingTowers());
            yield return TryStep(Level6());
            yield return TryStep(Turret());
            yield return TryStep(InfinitePowerups());
            yield return TryStep(InfiniteMode());
            yield return TryStep(InfiniteRespawn());
            yield return TryStep(ZeppelinBoss());
            yield return TryStep(Sounds());
            Finish();
        }

        IEnumerator TryStep(IEnumerator step)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!step.MoveNext()) yield break;
                    current = step.Current;
                }
                catch (Exception e)
                {
                    Check("step threw no exception", false, e.GetType().Name + ": " + e.Message + " @ " + e.StackTrace);
                    yield break;
                }
                yield return current;
            }
        }

        // ---------------------------------------------------------------- rendered previews

        static string ShotsDirFromArgs()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-mpShotsDir") return args[i + 1];
            return null;
        }

        Camera _shotCamera;
        RenderTexture _shotTarget;
        string _shotsDir;
        int _shotIndex;

        void Shoot(string name)
        {
            RenderTexture.active = _shotTarget;
            _shotCamera.Render();
            var tex = new Texture2D(_shotTarget.width, _shotTarget.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, _shotTarget.width, _shotTarget.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            _shotIndex++;
            string file = System.IO.Path.Combine(_shotsDir, _shotIndex.ToString("00") + "_" + name + ".png");
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[AUTOPILOT] shot " + file);
        }

        Vector3 InFront(float forward, float right = 0f, float up = 0f)
        {
            return _head.position + Vector3.forward * forward + Vector3.right * right + Vector3.up * up;
        }

        IEnumerator Shots(string dir)
        {
            _shotsDir = dir;
            System.IO.Directory.CreateDirectory(dir);
            DrawPathSheet(dir);
            Progression.ResetAll(_gm != null ? _gm.catalog : null);
            Progression.MarkPassed(1);

            _shotCamera = _head.gameObject.AddComponent<Camera>();
            _shotCamera.clearFlags = CameraClearFlags.SolidColor;
            _shotCamera.backgroundColor = new Color(0.72f, 0.74f, 0.74f);   // stands in for the passthrough room
            _shotCamera.nearClipPlane = 0.03f;
            _shotCamera.fieldOfView = 70f;
            _shotTarget = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            _shotTarget.Create();
            _shotCamera.targetTexture = _shotTarget;

            // The opening splash: logo, then the Start button.
            if (_gm.splash != null)
            {
                _gm.menu.Close();
                _gm.splash.Show();
                yield return WaitSeconds(0.6f);
                Shoot("splash_logo_fading_in");
                yield return WaitFor(() => _gm.splash.ButtonShown, 6f);
                Shoot("splash_logo_and_start");
                _gm.splash.Close();
            }

            // The start panel.
            _gm.ShowMenu();
            yield return WaitFor(() => _gm.menu.IsStartPage, 5f);
            yield return null;
            Shoot("start_panel");

            // A line of every model, noses pointing right, to check orientation and scale.
            var sheet = new GameObject("ModelSheet").transform;
            GameObject[] prefabs = { _gm.art.playerPlane, _gm.art.enemyFighter, _gm.art.enemyBomber, _gm.art.balloonShield, _gm.art.balloonBomb };
            float[] sizes = { 0.042f, 0.038f, 0.055f, 0.07f, 0.07f };
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] == null) continue;
                var holder = new GameObject("Holder" + i).transform;
                holder.SetParent(sheet, false);
                holder.position = InFront(0.24f, (i - 2.5f) * 0.07f, i < 3 ? -0.015f : 0.02f);
                holder.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Euler(0f, -25f, 0f);
                Material[] materials = { _gm.art.PlayerMaterial, _gm.art.FighterMaterial, _gm.art.BomberMaterial, _gm.art.planeMatte, _gm.art.planeMatte };
                ModelUtil.Attach(holder, prefabs[i], materials[i], sizes[i],
                    i < 3 ? ModelUtil.FitAxis.Width : ModelUtil.FitAxis.Height);
            }
            _gm.menu.Close();
            yield return null;
            Shoot("model_sheet");
            Destroy(sheet.gameObject);
            yield return null;

            // Level 1 in flight: HUD, faint line.
            _gm.StartLevel(0);
            yield return null;
            yield return WaitSeconds(1f);
            Shoot("level_intro_start_button");
            _gm.ConfirmStart();
            yield return WaitSeconds(0.6f);
            Shoot("countdown_caption");
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Flying, 8f);
            yield return WaitSeconds(6f);
            Shoot("flight_hud_and_line");

            // The bubble ring: radial fill stage, then full, on a C in front of the camera.
            Vector3 ringAt = InFront(0.22f, -0.03f);
            _right.PoseC(ringAt, Vector3.forward, Vector3.right, 0.034f, 1.35f);
            yield return WaitSeconds(_gm.rings[1].fillSeconds * 0.55f);
            Shoot("ring_radial_fill");
            yield return WaitSeconds(_gm.rings[1].fillSeconds);
            Shoot("ring_full");

            // Two rings squeezing together: peanut, then the big O.
            _left.PoseC(InFront(0.26f, -0.065f), Vector3.forward, Vector3.right);
            _right.PoseC(InFront(0.26f, 0.065f), Vector3.forward, -Vector3.right);
            yield return WaitSeconds(_gm.rings[1].fillSeconds + 0.02f);
            Shoot("ring_merge_peanut");
            yield return WaitSeconds(_gm.merger.mergeSeconds + 0.1f);
            Shoot("ring_big_o");
            ParkHands();
            yield return null;

            // Late in the flight, only the short stretch of line still ahead should remain.
            yield return WaitFor(() => _gm.Plane.Progress > 0.8f || !_gm.Plane.IsFlying, 25f);
            Shoot("line_late_in_flight");
            yield return AbortToMenu();
            _gm.menu.Close();

            // The plane close up, fully drawn, then with a hand-material block in front of half of it.
            var cfg = _gm.levels[0];
            var planePath = FlightPathGenerator.Build(cfg, _head.position, Vector3.forward, 3);
            _gm.Plane.PrepareAtStart(planePath, cfg, false);
            yield return null;
            _gm.Plane.transform.position = InFront(0.2f);
            _gm.Plane.transform.rotation = Quaternion.LookRotation(Vector3.right + Vector3.forward * 0.6f, Vector3.up);
            yield return null;
            Shoot("plane_close");
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(blocker.GetComponent<Collider>());
            blocker.transform.position = InFront(0.14f, 0.014f);
            blocker.transform.localScale = new Vector3(0.03f, 0.12f, 0.01f);
            blocker.GetComponent<MeshRenderer>().sharedMaterial = _gm.art.handRock;
            yield return null;
            Shoot("plane_behind_hand_silhouette");
            Destroy(blocker);

            // Shield and fuel gauge on the plane.
            _gm.Plane.GiveShield();
            yield return null;
            Shoot("plane_shield");
            _gm.Plane.EquipTurret(cfg);
            yield return null;
            Shoot("plane_with_turret");
            _gm.Plane.Hide();

            // The Supersonic jet with its twin turrets on the domes.
            if (_gm.catalog != null && _gm.catalog.Count >= 4)
            {
                string flying = Progression.SelectedPlane;
                Progression.GrantPlane(_gm.catalog.Get(3).id);
                Progression.SelectedPlane = _gm.catalog.Get(3).id;
                _gm.ApplyPlaneSelection();
                _gm.Plane.PrepareAtStart(planePath, cfg, false);
                yield return null;
                _gm.Plane.transform.position = InFront(0.2f);
                _gm.Plane.transform.rotation = Quaternion.LookRotation(Vector3.right + Vector3.forward * 0.6f, Vector3.up);
                _gm.Plane.EquipTurret(cfg);
                yield return null;
                Shoot("jet_twin_turrets");
                _gm.Plane.transform.rotation = Quaternion.LookRotation(-Vector3.forward, Vector3.up) * Quaternion.Euler(35f, 0f, 0f);
                yield return null;
                Shoot("jet_twin_turrets_front");
                _gm.Plane.Hide();
                Progression.SelectedPlane = flying;
                _gm.ApplyPlaneSelection();
            }

            // Comic effects at their real sizes.
            ComicVfx.Play(ComicKind.RingPass, InFront(0.42f, -0.2f));
            ComicVfx.Play(ComicKind.Plus2, InFront(0.42f, -0.2f, 0.06f));
            ComicVfx.Play(ComicKind.Crash, InFront(0.42f, 0f));
            ComicVfx.Play(ComicKind.Flick, InFront(0.42f, 0.2f));
            ComicVfx.Play(ComicKind.Plus1, InFront(0.42f, 0.2f, 0.06f));
            yield return WaitSeconds(0.15f);
            Shoot("comic_vfx_peak");
            yield return WaitSeconds(1f);

            // Balloons, crosshair and armed bomb, in level 5 and 6 settings.
            Progression.UnlockAllOverride = true;
            yield return StartAndWaitForFlight(5);
            var shield = _gm.spawner.SpawnBalloon(BalloonKind.Shield);
            var gold = _gm.spawner.SpawnBalloon(BalloonKind.Turret);
            gold.transform.position = InFront(0.3f, -0.18f);
            gold.DragTo(InFront(0.3f, -0.18f));
            shield.DragTo(InFront(0.3f, -0.05f, -0.06f));
            yield return null;
            shield.transform.position = InFront(0.3f, -0.1f);
            var bomb = MakeBomb(InFront(0.3f, 0.08f));
            var bomb2 = MakeBomb(InFront(0.3f, 0.16f));
            bomb2.Arm();
            float until = Time.time + 0.5f;
            while (Time.time < until) yield return null;
            Shoot("balloons_crosshair_bomb");
            yield return AbortToMenu();

            // The result panel.
            _gm.menu.ShowResult("LEVEL PASSED", "Level 3 is unlocked.", "SCORE  14", true, true, "NEXT");
            yield return null;
            Shoot("result_panel");
            Progression.AddCoins(1500);
            _gm.menu.ShowStart(_gm.levels);
            yield return null;
            Shoot("start_panel_with_hangar");
            _gm.menu.ShowHangar();
            _gm.menu.Press("plane1");
            yield return WaitSeconds(0.3f);
            Shoot("hangar_locked_plane");
            _gm.menu.Press("plane0");
            yield return WaitSeconds(0.3f);
            Shoot("hangar_owned_plane");
            for (int i = 1; i < 4; i++)
            {
                if (_gm.catalog == null || _gm.catalog.Count <= i) break;
                Progression.GrantPlane(_gm.catalog.Get(i).id);
                _gm.menu.Press("plane" + i);
                yield return WaitSeconds(0.3f);
                Shoot("hangar_plane" + (i + 1));
            }
            _gm.menu.Close();
            _gm.menu.Close();

            // Glass panels in front of a few 3D things, to see them bend.
            var behind = new GameObject("GlassBackdrop").transform;
            for (int i = 0; i < 5; i++)
            {
                var b = new GameObject("Behind" + i).transform;
                b.SetParent(behind, false);
                b.position = InFront(1.15f, -0.45f + 0.22f * i, -0.05f + 0.06f * (i % 2));
                ModelUtil.Attach(b, i % 2 == 0 ? _gm.art.balloonShield : _gm.art.balloonBomb, _gm.art.planeMatte, 0.32f, ModelUtil.FitAxis.Height);
            }
            _gm.menu.ShowStart(_gm.levels);
            yield return WaitSeconds(0.3f);
            Shoot("glass_menu_over_scene");
            Destroy(behind.gameObject);
            _gm.menu.Close();

            // Infinite's game-over page with its Respawn offer.
            _gm.menu.ShowResult("GAME OVER", "You reached wave 6. Your plane hit a floating island.\nRespawn to carry on from wave 6.", "SCORE  42", false, false, "MENU", 18);
            _gm.menu.OfferRespawn(100, true);
            yield return WaitSeconds(0.3f);
            Shoot("game_over_respawn");
            _gm.menu.OfferRespawn(150, false);
            yield return WaitSeconds(0.3f);
            Shoot("game_over_respawn_unaffordable");
            _gm.menu.Close();

            // A tutorial's Start page with icons in its text.
            Progression.UnlockAllOverride = true;
            _gm.menu.ShowIntro(_gm.levels[6].levelNumber, _gm.levels[6].levelName, _gm.levels[6].introCaption, 3);
            yield return WaitSeconds(0.3f);
            Shoot("intro_with_icons");
            _gm.menu.ShowIntro(_gm.levels[5].levelNumber, _gm.levels[5].levelName, _gm.levels[5].introCaption, 3);
            yield return WaitSeconds(0.3f);
            Shoot("intro_with_icons_bombers");
            _gm.menu.Close();

            // Booster tubes: the four shapes.
            var tubes = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("PreviewTube");
                var t = go.AddComponent<BoosterTube>();
                t.Setup((TubeShape)i, null, null, _gm.art, InFront(0.4f, i < 2 ? -0.03f : 0.0f, 0.09f - 0.065f * i),
                    Quaternion.LookRotation(Vector3.right, Vector3.up));
                tubes.Add(go);
            }
            yield return null;
            Shoot("booster_tubes");
            foreach (var go in tubes) Destroy(go);

            // Floating islands, then one shattered.
            var islandCfg = Instantiate(_gm.levels[5]);
            islandCfg.islandCount = 6;
            islandCfg.jammingTowers = 3;
            _gm.Islands.Build(islandCfg, null, _head.position, Vector3.forward, 5);
            yield return null;
            Shoot("floating_islands");
            // A jamming tower up close: bring one tower's island in front of the head, then put it back.
            var towered = JammingTower.Active.Count > 0 ? JammingTower.Active[0].Island : null;
            if (towered != null)
            {
                Vector3 was = towered.Position;
                towered.MoveTo(InFront(0.42f, 0f, -0.12f));
                yield return WaitSeconds(0.3f);
                Shoot("jamming_tower");
                towered.MoveTo(was);
                yield return null;
            }
            FloatingIsland facing = null;
            float bestDot = -2f;
            foreach (var isl in FloatingIsland.Active)
            {
                float dot = Vector3.Dot((isl.Position - _head.position).normalized, Vector3.forward);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    facing = isl;
                }
            }
            if (facing != null) facing.Shatter(Vector3.zero);
            yield return WaitSeconds(0.12f);
            Shoot("island_smoke");
            yield return WaitSeconds(0.6f);
            Shoot("island_shards");
            _gm.Islands.Clear();
            Destroy(islandCfg);

            // The zeppelin arriving, firing, and a missile close up.
            var bossCfg = Instantiate(_gm.levels[6]);
            var zgo = new GameObject("PreviewZeppelin");
            var zep = zgo.AddComponent<Zeppelin>();
            zep.Setup(bossCfg, _gm.Plane, _head, _gm.spawner.hands, _gm.art, _gm.spawner, _head.position, Vector3.forward, 3);
            yield return WaitSeconds(zep.arriveSeconds + 0.2f);
            Shoot("zeppelin_boss");
            yield return WaitSeconds(2f);
            Shoot("zeppelin_barrage");
            var shotMissile = new GameObject("PreviewMissile").AddComponent<BossMissile>();
            shotMissile.Setup(bossCfg, null, null, _gm.art, InFront(0.2f), Vector3.right + Vector3.back * 0.3f);
            yield return null;
            Shoot("missile_close");
            Destroy(shotMissile.gameObject);
            Zeppelin.DestroyAll();
            Projectile.DestroyAll();
            Destroy(bossCfg);

            // The wrist button, then the pause panel it opens.
            yield return StartAndWaitForFlight(0);
            _left.PoseOpenPalm(InFront(0.3f, -0.02f, -0.12f), Vector3.up, -Vector3.forward);
            yield return WaitSeconds(_gm.wristMenu.showDelaySeconds + 0.25f);
            Shoot("wrist_menu_button");
            _gm.Pause();
            yield return null;
            yield return null;
            Shoot("pause_panel");
            _gm.Resume();
            _gm.Plane.Crash("test crash");
            yield return WaitFor(() => _gm.Plane.IsFlying, 4f);
            _gm.Plane.EquipTurret(_gm.CurrentConfig);
            _gm.Plane.transform.position = InFront(0.2f);
            _gm.Plane.transform.rotation = Quaternion.LookRotation(Vector3.right + Vector3.forward * 0.6f, Vector3.up);
            yield return null;
            Shoot("hud_lives_and_turret");
            yield return AbortToMenu();
        }

        // ---------------------------------------------------------------- setup

        void Rig()
        {
            _gm = FindAnyObjectByType<GameManager>();
            var headGo = new GameObject("FakeHead");
            headGo.AddComponent<AudioListener>();
            _head = headGo.transform;
            _head.position = new Vector3(0f, 1.2f, 0f);
            _head.rotation = Quaternion.identity;

            _left = new GameObject("FakeLeft").AddComponent<FakeHandSource>();
            _left.side = HandSide.Left;
            _right = new GameObject("FakeRight").AddComponent<FakeHandSource>();
            _right.side = HandSide.Right;
            ParkHands();

            // Move the gameplay components off the Meta rig, then switch the rig off.
            var rings = _gm.rings;
            var sources = new HandSource[] { _left, _right };
            for (int i = 0; i < rings.Length; i++)
            {
                var go = rings[i].gameObject;
                go.transform.SetParent(null, false);
                foreach (var c in go.GetComponents<MonoBehaviour>())
                    if (c is OVRHand || c is OVRSkeleton || c is OVRMesh || c is OVRMeshRenderer || c is MetaHandSource)
                        c.enabled = false;
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr != null) smr.enabled = false;

                go.GetComponent<HandRing>().hand = sources[i];
                go.GetComponent<HandHazard>().hand = sources[i];
                go.GetComponent<FlickDetector>().hand = sources[i];
                go.GetComponent<BalloonGrabber>().hand = sources[i];
            }
            var rig = GameObject.Find("OVRCameraRig");
            if (rig != null) rig.SetActive(false);

            _gm.head = _head;
            _gm.Plane.SetHead(_head);
            ComicVfx.Instance.SetHead(_head);
            if (Sfx.Instance != null) Sfx.Instance.SetHead(_head);
            Billboard.Head = _head;
            _gm.hud.SetHead(_head);
            _gm.menu.SetHead(_head);
            _gm.menu.SetHands(sources);
            _gm.spawner.head = _head;
            _gm.spawner.hands = sources;
            _gm.wristMenu.head = _head;
            _gm.wristMenu.hands = sources;
            _gm.pausePanel.SetHead(_head);
            _gm.pausePanel.SetHands(sources);
            if (_gm.splash != null)
            {
                _gm.splash.SetHead(_head);
                _gm.splash.SetHands(sources);
            }
            _gm.Islands.Init(_gm.art, sources, _gm.Plane);
        }

        void ParkHands()
        {
            _left.Park(new Vector3(-5f, 0f, -5f));
            _right.Park(new Vector3(5f, 0f, -5f));
        }

        // ---------------------------------------------------------------- pure checks

        void CheckPaths()
        {
            Note("Paths stay in the front half and match their planned length");
            float worstAz = 0f;
            float worstRatio = 1f;
            float minHeadDist = float.PositiveInfinity;
            float tightest = float.PositiveInfinity;
            for (int level = 0; level < _gm.levels.Count; level++)
            {
                var cfg = _gm.levels[level];
                for (int seed = 0; seed < 60; seed++)
                {
                    var p = FlightPathGenerator.Build(cfg, _head.position, Vector3.forward, seed * 37 + level);
                    worstAz = Mathf.Max(worstAz, FlightPathGenerator.MaxAzimuthOf(p, _head.position, Vector3.forward));
                    float ratio = p.Length / (cfg.planeSpeed * cfg.pathDurationSeconds);
                    if (Mathf.Abs(ratio - 1f) > Mathf.Abs(worstRatio - 1f)) worstRatio = ratio;
                    foreach (var s in p.Samples) minHeadDist = Mathf.Min(minHeadDist, Vector3.Distance(s, _head.position));
                    float turn = FlightPathGenerator.TightestTurn(p);
                    if (turn < tightest)
                    {
                        tightest = turn;
                        LogTightest(p, level, seed);
                    }
                }
            }
            Check("no path goes more than 90 degrees from straight ahead", worstAz <= 90f, "worst " + worstAz.ToString("F1"));
            Check("path length is within 35% of speed x duration", Mathf.Abs(worstRatio - 1f) < 0.35f, "worst ratio " + worstRatio.ToString("F2"));
            Check("paths keep clear of the player's head", minHeadDist > 0.2f, "closest " + minHeadDist.ToString("F2") + " m");
            Check("paths have no kinks tighter than 3 cm radius", tightest >= FlightPathGenerator.MinTurnRadius, "tightest " + (tightest * 100f).ToString("F1") + " cm");
            CheckPathVariety();

            var l1 = _gm.levels[0];
            var l2 = _gm.levels[1];
            Check("level 1 is slower and shorter than level 2", l1.planeSpeed < l2.planeSpeed && l1.pathDurationSeconds < l2.pathDurationSeconds);
            Check("level 1 and 2 have no enemies", l1.maxFighters == 0 && l2.maxFighters == 0 && l1.maxBombers == 0 && l2.maxBombers == 0);
            Check("level 1 and 2 are low risk", l1.hitsToCrash > 1 && l2.hitsToCrash > 1);
            Check("three rings fill the tank", Mathf.Abs(_gm.levels[2].FuelPerRing * 3f - _gm.levels[2].fuelMax) < 0.01f);
            Check("fighters first appear in level 4", _gm.levels[2].maxFighters == 0 && _gm.levels[3].maxFighters > 0);
            Check("balloons first appear in level 5", !_gm.levels[3].useBalloons && _gm.levels[4].useBalloons);
            Check("bombers first appear in level 6", _gm.levels[4].maxBombers == 0 && _gm.levels[5].maxBombers > 0);
        }

        /// <summary>
        /// Flights of a level must differ from each other, and each must not retrace itself. A figure-8 fails
        /// the second test, because every lap runs back over the same ground.
        /// </summary>
        void CheckPathVariety()
        {
            float leastDifferent = float.PositiveInfinity;
            float mostRetraced = 0f;
            for (int level = 0; level < _gm.levels.Count; level++)
            {
                var cfg = _gm.levels[level];
                var paths = new System.Collections.Generic.List<CatmullRomPath>();
                for (int seed = 0; seed < 8; seed++) paths.Add(FlightPathGenerator.Build(cfg, _head.position, Vector3.forward, 500 + seed * 13 + level));

                for (int i = 0; i < paths.Count; i++)
                {
                    mostRetraced = Mathf.Max(mostRetraced, RetracedShare(paths[i]));
                    for (int j = i + 1; j < paths.Count; j++)
                    {
                        float sum = 0f;
                        for (int k = 0; k <= 60; k++)
                            sum += Vector3.Distance(paths[i].PositionAt(paths[i].Length * k / 60f), paths[j].PositionAt(paths[j].Length * k / 60f));
                        leastDifferent = Mathf.Min(leastDifferent, sum / 61f);
                    }
                }
            }
            Check("two flights of a level are clearly different shapes", leastDifferent > 0.1f, "closest pair averages " + (leastDifferent * 100f).ToString("F0") + " cm apart");
            Check("no flight keeps retracing its own route", mostRetraced < 0.5f, "worst retraced share " + mostRetraced.ToString("F2"));
        }

        /// <summary>Share of a path's second half lying within 3 cm of its first half, heading the same way.</summary>
        static float RetracedShare(CatmullRomPath p)
        {
            const float step = 0.05f;
            int total = 0, retraced = 0;
            for (float d = p.Length * 0.5f; d < p.Length; d += step)
            {
                total++;
                Vector3 at = p.PositionAt(d);
                Vector3 dir = p.TangentAt(d);
                for (float e = 0f; e < p.Length * 0.5f; e += step * 0.5f)
                {
                    if (Vector3.Distance(at, p.PositionAt(e)) < 0.03f && Vector3.Dot(dir, p.TangentAt(e)) > 0.7f)
                    {
                        retraced++;
                        break;
                    }
                }
            }
            return total > 0 ? retraced / (float)total : 0f;
        }

        /// <summary>Front view of six flights, so a person can look at the shapes.</summary>
        void DrawPathSheet(string dir)
        {
            const int cell = 420;
            var tex = new Texture2D(cell * 3, cell * 2, TextureFormat.RGB24, false);
            var pixels = new Color32[tex.width * tex.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(245, 240, 230, 255);
            int[] levels = { 0, 1, 2, 3, 4, 5 };
            for (int n = 0; n < levels.Length; n++)
            {
                int ox = (n % 3) * cell, oy = (n / 3) * cell;
                for (int i = 0; i < cell; i++)
                {
                    pixels[oy * tex.width + ox + i] = new Color32(190, 185, 175, 255);
                    pixels[(oy + i) * tex.width + ox] = new Color32(190, 185, 175, 255);
                }
                var cfg = _gm.levels[levels[n]];
                var p = FlightPathGenerator.Build(cfg, _head.position, Vector3.forward, 900 + n * 7);
                float l = p.Length;
                for (float d = 0f; d < l; d += 0.002f)
                {
                    Vector3 s = p.PositionAt(d) - _head.position;
                    if (s.z < 0.02f) continue;
                    // A simple perspective view from the head.
                    float x = s.x / s.z, y = s.y / s.z;
                    int px = ox + cell / 2 + Mathf.RoundToInt(x * cell * 0.11f);
                    int py = oy + cell * 2 / 3 + Mathf.RoundToInt(y * cell * 0.3f);
                    if (px <= ox || px >= ox + cell || py <= oy || py >= oy + cell) continue;
                    float t = d / l;
                    pixels[py * tex.width + px] = new Color32((byte)(30 + 200 * t), 90, (byte)(170 - 130 * t), 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "00_path_sheet.png"), tex.EncodeToPNG());
            Destroy(tex);
        }

        void LogTightest(CatmullRomPath p, int level, int seed)
        {
            const float step = 0.02f;
            float bestD = 0f, best = float.PositiveInfinity;
            for (float d = step; d < p.Length - step; d += step)
            {
                float a = Vector3.Angle(p.TangentAt(d - step * 0.5f, 0.005f), p.TangentAt(d + step * 0.5f, 0.005f)) * Mathf.Deg2Rad;
                if (a > 1e-3f && step / a < best)
                {
                    best = step / a;
                    bestD = d;
                }
            }
            Debug.Log("[AUTOPILOT] tight level=" + level + " seed=" + seed + " r=" + best.ToString("F4") + " at " + bestD.ToString("F3") + "/" + p.Length.ToString("F3") +
                      " p-=" + p.PositionAt(bestD - 0.02f).ToString("F3") + " p=" + p.PositionAt(bestD).ToString("F3") + " p+=" + p.PositionAt(bestD + 0.02f).ToString("F3"));
        }

        void CheckDifficultyCurve()
        {
            Note("Infinite difficulty rises, then is capped");
            var inf = _gm.infinite;
            var cfg = ScriptableObject.CreateInstance<LevelConfig>();
            float prevSpeed = 0f;
            bool rising = true;
            float maxSpeed = 0f;
            int maxFighters = 0, maxBombers = 0;
            float worstAz = 0f;
            for (int wave = 1; wave <= 200; wave++)
            {
                inf.Apply(cfg, wave);
                if (cfg.planeSpeed + 1e-4f < prevSpeed) rising = false;
                prevSpeed = cfg.planeSpeed;
                maxSpeed = Mathf.Max(maxSpeed, cfg.planeSpeed);
                maxFighters = Mathf.Max(maxFighters, cfg.maxFighters);
                maxBombers = Mathf.Max(maxBombers, cfg.maxBombers);
                if (wave % 20 == 0)
                {
                    var p = FlightPathGenerator.Build(cfg, _head.position, Vector3.forward, wave);
                    worstAz = Mathf.Max(worstAz, FlightPathGenerator.MaxAzimuthOf(p, _head.position, Vector3.forward));
                }
            }
            Check("plane speed never falls between waves", rising);
            Check("plane speed respects the cap", maxSpeed <= inf.capSpeed + 1e-3f, "max " + maxSpeed.ToString("F2") + " m/s");
            Check("fighter count respects the cap", maxFighters <= inf.capFighters, "max " + maxFighters);
            Check("bomber count respects the cap", maxBombers <= inf.capBombers, "max " + maxBombers);
            Check("late waves stay in the front half", worstAz <= 90f, "worst " + worstAz.ToString("F1"));
            Destroy(cfg);
        }

        // ---------------------------------------------------------------- helpers

        IEnumerator WaitFor(Func<bool> condition, float simSeconds)
        {
            float t = 0f;
            while (!condition() && t < simSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        IEnumerator WaitSeconds(float simSeconds)
        {
            float t = 0f;
            while (t < simSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        IEnumerator StartAndWaitForFlight(int level)
        {
            ParkHands();
            _gm.StartLevel(level);
            yield return null;
            yield return WaitFor(() => _gm.menu.IsIntroPage, 3f);
            _gm.ConfirmStart();
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Flying, 15f);
        }

        IEnumerator AbortToMenu()
        {
            ParkHands();
            _gm.ShowMenu();
            yield return WaitFor(() => _gm.menu.IsStartPage, 5f);
        }

        /// <summary>Holds one C on the path ahead of the plane, leapfrogging forward until enough rings pass.</summary>
        IEnumerator FlyThroughRings(int rings, float timeout)
        {
            var plane = _gm.Plane;
            float placedAt = -1f;
            float t = 0f;
            int startRings = _gm.RingsThisFlight;
            while (plane.IsFlying && _gm.RingsThisFlight - startRings < rings && t < timeout)
            {
                float ahead = Mathf.Max(0.16f, plane.Speed * 0.6f);
                if (plane.Distance > placedAt + 0.03f)
                {
                    float d = plane.Distance + ahead;
                    if (d < plane.Path.Length - 0.1f)
                    {
                        PoseRingOnPath(_right, d);
                        placedAt = d;
                    }
                }
                t += Time.deltaTime;
                yield return null;
            }
            ParkHands();
        }

        void PoseRingOnPath(FakeHandSource hand, float distanceAlong)
        {
            var path = _gm.Plane.Path;
            Vector3 at = path.PositionAt(distanceAlong);
            Vector3 tangent = path.TangentAt(distanceAlong);
            // Choose the side where the palm is furthest from every leg of the path.
            Vector3 a = Vector3.Cross(tangent, Vector3.up);
            if (a.sqrMagnitude < 1e-4f) a = Vector3.right;
            a.Normalize();
            Vector3 b = Vector3.Cross(tangent, a).normalized;
            Vector3[] candidates = { a, -a, b, -b };
            Vector3 open = a;
            float bestClearance = -1f;
            foreach (var c in candidates)
            {
                Vector3 palm = at - c * 0.088f;
                float clearance = float.PositiveInfinity;
                foreach (var s in path.Samples) clearance = Mathf.Min(clearance, Vector3.Distance(s, palm));
                if (clearance > bestClearance)
                {
                    bestClearance = clearance;
                    open = c;
                }
            }
            hand.PoseC(at, tangent, open);
        }

        /// <summary>Runs frames while the game clock is stopped, when WaitSeconds would never finish.</summary>
        IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        /// <summary>Presses a pause panel button with the right index finger and pulls back.</summary>
        IEnumerator PokePause(string id)
        {
            Vector3 target = _gm.pausePanel.ButtonPosition(id).Value;
            Vector3 dir = (target - _head.position).normalized;
            _right.PosePoint(target - dir * 0.06f, dir);
            yield return Frames(_gm.pausePanel.inputDelayFrames + 4);
            _right.PosePoint(target + dir * 0.002f, dir);
            yield return Frames(3);
            _right.PosePoint(target - dir * 0.06f, dir);
            yield return Frames(3);
            ParkHands();
            yield return Frames(2);
        }

        IEnumerator Poke(Vector3 target)
        {
            // Press in and pull back along the line from the eyes, which is square to the curved panel.
            Vector3 forward = (target - _head.position).normalized;
            _right.PosePoint(target - forward * 0.05f, forward);
            yield return WaitSeconds(0.9f);
            _right.PosePoint(target + forward * 0.002f, forward);
            yield return WaitSeconds(0.1f);
            _right.PosePoint(target - forward * 0.06f, forward);
            yield return WaitSeconds(0.1f);
            ParkHands();
            yield return null;
        }

        // ---------------------------------------------------------------- scenarios

        IEnumerator MenuAndUnlocks()
        {
            Note("Start panel and locks");
            _gm.ShowMenu();
            yield return WaitFor(() => _gm.menu.IsStartPage, 5f);
            Check("the start panel opens", _gm.menu.IsStartPage);
            Check("level 1 is open", _gm.menu.IsButtonInteractable("level0"));
            bool othersLocked = true;
            for (int i = 1; i < Progression.TutorialLevelCount; i++) othersLocked &= !_gm.menu.IsButtonInteractable("level" + i);
            Check("levels 2 to 7 are locked", othersLocked && _gm.menu.levelRows.Length >= 7);
            Check("Infinite is locked", !_gm.menu.IsButtonInteractable("infinite"));
            Check("the level list can scroll", _gm.menu.ScrollMax > 0f, "max " + _gm.menu.ScrollMax);

            _gm.StartLevel(1);
            yield return WaitSeconds(0.2f);
            Check("a locked level refuses to start", _gm.CurrentFlow == GameManager.Flow.Menu);

            _gm.menu.SetScroll(0f);
            var pos = _gm.menu.ButtonPosition("level0");
            Check("the level 1 button exists", pos.HasValue);
            if (pos.HasValue) yield return Poke(pos.Value);
            yield return WaitFor(() => _gm.CurrentFlow != GameManager.Flow.Menu, 2f);
            Check("poking level 1 opens its intro page", _gm.CurrentFlow == GameManager.Flow.Intro && _gm.menu.IsIntroPage, "flow " + _gm.CurrentFlow);
            yield return WaitSeconds(1.5f);
            Check("the level waits on its Start button", _gm.CurrentFlow == GameManager.Flow.Intro && !_gm.Plane.IsFlying);
            var start = _gm.menu.ButtonPosition("start");
            Check("the intro page has a Start button", start.HasValue);
            if (start.HasValue) yield return Poke(start.Value);
            yield return WaitFor(() => _gm.CurrentFlow != GameManager.Flow.Intro, 2f);
            Check("poking Start begins the countdown", _gm.CurrentFlow == GameManager.Flow.PreFlight, "flow " + _gm.CurrentFlow);
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Flying, 4f);
            Check("the plane takes off after the countdown", _gm.Plane.IsFlying);
            yield return AbortToMenu();

            Check("the start panel curves around the player", _gm.menu.poke.GetComponent<CurvedCanvas>().IsCurved);
            int glassMenu = 0, solidMenu = 0;
            foreach (var img in _gm.menu.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (UiGlass.IsGlass(img)) glassMenu++;
                else if (img.sprite != null && img.sprite.name == UiGlass.RoundedSprite) solidMenu++;
            }
            Check("the menu's panels and buttons are glass", glassMenu >= 10 && solidMenu == 0, glassMenu + " glass, " + solidMenu + " still solid");
            Check("the pause panel and wrist button are glass",
                Array.Exists(_gm.pausePanel.GetComponentsInChildren<UnityEngine.UI.Image>(true), UiGlass.IsGlass) &&
                Array.Exists(_gm.wristMenu.GetComponentsInChildren<UnityEngine.UI.Image>(true), UiGlass.IsGlass));
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            Check("the glass is firm enough to read through", _gm.art.uiGlass != null && _gm.art.uiGlass.GetFloat("_Opacity") >= 0.75f && _gm.art.uiGlass.GetFloat("_TintStrength") >= 0.75f);
            Check("the scene behind the glass is available to bend", urp != null && urp.supportsCameraOpaqueTexture);
            Check("the start panel shows the coins", _gm.menu.startCoins != null && _gm.menu.startCoins.text == Progression.Coins.ToString(),
                _gm.menu.startCoins != null ? _gm.menu.startCoins.text : "none");
        }

        IEnumerator Level1()
        {
            Note("Level 1: visible path, slow flight, C ring scores 2");
            int coinsAtStart = Progression.Coins;
            _gm.StartLevel(0);
            yield return null;
            yield return WaitSeconds(1f);
            Check("the path line shows before take-off", _gm.revealer.CurrentAlpha > 0.05f, "alpha " + _gm.revealer.CurrentAlpha.ToString("F2"));
            Check("the HUD shows the level number", _gm.hud.LevelText == "LV 1", _gm.hud.LevelText);
            Check("the plane waits at the start", _gm.Plane.CurrentState == MiniPlane.State.Waiting);
            Check("the HUD shows 3 lives", _gm.hud.LivesShown == 3 && _gm.Lives == 3, _gm.Lives.ToString());
            _gm.ConfirmStart();
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Flying, 8f);
            Check("the plane takes off", _gm.Plane.IsFlying);

            float p0 = _gm.hud.Progress;
            yield return WaitSeconds(2f);
            Check("the path stays visible in flight", _gm.revealer.CurrentAlpha > 0.05f);
            Check("the HUD progress bar advances", _gm.hud.Progress > p0, _gm.hud.Progress.ToString("F3"));
            var hud = _gm.hud;
            if (hud.progressKnob != null && hud.progressTrack != null && hud.finishFlag != null)
            {
                float half = hud.progressTrack.rect.width * 0.5f;
                float want = hud.progressTrack.anchoredPosition.x + Mathf.Lerp(-half, half, hud.Progress);
                Check("the plane knob rides the progress bar", Mathf.Abs(hud.progressKnob.anchoredPosition.x - want) < 2f,
                    hud.progressKnob.anchoredPosition.x.ToString("F0") + " vs " + want.ToString("F0"));
                Check("the finish flag sits at the end of the bar", hud.finishFlag.anchoredPosition.x > hud.progressTrack.anchoredPosition.x + half - 40f);
            }
            else Check("the HUD has a plane knob and a finish flag", false);
            Check("the line only draws the path still ahead of the plane",
                _gm.Plane.Distance > 0.1f && _gm.revealer.VisibleFromDistance >= _gm.Plane.Distance - 0.02f && _gm.revealer.VisibleFromDistance < _gm.Plane.Distance + 0.1f,
                "plane at " + _gm.Plane.Distance.ToString("F2") + ", line from " + _gm.revealer.VisibleFromDistance.ToString("F2"));
            Check("level 2's line returns for 6 s after a streak", Mathf.Abs(_gm.levels[1].streakRevealSeconds - 6f) < 0.001f, _gm.levels[1].streakRevealSeconds.ToString("F1"));

            // All placed well away from the plane. An 8 cm gap is wider than the 7.5 cm the first version accepted.
            Vector3 clear = InFront(0.3f, 0.3f, -0.5f);
            var ring = _gm.rings[1];
            _right.PoseC(clear, Vector3.forward, Vector3.right, 0.07f);
            yield return WaitSeconds(ring.fillSeconds + 0.05f);
            Check("a loose, wide C makes a full ring", ring.IsActive && ring.Gap > 0.075f, "gap " + ring.Gap.ToString("F3"));

            // The hand made taller than it is deep: the bubble must sit on the fingers and take that shape.
            const float across = 1.35f;
            _right.PoseC(clear, Vector3.forward, Vector3.right, 0.034f, across);
            yield return WaitSeconds(0.4f);
            Vector3 fingerUp = Vector3.Cross(Vector3.forward, Vector3.right);
            float along = 0f, cross = 0f;
            for (int i = 0; i < BubbleRingVisual.ContourSamples; i++)
            {
                Vector3 d = ring.RimPoint(i) - ring.Center;
                along = Mathf.Max(along, Mathf.Abs(Vector3.Dot(d, Vector3.right)));
                cross = Mathf.Max(cross, Mathf.Abs(Vector3.Dot(d, fingerUp)));
            }
            Check("the bubble sits on the finger loop", Vector3.Distance(ring.Center, clear) < 0.03f, "off by " + Vector3.Distance(ring.Center, clear).ToString("F3") + " m");
            Check("the bubble is 1.1 times its original size", Mathf.Abs(ring.BubbleRadius / (ring.Radius * 0.85f) - 1.1f) < 0.001f, ring.BubbleRadius.ToString("F3") + " m");
            Check("the bubble's outline follows the fingers", cross > along * 1.15f, "tall " + cross.ToString("F3") + " wide " + along.ToString("F3"));

            ParkHands();
            yield return WaitSeconds(ring.unfillSeconds + ring.releaseGraceSeconds + 0.05f);
            _right.PoseC(clear, Vector3.forward, Vector3.right, 0.09f);
            yield return WaitSeconds(ring.fillSeconds + 0.05f);
            Check("a hand opened wider than the limit makes no ring", !ring.IsActive, "gap " + ring.Gap.ToString("F3"));
            ParkHands();
            yield return null;


            int before = _gm.Score;
            yield return FlyThroughRings(1, 10f);
            Check("a single C ring scores 2 points", _gm.Score == before + 2, "score " + _gm.Score);
            Check("a ring in a tutorial pays no coins", Progression.Coins == coinsAtStart, Progression.Coins + " from " + coinsAtStart);
            Check("the HUD shows the score", _gm.hud.ScoreText == _gm.Score.ToString(), _gm.hud.ScoreText);

            yield return FlyThroughRings(2, 15f);
            Check("two rings were flown", _gm.RingsThisFlight >= 2, "rings " + _gm.RingsThisFlight);
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Result, 30f);
            Check("level 1 ends with a result panel", _gm.CurrentFlow == GameManager.Flow.Result);
            Check("level 1 passes", _gm.LastFlightPassed, "plane " + _gm.Plane.LastCrashReason);
            Check("passing level 1 unlocks level 2", Progression.IsLevelUnlocked(1) && !Progression.IsLevelUnlocked(2));
            Check("the first clear of a tutorial level pays 5 coins", _gm.LastFirstClearCoins == 5 && Progression.Coins == coinsAtStart + 5,
                Progression.Coins + " from " + coinsAtStart);
            Check("the Next button is offered", _gm.menu.ButtonPosition("next").HasValue);
        }

        IEnumerator PauseMenu()
        {
            Note("Wrist Menu button and the pause panel");
            yield return StartAndWaitForFlight(0);
            var plane = _gm.Plane;
            var wrist = _gm.wristMenu;
            Vector3 wristAt = InFront(0.3f, -0.1f, -0.2f);
            Vector3 toFace = (_head.position - wristAt).normalized;

            yield return WaitSeconds(0.2f);
            Check("no wrist button with hands down", !wrist.IsShowing);

            _left.PoseOpenPalm(wristAt, Vector3.up, -toFace);
            yield return WaitSeconds(wrist.showDelaySeconds + 0.3f);
            Check("no wrist button with the palm facing away", !wrist.IsShowing);

            _left.PoseOpenPalm(wristAt, Vector3.up, toFace);
            yield return WaitSeconds(wrist.showDelaySeconds + 0.3f);
            Check("the wrist button shows with the palm facing the face", wrist.IsShowing && wrist.ActiveHand == 0);
            Check("the button sits on that wrist", Vector3.Distance(wrist.ButtonPosition, wristAt) < 0.06f, Vector3.Distance(wrist.ButtonPosition, wristAt).ToString("F3") + " m");

            // Press it with the other hand: first somewhere clear, then on the button.
            Vector3 press = wrist.ButtonPosition;
            _right.PosePoint(press + toFace * 0.1f, -toFace);
            yield return null;
            yield return null;
            _right.PosePoint(press, -toFace);
            yield return null;
            yield return null;
            Check("pressing the wrist button pauses the game", GameManager.IsPaused && Time.timeScale == 0f);
            Check("the pause panel opens", _gm.pausePanel.IsOpen);
            Check("the HUD hides for the pause", !_gm.hud.gameObject.activeSelf);
            yield return null;
            Check("the wrist button hides while paused", !wrist.IsShowing);

            // Nothing may happen during the pause, even with hands on the plane.
            float distance = plane.Distance;
            int hits = plane.HitsTaken;
            _left.PosePalm(plane.Position);
            _right.PoseC(plane.Position, plane.Forward, Vector3.up);
            yield return Frames(40);
            Check("the plane stops during the pause", Mathf.Approximately(plane.Distance, distance));
            Check("hands can't hurt the plane during the pause", plane.HitsTaken == hits && plane.IsFlying);
            Check("no ring forms during the pause", !_gm.rings[1].IsActive);
            ParkHands();
            yield return Frames(2);

            // Turning the head lets a real clock move the panel; here it only needs to stay usable.
            yield return PokePause("resume");
            Check("Resume closes the panel and restarts the clock", !GameManager.IsPaused && Time.timeScale == 1f && !_gm.pausePanel.IsOpen);
            Check("the HUD returns after the pause", _gm.hud.gameObject.activeSelf);
            yield return WaitSeconds(0.3f);
            Check("the flight carries on", plane.IsFlying && plane.Distance > distance, plane.Distance.ToString("F2") + " from " + distance.ToString("F2"));

            int ringsBefore = _gm.RingsThisFlight;
            yield return FlyThroughRings(1, 10f);
            Check("rings work again after resuming", _gm.RingsThisFlight > ringsBefore);

            _gm.Pause();
            yield return null;
            yield return PokePause("restart");
            Check("Restart unpauses", !GameManager.IsPaused && Time.timeScale == 1f);
            yield return null;
            Check("Restart begins the level again", _gm.CurrentFlow == GameManager.Flow.Intro && plane.Distance == 0f && _gm.Score == 0, _gm.CurrentFlow + " " + plane.Distance.ToString("F2") + " score " + _gm.Score);

            _gm.ConfirmStart();
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Flying, 8f);
            _gm.Pause();
            yield return null;
            yield return PokePause("mainmenu");
            Check("Main Menu unpauses", !GameManager.IsPaused && Time.timeScale == 1f);
            yield return WaitFor(() => _gm.menu.IsStartPage, 5f);
            Check("Main Menu opens the start panel", _gm.menu.IsStartPage);

            _left.PoseOpenPalm(wristAt, Vector3.up, toFace);
            yield return WaitSeconds(wrist.showDelaySeconds + 0.3f);
            Check("no wrist button on the start panel", !wrist.IsShowing);
            ParkHands();
            yield return null;
        }

        IEnumerator LivesAndCoins()
        {
            Note("Lives, respawning and coins");
            Progression.AddCoins(-Progression.Coins);
            yield return StartAndWaitForFlight(0);
            var plane = _gm.Plane;
            int coins = Progression.Coins;
            yield return FlyThroughRings(1, 10f);
            Check("a C ring in a tutorial pays no coins", Progression.Coins == coins, Progression.Coins + " from " + coins);
            Check("the HUD shows the coins", _gm.hud.CoinsText == Progression.Coins.ToString(), _gm.hud.CoinsText);

            float at = plane.Distance;
            plane.Crash("test crash");
            yield return WaitFor(() => plane.IsFlying, 4f);
            Check("a crash costs a life", _gm.Lives == 2 && _gm.hud.LivesShown == 2, _gm.Lives.ToString());
            Check("the plane comes back where it fell", plane.IsFlying && Mathf.Abs(plane.Distance - at) < 0.3f, at.ToString("F2") + " to " + plane.Distance.ToString("F2"));
            plane.Damage("test hit");
            Check("a respawned plane is briefly safe", plane.IsFlying && plane.HitsTaken == 0);

            yield return WaitSeconds(plane.respawnGraceSeconds + 0.2f);
            plane.Crash("test crash");
            yield return WaitFor(() => plane.IsFlying, 4f);
            yield return WaitSeconds(0.2f);
            plane.Crash("test crash");
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Result, 6f);
            Check("losing the last life ends the level", _gm.CurrentFlow == GameManager.Flow.Result && !_gm.LastFlightPassed && _gm.Lives == 0);
            yield return AbortToMenu();
        }

        IEnumerator Hangar()
        {
            Note("Hangar: planes, prices and paint");
            var catalog = _gm.catalog;
            Check("the Hangar has four planes", catalog != null && catalog.Count == 4, catalog != null ? catalog.Count.ToString() : "none");
            if (catalog != null && catalog.Count == 4)
            {
                var jet = catalog.Get(3);
                bool priciest = true;
                for (int i = 0; i < 3; i++) priciest &= catalog.Get(i).price < jet.price;
                Check("the fourth plane is the Supersonic jet, the most expensive", jet.id == "jet" && priciest && !catalog.IsOwned(3), jet.price + " coins");
                Check("the jet has twin wave turrets and a wave boost",
                    jet.Has(PlanePerk.WaveTurret) && jet.Has(PlanePerk.TwinTurrets) && jet.Has(PlanePerk.WaveBoost) && jet.description.Contains("Twin"));
                Check("all four planes have a row in the Hangar", _gm.menu.planeRows.Length >= 4);
            }
            if (catalog == null || catalog.Count < 3) yield break;
            Check("only the first plane is owned at the start", catalog.IsOwned(0) && !catalog.IsOwned(1) && !catalog.IsOwned(2));
            Check("plane 2 costs 500 and plane 3 costs 1000", catalog.Get(1).price == 500 && catalog.Get(2).price == 1000);

            var hangar = _gm.menu.ButtonPosition("hangar");
            Check("the start panel has a Hangar button", hangar.HasValue);
            if (hangar.HasValue) yield return Poke(hangar.Value);
            Check("poking it opens the Hangar", _gm.menu.IsHangarPage);
            Check("the Hangar shows a 3D preview of the plane", _gm.menu.previewAnchor != null && _gm.menu.previewAnchor.GetComponentInChildren<MeshRenderer>() != null);
            Check("the paint panel shows for an owned plane", _gm.menu.palettePanel.activeInHierarchy);

            _gm.menu.Press("plane1");
            Check("a locked plane hides the paint panel", !_gm.menu.palettePanel.activeInHierarchy);
            Check("a locked plane can't be bought without coins", !_gm.menu.IsButtonInteractable("plane_action"));
            yield return null;
            var previewRenderer = _gm.menu.previewAnchor.GetComponentInChildren<MeshRenderer>();
            var previewBlock = new MaterialPropertyBlock();
            if (previewRenderer != null) previewRenderer.GetPropertyBlock(previewBlock);
            float previewSat = previewRenderer != null ? previewBlock.GetFloat("_SatScale") : -1f;
            Check("a locked plane shows in its own colours", previewRenderer != null && (Mathf.Approximately(previewSat, 0f) || Mathf.Abs(previewSat - 1f) < 0.01f),
                "saturation " + previewSat.ToString("F2"));
            var actionBg = _gm.menu.planeAction.background;
            Check("only the Buy button greys out", actionBg != null && actionBg.color == _gm.menu.actionUnavailable,
                actionBg != null ? actionBg.color.ToString() : "none");

            Progression.AddCoins(600);
            _gm.menu.Press("plane1");
            int before = Progression.Coins;
            _gm.menu.Press("plane_action");
            Check("buying plane 2 spends 500 coins", Progression.Coins == before - 500, Progression.Coins + " from " + before);
            Check("the bought plane is owned and flown", catalog.IsOwned(1) && Progression.SelectedPlane == catalog.Get(1).id);
            Check("the mini plane switches to it", _gm.Plane.Definition == catalog.Get(1));

            _gm.menu.Press("palette2");
            Check("choosing paint saves it", Progression.PaletteFor(catalog.Get(1).id) == 2);
            var block = new MaterialPropertyBlock();
            var renderer = _gm.Plane.GetComponentInChildren<MeshRenderer>(true);
            renderer.GetPropertyBlock(block);
            Check("the paint reaches the plane", Mathf.Abs(block.GetFloat("_HueShift") - catalog.Get(1).palettes[2].hueShift) < 1e-4f, block.GetFloat("_HueShift").ToString("F3"));

            _gm.menu.Press("plane0");
            _gm.menu.Press("plane_action");
            Check("the first plane can be chosen again", Progression.SelectedPlane == catalog.Get(0).id && _gm.Plane.Definition == catalog.Get(0));
            _gm.menu.Press("hangar_back");
            Check("Back returns to the start panel", _gm.menu.IsStartPage);
            yield return null;
        }

        IEnumerator Turret()
        {
            Note("Gold balloon fits a turret that shoots enemies down");
            yield return StartAndWaitForFlight(5);
            var plane = _gm.Plane;
            plane.AddFuel(100f);
            var balloon = _gm.spawner.SpawnBalloon(BalloonKind.Turret);
            yield return null;
            Check("the gold balloon carries a turret icon", balloon.GetComponentInChildren<Billboard>() != null);
            var shield = _gm.spawner.SpawnBalloon(BalloonKind.Shield);
            yield return null;
            Check("the blue balloon carries a shield icon", shield.GetComponentInChildren<Billboard>() != null);
            Destroy(shield.gameObject);

            balloon.MoveTo(plane.Path.PositionAt(plane.Distance + plane.Speed * 0.1f));
            yield return null;
            yield return null;
            Check("flying through the gold balloon fits a turret", plane.HasTurret);

            plane.GiveShield();
            int score = _gm.Score;
            int coins = Progression.Coins;
            var tcfg = _gm.CurrentConfig;
            Check("the turret fires at least 6 rounds a second", tcfg.turretFireInterval <= 1f / 6f, (1f / tcfg.turretFireInterval).ToString("F1") + " a second");
            Check("a fitted turret starts with 6 seconds", plane.Turret != null && Mathf.Abs(plane.Turret.TimeLeft - 6f) < 0.1f,
                plane.Turret != null ? plane.Turret.TimeLeft.ToString("F2") : "none");
            float fittedAt = Time.time;
            var fighter = _gm.spawner.SpawnFighter();
            fighter.transform.position = plane.Position + plane.Forward * 0.25f + Vector3.up * 0.05f;
            float aimedAt = Time.time;
            yield return WaitFor(() => fighter == null || !fighter.IsAlive, 6f);
            float took = Time.time - aimedAt;
            Check("the turret shoots a fighter down", fighter == null || !fighter.IsAlive, "shots " + (plane.Turret != null ? plane.Turret.ShotsFired : 0));
            Check("it takes the turret under a second", took < 1f, took.ToString("F2") + " s");
            Check("a turret kill scores", _gm.Score > score);

            // Fighters circling at different sides: the turret should get nearly all of them.
            var circlers = new System.Collections.Generic.List<EnemyPlane>();
            for (int i = 0; i < 3; i++)
            {
                var f = _gm.spawner.SpawnFighter();
                Vector3 side = Quaternion.AngleAxis(120f * i, Vector3.up) * plane.Forward;
                f.transform.position = plane.Position + side * 0.35f + Vector3.up * (0.04f * (i - 1));
                circlers.Add(f);
            }
            yield return WaitFor(() => !circlers.Exists(f => f != null && f.IsAlive) || !plane.HasTurret, 3f);
            int downed = circlers.FindAll(f => f == null || !f.IsAlive).Count;
            Check("the turret downs fighters all round the plane", downed >= 3, downed + " of 3");

            if (plane.IsFlying && plane.HasTurret)
            {
                yield return WaitFor(() => !plane.HasTurret || !plane.IsFlying, 8f);
                float lasted = Time.time - fittedAt;
                Check("the turret comes off after 6 seconds", !plane.HasTurret && lasted > 5.5f && lasted < 6.6f, lasted.ToString("F2") + " s");
            }

            yield return AbortToMenu();
            Check("the turret only lasts the flight", !plane.HasTurret);
        }

        IEnumerator Level2()
        {
            Note("Level 2: line fades after 3 s, returns after 3 straight rings");
            var cfg = _gm.levels[1];
            yield return StartAndWaitForFlight(1);
            Check("the line shows at take-off", _gm.revealer.IsRevealing);
            yield return WaitSeconds(cfg.startRevealSeconds - 0.4f);
            Check("the line is still on just before its time is up", _gm.revealer.IsRevealing);
            yield return WaitSeconds(0.5f + cfg.revealFadeSeconds + 0.05f);
            Check("the line is gone after its time is up", !_gm.revealer.IsRevealing && _gm.revealer.CurrentAlpha < 0.01f, "alpha " + _gm.revealer.CurrentAlpha.ToString("F2"));

            yield return FlyThroughRings(2, 15f);
            Check("two rings do not bring the line back", !_gm.revealer.IsRevealing, "rings " + _gm.RingsThisFlight);
            yield return FlyThroughRings(1, 10f);
            Check("the third ring brings the line back", _gm.revealer.IsRevealing, "rings " + _gm.RingsThisFlight);
            float planeAt = _gm.Plane.Distance / _gm.Plane.Path.Length;
            float reach0 = _gm.revealer.Reach;
            Check("it starts growing from the plane", _gm.revealer.StreakRevealing && reach0 < 0.9f && reach0 >= planeAt - 0.02f,
                "reach " + reach0.ToString("F2") + ", plane at " + planeAt.ToString("F2"));
            yield return WaitSeconds(cfg.streakGrowSeconds * 0.5f);
            float reachMid = _gm.revealer.Reach;
            Check("it grows toward the end of the path", reachMid > reach0 + 0.05f && reachMid < 1f, reach0.ToString("F2") + " to " + reachMid.ToString("F2"));
            yield return WaitSeconds(cfg.streakGrowSeconds * 0.5f + 0.1f);
            Check("it reaches the end of the path", _gm.revealer.Reach >= 0.999f && _gm.revealer.CurrentAlpha > 0.9f * cfg.pathAlpha);
            yield return WaitSeconds(cfg.streakRevealSeconds - cfg.streakGrowSeconds - cfg.streakFadeSeconds * 0.5f - 0.1f);
            float fading = _gm.revealer.CurrentAlpha;
            Check("then it fades out", fading < 0.8f * cfg.pathAlpha && fading > 0.05f * cfg.pathAlpha, fading.ToString("F2"));
            yield return WaitSeconds(cfg.streakFadeSeconds * 0.5f + 0.15f);
            Check("the line fades away completely after its reveal", !_gm.revealer.IsRevealing && _gm.revealer.CurrentAlpha < 0.001f, _gm.revealer.CurrentAlpha.ToString("F3"));

            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Result, 40f);
            Check("level 2 passes", _gm.LastFlightPassed, "rings " + _gm.RingsThisFlight + " " + _gm.Plane.LastCrashReason);
        }

        IEnumerator Level3()
        {
            Note("Level 3: fuel drains at a constant rate, 3 rings fill it, empty crashes");
            var cfg = _gm.levels[2];
            yield return StartAndWaitForFlight(2);
            var plane = _gm.Plane;
            Check("fuel is on", plane.FuelEnabled);
            float f0 = plane.Fuel;
            yield return WaitSeconds(2f);
            float rate = (f0 - plane.Fuel) / 2f;
            Check("fuel drains at the configured rate", Mathf.Abs(rate - cfg.fuelDrainPerSecond) < 0.2f, "rate " + rate.ToString("F2"));

            yield return FlyThroughRings(1, 10f);
            float afterRing = plane.Fuel;
            Check("a ring adds fuel", afterRing > 0f && _gm.RingsThisFlight == 1);
            // Measure the refill directly from a known level.
            plane.AddFuel(-plane.Fuel + 0f);
            plane.AddFuel(cfg.FuelPerRing);
            plane.AddFuel(cfg.FuelPerRing);
            plane.AddFuel(cfg.FuelPerRing);
            Check("three ring refills take an empty tank to full", plane.Fuel01 > 0.999f, plane.Fuel01.ToString("F3"));
            yield return AbortToMenu();

            yield return StartAndWaitForFlight(2);
            _gm.DebugSetLives(1);
            float start = Time.time;
            yield return WaitFor(() => !_gm.Plane.IsFlying, 30f);
            float lasted = Time.time - start;
            float expected = cfg.fuelMax / cfg.fuelDrainPerSecond;
            Check("no rings means the plane runs dry", _gm.Plane.LastCrashReason == "ran out of fuel", _gm.Plane.LastCrashReason);
            Check("the tank lasts its configured time", Mathf.Abs(lasted - expected) < 0.5f, lasted.ToString("F1") + " s");
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Result, 5f);
            Check("running dry fails the level", !_gm.LastFlightPassed);
            Check("Next is not offered on a failure", !_gm.menu.ButtonPosition("next").HasValue);
            Check("level 4 stays locked after failing", !Progression.IsLevelUnlocked(3));

            yield return StartAndWaitForFlight(2);
            yield return FlyThroughRings(99, 60f);
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Result, 10f);
            Check("level 3 passes with enough rings", _gm.LastFlightPassed, "rings " + _gm.RingsThisFlight + " " + _gm.Plane.LastCrashReason);
        }

        IEnumerator MergedRing()
        {
            Note("Two C rings close together merge into a big O that scores 1");
            yield return StartAndWaitForFlight(0);
            var plane = _gm.Plane;
            var merger = _gm.merger;

            float d = plane.Distance + 0.3f;
            Vector3 at = plane.Path.PositionAt(d);
            Vector3 tangent = plane.Path.TangentAt(d);
            Vector3 axis = Vector3.Cross(tangent, Vector3.up).normalized;

            // Apart: two separate rings.
            _left.PoseC(at - axis * 0.2f, tangent, axis);
            _right.PoseC(at + axis * 0.2f, tangent, -axis);
            yield return WaitSeconds(0.5f);
            Check("each hand forms its own ring", _gm.rings[0].IsActive && _gm.rings[1].IsActive);
            Check("distant rings do not merge", !merger.IsMerged);

            _left.PoseC(at - axis * 0.065f, tangent, axis);
            _right.PoseC(at + axis * 0.065f, tangent, -axis);
            yield return null;
            yield return null;
            Check("close rings merge", merger.IsMerged);
            Check("single rings hide during the merge", _gm.rings[0].MergedHidden && _gm.rings[1].MergedHidden);
            yield return WaitSeconds(merger.mergeSeconds + 0.05f);
            Check("the merge completes", merger.MergeProgress >= 1f);
            Check("the O is bigger than a C", merger.Radius > _gm.rings[0].BubbleRadius, merger.Radius.ToString("F3") + " vs " + _gm.rings[0].BubbleRadius.ToString("F3"));

            int before = _gm.Score;
            yield return WaitFor(() => plane.Distance > d + 0.05f || !plane.IsFlying, 5f);
            Check("the big O scores 1 point", _gm.Score == before + 1, "score " + _gm.Score + " from " + before);

            _left.PoseC(at - axis * 0.2f, tangent, axis);
            _right.PoseC(at + axis * 0.2f, tangent, -axis);
            yield return null;
            yield return null;
            Check("pulling hands apart splits the O", !merger.IsMerged);
            yield return AbortToMenu();
        }

        IEnumerator FlickEnemy(EnemyPlane enemy)
        {
            // A fast swipe through the enemy with the left hand.
            Vector3 dir = Vector3.right;
            for (int i = 0; i < 12 && enemy != null && enemy.IsAlive; i++)
            {
                Vector3 offset = dir * ((i % 2 == 0) ? -0.012f : 0.012f);
                _left.PosePalm(enemy.Position + offset);
                yield return null;
            }
            ParkHands();
        }

        IEnumerator Level4()
        {
            Note("Level 4: fighters shoot bursts; hands block; flicks destroy for 2 points");
            Progression.MarkPassed(2);
            yield return StartAndWaitForFlight(3);
            var plane = _gm.Plane;
            plane.GiveShield();    // Keep the flight alive while we test.
            var fighter = _gm.spawner.SpawnFighter();
            Check("a fighter spawns", fighter != null && fighter.IsAlive);

            yield return WaitFor(() => Projectile.Active.Count > 0 || !plane.IsFlying, 15f);
            Check("the fighter opens fire", Projectile.Active.Count > 0, "plane " + plane.CurrentState);
            if (Projectile.Active.Count > 0)
            {
                var shot = Projectile.Active[0];
                _right.PosePalm(shot.Position);
                yield return null;
                yield return null;
                Check("a hand blocks a bullet", shot == null);
                ParkHands();
            }

            // The block above teleports a simulated hand, which itself reads as a fast flick, so use a
            // fresh fighter for the scoring check.
            plane.GiveShield();
            yield return WaitSeconds(0.5f);
            var target = _gm.spawner.SpawnFighter();
            yield return null;
            int before = _gm.Score;
            yield return FlickEnemy(target);
            Check("a flick destroys the fighter", target == null || !target.IsAlive);
            Check("a flick scores 2 points", _gm.Score == before + 2, "score " + _gm.Score + " from " + before);
            yield return AbortToMenu();

            // A fighter left alone eventually brings the plane down: real danger, not decoration.
            yield return StartAndWaitForFlight(3);
            _gm.spawner.SpawnFighter();
            yield return WaitFor(() => !_gm.Plane.IsFlying, 45f);
            string reason = _gm.Plane.LastCrashReason;
            Check("an unanswered fighter can down the plane or the fuel runs out",
                reason == "shot down" || reason == "rammed by a fighter" || reason == "ran out of fuel", reason);
            yield return AbortToMenu();
        }

        IEnumerator Level5()
        {
            Note("Level 5: pinch-drag a crosshair; balloon follows slowly; shield absorbs one hit");
            Progression.MarkPassed(3);
            yield return StartAndWaitForFlight(4);
            var plane = _gm.Plane;
            var balloon = _gm.spawner.SpawnBalloon(BalloonKind.Shield);
            yield return null;

            float d = plane.Distance + plane.Speed * 5f;
            Vector3 onPath = plane.Path.PositionAt(d);
            Vector3 grabFrom = balloon.CrosshairPosition;

            _right.PosePinch(grabFrom, Vector3.forward);
            yield return null;
            yield return null;
            Check("a pinch picks up the crosshair", balloon.IsHeld);
            Check("holding a crosshair suppresses that hand's ring", _gm.rings[1].Suppressed);

            _right.PosePinch(onPath, Vector3.forward);
            yield return null;
            yield return null;
            Check("the crosshair follows the pinch at once", Vector3.Distance(balloon.CrosshairPosition, onPath) < 0.01f);
            float gap = Vector3.Distance(balloon.Position, onPath);
            float startGap = Vector3.Distance(grabFrom, onPath);
            Check("the balloon lags behind the crosshair", gap > startGap * 0.8f || startGap < 0.02f, gap.ToString("F2") + " of " + startGap.ToString("F2"));

            ParkHands();
            yield return null;
            Check("releasing the pinch lets go", !balloon.IsHeld);

            yield return WaitSeconds(_gm.levels[4].balloonFollowSeconds * 1.5f);
            if (balloon != null)
                Check("the balloon drifts to the crosshair", Vector3.Distance(balloon.Position, onPath) < 0.06f, Vector3.Distance(balloon.Position, onPath).ToString("F3"));

            yield return WaitFor(() => balloon == null || plane.HasShield || !plane.IsFlying, 12f);
            Check("flying through the blue balloon gives a shield", plane.HasShield, plane.CurrentState + " " + plane.LastCrashReason);

            if (plane.IsFlying)
            {
                plane.AddFuel(100f);
                plane.Damage("test blast");
                Check("the shield absorbs one hit", plane.IsFlying && !plane.HasShield);
            }
            yield return AbortToMenu();
        }

        BombBalloon MakeBomb(Vector3 at)
        {
            var go = new GameObject("TestBomb");
            var b = go.AddComponent<BombBalloon>();
            b.Setup(_gm.CurrentConfig, _gm.Plane, _gm.spawner.hands, _gm.art, at);
            return b;
        }

        IEnumerator BomberPropellers()
        {
            var bomber = _gm.spawner.SpawnBomber();
            yield return null;
            var props = new System.Collections.Generic.List<Transform>();
            foreach (var t in bomber.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Propeller")) props.Add(t);
            Check("the bomber has two propellers of its own", props.Count == 2, props.Count + " found");
            var before = props.ConvertAll(t => t.localRotation);
            yield return WaitSeconds(0.1f);
            bool spun = props.Count > 0;
            for (int i = 0; i < props.Count; i++) spun &= Quaternion.Angle(before[i], props[i].localRotation) > 5f;
            Check("the bomber's propellers spin", spun);
            Destroy(bomber.gameObject);
        }

        IEnumerator Level6()
        {
            Note("Level 6: bomber, balloon chain, flash, touch, cover");
            Progression.MarkPassed(4);
            yield return StartAndWaitForFlight(5);
            var cfg = _gm.levels[5];
            Check("the bomber level sends bombers and no fighters", cfg.maxBombers > 0 && cfg.maxFighters == 0, cfg.maxFighters + " fighters");
            bool sawFighter = false, sawBomber = false;
            _gm.Plane.DebugInvulnerable = true;
            _gm.Plane.AddFuel(100f);
            for (float t = 0f; t < cfg.bomberSpawnInterval + 3f && _gm.Plane.IsFlying; t += Time.deltaTime)
            {
                foreach (var e in EnemyPlane.Active)
                {
                    if (e == null || !e.IsAlive) continue;
                    if (e is BomberPlane) sawBomber = true;
                    else sawFighter = true;
                }
                yield return null;
            }
            _gm.Plane.DebugInvulnerable = false;
            Check("only bombers fly in the bomber level", sawBomber && !sawFighter, "bomber " + sawBomber + ", fighter " + sawFighter);
            foreach (var e in EnemyPlane.Active.ToArray()) if (e != null) Destroy(e.gameObject);
            foreach (var bb in BombBalloon.Active.ToArray()) if (bb != null) Destroy(bb.gameObject);
            yield return null;
            yield return BomberPropellers();
            var plane = _gm.Plane;
            plane.AddFuel(100f);

            // Flick before the drop: no balloon at all.
            var b1 = _gm.spawner.SpawnBomber();
            yield return WaitSeconds(0.2f);
            yield return FlickEnemy(b1);
            Check("a bomber flicked before its drop leaves no balloon", BombBalloon.Active.Count == 0);

            // Drop happens at the path, then it dives.
            var b2 = _gm.spawner.SpawnBomber();
            b2.ReleaseBalloon();
            Check("the bomber releases a balloon", b2.Balloon != null);
            var chained = b2.Balloon;
            yield return null;
            Check("the balloon starts unarmed", chained.CurrentState == BombBalloon.State.Idle);
            plane.GiveShield();
            yield return FlickEnemy(b2);
            Check("a bomber destroyed inside explosion range sets its balloon off", chained == null || !chained.IsAlive);
            yield return WaitSeconds(0.1f);

            // Far from its balloon: the balloon survives.
            var b3 = _gm.spawner.SpawnBomber();
            b3.transform.position = plane.Position + Vector3.up * 0.6f;
            b3.ReleaseBalloon();
            var survivor = b3.Balloon;
            b3.transform.position = survivor.Position + Vector3.up * (cfg.explosionRange + 0.2f);
            yield return null;
            yield return FlickEnemy(b3);
            Check("a bomber destroyed outside explosion range leaves its balloon", survivor != null && survivor.IsAlive);
            if (survivor != null) Destroy(survivor.gameObject);
            yield return null;

            // Hand touch sets a balloon off.
            Vector3 clear = plane.Position + Vector3.up * 0.9f;
            var touched = MakeBomb(clear);
            yield return null;
            _left.PosePalm(touched.Position);
            yield return null;
            yield return null;
            Check("a hand touching a balloon sets it off", touched == null || !touched.IsAlive);
            ParkHands();
            yield return null;

            // Near plane: arms and flashes; both hands cover it; plane unharmed.
            plane.AddFuel(100f);
            int coinsBeforeCover = Progression.Coins;
            var covered = MakeBomb(plane.Position + Vector3.up * 0.1f);
            Check("a bomb balloon carries a bomb icon", covered.GetComponentInChildren<Billboard>() != null);
            Check("an idle bomb shows the don't-touch icon", covered.IconName == "NoTouchIcon", covered.IconName);
            yield return null;
            yield return null;
            Check("the plane close by arms the balloon", covered.CurrentState == BombBalloon.State.Armed, covered.CurrentState.ToString());
            Check("a flashing bomb shows the animated cup-it icon", covered.IconName == "CupIcon" && covered.GetComponentInChildren<CupIconMotion>() != null, covered.IconName);
            Vector3 c = covered.Position;
            _left.PosePalm(c - Vector3.right * 0.08f);
            _right.PosePalm(c + Vector3.right * 0.08f);
            Check("cupping reads as covering", covered.BothHandsCovering());
            yield return WaitSeconds(cfg.bombCoverHoldSeconds + 0.05f);
            Check("both hands contain the blast", covered == null || covered.WasContained);
            Check("covering a bomb in a tutorial pays no coins", Progression.Coins == coinsBeforeCover, Progression.Coins + " from " + coinsBeforeCover);
            Check("a contained blast leaves the plane flying", plane.IsFlying && plane.HitsTaken == 0);
            ParkHands();
            yield return null;

            // A looser cup: palms further off and only roughly opposite still contain it.
            var loose = MakeBomb(plane.Position + Vector3.up * 0.1f);
            yield return null;
            yield return null;
            Vector3 lc = loose.Position;
            _left.PosePalm(lc - Vector3.right * 0.155f);
            _right.PosePalm(lc + Vector3.up * 0.155f);
            Check("a loose, angled cup counts as covering", loose.CurrentState == BombBalloon.State.Armed && loose.BothHandsCovering());
            yield return WaitSeconds(cfg.bombCoverHoldSeconds + 0.05f);
            Check("and contains the bomb", loose == null || loose.WasContained);
            ParkHands();
            yield return null;

            yield return AbortToMenu();

            // A balloon just off the upcoming path, left uncovered, in a fresh flight before any enemy
            // spawns: the plane must arm it naturally and still be in range when the fuse runs out.
            yield return StartAndWaitForFlight(5);
            plane = _gm.Plane;
            // Far enough ahead that the plane arms it a full fuse before passing, at a spot whose closest pass
            // is the one the plane reaches there: where a path doubles back, the balloon times its fuse to the
            // closer pass, and the test would be measuring the wrong one.
            float along = plane.Distance + plane.Speed * (cfg.bombFuseSeconds + 0.6f);
            BombBalloon uncovered = null;
            for (float tryAt = along; tryAt < Mathf.Min(along + 2.5f, plane.Path.Length - 0.3f); tryAt += 0.05f)
            {
                Vector3 trySide = Vector3.Cross(plane.Path.TangentAt(tryAt), Vector3.up).normalized;
                var candidate = MakeBomb(plane.Path.PositionAt(tryAt) + trySide * cfg.bombPathOffset);
                if (Mathf.Abs(candidate.PassAt - tryAt) < 0.06f && candidate.PassMiss <= cfg.bombBlastRadius)
                {
                    uncovered = candidate;
                    break;
                }
                DestroyImmediate(candidate.gameObject);
            }
            Check("a clean spot for the bomb test was found", uncovered != null);
            if (uncovered == null)
            {
                yield return AbortToMenu();
                yield break;
            }
            yield return WaitFor(() => uncovered != null && uncovered.CurrentState == BombBalloon.State.Armed, 3f);
            Check("the plane flying past arms the balloon", uncovered != null && uncovered.CurrentState == BombBalloon.State.Armed);
            float armedAhead = uncovered != null ? Vector3.Distance(uncovered.Position, plane.Position) : 0f;
            Check("it arms early enough to flash for the whole 2 s fuse", armedAhead > plane.Speed * cfg.bombFuseSeconds * 0.7f, armedAhead.ToString("F2") + " m away");
            yield return WaitFor(() => uncovered == null || !plane.IsFlying, cfg.bombFuseSeconds + 0.3f);
            Check("an uncovered blast brings the plane down", plane.LastCrashReason == "caught in a bomb blast", plane.LastCrashReason);
            yield return AbortToMenu();

            // After the drop a bomber flies away instead of diving at the plane.
            yield return StartAndWaitForFlight(5);
            plane = _gm.Plane;
            plane.AddFuel(100f);
            var leaver = _gm.spawner.SpawnBomber();
            leaver.ReleaseBalloon();
            if (leaver.Balloon != null) Destroy(leaver.Balloon.gameObject);
            Vector3 dropAt = leaver.Position;
            yield return WaitSeconds(2.5f);
            float away = leaver != null ? Vector3.Distance(leaver.Position, _head.position) : 99f;
            Check("a bomber leaves after its drop", leaver == null || away > Vector3.Distance(dropAt, _head.position) + 0.3f, "now " + away.ToString("F2") + " m from the head");
            Check("a leaving bomber doesn't hurt the plane", plane.IsFlying && plane.HitsTaken == 0, plane.LastCrashReason);
            if (leaver != null) Destroy(leaver.gameObject);
            yield return null;

            // Flicking a bomber is worth extra points and a coin.
            var target = _gm.spawner.SpawnBomber();
            yield return WaitSeconds(0.2f);
            int before = _gm.Score;
            int coins = Progression.Coins;
            yield return FlickEnemy(target);
            Check("flicking a bomber scores extra", _gm.Score == before + _gm.bomberFlickPoints && _gm.bomberFlickPoints > _gm.flickPoints, "score " + _gm.Score + " from " + before);
            Check("downing an enemy in a tutorial pays no coins", Progression.Coins == coins, Progression.Coins + " from " + coins);
            yield return AbortToMenu();
        }

        IEnumerator InfiniteMode()
        {
            Note("Infinite: unlock, waves, game over, high score");
            _gm.ShowMenu();
            yield return WaitFor(() => _gm.menu.IsStartPage, 5f);
            Check("Infinite stays locked until level 6 is passed", !Progression.InfiniteUnlocked);
            Progression.MarkPassed(5);
            Check("passing level 6 unlocks Infinite", Progression.InfiniteUnlocked);
            _gm.ShowMenu();
            yield return WaitFor(() => _gm.menu.IsStartPage, 5f);
            Check("the Infinite button opens", _gm.menu.IsButtonInteractable("infinite"));

            _gm.StartInfinite();
            yield return null;
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Flying, 10f);
            Check("Infinite starts at wave 1", _gm.Wave == 1 && _gm.InInfinite);
            Check("the HUD shows the wave number", _gm.hud.LevelText == "W 1", _gm.hud.LevelText);
            float speed1 = _gm.Plane.Speed;

            yield return WaitFor(() => _gm.Wave >= 2 && _gm.CurrentFlow == GameManager.Flow.Flying, 60f);
            Check("surviving a wave starts the next", _gm.Wave >= 2);
            Check("the next wave flies faster", _gm.Plane.Speed > speed1, _gm.Plane.Speed.ToString("F3") + " vs " + speed1.ToString("F3"));

            Check("Infinite starts with 3 lives", _gm.LivesTotal == 3);
            Check("a survived wave pays 5 coins", _gm.CoinsThisRun >= _gm.infinite.coinsPerWave, "run coins " + _gm.CoinsThisRun);

            // Score something so a high score is recorded, then lose every life.
            int ringCoins = Progression.Coins;
            yield return FlyThroughRings(1, 10f);
            Check("a C ring in Infinite pays 2 coins", Progression.Coins == ringCoins + 2, Progression.Coins + " from " + ringCoins);
            int score = _gm.Score;

            if (_gm.Plane.IsFlying)
            {
                int coverCoins = Progression.Coins;
                var bomb = MakeBomb(_gm.Plane.Position + Vector3.up * 0.1f);
                yield return null;
                yield return null;
                Vector3 bc = bomb.Position;
                _left.PosePalm(bc - Vector3.right * 0.08f);
                _right.PosePalm(bc + Vector3.right * 0.08f);
                yield return WaitSeconds(_gm.CurrentConfig.bombCoverHoldSeconds + 0.05f);
                Check("covering a bomb in Infinite pays 5 coins", Progression.Coins == coverCoins + 5, Progression.Coins + " from " + coverCoins);
                ParkHands();
                yield return null;
            }
            for (int life = 0; life < 4 && _gm.CurrentFlow == GameManager.Flow.Flying; life++)
            {
                yield return WaitFor(() => _gm.Plane.IsFlying || _gm.CurrentFlow != GameManager.Flow.Flying, 5f);
                _gm.Plane.Crash("test crash");
                yield return WaitSeconds(0.2f);
            }
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Result, 20f);
            Check("Infinite ends in a result panel", _gm.CurrentFlow == GameManager.Flow.Result, _gm.Plane.LastCrashReason);
            Check("the high score is saved", Progression.HighScore >= score && score > 0, "high " + Progression.HighScore + " score " + score);
        }

        // ---------------------------------------------------------------- coins, perks, tubes, islands, zeppelin

        IEnumerator TutorialReplayPaysNothing()
        {
            Note("A passed tutorial level pays nothing when replayed");
            int coins = Progression.Coins;
            yield return StartAndWaitForFlight(0);
            yield return FlyThroughRings(2, 15f);
            Check("rings in a replayed tutorial pay no coins", Progression.Coins == coins, Progression.Coins + " from " + coins);
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Result, 30f);
            Check("the replayed level passes", _gm.LastFlightPassed, _gm.Plane.LastCrashReason);
            Check("passing it again pays no first-clear coins", _gm.LastFirstClearCoins == 0 && Progression.Coins == coins, Progression.Coins + " from " + coins);
            yield return AbortToMenu();
        }

        IEnumerator PlanePerks()
        {
            Note("The Bluebird fits its own turret; the Pusher Racer boosts at each wave start");
            var catalog = _gm.catalog;
            if (catalog == null || catalog.Count < 3) yield break;
            Check("the Bluebird has the wave turret perk", catalog.Get(1).perk == PlanePerk.WaveTurret);
            Check("the Bluebird's turret is a single one", !catalog.Get(1).Has(PlanePerk.TwinTurrets));
            Check("the Pusher Racer has the wave boost perk", catalog.Get(2).perk == PlanePerk.WaveBoost);
            Check("their descriptions say so", catalog.Get(1).description.Contains("turret") && catalog.Get(2).description.Contains("Boosts"));

            Progression.GrantPlane(catalog.Get(1).id);
            Progression.SelectedPlane = catalog.Get(1).id;
            _gm.ApplyPlaneSelection();
            Progression.MarkPassed(2);
            yield return StartAndWaitForFlight(3);
            var plane = _gm.Plane;
            plane.AddFuel(100f);
            Check("no turret before an enemy shows up", !plane.HasTurret);
            yield return WaitFor(() => EnemyPlane.Active.Count > 0, 10f);
            yield return null;
            Check("the Bluebird fits its turret when the first fighter shows up", plane.HasTurret);
            Check("its turret has the usual 6 seconds", plane.Turret != null && plane.Turret.TimeLeft > 5.5f && plane.Turret.TimeLeft <= 6f,
                plane.Turret != null ? plane.Turret.TimeLeft.ToString("F2") : "none");
            yield return AbortToMenu();

            Progression.GrantPlane(catalog.Get(2).id);
            Progression.SelectedPlane = catalog.Get(2).id;
            _gm.ApplyPlaneSelection();
            yield return StartAndWaitForFlight(0);
            yield return WaitSeconds(0.6f);
            Check("the Pusher Racer boosts at the start of the wave", plane.IsBoosting && plane.Speed > plane.BaseSpeed * 1.4f,
                plane.Speed.ToString("F3") + " vs " + plane.BaseSpeed.ToString("F3"));
            yield return WaitSeconds(catalog.Get(2).perkBoostSeconds);
            Check("its boost ends after 3 seconds", !plane.IsBoosting && Mathf.Abs(plane.Speed - plane.BaseSpeed) < 1e-3f, plane.Speed.ToString("F3"));
            yield return AbortToMenu();

            if (catalog.Count >= 4)
            {
                var jet = catalog.Get(3);
                Progression.GrantPlane(jet.id);
                Progression.SelectedPlane = jet.id;
                _gm.ApplyPlaneSelection();
                yield return StartAndWaitForFlight(3);
                plane = _gm.Plane;
                plane.AddFuel(100f);
                plane.DebugInvulnerable = true;
                yield return WaitSeconds(0.6f);
                Check("the jet boosts at the start of the wave", plane.IsBoosting && plane.Speed > plane.BaseSpeed * 1.4f,
                    plane.Speed.ToString("F3") + " vs " + plane.BaseSpeed.ToString("F3"));
                Check("no jet turrets before an enemy shows up", !plane.HasTurret);
                yield return WaitFor(() => EnemyPlane.Active.Count > 0, 10f);
                yield return null;
                Check("the jet fits twin turrets when the first fighter shows up", plane.TurretCount == 2 && plane.TwinTurret != null);
                if (plane.TurretCount == 2)
                {
                    Vector3 a = plane.transform.InverseTransformPoint(plane.Turret.transform.position);
                    Vector3 b = plane.transform.InverseTransformPoint(plane.TwinTurret.transform.position);
                    Check("the twins sit one each side", Mathf.Abs(a.x + b.x) < 1e-3f && Mathf.Abs(a.x) > 1e-3f && Mathf.Abs(a.y - b.y) < 1e-3f,
                        a.x.ToString("F3") + " / " + b.x.ToString("F3"));
                    Check("both have the usual 6 seconds", plane.Turret.TimeLeft > 5.5f && plane.TwinTurret.TimeLeft > 5.5f);
                    // Two enemies in range: the twins split them.
                    var f1 = _gm.spawner.SpawnFighterAt(plane.Position + plane.transform.right * 0.15f + Vector3.up * 0.05f);
                    var f2 = _gm.spawner.SpawnFighterAt(plane.Position - plane.transform.right * 0.15f + Vector3.up * 0.05f);
                    foreach (var e in EnemyPlane.Active) if (e != null && e != f1 && e != f2) e.transform.position += Vector3.up * 5f;
                    bool split = false;
                    int shots = 0;
                    for (int i = 0; i < 6 && !split; i++)
                    {
                        yield return null;
                        var ta = plane.Turret != null ? plane.Turret.CurrentTarget : null;
                        var tb = plane.TwinTurret != null ? plane.TwinTurret.CurrentTarget : null;
                        split = ta != null && tb != null && ta != tb;
                    }
                    Check("with two enemies near, each twin takes its own", split);
                    yield return WaitSeconds(0.5f);
                    shots = (plane.Turret != null ? plane.Turret.ShotsFired : 0) + (plane.TwinTurret != null ? plane.TwinTurret.ShotsFired : 0);
                    Check("both twins fire", plane.Turret != null && plane.TwinTurret != null && plane.Turret.ShotsFired > 0 && plane.TwinTurret.ShotsFired > 0, shots + " shots");
                    float left = plane.Turret != null ? plane.Turret.TimeLeft : 0f;
                    yield return WaitSeconds(left + 0.3f);
                    Check("the twins come off together when their time is up", plane.TurretCount == 0);
                }
                plane.DebugInvulnerable = false;
                yield return AbortToMenu();
            }

            Progression.SelectedPlane = catalog.Get(0).id;
            _gm.ApplyPlaneSelection();
        }

        IEnumerator BoosterTubes()
        {
            Note("Booster tubes: four shapes, held like an object, a clean pass boosts, the wall breaks them");
            Progression.MarkPassed(3);
            yield return StartAndWaitForFlight(4);
            var plane = _gm.Plane;
            var cfg = _gm.CurrentConfig;
            plane.AddFuel(100f);
            plane.GiveShield();
            Check("level 5 hands out booster tubes", cfg.boosterTubeInterval > 0f);

            var shapes = new System.Collections.Generic.List<BoosterTube>();
            for (int i = 0; i < 4; i++) shapes.Add(_gm.spawner.SpawnTube((TubeShape)i));
            yield return null;
            int straightPicks = 0;
            for (int i = 0; i < 400; i++)
            {
                var pick = _gm.spawner.PickTubeShape();
                if (pick == TubeShape.StraightShort || pick == TubeShape.StraightLong) straightPicks++;
            }
            Check("most tubes are straight", straightPicks > 240 && straightPicks < 330, straightPicks + " of 400");
            Check("straight tubes come short and long", shapes[0].BendRadius == 0f && shapes[1].BendRadius == 0f && shapes[1].Length > shapes[0].Length * 1.5f);
            Check("curved tubes come short and long", shapes[2].BendRadius > 0f && shapes[3].BendRadius > 0f && shapes[3].Length > shapes[2].Length * 1.5f);
            float chord = Vector3.Distance(shapes[3].EndPosition(0), shapes[3].EndPosition(1));
            Check("a curved tube boosts 1.5 times as hard as a straight one", Mathf.Abs(shapes[3].BoostMultiplier - shapes[0].BoostMultiplier * 1.5f) < 1e-4f,
                shapes[0].BoostMultiplier.ToString("F2") + " vs " + shapes[3].BoostMultiplier.ToString("F2"));
            Check("a curved tube really bends", chord < shapes[3].Length * 0.95f, chord.ToString("F3") + " of " + shapes[3].Length.ToString("F3"));
            Check("a tube is roomy enough to fly through easily", shapes[0].innerRadius >= 0.045f && shapes[0].innerRadius < 0.07f, (shapes[0].innerRadius * 200f).ToString("F0") + " cm across");
            for (int i = 1; i < 4; i++) Destroy(shapes[i].gameObject);
            var tube = shapes[0];

            // Pick it up by its wall, move it, turn it.
            var grabber = _gm.rings[1].GetComponent<BalloonGrabber>();
            Vector3 grip = tube.transform.TransformPoint(tube.LocalCentreline[8] + Vector3.up * (tube.innerRadius + tube.wallThickness));
            Vector3 dir = (grip - _head.position).normalized;
            _right.PosePinch(grip, dir);
            yield return null;
            yield return null;
            Check("a pinch picks up a booster tube", grabber.HeldTube == tube);
            Check("a tube has a drag cursor", tube.HasCursor);
            Vector3 startPos = tube.transform.position;
            Quaternion startRot = tube.transform.rotation;
            _right.PosePinch(grip + Vector3.right * 0.1f, dir);
            yield return null;
            yield return null;
            Check("the drag cursor jumps to the hand", Vector3.Distance(tube.CursorPosition, grip + Vector3.right * 0.1f) < 0.005f);
            Check("the tube glides after it rather than jumping", Vector3.Distance(tube.transform.position, startPos) < 0.05f,
                Vector3.Distance(tube.transform.position, startPos).ToString("F3") + " m moved");
            yield return WaitSeconds(1.2f);
            Check("a held tube moves with the hand", Vector3.Distance(tube.transform.position, startPos + Vector3.right * 0.1f) < 0.01f,
                Vector3.Distance(tube.transform.position, startPos + Vector3.right * 0.1f).ToString("F3"));
            _right.PosePinch(grip + Vector3.right * 0.1f, Quaternion.AngleAxis(60f, Vector3.up) * dir);
            yield return WaitSeconds(1.2f);
            float turned = Quaternion.Angle(startRot, tube.transform.rotation);
            Check("a held tube turns with the hand", Mathf.Abs(turned - 60f) < 10f, turned.ToString("F0") + " degrees");
            ParkHands();
            yield return null;
            Vector3 leftAt = tube.transform.position;
            yield return WaitSeconds(0.5f);
            Check("letting go leaves the tube where it was put", grabber.HeldTube == null && Vector3.Distance(tube.transform.position, leftAt) < 0.002f);

            // Line it up with the straightest stretch of path just ahead.
            float L = tube.Length;
            float bestD = -1f, bestDev = float.PositiveInfinity;
            float end = Mathf.Min(plane.Distance + 0.7f, plane.Path.Length - L - 0.1f);
            for (float d = plane.Distance + 0.2f; d < end; d += 0.01f)
            {
                Vector3 a = plane.Path.PositionAt(d), b = plane.Path.PositionAt(d + L);
                float dev = 0f;
                for (int k = 1; k < 8; k++) dev = Mathf.Max(dev, HandRing.DistancePointSegment(plane.Path.PositionAt(d + L * k / 8f), a, b));
                if (dev < bestDev)
                {
                    bestDev = dev;
                    bestD = d;
                }
            }
            // Count only the tube under test: the level keeps spawning tubes of its own.
            int boosts = 0, walls = 0;
            BoosterTube watched = tube;
            Action<BoosterTube> onBoost = t => { if (t == watched) boosts++; };
            Action<BoosterTube> onWall = t => { if (t == watched) walls++; };
            BoosterTube.AnyBoosted += onBoost;
            BoosterTube.AnyWallHit += onWall;
            Vector3 p0 = plane.Path.PositionAt(bestD), p1 = plane.Path.PositionAt(bestD + L);
            ClearTubesExcept(tube);
            tube.PlaceAt((p0 + p1) * 0.5f, Quaternion.LookRotation(p1 - p0, Vector3.up));
            yield return WaitFor(() => boosts + walls > 0 || plane.Distance > bestD + L + 0.1f || !plane.IsFlying, 6f);
            Check("flying cleanly through a tube boosts the plane", boosts == 1 && plane.IsBoosting,
                "boosts " + boosts + " walls " + walls + " bend " + bestDev.ToString("F3"));
            yield return WaitSeconds(0.3f);
            Check("a boost speeds the plane up", plane.Speed > plane.BaseSpeed * (cfg.boostSpeedMultiplier - 0.05f), plane.Speed.ToString("F3"));
            yield return WaitSeconds(cfg.boostSeconds + 0.3f);
            Check("the boost wears off", !plane.IsBoosting && Mathf.Abs(plane.Speed - plane.BaseSpeed) < 1e-3f, plane.Speed.ToString("F3"));

            // A tube lying across the path: the plane clips its wall.
            yield return WaitFor(() => plane.IsFlying, 5f);
            var blocker = _gm.spawner.SpawnTube(TubeShape.StraightLong);
            watched = blocker;
            int hitsBefore = plane.HitsTaken;
            float dW = plane.Distance + plane.Speed * 0.8f;
            Vector3 at = plane.Path.PositionAt(dW);
            Vector3 tan = plane.Path.TangentAt(dW);
            Vector3 across = Vector3.Cross(tan, Vector3.up);
            if (across.sqrMagnitude < 1e-4f) across = Vector3.right;
            Vector3 lift = Vector3.Cross(across.normalized, tan).normalized;
            ClearTubesExcept(blocker);
            blocker.PlaceAt(at + lift * (blocker.innerRadius + blocker.wallThickness * 0.5f), Quaternion.LookRotation(across.normalized, lift));
            yield return WaitFor(() => walls > 0 || plane.Distance > dW + 0.1f || !plane.IsFlying, 4f);
            yield return null;
            Check("clipping a tube's wall breaks it", walls == 1 && blocker == null, "walls " + walls);
            Check("a tube is used up by a clean pass", tube == null);
            Check("a broken tube gives no boost", !plane.IsBoosting);
            Check("a tube's wall doesn't hurt the plane", plane.HitsTaken == hitsBefore, plane.HitsTaken + " hits");
            // Hands never break a tube: a gentle touch or a hard knock leaves it.
            var touched = _gm.spawner.SpawnTube(TubeShape.StraightShort);
            ClearTubesExcept(touched);
            yield return WaitSeconds(0.2f);
            Vector3 side = touched.transform.TransformPoint(touched.LocalCentreline[8] + Vector3.up * (touched.innerRadius + touched.wallThickness * 0.5f));
            HandMotion.Reset(_left);
            for (int i = 0; i < 20; i++)
            {
                _left.PosePalm(side + Vector3.up * 0.002f);
                yield return null;
            }
            Check("a gentle touch leaves a tube alone", touched != null);
            ParkHands();
            yield return null;
            HandMotion.Reset(_left);
            _left.PosePalm(side + Vector3.up * 0.2f);
            yield return null;
            for (int i = 0; i < 8 && touched != null; i++)
            {
                _left.PosePalm(side + Vector3.up * (0.15f - 0.04f * i));
                yield return null;
            }
            yield return null;
            Check("a hand knocking into a tube leaves it too", touched != null && touched.IsAlive);
            if (touched != null)
            {
                float lifeBefore = touched.TimeLeft;
                yield return WaitSeconds(0.5f);
                Check("a tube counts down to vanishing by itself", touched != null && touched.TimeLeft < lifeBefore - 0.4f && touched.TimeLeft <= cfg.boosterTubeLifetime,
                    touched != null ? touched.TimeLeft.ToString("F1") + " s left" : "gone");
            }
            ParkHands();
            BoosterTube.AnyBoosted -= onBoost;
            BoosterTube.AnyWallHit -= onWall;
            yield return AbortToMenu();
        }

        /// <summary>The level spawns its own tubes; tests that count tube events need only theirs.</summary>
        static void ClearTubesExcept(BoosterTube keep)
        {
            foreach (var t in BoosterTube.Active.ToArray())
                if (t != null && t != keep) DestroyImmediate(t.gameObject);
        }

        IEnumerator Islands()
        {
            Note("Floating islands: Infinite from wave 4, off the path, shatter, shards, dust");
            var inf = _gm.infinite;
            var probe = ScriptableObject.CreateInstance<LevelConfig>();
            inf.Apply(probe, inf.islandsFromWave - 1);
            int beforeCount = probe.islandCount;
            inf.Apply(probe, inf.islandsFromWave);
            Check("islands first appear in Infinite wave 4", inf.islandsFromWave == 4 && beforeCount == 0 && probe.islandCount > 0, probe.islandCount.ToString());
            inf.Apply(probe, 40);
            Check("island numbers respect the cap", probe.islandCount <= inf.capIslands, probe.islandCount.ToString());
            Destroy(probe);
            Check("tutorial levels have no islands", _gm.levels.TrueForAll(l => l.islandCount == 0));

            yield return StartAndWaitForFlight(0);
            var plane = _gm.Plane;
            var cfg = Instantiate(_gm.CurrentConfig);
            cfg.islandCount = 6;
            _gm.Islands.Build(cfg, plane.Path, _head.position, Vector3.forward, 11);
            yield return null;
            int count = FloatingIsland.Active.Count;
            Check("the islands find room around the player", count >= 5, count + " of 6");
            float smallest = float.PositiveInfinity;
            foreach (var isl in FloatingIsland.Active) smallest = Mathf.Min(smallest, isl.Size);
            Check("islands are big", smallest >= 0.12f, (smallest * 100f).ToString("F0") + " cm across at the smallest");
            float worst = float.PositiveInfinity;
            foreach (var isl in FloatingIsland.Active)
                worst = Mathf.Min(worst, IslandField.DistanceToPath(plane.Path, isl.Position) - isl.Size * 0.6f);
            Check("no island sits on the flight path", worst >= cfg.islandPathClearance - 1e-3f, worst.ToString("F3") + " m clear");

            // The plane flying into one.
            var first = FloatingIsland.Active[0];
            int hits = plane.HitsTaken;
            first.MoveTo(plane.Path.PositionAt(plane.Distance + plane.Speed * 0.4f) + Vector3.down * first.Size * 0.15f);
            yield return WaitFor(() => first == null || plane.HitsTaken > hits || !plane.IsFlying, 2f);
            yield return null;
            Check("flying into an island hurts the plane", plane.HitsTaken > hits || plane.CurrentState == MiniPlane.State.Crashed, plane.HitsTaken + " hits");
            Check("the island shatters", first == null);
            IslandDebris.DestroyAll();
            yield return null;

            // A hand touching one.
            int shattered = 0;
            Action<FloatingIsland> onShatter = i => shattered++;
            FloatingIsland.AnyShattered += onShatter;
            var second = FloatingIsland.Active.Count > 0 ? FloatingIsland.Active[0] : null;
            if (second != null) _left.PosePalm(second.Position);
            yield return null;
            yield return null;
            Check("a hand touching an island shatters it", shattered == 1 && second == null);
            int shards = IslandDebris.Active.Count;
            Check("it breaks into several shards", shards >= 4, shards.ToString());
            float fastest = 0f;
            foreach (var d in IslandDebris.Active) fastest = Mathf.Max(fastest, d.Velocity.magnitude);
            Check("the shards drift slowly", fastest > 0f && fastest < 0.12f, fastest.ToString("F3") + " m/s");
            ParkHands();
            yield return WaitSeconds(0.5f);

            // A hand dusting a shard.
            int dusted = 0;
            Action<IslandDebris> onDust = d => dusted++;
            IslandDebris.AnyDusted += onDust;
            var shard = IslandDebris.Active.Count > 0 ? IslandDebris.Active[0] : null;
            if (shard != null) _left.PosePalm(shard.Position);
            yield return null;
            yield return null;
            Check("a hand turns a shard to dust", dusted >= 1 && shard == null, dusted + " dusted");
            ParkHands();

            // A shard drifting into the plane.
            yield return WaitFor(() => plane.IsFlying, 6f);
            yield return WaitSeconds(plane.respawnGraceSeconds + 0.2f);
            var rock = IslandDebris.Active.Count > 0 ? IslandDebris.Active[0] : null;
            hits = plane.HitsTaken;
            if (rock != null)
            {
                rock.SetVelocity(Vector3.zero);
                rock.transform.position = plane.Path.PositionAt(plane.Distance + plane.Speed * 0.3f);
            }
            yield return WaitFor(() => rock == null || plane.HitsTaken > hits || !plane.IsFlying, 2f);
            Check("a shard hitting the plane hurts it", plane.HitsTaken > hits || plane.CurrentState == MiniPlane.State.Crashed, plane.HitsTaken + " hits");
            FloatingIsland.AnyShattered -= onShatter;
            IslandDebris.AnyDusted -= onDust;
            Destroy(cfg);
            yield return AbortToMenu();
            Check("islands clear away with the level", FloatingIsland.Active.Count == 0 && IslandDebris.Active.Count == 0);
        }

        IEnumerator ZeppelinBoss()
        {
            Note("Zeppelin: level 7, bullet trails, fighters, palm-held missiles, Infinite boss waves");
            Check("there are seven tutorial levels", _gm.levels.Count == 7, _gm.levels.Count.ToString());
            var z7 = _gm.levels[6];
            Check("level 7 is the zeppelin level, with missiles", z7.bossLevel > 0 && z7.bossMissiles, z7.levelName);
            Check("level 7 opens once level 6 is passed", Progression.IsLevelUnlocked(6));
            Check("Infinite doesn't wait for level 7", Progression.InfiniteUnlocked && !Progression.HasPassed(6));

            var inf = _gm.infinite;
            Check("every 5th wave is a boss wave", inf.BossNumber(5) == 1 && inf.BossNumber(10) == 2 && inf.BossNumber(4) == 0 && inf.BossNumber(6) == 0);
            var w = ScriptableObject.CreateInstance<LevelConfig>();
            inf.Apply(w, 5);
            bool firstNoMissiles = w.bossLevel == 1 && !w.bossMissiles && w.maxFighters == 0 && w.maxBombers == 0;
            int trails5 = w.bossTrails, batch5 = w.bossFighterBatch;
            inf.Apply(w, 10);
            bool secondMissiles = w.bossMissiles;
            inf.Apply(w, 20);
            Check("the first boss wave has bullets and fighters but no missiles", firstNoMissiles);
            Check("the following boss waves add missiles", secondMissiles && w.bossMissiles);
            Check("each boss wave is tougher", w.bossTrails > trails5 && w.bossFighterBatch > batch5 && w.bossMissileCount > 1,
                "trails " + trails5 + " to " + w.bossTrails + ", fighters " + batch5 + " to " + w.bossFighterBatch + ", missiles " + w.bossMissileCount);
            inf.Apply(w, 6);
            Check("normal waves have no zeppelin", w.bossLevel == 0);
            Destroy(w);

            yield return StartAndWaitForFlight(6);
            var plane = _gm.Plane;
            var cfg = _gm.CurrentConfig;
            var z = Zeppelin.Current;
            Check("the zeppelin appears", z != null);
            if (z == null)
            {
                yield return AbortToMenu();
                yield break;
            }
            yield return WaitSeconds(z.arriveSeconds);
            Check("it hangs in the distance", Vector3.Distance(z.Position, _head.position) > 1.5f, Vector3.Distance(z.Position, _head.position).ToString("F2") + " m");
            Check("it can't be flicked or shot down", z.GetComponent<EnemyPlane>() == null);

            // Parked test hands can't block, so the accurate trails would shoot the plane down for good.
            plane.DebugInvulnerable = true;
            yield return WaitFor(() => z.RoundsFired > 0, 8f);
            Check("it fires bullet trails", z.RoundsFired > 0 && z.BarragesFired > 0);
            // Each round's course, read from two frames, against where the plane's path takes it.
            var firstSeen = new System.Collections.Generic.Dictionary<Projectile, Vector3>();
            var miss = new System.Collections.Generic.Dictionary<Projectile, float>();
            float watched = 0f;
            while (watched < 3f)
            {
                if (plane.IsFlying)
                    foreach (var p in Projectile.Active)
                    {
                        if (p == null || miss.ContainsKey(p)) continue;
                        if (!firstSeen.TryGetValue(p, out var seen))
                        {
                            firstSeen[p] = p.Position;
                            continue;
                        }
                        Vector3 v = (p.Position - seen) / Mathf.Max(Time.deltaTime, 1e-4f);
                        float best = float.PositiveInfinity;
                        for (float t = 0f; t < 4f; t += 0.01f)
                            best = Mathf.Min(best, Vector3.Distance(p.Position + v * t, plane.Path.PositionAt(plane.Distance + plane.Speed * t)));
                        miss[p] = best;
                    }
                watched += Time.deltaTime;
                yield return null;
            }
            int near = 0;
            foreach (var d in miss.Values) if (d < 0.1f) near++;
            Check("most of the zeppelin's rounds are on course for the plane", miss.Count >= 6 && near >= miss.Count * 0.6f,
                near + " of " + miss.Count + " pass within 10 cm");
            yield return WaitSeconds(1f);
            Check("its trails are streams of rounds", Projectile.Active.Count >= 3, Projectile.Active.Count.ToString());
            var round = Projectile.Active.Count > 0 ? Projectile.Active[0] : null;
            if (round != null) _left.PosePalm(round.Position);
            yield return null;
            yield return null;
            Check("a hand blocks the zeppelin's bullets", round == null);
            ParkHands();

            float azimuthBefore = z.Azimuth;
            int movesBefore = z.Moves;
            yield return WaitFor(() => z.FighterBatches > 0, 25f);
            Check("the zeppelin moves round the path before its next attack", z.Moves > movesBefore && Mathf.Abs(z.Azimuth - azimuthBefore) >= z.minMoveDeg - 0.5f,
                azimuthBefore.ToString("F0") + " to " + z.Azimuth.ToString("F0") + " degrees");
            Check("it stays in front of the player", Mathf.Abs(z.Azimuth) <= z.maxAzimuthDeg + 0.01f);
            yield return WaitSeconds(cfg.bossFighterBatch * 0.5f + 0.3f);
            Check("it launches a batch of fighters from its decks", z.FighterBatches > 0 && EnemyPlane.Active.Count >= cfg.bossFighterBatch,
                EnemyPlane.Active.Count + " fighters");
            foreach (var e in EnemyPlane.Active.ToArray())
                if (e != null && e.IsAlive) e.Flick(Vector3.up);

            // A missile held in the palm drops.
            yield return WaitFor(() => plane.IsFlying, 6f);
            int score = _gm.Score;
            var m = z.LaunchMissile();
            yield return WaitSeconds(0.5f);
            Vector3 toPlane = (plane.Position - m.Position).normalized;
            Check("a missile homes on the plane", Vector3.Dot(m.Forward, toPlane) > 0.5f, Vector3.Dot(m.Forward, toPlane).ToString("F2"));
            HandMotion.Reset(_right);
            Vector3 heldAt = m.Position;
            for (int i = 0; i < 10; i++)
            {
                _right.PosePalm(heldAt);
                yield return null;
            }
            Check("a palm holds a missile still", m != null && m.IsHeld && Vector3.Distance(m.Position, heldAt) < 0.005f);
            float held = 0f;
            while (m != null && m.IsAlive && held < 2.5f)
            {
                _right.PosePalm(heldAt);
                held += Time.deltaTime;
                yield return null;
            }
            Check("holding it about 1.5 s drops it", m != null && m.CurrentState == BossMissile.State.Falling && held > 1.2f && held < 1.7f,
                held.ToString("F2") + " s");
            Check("a dropped missile scores", _gm.Score == score + _gm.missileDefusePoints, "score " + _gm.Score + " from " + score);
            ParkHands();
            yield return WaitSeconds(0.2f);

            // A smacked missile goes off; far from the plane that's harmless.
            int blasts = 0;
            Action<BossMissile> onBlast = b => blasts++;
            BossMissile.AnyExploded += onBlast;
            var m2 = z.LaunchMissile();
            yield return WaitSeconds(0.4f);
            int hits = plane.HitsTaken;
            bool flyingBefore = plane.IsFlying;
            HandMotion.Reset(_right);
            _right.PosePalm(m2.Position + Vector3.right * 0.2f);
            yield return null;
            for (int i = 0; i < 6 && m2 != null && m2.IsAlive; i++)
            {
                _right.PosePalm(m2.Position + Vector3.right * (0.12f - 0.04f * i));
                yield return null;
            }
            yield return null;
            Check("smacking a missile sets it off", blasts == 1 && m2 == null, "blasts " + blasts);
            Check("a blast far from the plane does no harm", !flyingBefore || (plane.HitsTaken == hits && plane.IsFlying));
            ParkHands();

            // A missile reaching the plane.
            plane.DebugInvulnerable = false;
            yield return WaitFor(() => plane.IsFlying, 6f);
            // Keep the zeppelin's rounds out of it, so a stray hit's grace doesn't swallow the blast.
            float clear = 0f;
            while (clear < Mathf.Max(plane.hitGraceSeconds, plane.respawnGraceSeconds) + 0.2f)
            {
                Projectile.DestroyAll();
                clear += Time.deltaTime;
                yield return null;
            }
            hits = plane.HitsTaken;
            int hurt = 0;
            Action<MiniPlane> onShield = pl => hurt++;
            Action<MiniPlane, string> onHurt = (pl, r) => hurt++;
            plane.ShieldLost += onShield;
            plane.Hit += onHurt;
            plane.Crashed += onHurt;
            var m3 = z.LaunchMissile();
            Vector3 ahead = plane.Path.PositionAt(plane.Distance + 0.12f);
            m3.SetCourse(ahead, plane.Position - ahead);
            float flown = 0f;
            while (m3 != null && hurt == 0 && plane.IsFlying && flown < 3f)
            {
                Projectile.DestroyAll();
                flown += Time.deltaTime;
                yield return null;
            }
            Check("a missile reaching the plane hurts it", hurt > 0 || plane.HitsTaken > hits, plane.HitsTaken + " hits");
            plane.ShieldLost -= onShield;
            plane.Hit -= onHurt;
            plane.Crashed -= onHurt;
            BossMissile.AnyExploded -= onBlast;
            yield return AbortToMenu();
            Check("the zeppelin leaves with the level", Zeppelin.Current == null && BossMissile.Active.Count == 0);
            plane.DebugInvulnerable = false;
        }

        IEnumerator InfinitePowerups()
        {
            Note("Infinite: fuel from wave 1, fewer powerups, rare turret balloons, at most 3 tubes a wave");
            var inf = _gm.infinite;
            var w = ScriptableObject.CreateInstance<LevelConfig>();
            inf.Apply(w, 1);
            Check("fuel is on from wave 1", w.useFuel);
            int turretWaves = 0, shieldWaves = 0;
            bool tubesOk = true, earlyOk = true, shieldsOk = true;
            for (int wave = 1; wave <= 20; wave++)
            {
                if (wave == 1) continue;
                inf.Apply(w, wave);
                if (w.maxTurretBalloons > 0) turretWaves++;
                if (w.useBalloons && w.maxShieldBalloons != 0) shieldWaves++;
                shieldsOk &= w.maxShieldBalloons >= 0 && w.maxShieldBalloons <= 2 && w.maxTurretBalloons <= 1;
                tubesOk &= w.maxBoosterTubes >= 0 && w.maxBoosterTubes <= 3;
                if (wave <= 5) earlyOk &= w.maxBoosterTubes >= 1 && w.maxBoosterTubes <= 2;
            }
            Check("balloons are capped per wave", shieldsOk);
            Check("the turret balloon is rarer than the shield", turretWaves > 0 && turretWaves * 2 <= shieldWaves, turretWaves + " turret waves, " + shieldWaves + " shield waves");
            Check("at most 3 tubes a wave", tubesOk);
            Check("1 or 2 tubes in waves 2 to 5", earlyOk);
            inf.Apply(w, 12);
            Check("at most 2 tubes a wave and at once", w.maxBoosterTubes <= 2 && w.maxTubesAtOnce == 2, w.maxBoosterTubes + " a wave");
            inf.Apply(w, 1);
            Check("no tubes in wave 1", w.maxBoosterTubes == 0 && w.boosterTubeInterval == 0f);
            inf.Apply(w, 1);
            float r1 = w.randomness, turn1 = w.turnRadius, speed1 = w.planeSpeed;
            Check("wave 1 flies a simple path", r1 <= 0.12f && turn1 >= 0.14f && !w.allowLoops,
                "randomness " + r1.ToString("F2") + ", turn " + turn1.ToString("F3"));
            inf.Apply(w, 5);
            float r5 = w.randomness;
            float oldR3 = Mathf.Lerp(inf.startRandomness, inf.capRandomness, inf.Ramp(3));
            Check("wave 5's path is as twisty as wave 3's used to be", Mathf.Abs(inf.PathRamp(5) - inf.Ramp(3)) < 1e-4f,
                "path ramp " + inf.PathRamp(5).ToString("F3") + " vs " + inf.Ramp(3).ToString("F3"));
            Check("the path gets harder wave by wave", r5 > r1 && w.planeSpeed > speed1);

            // The spawner keeps to the caps however quickly its timers run.
            inf.Apply(w, 6);
            w.boosterTubeInterval = 0.4f;
            w.shieldBalloonInterval = 0.4f;
            w.turretBalloonInterval = 0.4f;
            w.maxTurretBalloons = 1;
            w.maxFighters = 0;
            w.maxBombers = 0;
            w.SetBoss(0, false);
            _gm.spawner.Begin(w, _gm.Plane, Vector3.forward, 5);
            yield return WaitSeconds(4f);
            int tubes = BoosterTube.Active.Count;
            int shields = PowerBalloon.Active.FindAll(b => b != null && b.Kind == BalloonKind.Shield).Count;
            int turrets = PowerBalloon.Active.FindAll(b => b != null && b.Kind == BalloonKind.Turret).Count;
            Check("the spawner stops at the tube cap", tubes == w.maxBoosterTubes, tubes + " of " + w.maxBoosterTubes);
            Check("the spawner stops at the balloon caps", shields == w.maxShieldBalloons && turrets == 1, shields + " shields, " + turrets + " turrets");
            _gm.spawner.End();
            Destroy(w);
            yield return null;
        }

        IEnumerator Sounds()
        {
            Note("Sound effects: every sound has a clip, and game events play them");
            var gs = _gm.sounds;
            Check("the game has a sound list", gs != null);
            if (gs == null) yield break;
            bool all = true;
            string missing = "";
            foreach (SoundKind k in Enum.GetValues(typeof(SoundKind)))
            {
                var e = gs.Get(k);
                if (e == null || e.clip == null)
                {
                    all = false;
                    missing += k + " ";
                }
            }
            Check("every sound has a clip", all, missing);
            Check("the sound player is running", Sfx.Instance != null);
            Sfx.Counts.Clear();
            yield return StartAndWaitForFlight(0);
            yield return WaitSeconds(0.5f);
            var engine = _gm.Plane.GetComponentInChildren<AudioSource>();
            Check("the plane's engine hums while it flies", engine != null && engine.isPlaying && engine.loop);
            yield return FlyThroughRings(1, 10f);
            Check("a ring pass plays its chime", Sfx.Counts.ContainsKey(SoundKind.RingPass));
            _gm.Pause();
            yield return Frames(3);
            Check("pausing plays a sound and pauses the world's sounds", Sfx.Counts.ContainsKey(SoundKind.Pause) && AudioListener.pause);
            _gm.Resume();
            yield return Frames(3);
            Check("resuming plays a sound and lets the world's sounds go on", Sfx.Counts.ContainsKey(SoundKind.Unpause) && !AudioListener.pause);
            _gm.Plane.Crash("test crash");
            yield return null;
            Check("a crash plays its sound", Sfx.Counts.ContainsKey(SoundKind.PlaneCrash));
            yield return AbortToMenu();
            Check("the engine stops when the plane is put away", engine == null || !engine.isPlaying);
        }

        IEnumerator IntroIcons()
        {
            Note("Tutorial Start pages show icons in place of keywords");
            for (int i = 3; i < _gm.levels.Count; i++)
            {
                var cfg = _gm.levels[i];
                int tokens = 0;
                foreach (char ch in cfg.introCaption) if (ch == '{') tokens++;
                Check("level " + cfg.levelNumber + "'s text names its enemies or powerups with icons", tokens >= 2, tokens + " icons");
                _gm.menu.ShowIntro(cfg.levelNumber, cfg.levelName, cfg.introCaption, 3);
                yield return null;
                var icons = _gm.menu.introIcons;
                var text = _gm.menu.introCaption;
                Check("level " + cfg.levelNumber + " draws every icon", icons != null && icons.IconCount == tokens && !text.text.Contains("{"),
                    icons != null ? icons.IconCount + " of " + tokens : "none");
                if (icons == null) continue;
                var r = text.rectTransform.rect;
                bool inside = true;
                Vector2 previous = new Vector2(float.NegativeInfinity, float.PositiveInfinity);
                bool ordered = true;
                foreach (var img in icons.Placed)
                {
                    var at = img.rectTransform.anchoredPosition;
                    inside &= at.x >= r.xMin - 5f && at.x <= r.xMax + 5f && at.y >= r.yMin - 30f && at.y <= r.yMax + 30f;
                    // Reading order: left to right on a line, then down.
                    if (at.y > previous.y + 5f || (Mathf.Abs(at.y - previous.y) <= 5f && at.x <= previous.x)) ordered = false;
                    previous = at;
                }
                Check("level " + cfg.levelNumber + "'s icons sit in the text, in reading order", inside && ordered);
            }
            _gm.ShowMenu();
            yield return WaitFor(() => _gm.menu.IsStartPage, 5f);
        }

        IEnumerator HeartBalloon()
        {
            Note("Heart balloon: small, gives a life back, does nothing at full lives");
            var inf = _gm.infinite;
            var w = ScriptableObject.CreateInstance<LevelConfig>();
            int lifeWaves = 0, turretWaves = 0;
            for (int wave = 1; wave <= 12; wave++)
            {
                inf.Apply(w, wave);
                if (w.maxLifeBalloons > 0) lifeWaves++;
                if (w.maxTurretBalloons > 0) turretWaves++;
            }
            inf.Apply(w, 1);
            Check("Infinite offers heart balloons some waves, never wave 1", lifeWaves > 0 && w.maxLifeBalloons == 0, lifeWaves + " of 12 waves");
            Destroy(w);
            Check("level 5 offers a heart balloon", _gm.levels[4].lifeBalloonInterval > 0f && _gm.levels[4].maxLifeBalloons == 1);

            Progression.MarkPassed(3);
            yield return StartAndWaitForFlight(4);
            var plane = _gm.Plane;
            plane.AddFuel(100f);
            var heart = _gm.spawner.SpawnBalloon(BalloonKind.Life);
            var shield = _gm.spawner.SpawnBalloon(BalloonKind.Shield);
            yield return null;
            Check("the heart balloon is smaller than the others", heart.Size < shield.Size * 0.8f && heart.CatchRadius < shield.CatchRadius * 0.8f,
                (heart.Size * 100f).ToString("F1") + " vs " + (shield.Size * 100f).ToString("F1") + " cm");
            Check("the heart balloon carries a heart icon", heart.GetComponentInChildren<Billboard>() != null);
            Destroy(shield.gameObject);

            _gm.DebugSetLives(2);
            heart.MoveTo(plane.Path.PositionAt(plane.Distance + plane.Speed * 0.1f));
            yield return WaitFor(() => heart == null, 1f);
            Check("flying through it gives a life back", heart == null && _gm.Lives == 3 && _gm.hud.LivesShown == 3, _gm.Lives + " lives");

            var spare = _gm.spawner.SpawnBalloon(BalloonKind.Life);
            yield return null;
            spare.MoveTo(plane.Path.PositionAt(plane.Distance + plane.Speed * 0.1f));
            yield return WaitFor(() => spare == null, 1f);
            Check("at full lives it is used up and does nothing", spare == null && _gm.Lives == 3, _gm.Lives + " lives");
            yield return AbortToMenu();
        }

        IEnumerator LoseLastLife()
        {
            yield return WaitFor(() => _gm.Plane.IsFlying, 6f);
            _gm.DebugSetLives(1);
            _gm.Plane.Crash("test crash");
            yield return WaitFor(() => _gm.OfferingRespawn, 15f);
        }

        IEnumerator InfiniteRespawn()
        {
            Note("Infinite: buy a respawn on the game-over page; each one costs more");
            var inf = _gm.infinite;
            Progression.MarkPassed(5);
            Progression.AddCoins(-Progression.Coins);
            _gm.StartInfinite();
            yield return null;
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Flying, 10f);
            yield return FlyThroughRings(1, 10f);
            int score = _gm.Score;
            int wave = _gm.Wave;
            yield return LoseLastLife();
            Check("game over offers a respawn", _gm.OfferingRespawn && _gm.menu.respawnButton != null && _gm.menu.respawnButton.gameObject.activeInHierarchy);
            Check("the first respawn costs the base price", _gm.RespawnCost == inf.respawnBaseCost && inf.respawnBaseCost >= 25, _gm.RespawnCost + " coins");
            Check("respawns cost more the further the run got", inf.RespawnCost(0, 10) > inf.RespawnCost(0, 5) && inf.RespawnCost(0, 5) > inf.RespawnCost(0, 1),
                "wave 1: " + inf.RespawnCost(0, 1) + ", wave 5: " + inf.RespawnCost(0, 5) + ", wave 10: " + inf.RespawnCost(0, 10));
            Check("the game-over page shows the coins you have", _gm.menu.resultWallet != null && _gm.menu.resultWallet.text == Progression.Coins.ToString(),
                _gm.menu.resultWallet != null ? _gm.menu.resultWallet.text : "none");
            Check("the button shows the price", _gm.menu.respawnLabel != null && _gm.menu.respawnLabel.text.Contains(_gm.RespawnCost.ToString()));
            Check("it greys out without the coins", !_gm.menu.IsButtonInteractable("respawn") && Progression.Coins < _gm.RespawnCost,
                Progression.Coins + " coins");
            _gm.menu.Press("respawn");
            yield return Frames(3);
            Check("pressing it without the coins does nothing", _gm.OfferingRespawn && _gm.RespawnsBought == 0);

            // Come back with coins: the page refreshes when shown again, so die again after buying in.
            Progression.AddCoins(1000);
            int coinsBefore = Progression.Coins;
            _gm.BuyRespawn();
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Flying, 10f);
            Check("a respawn spends its price", Progression.Coins == coinsBefore - inf.respawnBaseCost, Progression.Coins + " from " + coinsBefore);
            Check("the run carries on from the same wave with the score kept", _gm.Wave == wave && _gm.Score == score && _gm.InInfinite,
                "wave " + _gm.Wave + " score " + _gm.Score);
            Check("a respawn gives back its lives", _gm.Lives == inf.respawnLives, _gm.Lives + " lives");

            yield return LoseLastLife();
            Check("the next respawn costs more", _gm.RespawnCost == inf.respawnBaseCost + inf.respawnCostStep && inf.respawnCostStep > 0, _gm.RespawnCost + " coins");
            Check("it is affordable now", _gm.menu.IsButtonInteractable("respawn"));
            int before = Progression.Coins;
            _gm.menu.Press("respawn");
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Flying, 10f);
            Check("buying it again spends the higher price", Progression.Coins == before - (inf.respawnBaseCost + inf.respawnCostStep), Progression.Coins + " from " + before);

            yield return LoseLastLife();
            Check("the price keeps rising", _gm.RespawnCost == inf.respawnBaseCost + 2 * inf.respawnCostStep, _gm.RespawnCost + " coins");
            _gm.menu.Press("menu");
            yield return WaitFor(() => _gm.menu.IsStartPage, 5f);
            Check("Menu leaves the run without buying", _gm.menu.IsStartPage && !_gm.OfferingRespawn && _gm.RespawnsBought == 2);

            _gm.StartInfinite();
            yield return null;
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Flying, 10f);
            Check("a new run starts at the base price again", _gm.RespawnsBought == 0 && _gm.RespawnCost == inf.respawnBaseCost);
            yield return AbortToMenu();
        }

        IEnumerator Splash()
        {
            Note("Opening: the logo fades in, Start appears 1.5 s later, Start fades into the menu");
            var splash = _gm.splash;
            Check("the game has a splash screen", splash != null);
            if (splash == null) yield break;
            // The scene booted before the test rig: start the opening over with the fake head in place.
            _gm.ShowMenu();
            yield return null;
            var start = typeof(GameManager).GetMethod("SplashRoutine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var flow = typeof(GameManager).GetMethod("Switch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            flow.Invoke(_gm, new object[] { start.Invoke(_gm, null) });
            yield return WaitFor(() => splash.IsOpen, 4f);
            Check("the splash opens when the game starts", splash.IsOpen && !_gm.menu.IsOpen);
            yield return null;
            yield return null;
            Check("the logo starts clear and fades in", splash.logo.alpha > 0f && splash.logo.alpha < 0.5f, splash.logo.alpha.ToString("F2"));
            Check("the Start button waits", splash.startButton.alpha == 0f && !splash.ButtonShown);
            var logoImage = splash.logo.GetComponentInChildren<UnityEngine.UI.Image>();
            Check("the logo is the SkyLooper art", logoImage != null && logoImage.sprite != null && logoImage.sprite.name == "SkyLooperLogo",
                logoImage != null && logoImage.sprite != null ? logoImage.sprite.name : "none");
            Check("the splash follows the head lazily", splash.follow != null && splash.follow.enabled);

            splash.PressStart();
            yield return null;
            Check("Start can't be pressed before it shows", splash.IsOpen && !splash.Leaving);

            yield return WaitFor(() => splash.ButtonShown, 6f);
            float gap = splash.ButtonShownAt - splash.LogoShownAt;
            Check("Start appears 1.5 s after the logo is fully shown", splash.LogoShown && splash.ButtonShown && gap >= splash.buttonDelaySeconds - 0.05f,
                gap.ToString("F2") + " s after");
            Check("the Start button is rounded glass", UiGlass.IsGlass(splash.startButton.GetComponentInChildren<UnityEngine.UI.Image>()));

            var pos = splash.ButtonPosition();
            Check("the Start button can be found", pos.HasValue);
            if (pos.HasValue) yield return Poke(pos.Value);
            Check("poking Start fades the splash away", splash.Leaving || !splash.IsOpen);
            yield return WaitFor(() => !splash.IsOpen, 3f);
            yield return null;
            Check("then the start menu opens, fading in", _gm.menu.IsStartPage && _gm.menu.Opacity < 1f, _gm.menu.Opacity.ToString("F2"));
            yield return WaitFor(() => _gm.menu.Opacity >= 1f, 2f);
            Check("the menu ends fully shown", _gm.menu.Opacity >= 1f);
        }

        /// <summary>Lets a sound play again at once: the autopilot runs faster than the real-time repeat gap.</summary>
        static void ForgetSound(SoundKind kind)
        {
            var field = typeof(Sfx).GetField("_lastPlayed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (Sfx.Instance != null && field != null && field.GetValue(Sfx.Instance) is System.Collections.Generic.Dictionary<SoundKind, float> last) last.Remove(kind);
        }

        IEnumerator JammingTowers()
        {
            Note("Jamming towers: on the islands from their second wave, silence enemy warnings until smashed");
            var inf = _gm.infinite;
            int first = inf.islandsFromWave;
            Check("no towers the first time islands appear", inf.TowersForWave(first) == 0);
            Check("one tower the second time", inf.TowersForWave(first + 1) == 1);
            bool rising = true;
            int most = 0;
            for (int wave = first + 1; wave < first + 30; wave++)
            {
                rising &= inf.TowersForWave(wave + 1) >= inf.TowersForWave(wave);
                most = Mathf.Max(most, inf.TowersForWave(wave));
            }
            Check("towers creep up slowly to a cap", rising && most > 1 && most <= inf.capTowers, "up to " + most);

            yield return StartAndWaitForFlight(0);
            var plane = _gm.Plane;
            var cfg = Instantiate(_gm.CurrentConfig);
            cfg.islandCount = 6;
            cfg.jammingTowers = 2;
            _gm.Islands.Build(cfg, plane.Path, _head.position, Vector3.forward, 21);
            yield return null;
            int towers = JammingTower.Active.Count;
            Check("towers stand on some islands, one fewer or the full number", towers >= 1 && towers <= 2, towers + " towers");
            var tower = JammingTower.Active.Count > 0 ? JammingTower.Active[0] : null;
            Check("each tower stands on an island and shows the jamming icon", tower != null && tower.Island != null && tower.Island.Tower == tower &&
                tower.GetComponentInChildren<Billboard>() != null && tower.GetComponentInChildren<Billboard>().name == "JamIcon");

            // Jammed: a fighter into a clear sky makes no warning.
            ForgetSound(SoundKind.EnemyWarning);
            Sfx.Counts.TryGetValue(SoundKind.EnemyWarning, out int warnings);
            var fighter = _gm.spawner.SpawnFighter();
            yield return null;
            Sfx.Counts.TryGetValue(SoundKind.EnemyWarning, out int after);
            Check("a tower silences the enemy warning", after == warnings, (after - warnings) + " warnings");
            Destroy(fighter.gameObject);
            yield return null;

            // Smash every island with a tower: the towers go, and so does the jamming.
            int destroyed = 0;
            Action<JammingTower> onGone = t => destroyed++;
            JammingTower.AnyDestroyed += onGone;
            foreach (var t in JammingTower.Active.ToArray())
            {
                if (t == null) continue;
                _left.PosePalm(t.Top - Vector3.up * t.Height * 0.3f);
                yield return null;
                yield return null;
                ParkHands();
                yield return null;
            }
            JammingTower.AnyDestroyed -= onGone;
            Check("touching a tower shatters its island and the tower with it", destroyed == towers && !JammingTower.AnyActive, destroyed + " of " + towers);
            ForgetSound(SoundKind.EnemyWarning);
            Sfx.Counts.TryGetValue(SoundKind.EnemyWarning, out warnings);
            fighter = _gm.spawner.SpawnFighter();
            yield return null;
            Sfx.Counts.TryGetValue(SoundKind.EnemyWarning, out after);
            Check("with the towers gone the warning sounds again", after == warnings + 1, (after - warnings) + " warnings");
            Destroy(cfg);
            yield return AbortToMenu();
        }
    }
}
#endif
