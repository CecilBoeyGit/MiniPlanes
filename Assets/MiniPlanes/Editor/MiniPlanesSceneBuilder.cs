using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MiniPlanes.Editor
{
    /// <summary>
    /// Assembles the playable scene from code: Meta camera rig with passthrough, tracked hands with
    /// ring / rock / flick behaviours, the game manager, enemy spawner, HUD and all materials.
    /// </summary>
    public static class MiniPlanesSceneBuilder
    {
        public const string ScenePath = "Assets/MiniPlanes/Scenes/MiniPlanes.unity";
        const string MaterialsDir = "Assets/MiniPlanes/Materials";
        const string LevelsDir = "Assets/MiniPlanes/Levels";
        const string RigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        const string HandPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab";

        class Mats
        {
            public Material planeBody, planeWing, enemyBody, enemyWing, ringActive, ringScored, projectile, rock, smoke, pathDebug;
        }

        class HandParts
        {
            public MetaHandSource source;
            public HandRing ring;
            public HandHazard hazard;
            public FlickDetector flick;
        }

        [MenuItem("MiniPlanes/2. Build Game Scene")]
        public static void Build()
        {
            EnsureFolder("Assets/MiniPlanes");
            EnsureFolder(MaterialsDir);
            EnsureFolder(LevelsDir);
            EnsureFolder("Assets/MiniPlanes/Scenes");

            var mats = CreateMaterials();
            var levels = CreateLevels();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLighting();

            CreateRig(out Transform head, out Transform leftAnchor, out Transform rightAnchor);
            var left = CreateHand(leftAnchor, HandSide.Left, mats);
            var right = CreateHand(rightAnchor, HandSide.Right, mats);

            var game = new GameObject("Game");
            var gm = game.AddComponent<GameManager>();

            var pathGo = new GameObject("FlightPath");
            pathGo.transform.SetParent(game.transform, false);
            var pathGen = pathGo.AddComponent<FlightPathGenerator>();
            var line = pathGo.AddComponent<LineRenderer>();
            line.sharedMaterial = mats.pathDebug;
            line.widthMultiplier = 0.003f;
            line.positionCount = 0;
            line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            var spawnerGo = new GameObject("EnemySpawner");
            spawnerGo.transform.SetParent(game.transform, false);
            var spawner = spawnerGo.AddComponent<EnemySpawner>();
            spawner.head = head;
            spawner.enemyBodyMaterial = mats.enemyBody;
            spawner.enemyWingMaterial = mats.enemyWing;
            spawner.projectileMaterial = mats.projectile;

            var smoke = CreateSmoke(mats.smoke);
            smoke.transform.SetParent(game.transform, false);

            var hud = CreateHud(head);

            gm.head = head;
            gm.pathGenerator = pathGen;
            gm.spawner = spawner;
            gm.hud = hud;
            gm.crashSmoke = smoke;
            gm.rings = new[] { left.ring, right.ring };
            gm.hazards = new[] { left.hazard, right.hazard };
            gm.flickers = new[] { left.flick, right.flick };
            gm.levels = levels;
            gm.planeBodyMaterial = mats.planeBody;
            gm.planeWingMaterial = mats.planeWing;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[MiniPlanes] Scene built and saved to " + ScenePath);
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

        // ------------------------------------------------------------------ materials

        static Mats CreateMaterials()
        {
            var m = new Mats
            {
                planeBody = Lit("PlaneBody", Color.white, 0.45f),
                planeWing = Lit("PlaneWing", Color.white, 0.35f),
                enemyBody = Lit("EnemyBody", Color.white, 0.5f),
                enemyWing = Lit("EnemyWing", Color.white, 0.4f),
                ringActive = Unlit("RingActive", new Color(0.25f, 0.95f, 1f, 1f)),
                ringScored = Unlit("RingScored", new Color(1f, 0.85f, 0.2f, 1f)),
                projectile = Unlit("Projectile", new Color(1f, 0.9f, 0.3f, 1f)),
                rock = Lit("HandRock", new Color(0.46f, 0.44f, 0.42f, 1f), 0.08f),
                pathDebug = Unlit("PathDebug", new Color(1f, 0.2f, 0.9f, 1f)),
                smoke = Smoke("CrashSmoke", new Color(0.4f, 0.4f, 0.4f, 0.85f)),
            };
            return m;
        }

        static Material GetOrCreate(string name, string shaderName)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("[MiniPlanes] Shader not found: " + shaderName);
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }
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
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Lit(string name, Color color, float smoothness)
        {
            var mat = GetOrCreate(name, "Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            return mat;
        }

        static Material Unlit(string name, Color color)
        {
            var mat = GetOrCreate(name, "Universal Render Pipeline/Unlit");
            mat.SetColor("_BaseColor", color);
            return mat;
        }

        static Material Smoke(string name, Color color)
        {
            var mat = GetOrCreate(name, "Universal Render Pipeline/Particles/Unlit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }

        // ------------------------------------------------------------------ levels

        static List<LevelConfig> CreateLevels()
        {
            var list = new List<LevelConfig>();
            for (int i = 1; i <= 5; i++)
            {
                string path = LevelsDir + "/Level" + i + ".asset";
                var cfg = AssetDatabase.LoadAssetAtPath<LevelConfig>(path);
                if (cfg == null)
                {
                    cfg = ScriptableObject.CreateInstance<LevelConfig>();
                    AssetDatabase.CreateAsset(cfg, path);
                }
                // Always re-apply the code defaults so the assets track the current difficulty curve.
                cfg.ApplyDefaults(i);
                EditorUtility.SetDirty(cfg);
                list.Add(cfg);
            }
            return list;
        }

        // ------------------------------------------------------------------ scene pieces

        static void SetupLighting()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.48f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
        }

        static GameObject CreateRig(out Transform head, out Transform leftAnchor, out Transform rightAnchor)
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

            return rig;
        }

        static HandParts CreateHand(Transform anchor, HandSide side, Mats mats)
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
            Debug.Log("[MiniPlanes] " + go.name + " configured: HandType=" + ReadInt(ovrHand, "HandType") +
                      " skeleton=" + ReadInt(skeleton, "_skeletonType") + " mesh=" + ReadInt(mesh, "_meshType"));

            var skeletonRenderer = go.GetComponent<OVRSkeletonRenderer>();
            if (skeletonRenderer != null) skeletonRenderer.enabled = false;

            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null)
            {
                smr.sharedMaterial = mats.rock;
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
            parts.ring.activeMaterial = mats.ringActive;
            parts.ring.scoredMaterial = mats.ringScored;

            parts.hazard = go.AddComponent<HandHazard>();
            parts.hazard.hand = parts.source;
            parts.hazard.ring = parts.ring;

            parts.flick = go.AddComponent<FlickDetector>();
            parts.flick.hand = parts.source;
            return parts;
        }

        static int ReadInt(Component component, string property)
        {
            if (component == null) return -999;
            var p = new SerializedObject(component).FindProperty(property);
            return p == null ? -999 : p.intValue;
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

        static ParticleSystem CreateSmoke(Material material)
        {
            var go = new GameObject("CrashSmoke");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.8f;
            main.startSpeed = 0.18f;
            main.startSize = 0.016f;
            main.startColor = new Color(0.4f, 0.4f, 0.4f, 0.9f);
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.02f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)20) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.006f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        static ScoreHud CreateHud(Transform head)
        {
            var go = new GameObject("HUD");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(700f, 240f);
            go.transform.localScale = Vector3.one * 0.0008f;
            go.transform.position = new Vector3(0f, 1.0f, 0.9f);

            var panel = new GameObject("Panel").AddComponent<Image>();
            panel.transform.SetParent(go.transform, false);
            var prt = panel.rectTransform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            panel.color = new Color(0f, 0f, 0f, 0.35f);
            panel.raycastTarget = false;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var status = MakeText(go.transform, "Status", 60f, 44, FontStyle.Bold, font);
            var message = MakeText(go.transform, "Message", -40f, 34, FontStyle.Normal, font);

            var hud = go.AddComponent<ScoreHud>();
            hud.head = head;
            hud.statusText = status;
            hud.messageText = message;
            return hud;
        }

        static Text MakeText(Transform parent, string name, float y, int size, FontStyle style, Font font)
        {
            var text = new GameObject(name).AddComponent<Text>();
            text.transform.SetParent(parent, false);
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(-40f, 110f);
            rt.anchoredPosition = new Vector2(0f, y);
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
