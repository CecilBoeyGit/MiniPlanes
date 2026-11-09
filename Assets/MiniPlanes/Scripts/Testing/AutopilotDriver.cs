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
            yield return TryStep(Hangar());
            yield return TryStep(Level4());
            yield return TryStep(Level5());
            yield return TryStep(Level6());
            yield return TryStep(Turret());
            yield return TryStep(InfiniteMode());
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
            for (int i = 1; i < 3; i++)
            {
                if (_gm.catalog == null || _gm.catalog.Count <= i) break;
                Progression.GrantPlane(_gm.catalog.Get(i).id);
                _gm.menu.Press("plane" + i);
                yield return WaitSeconds(0.3f);
                Shoot("hangar_plane" + (i + 1));
            }
            _gm.menu.Close();
            _gm.menu.Close();

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
            for (int i = 1; i < 6; i++) othersLocked &= !_gm.menu.IsButtonInteractable("level" + i);
            Check("levels 2 to 6 are locked", othersLocked);
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
            Check("the start panel shows the coins", _gm.menu.startCoins != null && _gm.menu.startCoins.text == Progression.Coins.ToString(),
                _gm.menu.startCoins != null ? _gm.menu.startCoins.text : "none");
        }

        IEnumerator Level1()
        {
            Note("Level 1: visible path, slow flight, C ring scores 2");
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
            Check("the line only draws the path still ahead of the plane",
                _gm.Plane.Distance > 0.1f && _gm.revealer.VisibleFromDistance >= _gm.Plane.Distance - 0.02f && _gm.revealer.VisibleFromDistance < _gm.Plane.Distance + 0.1f,
                "plane at " + _gm.Plane.Distance.ToString("F2") + ", line from " + _gm.revealer.VisibleFromDistance.ToString("F2"));
            Check("level 2's line returns for 2.5 s after a streak", Mathf.Abs(_gm.levels[1].streakRevealSeconds - 2.5f) < 0.001f, _gm.levels[1].streakRevealSeconds.ToString("F1"));

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
            Check("the HUD shows the score", _gm.hud.ScoreText == _gm.Score.ToString(), _gm.hud.ScoreText);

            yield return FlyThroughRings(2, 15f);
            Check("two rings were flown", _gm.RingsThisFlight >= 2, "rings " + _gm.RingsThisFlight);
            yield return WaitFor(() => _gm.CurrentFlow == GameManager.Flow.Result, 30f);
            Check("level 1 ends with a result panel", _gm.CurrentFlow == GameManager.Flow.Result);
            Check("level 1 passes", _gm.LastFlightPassed, "plane " + _gm.Plane.LastCrashReason);
            Check("passing level 1 unlocks level 2", Progression.IsLevelUnlocked(1) && !Progression.IsLevelUnlocked(2));
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
            Check("a C ring earns 2 coins", Progression.Coins == coins + _gm.singleRingCoins, Progression.Coins + " from " + coins);
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
            Check("the Hangar has three planes", catalog != null && catalog.Count == 3, catalog != null ? catalog.Count.ToString() : "none");
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
            var fighter = _gm.spawner.SpawnFighter();
            fighter.transform.position = plane.Position + plane.Forward * 0.25f + Vector3.up * 0.05f;
            yield return WaitFor(() => fighter == null || !fighter.IsAlive, 6f);
            Check("the turret shoots a fighter down", fighter == null || !fighter.IsAlive, "shots " + (plane.Turret != null ? plane.Turret.ShotsFired : 0));
            Check("a turret kill scores and pays a coin", _gm.Score > score && Progression.Coins > coins);

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
            yield return WaitSeconds(cfg.streakRevealSeconds + 0.1f);
            Check("the line fades again after its reveal", !_gm.revealer.IsRevealing);

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

        IEnumerator Level6()
        {
            Note("Level 6: bomber, balloon chain, flash, touch, cover");
            Progression.MarkPassed(4);
            yield return StartAndWaitForFlight(5);
            var plane = _gm.Plane;
            var cfg = _gm.levels[5];
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
            yield return null;
            yield return null;
            Check("the plane close by arms the balloon", covered.CurrentState == BombBalloon.State.Armed, covered.CurrentState.ToString());
            Vector3 c = covered.Position;
            _left.PosePalm(c - Vector3.right * 0.08f);
            _right.PosePalm(c + Vector3.right * 0.08f);
            Check("cupping reads as covering", covered.BothHandsCovering());
            yield return WaitSeconds(cfg.bombCoverHoldSeconds + 0.05f);
            Check("both hands contain the blast", covered == null || covered.WasContained);
            Check("containing a bomb earns 2 coins", Progression.Coins >= coinsBeforeCover + _gm.bombCoverCoins, Progression.Coins + " from " + coinsBeforeCover);
            Check("a contained blast leaves the plane flying", plane.IsFlying && plane.HitsTaken == 0);
            ParkHands();
            yield return null;

            yield return AbortToMenu();

            // A balloon just off the upcoming path, left uncovered, in a fresh flight before any enemy
            // spawns: the plane must arm it naturally and still be in range when the fuse runs out.
            yield return StartAndWaitForFlight(5);
            plane = _gm.Plane;
            // Far enough ahead that the plane arms it a full fuse before passing.
            float along = plane.Distance + plane.Speed * (cfg.bombFuseSeconds + 0.6f);
            Vector3 side = Vector3.Cross(plane.Path.TangentAt(along), Vector3.up).normalized;
            var uncovered = MakeBomb(plane.Path.PositionAt(along) + side * cfg.bombPathOffset);
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
            Check("downing an enemy earns a coin", Progression.Coins == coins + _gm.enemyCoins, Progression.Coins + " from " + coins);
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
            yield return FlyThroughRings(1, 10f);
            int score = _gm.Score;
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
    }
}
#endif
