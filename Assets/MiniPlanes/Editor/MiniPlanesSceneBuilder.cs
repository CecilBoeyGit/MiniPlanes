using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MiniPlanes.Editor
{
    /// <summary>
    /// Assembles the playable scene from code: Meta camera rig with passthrough, tracked hands with
    /// ring, rock, flick and pinch behaviours, the game manager, HUD, menu, and all art references.
    /// Level assets keep your edits; bump LevelConfig.CurrentDefaultsVersion to reset them.
    /// </summary>
    public static class MiniPlanesSceneBuilder
    {
        public const string ScenePath = "Assets/MiniPlanes/Scenes/MiniPlanes.unity";
        const string MaterialsDir = "Assets/MiniPlanes/Materials";
        const string LevelsDir = "Assets/MiniPlanes/Levels";
        const string ArtDir = "Assets/MiniPlanes/Art";
        const string GameArtPath = "Assets/MiniPlanes/Levels/GameArt.asset";
        const string InfinitePath = "Assets/MiniPlanes/Levels/Infinite.asset";
        const string RigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        const string HandPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab";

        class HandParts
        {
            public MetaHandSource source;
            public HandRing ring;
            public HandHazard hazard;
            public FlickDetector flick;
            public BalloonGrabber grabber;
        }

        [MenuItem("MiniPlanes/2. Build Game Scene")]
        public static void Build()
        {
            EnsureFolder("Assets/MiniPlanes");
            EnsureFolder(MaterialsDir);
            EnsureFolder(LevelsDir);
            EnsureFolder("Assets/MiniPlanes/Scenes");

            ConfigureArtImports();
            var art = CreateGameArt();
            var levels = CreateLevels();
            var catalog = CreateCatalog(art);
            var infinite = CreateInfinite(levels);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLighting();

            CreateRig(out Transform head, out Transform leftAnchor, out Transform rightAnchor);
            var left = CreateHand(leftAnchor, HandSide.Left, art);
            var right = CreateHand(rightAnchor, HandSide.Right, art);
            var handSources = new HandSource[] { left.source, right.source };

            var game = new GameObject("Game");
            var gm = game.AddComponent<GameManager>();

            var mergerGo = new GameObject("RingMerger");
            mergerGo.transform.SetParent(game.transform, false);
            var merger = mergerGo.AddComponent<RingMerger>();
            merger.left = left.ring;
            merger.right = right.ring;
            merger.bubbleMaterial = art.bubbleRing;
            left.hazard.merger = merger;
            right.hazard.merger = merger;

            var pathGo = new GameObject("FlightPathLine");
            pathGo.transform.SetParent(game.transform, false);
            var line = pathGo.AddComponent<LineRenderer>();
            line.sharedMaterial = art.pathLine;
            line.positionCount = 0;
            var revealer = pathGo.AddComponent<PathRevealer>();

            var spawnerGo = new GameObject("EnemySpawner");
            spawnerGo.transform.SetParent(game.transform, false);
            var spawner = spawnerGo.AddComponent<EnemySpawner>();
            spawner.head = head;
            spawner.art = art;
            spawner.hands = handSources;

            // The UI comes from editable prefabs. Existing prefabs are kept as they are.
            var ui = MiniPlanesUiBuilder.EnsureAll(art);
            art.shieldIcon = ui.shieldIcon;
            art.bombIcon = ui.bombIcon;
            art.turretIcon = ui.turretIcon;
            art.crosshair = ui.crosshair;
            art.fuelGauge = ui.fuelGauge;
            EditorUtility.SetDirty(art);

            var hud = Place<CurvedHud>(ui.hud);
            hud.SetHead(head);

            var menu = Place<MenuPanel>(ui.menu);
            menu.art = art;
            menu.catalog = catalog;
            menu.SetHead(head);
            menu.SetHands(handSources);
            Dirty(menu);

            var pause = Place<PausePanel>(ui.pause);
            pause.SetHead(head);
            pause.SetHands(handSources);
            Dirty(pause);

            var wrist = Place<WristMenuButton>(ui.wrist);
            wrist.game = gm;
            wrist.head = head;
            wrist.hands = handSources;
            wrist.rings = new[] { left.ring, right.ring };

            gm.catalog = catalog;
            gm.pausePanel = pause;
            gm.wristMenu = wrist;
            gm.head = head;
            gm.art = art;
            gm.hud = hud;
            gm.menu = menu;
            gm.revealer = revealer;
            gm.spawner = spawner;
            gm.rings = new[] { left.ring, right.ring };
            gm.merger = merger;
            gm.hazards = new[] { left.hazard, right.hazard };
            gm.flickers = new[] { left.flick, right.flick };
            gm.levels = levels;
            gm.infinite = infinite;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[MiniPlanes] Scene built and saved to " + ScenePath);
        }

        /// <summary>Puts a prefab instance in the scene, so later edits to the prefab still flow through.</summary>
        static T Place<T>(GameObject prefab) where T : Component
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            return go.GetComponent<T>();
        }

        /// <summary>Records scene-side values set on a prefab instance and its children.</summary>
        static void Dirty(Component c)
        {
            foreach (var part in c.GetComponentsInChildren<Component>(true))
                if (part != null) PrefabUtility.RecordPrefabInstancePropertyModifications(part);
        }

        const string CatalogPath = "Assets/MiniPlanes/Levels/Planes.asset";

        /// <summary>The Hangar's planes. Created once with starter prices and palettes; your edits are kept.</summary>
        static PlaneCatalog CreateCatalog(GameArt art)
        {
            var cat = AssetDatabase.LoadAssetAtPath<PlaneCatalog>(CatalogPath);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<PlaneCatalog>();
                cat.planes = new[]
                {
                    new PlaneDefinition
                    {
                        id = "skipper", displayName = "Skipper", price = 0,
                        description = "The trusty teal trainer.",
                        turretMount = new Vector3(0f, 0.17f, -0.12f),
                        palettes = new[]
                        {
                            Paint("Original", new Color(0.24f, 0.62f, 0.56f), 0f, 1f, 1f),
                            Paint("Sunset", new Color(0.94f, 0.45f, 0.36f), -0.44f, 1.05f, 1.05f),
                            Paint("Lemon", new Color(0.98f, 0.82f, 0.3f), -0.33f, 1.1f, 1.2f),
                            Paint("Grape", new Color(0.56f, 0.4f, 0.8f), 0.3f, 1f, 1f)
                        }
                    },
                    new PlaneDefinition
                    {
                        id = "biplane", displayName = "Bluebird", price = 500,
                        description = "A stacked-wing classic.",
                        turretMount = new Vector3(0f, 0.17f, 0.02f),
                        palettes = new[]
                        {
                            Paint("Original", new Color(0.3f, 0.55f, 0.92f), 0f, 1f, 1f),
                            Paint("Crimson", new Color(0.86f, 0.3f, 0.3f), 0.4f, 1f, 1f),
                            Paint("Mint", new Color(0.4f, 0.8f, 0.62f), -0.18f, 0.9f, 1.05f),
                            Paint("Plum", new Color(0.66f, 0.36f, 0.72f), 0.22f, 1f, 1f)
                        }
                    },
                    new PlaneDefinition
                    {
                        id = "pusher", displayName = "Pusher Racer", price = 1000,
                        description = "Twin booms, rear propeller, fast lines.",
                        turretMount = new Vector3(0f, 0.09f, 0.33f),
                        palettes = new[]
                        {
                            Paint("Original", new Color(0.6f, 0.86f, 0.62f), 0f, 1f, 1f),
                            Paint("Sky", new Color(0.5f, 0.74f, 0.96f), 0.23f, 1f, 1f),
                            Paint("Peach", new Color(0.98f, 0.66f, 0.5f), -0.3f, 1.1f, 1f),
                            Paint("Lilac", new Color(0.74f, 0.62f, 0.92f), 0.38f, 1f, 1f)
                        }
                    }
                };
                AssetDatabase.CreateAsset(cat, CatalogPath);
            }
            // Fill in models and materials that exist now, without touching ones you have set.
            Fill(cat, "skipper", "PlayerPlane", art.playerMatte);
            Fill(cat, "biplane", "PlayerBiplane", TexturedMatte("BiplaneMatte", "PlayerBiplane"));
            Fill(cat, "pusher", "PlayerPusher", TexturedMatte("PusherMatte", "PlayerPusher"));
            EditorUtility.SetDirty(cat);
            return cat;
        }

        static void Fill(PlaneCatalog cat, string id, string model, Material material)
        {
            foreach (var p in cat.planes)
            {
                if (p.id != id) continue;
                if (p.model == null) p.model = Model(model);
                if (p.material == null) p.material = material;
            }
        }

        static PlanePalette Paint(string name, Color swatch, float hue, float saturation, float brightness)
        {
            return new PlanePalette { name = name, swatch = swatch, hueShift = hue, saturation = saturation, brightness = brightness };
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ------------------------------------------------------------------ art

        static void ConfigureArtImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ArtDir + "/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                bool dirty = importer.materialImportMode != ModelImporterMaterialImportMode.None
                             || importer.importAnimation || importer.importCameras || importer.importLights
                             || importer.isReadable;
                if (!dirty) continue;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir + "/Vfx" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                bool dirty = !importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp || !importer.mipmapEnabled;
                if (!dirty) continue;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }

        static GameArt CreateGameArt()
        {
            var art = AssetDatabase.LoadAssetAtPath<GameArt>(GameArtPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<GameArt>();
                AssetDatabase.CreateAsset(art, GameArtPath);
            }

            art.planeMatte = Matte("PlaneMatte", Color.white, true, 0, StencilOp.Replace, 2000, 0.035f);
            art.playerMatte = TexturedMatte("PlayerMatte", "PlayerPlane");
            art.fighterMatte = TexturedMatte("FighterMatte", "EnemyFighter");
            art.bomberMatte = TexturedMatte("BomberMatte", "EnemyBomber");
            art.turretMatte = TexturedMatte("TurretMatte", "Turret");
            art.turretShot = Matte("TurretShot", new Color(1f, 0.72f, 0.16f, 1f), false, 0, StencilOp.Replace, 2000, 0f);
            art.handRock = Matte("HandRock", new Color(0.62f, 0.6f, 0.57f, 1f), false, 1, StencilOp.Replace, 1990, 0.05f);
            art.projectile = Matte("Projectile", new Color(1f, 0.82f, 0.3f, 1f), false, 0, StencilOp.Replace, 2000, 0f);

            art.silhouette = Plain("PlaneSilhouette", "MiniPlanes/OccludedSilhouette");
            art.silhouette.SetColor("_BaseColor", new Color(0.2f, 0.78f, 0.7f, 1f));
            art.bubbleRing = Plain("BubbleRing", "MiniPlanes/BubbleRing");
            art.pathLine = Plain("PathLine", "MiniPlanes/PathLine");
            art.comicSprite = Plain("ComicSprite", "MiniPlanes/ComicSprite");
            art.shieldBubble = Plain("ShieldBubble", "MiniPlanes/ShieldBubble");

            art.playerPlane = Model("PlayerPlane");
            art.enemyFighter = Model("EnemyFighter");
            art.enemyBomber = Model("EnemyBomber");
            art.balloonShield = Model("BalloonShield");
            art.balloonBomb = Model("BalloonBomb");
            art.balloonTurret = Model("BalloonTurret");
            art.turret = Model("Turret");

            art.ringOnset = Vfx("ring_onset");
            art.ringPeak = Vfx("ring_peak");
            art.ringBreakup = Vfx("ring_breakup");
            art.crashOnset = Vfx("crash_onset");
            art.crashPeak = Vfx("crash_peak");
            art.crashBreakup = Vfx("crash_breakup");
            art.flickOnset = Vfx("flick_onset");
            art.flickPeak = Vfx("flick_peak");
            art.flickBreakup = Vfx("flick_breakup");
            art.plus1 = Vfx("plus1");
            art.plus2 = Vfx("plus2");

            art.hudFont = AssetDatabase.LoadAssetAtPath<Font>(ArtDir + "/Fonts/ChakraPetch-SemiBold.ttf");
            art.headingFont = AssetDatabase.LoadAssetAtPath<Font>(ArtDir + "/Fonts/ChakraPetch-Bold.ttf");

            EditorUtility.SetDirty(art);
            Debug.Log("[MiniPlanes] Art: player=" + (art.playerPlane != null) + " fighter=" + (art.enemyFighter != null) +
                      " bomber=" + (art.enemyBomber != null) + " shield=" + (art.balloonShield != null) +
                      " bomb=" + (art.balloonBomb != null) + " turretBalloon=" + (art.balloonTurret != null) + " turret=" + (art.turret != null) +
                      " vfx=" + (art.crashPeak != null) + " font=" + (art.hudFont != null));
            return art;
        }

        static GameObject Model(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(ArtDir + "/Models/" + name + ".fbx");
        }

        static Texture2D Vfx(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/Vfx/" + name + ".png");
        }

        static Material Plain(string name, string shaderName)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.Exception("Shader not found: " + shaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>The plane matte with a model's baked texture, or null if that model has no texture.</summary>
        static Material TexturedMatte(string name, string model)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/Models/" + model + ".png");
            if (texture == null) return null;
            var mat = Matte(name, Color.white, true, 0, StencilOp.Replace, 2000, 0.035f);
            mat.SetTexture("_BaseMap", texture);
            return mat;
        }

        static Material Matte(string name, Color color, bool vertexColor, int stencilRef, StencilOp stencilPass, int queue, float layerStrength)
        {
            var mat = Plain(name, "MiniPlanes/Matte");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_UseVertexColor", vertexColor ? 1f : 0f);
            mat.SetInt("_StencilRef", stencilRef);
            mat.SetInt("_StencilPass", (int)stencilPass);
            mat.SetFloat("_LayerStrength", layerStrength);
            mat.renderQueue = queue;
            return mat;
        }

        // ------------------------------------------------------------------ levels

        static List<LevelConfig> CreateLevels()
        {
            var list = new List<LevelConfig>();
            for (int i = 1; i <= Progression.TutorialLevelCount; i++)
            {
                string path = LevelsDir + "/Level" + i + ".asset";
                var cfg = AssetDatabase.LoadAssetAtPath<LevelConfig>(path);
                if (cfg == null)
                {
                    cfg = ScriptableObject.CreateInstance<LevelConfig>();
                    cfg.ApplyDefaults(i);
                    AssetDatabase.CreateAsset(cfg, path);
                }
                else if (cfg.defaultsVersion < LevelConfig.CurrentDefaultsVersion)
                {
                    // Only reset when the code's defaults have moved on. Otherwise inspector edits are kept.
                    var fresh = ScriptableObject.CreateInstance<LevelConfig>();
                    fresh.ApplyDefaults(i);
                    fresh.name = cfg.name;
                    EditorUtility.CopySerialized(fresh, cfg);
                    Object.DestroyImmediate(fresh);
                    EditorUtility.SetDirty(cfg);
                }
                list.Add(cfg);
            }
            return list;
        }

        static InfiniteConfig CreateInfinite(List<LevelConfig> levels)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<InfiniteConfig>(InfinitePath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<InfiniteConfig>();
                AssetDatabase.CreateAsset(cfg, InfinitePath);
            }
            cfg.baseLevel = levels[levels.Count - 1];
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        // ------------------------------------------------------------------ scene pieces

        static void SetupLighting()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.62f, 0.65f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
        }

        static void CreateRig(out Transform head, out Transform leftAnchor, out Transform rightAnchor)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (prefab == null) throw new System.Exception("OVRCameraRig prefab not found at " + RigPrefabPath);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.name = "OVRCameraRig";

            var manager = rig.GetComponent<OVRManager>();
            if (manager != null)
            {
                manager.isInsightPassthroughEnabled = true;
                manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            }

            head = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
            leftAnchor = rig.transform.Find("TrackingSpace/LeftHandAnchor");
            rightAnchor = rig.transform.Find("TrackingSpace/RightHandAnchor");

            var cam = head.GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 50f;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderPostProcessing = false;
                    data.antialiasing = AntialiasingMode.None;
                }
            }

            var passthrough = rig.AddComponent<OVRPassthroughLayer>();
            var so = new SerializedObject(passthrough);
            var overlay = so.FindProperty("overlayType");
            if (overlay != null)
            {
                int idx = System.Array.IndexOf(overlay.enumNames, "Underlay");
                if (idx >= 0) overlay.enumValueIndex = idx;
            }
            var depth = so.FindProperty("compositionDepth");
            if (depth != null) depth.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static HandParts CreateHand(Transform anchor, HandSide side, GameArt art)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefabPath);
            if (prefab == null) throw new System.Exception("OVRHandPrefab not found at " + HandPrefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
            // Unpack so the per-hand field values are stored directly in the scene rather than as
            // prefab overrides, which were not reliably recorded for the first instance.
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = side == HandSide.Left ? "LeftHand" : "RightHand";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            bool isLeft = side == HandSide.Left;

            var ovrHand = go.GetComponent<OVRHand>();
            SetSerialized(ovrHand, "HandType", isLeft ? 0 : 1);
            var skeleton = go.GetComponent<OVRSkeleton>();
            SetSerialized(skeleton, "_skeletonType", isLeft ? 4 : 5);          // OpenXR Hand (Left / Right)
            SetSerialized(skeleton, "_enablePhysicsCapsules", 0);
            var mesh = go.GetComponent<OVRMesh>();
            SetSerialized(mesh, "_meshType", isLeft ? 4 : 5);

            var skeletonRenderer = go.GetComponent<OVRSkeletonRenderer>();
            if (skeletonRenderer != null) skeletonRenderer.enabled = false;

            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null)
            {
                smr.sharedMaterial = art.handRock;
                smr.shadowCastingMode = ShadowCastingMode.Off;
                smr.receiveShadows = false;
            }

            var parts = new HandParts();
            parts.source = go.AddComponent<MetaHandSource>();
            parts.source.side = side;
            parts.source.ovrHand = ovrHand;
            parts.source.skeleton = skeleton;

            parts.ring = go.AddComponent<HandRing>();
            parts.ring.hand = parts.source;
            parts.ring.bubbleMaterial = art.bubbleRing;

            parts.hazard = go.AddComponent<HandHazard>();
            parts.hazard.hand = parts.source;
            parts.hazard.ring = parts.ring;

            parts.flick = go.AddComponent<FlickDetector>();
            parts.flick.hand = parts.source;

            parts.grabber = go.AddComponent<BalloonGrabber>();
            parts.grabber.hand = parts.source;
            parts.grabber.ring = parts.ring;
            return parts;
        }

        static void SetSerialized(Component component, string property, int value)
        {
            if (component == null)
            {
                Debug.LogWarning("[MiniPlanes] Missing component for property " + property);
                return;
            }
            var so = new SerializedObject(component);
            var p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogWarning("[MiniPlanes] Property not found: " + property + " on " + component.GetType().Name);
                return;
            }
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value != 0;
            else p.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
