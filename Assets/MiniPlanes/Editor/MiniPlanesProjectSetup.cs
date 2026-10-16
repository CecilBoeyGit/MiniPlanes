using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace MiniPlanes.Editor
{
    /// <summary>
    /// One-shot configuration of player, URP, XR Plug-in Management, OpenXR and Meta settings
    /// for a Quest 3 hand-tracking build. Safe to run repeatedly.
    /// </summary>
    public static class MiniPlanesProjectSetup
    {
        const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string MetaFeatureSetId = "com.meta.openxr.featureset.metaxr";
        const string OculusTouchProfileId = "com.unity.openxr.feature.input.oculustouch";
        const string XrSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

        [MenuItem("MiniPlanes/1. Configure Project Settings")]
        public static void Run()
        {
            ConfigurePlayerSettings();
            ConfigureUrp("Assets/Settings/Mobile_RPAsset.asset");
            ConfigureUrp("Assets/Settings/PC_RPAsset.asset");
            ConfigureXr(BuildTargetGroup.Android);
            ConfigureXr(BuildTargetGroup.Standalone);
            ConfigureMeta();
            AssetDatabase.SaveAssets();
            Debug.Log("[MiniPlanes] Project settings configured.");
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "MiniPlanes";
            PlayerSettings.productName = "MiniPlanes";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, "com.miniplanes.quest");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCodeGeneration(android, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
            PlayerSettings.Android.optimizedFramePacing = false;
            PlayerSettings.SetMobileMTRendering(android, true);
            // Meta project-setup recommendations for Unity 2023.2+ and MR apps.
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.SplashScreen.show = false;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        }

        static void ConfigureUrp(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning("[MiniPlanes] URP asset not found: " + assetPath);
                return;
            }
            var so = new SerializedObject(asset);
            SetInt(so, "m_MSAA", 4);
            SetInt(so, "m_MainLightShadowsSupported", 0);
            SetInt(so, "m_AdditionalLightShadowsSupported", 0);
            SetInt(so, "m_SupportsHDR", 0);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        static void SetInt(SerializedObject so, string property, int value)
        {
            var p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogWarning("[MiniPlanes] Property not found: " + property + " on " + so.targetObject.name);
                return;
            }
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value != 0;
            else p.intValue = value;
        }

        static void ConfigureXr(BuildTargetGroup group)
        {
            var perTarget = GetOrCreateXrSettings();
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var settings = perTarget.SettingsForBuildTarget(group);
            if (settings == null)
            {
                Debug.LogError("[MiniPlanes] Could not create XR settings for " + group);
                return;
            }
            settings.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(settings.Manager, OpenXRLoaderType, group))
                Debug.LogWarning("[MiniPlanes] Could not assign the OpenXR loader for " + group);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(settings.Manager);
            EditorUtility.SetDirty(perTarget);

            var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (oxr != null)
            {
                oxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                EditorUtility.SetDirty(oxr);
            }

            var featureSet = OpenXRFeatureSetManager.GetFeatureSetWithId(group, MetaFeatureSetId);
            if (featureSet != null)
            {
                featureSet.isEnabled = true;
                OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(group);
                Debug.Log("[MiniPlanes] Meta XR feature set enabled for " + group);
            }
            else
            {
                Debug.LogWarning("[MiniPlanes] Meta XR feature set not found for " + group);
            }

            var touch = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, OculusTouchProfileId);
            if (touch != null)
            {
                touch.enabled = true;
                EditorUtility.SetDirty(touch);
            }
        }

        static XRGeneralSettingsPerBuildTarget GetOrCreateXrSettings()
        {
            string key = XRGeneralSettings.settingsKey;
            if (EditorBuildSettings.TryGetConfigObject(key, out XRGeneralSettingsPerBuildTarget existing) && existing != null)
                return existing;

            var loaded = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrSettingsPath);
            if (loaded == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                loaded = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(loaded, XrSettingsPath);
            }
            EditorBuildSettings.AddConfigObject(key, loaded, true);
            return loaded;
        }

        static void ConfigureMeta()
        {
            var cfg = OVRProjectConfig.CachedProjectConfig;
            if (cfg == null)
            {
                // In batchmode the editor-ready flag may not be set yet; call the loader directly.
                var method = typeof(OVRProjectConfig).GetMethod("GetOrCreateProjectConfig",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                cfg = method != null ? method.Invoke(null, null) as OVRProjectConfig : null;
            }
            if (cfg != null)
            {
                cfg.targetDeviceTypes = new List<OVRProjectConfig.DeviceType>
                {
                    OVRProjectConfig.DeviceType.Quest3, OVRProjectConfig.DeviceType.Quest3S
                };
                cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.HandsOnly;
                cfg.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.HIGH;
                cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
                cfg.systemLoadingScreenBackground = OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;
                cfg.metaXrFeaturePromptDeclined = false;
                OVRProjectConfig.CommitProjectConfig(cfg);
            }
            else
            {
                Debug.LogWarning("[MiniPlanes] OVRProjectConfig not available; hand tracking / passthrough manifest flags not set.");
            }

            var runtime = OVRRuntimeSettings.Instance;
            if (runtime != null)
            {
                runtime.HandSkeletonVersion = OVRHandSkeletonVersion.OpenXR;
                OVRRuntimeSettings.CommitRuntimeSettings(runtime);
            }
        }
    }
}
