using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PicoTest.Experiments.TrackerIMU.Editor
{
    /// <summary>
    /// 生成多 Tracker IMU 测试场景（XR Origin + TrackerImuProbe），并提供 ENABLE_BODY_TRACKING
    /// 编译开关菜单（R3 对照轮用；只改 Android 目标的 scripting defines）。
    /// </summary>
    public static class TrackerImuSceneBuilder
    {
        private const string ScenePath = "Assets/Experiments/Exp-TrackerIMU/Scenes/TrackerImuTest.unity";
        private const string BtDefine = "ENABLE_BODY_TRACKING";

        [MenuItem("PicoTest/Tracker IMU/Generate Test Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!EditorApplication.ExecuteMenuItem("GameObject/XR/Device-based/XR Origin (VR)"))
            {
                Debug.LogError("[TrackerImuSceneBuilder] 创建 XR Origin 失败——确认 XRI 已安装。");
                return;
            }

            var go = new GameObject("TrackerImuProbe");
            go.AddComponent<TrackerImuProbe>();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[TrackerImuSceneBuilder] 已生成并打开 {ScenePath}。\n" +
                      $"当前体追编译开关 {BtDefine}={(HasBtDefine() ? "开" : "关")}（菜单 PicoTest/Tracker IMU 切换）。\n" +
                      "打包：菜单 PicoTest/Tracker IMU/Build APK (in-editor)；真机流程见 Assets/Experiments/Exp-TrackerIMU/README.md。");
        }

        [MenuItem("PicoTest/Tracker IMU/Enable Body Tracking Define")]
        public static void EnableBt() => SetBtDefine(true);

        [MenuItem("PicoTest/Tracker IMU/Disable Body Tracking Define")]
        public static void DisableBt() => SetBtDefine(false);

        [MenuItem("PicoTest/Tracker IMU/Enable Body Tracking Define", true)]
        public static bool EnableBtValidate() => !HasBtDefine();

        [MenuItem("PicoTest/Tracker IMU/Disable Body Tracking Define", true)]
        public static bool DisableBtValidate() => HasBtDefine();

        static bool HasBtDefine()
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android, out string[] defines);
            return defines.Contains(BtDefine);
        }

        static void SetBtDefine(bool on)
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android, out string[] defines);
            var list = defines.ToList();
            if (on && !list.Contains(BtDefine)) list.Add(BtDefine);
            if (!on) list.RemoveAll(d => d == BtDefine);
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, list.ToArray());
            Debug.Log($"[TrackerImuSceneBuilder] {BtDefine}（Android）→ {(on ? "开" : "关")}。将触发重编译；构建 R3 对照轮前确认此状态。");
        }
    }
}
