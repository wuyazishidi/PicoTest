// Assets/Experiments/Exp-RobotStream/Editor/RobotStreamSceneBuilder.cs
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PicoTest.Rendering;

namespace PicoTest.Experiments.RobotStream.Editor
{
    /// <summary>
    /// Robot Stream Demo 场景生成。极简（宪法 #14）：XR Origin + 挂 RobotStreamFeeder 的对象。
    /// 打包/装机走中央注册表：菜单 PicoTest/Build APK/Robot Stream（Builder.SceneRegistry key=robotstream）。
    /// </summary>
    public static class RobotStreamSceneBuilder
    {
        private const string ScenePath = "Assets/Experiments/Exp-RobotStream/Scenes/RobotStreamDemo.unity";
        private const string CalDir = "Assets/Main/Settings/Calibration";

        [MenuItem("PicoTest/Robot Stream/Build Demo Scene")]
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
                Debug.LogError("[RobotStreamBuilder] 创建 XR Origin 失败——确认 XRI 已安装。");
                return;
            }

            var go = new GameObject("RobotStreamFeeder");
            var feeder = go.AddComponent<RobotStreamFeeder>();
            feeder.fallbackLeft = left;
            feeder.fallbackRight = right;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[RobotStreamBuilder] 已生成并打开 {ScenePath}。\n" +
                      "标定运行时读 StreamingAssets/cam_calib.json（Pico 参数当机器人相机），回退 RealLeft/RealRight。\n" +
                      "PC 环回：node Server/signaling.js + webrtc-sender.html 发 camera.mp4；编辑器假源 useRealWebRtc=false。\n" +
                      "打包：菜单 PicoTest/Build APK/Robot Stream。");
        }
    }
}
