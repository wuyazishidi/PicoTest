using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;

namespace PicoTest.Editor.Setup
{
    /// <summary>
    /// 一次性项目配置（M1）。全部挂在菜单上，可由 YIUIMCP ExecuteMenu 无人值守触发：
    ///   PicoTest/Setup/1 Configure Player Settings
    ///   PicoTest/Setup/2 Configure URP
    ///   PicoTest/Setup/3 Configure XR (PICO loader)
    ///   PicoTest/Setup/4 Create Main Scene
    ///   PicoTest/Setup/Run All
    /// </summary>
    public static class ProjectSetup
    {
        private const string SettingsDir = "Assets/Main/Settings";
        private const string MainScenePath = "Assets/Main/Scenes/Main.unity";

        [MenuItem("PicoTest/Setup/1 Configure Player Settings")]
        public static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "wuyazishidi";
            PlayerSettings.productName = "PicoTest";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.wuyazishidi.picotest");

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            // VR 必须关闭多线程渲染警告项按 PICO 文档默认即可；纹理压缩在构建脚本/EditorUserBuildSettings 设置
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] Player settings configured (IL2CPP/ARM64/API29/Linear/ASTC).");
        }

        [MenuItem("PicoTest/Setup/2 Configure URP")]
        public static void ConfigureURP()
        {
            Directory.CreateDirectory(SettingsDir);

            var rendererPath = $"{SettingsDir}/URP-Renderer.asset";
            var pipelinePath = $"{SettingsDir}/URP-Pipeline.asset";

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, rendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] URP configured and assigned.");
        }

        [MenuItem("PicoTest/Setup/3 Configure XR (PICO loader)")]
        public static void ConfigureXR()
        {
            // Android：启用 PICO loader，启动时初始化 XR
            var androidSettings = GetOrCreateXRSettings(BuildTargetGroup.Android);
            androidSettings.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(androidSettings.Manager, "Unity.XR.PXR.PXR_Loader", BuildTargetGroup.Android))
            {
                Debug.LogError("[ProjectSetup] FAILED to assign PXR_Loader for Android — is com.unity.xr.picoxr imported?");
                return;
            }

            // Standalone（Editor PlayMode）：禁止启动时初始化 XR —— 保证无设备 PC 测试可跑（宪法/计划要求）
            var standaloneSettings = GetOrCreateXRSettings(BuildTargetGroup.Standalone);
            standaloneSettings.InitManagerOnStart = false;

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] XR configured: Android=PXR_Loader(auto-init), Standalone=no auto-init.");
        }

        [MenuItem("PicoTest/Setup/4 Create Main Scene")]
        public static void CreateMainScene()
        {
            Directory.CreateDirectory("Assets/Main/Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 极简场景原则（宪法 #14）：只放 XR Origin 骨架 + Bootstrap，内容由代码实例化
            var origin = new GameObject("XR Origin");
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(origin.transform, false);
            offset.transform.localPosition = new Vector3(0, 1.6f, 0); // 无设备 PlayMode 下的近似人眼高

            var camGo = new GameObject("Main Camera");
            camGo.transform.SetParent(offset.transform, false);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();

            var xrOrigin = origin.AddComponent<XROrigin>();
            xrOrigin.CameraFloorOffsetObject = offset;
            xrOrigin.Camera = cam;

            var bootstrap = new GameObject("Bootstrap");
            bootstrap.AddComponent<PicoTest.Bootstrap>();

            var light = new GameObject("Directional Light");
            var l = light.AddComponent<Light>();
            l.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            EditorSceneManager.SaveScene(scene, MainScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
            Debug.Log($"[ProjectSetup] Main scene created at {MainScenePath} and set in Build Settings.");
        }

        [MenuItem("PicoTest/Setup/Run All")]
        public static void RunAll()
        {
            ConfigurePlayerSettings();
            ConfigureURP();
            ConfigureXR();
            CreateMainScene();
            Debug.Log("[ProjectSetup] ALL setup steps completed.");
        }

        private static XRGeneralSettings GetOrCreateXRSettings(BuildTargetGroup group)
        {
            XRGeneralSettingsPerBuildTarget perBuildTarget = null;
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out perBuildTarget);
            if (perBuildTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perBuildTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuildTarget, true);
            }

            var settings = perBuildTarget.SettingsForBuildTarget(group);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                perBuildTarget.SetSettingsForBuildTarget(group, settings);
                settings.name = $"{group} Settings";
                AssetDatabase.AddObjectToAsset(settings, perBuildTarget);

                var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = $"{group} Providers";
                AssetDatabase.AddObjectToAsset(manager, perBuildTarget);
                settings.Manager = manager;
            }

            return settings;
        }
    }
}
