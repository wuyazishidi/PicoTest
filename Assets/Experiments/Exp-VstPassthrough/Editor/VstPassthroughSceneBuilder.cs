// Assets/Experiments/Exp-VstPassthrough/Editor/VstPassthroughSceneBuilder.cs
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using PicoTest.Rendering;

namespace PicoTest.Experiments.VstPassthrough.Editor
{
    /// <summary>
    /// VST Passthrough Demo 场景生成 + 构建（自成一体，不动 Main 的 Builder/场景）。
    /// 场景极简（宪法 #14）：XR Origin + 一个挂 VstPassthroughFeeder 的对象，其余运行时装配。
    /// </summary>
    public static class VstPassthroughSceneBuilder
    {
        private const string ScenePath = "Assets/Experiments/Exp-VstPassthrough/Scenes/VstPassthroughDemo.unity";
        private const string CalDir = "Assets/Main/Settings/Calibration";
        private const string ApkName = "PicoTest-VstPassthrough.apk";

        [MenuItem("PicoTest/VST Passthrough/Build Demo Scene")]
        public static void BuildScene()
        {
            var left = AssetDatabase.LoadAssetAtPath<FisheyeCalibration>($"{CalDir}/RealLeft.asset");
            var right = AssetDatabase.LoadAssetAtPath<FisheyeCalibration>($"{CalDir}/RealRight.asset");
            if (left == null || right == null)
            {
                EditorUtility.DisplayDialog("缺标定",
                    "未找到 RealLeft/RealRight（回退资产要用）。先跑 PicoTest/Import Factory Calibration。", "好");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!EditorApplication.ExecuteMenuItem("GameObject/XR/Device-based/XR Origin (VR)"))
            {
                Debug.LogError("[VstPTBuilder] 创建 XR Origin 失败——确认 XRI 已安装。");
                return;
            }

            var go = new GameObject("VstPassthroughFeeder");
            var feeder = go.AddComponent<VstPassthroughFeeder>();
            feeder.fallbackLeft = left;
            feeder.fallbackRight = right;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[VstPTBuilder] 已生成并打开 {ScenePath}。\n" +
                      "标定运行时读 StreamingAssets/cam_calib.json（含外参），回退 RealLeft/RealRight。\n" +
                      "仅真机有效：菜单 PicoTest/VST Passthrough/Build APK (in-editor) 构建。");
        }

        /// <summary>
        /// 编辑器内 Release 构建（同 Builder 约定：Release 规避 PICO 回调线程 CheckJNI abort；
        /// Debug.Log Release 下仍输出）。产物 Builds/PicoTest-VstPassthrough.apk。
        /// </summary>
        [MenuItem("PicoTest/VST Passthrough/Build APK (in-editor)")]
        public static void BuildApk()
        {
            try
            {
                if (!File.Exists(ScenePath))
                {
                    Debug.LogError($"[VstPTBuilder] 场景不存在：{ScenePath} —— 先跑 Build Demo Scene 菜单。");
                    return;
                }
                Directory.CreateDirectory("Builds");
                string apkPath = Path.Combine("Builds", ApkName);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log($"[VstPTBuilder] Release {ApkName} result={summary.result} size={summary.totalSize} " +
                          $"time={summary.totalTime} errors={summary.totalErrors} output={apkPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VstPTBuilder] 构建异常: {e}");
            }
        }
    }
}
