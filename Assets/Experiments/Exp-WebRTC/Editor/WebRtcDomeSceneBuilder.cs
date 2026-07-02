using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PicoTest.Rendering;
using PicoTest.Experiments.WebRTC;

namespace PicoTest.Experiments.WebRTC.Editor
{
    /// <summary>
    /// 构建 WebRTC 双目鱼眼穹顶测试场景：XR Origin + WebRtcDomeFeeder（M0 默认用假帧源）。
    /// 菜单：PicoTest/Build WebRTC Dome Scene → 生成并打开 Scenes/WebRtcDomeXRLive.unity。
    /// 标定 M0 先复用 RealLeft/RealRight（1280×960）；机器人真值（1280×720）留 M4。
    /// </summary>
    public static class WebRtcDomeSceneBuilder
    {
        private const string ScenePath = "Assets/Experiments/Exp-WebRTC/Scenes/WebRtcDomeXRLive.unity";
        private const string CalDir = "Assets/Main/Settings/Calibration";

        [MenuItem("PicoTest/Build WebRTC Dome Scene")]
        public static void Build()
        {
            var left = AssetDatabase.LoadAssetAtPath<FisheyeCalibration>($"{CalDir}/RealLeft.asset");
            var right = AssetDatabase.LoadAssetAtPath<FisheyeCalibration>($"{CalDir}/RealRight.asset");
            if (left == null || right == null)
            {
                EditorUtility.DisplayDialog("缺标定",
                    "未找到 RealLeft/RealRight。先跑 PicoTest/Import Factory Calibration。", "好");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!EditorApplication.ExecuteMenuItem("GameObject/XR/Device-based/XR Origin (VR)"))
            {
                Debug.LogError("[WebRtcDomeSceneBuilder] 创建 XR Origin 失败——确认 XRI 已安装。");
                return;
            }

            var go = new GameObject("WebRtcDomeFeeder");
            var feeder = go.AddComponent<WebRtcDomeFeeder>();
            feeder.leftCalibration = left;
            feeder.rightCalibration = right;
            feeder.width = 2560; feeder.height = 720;   // SBS 每眼 1280×720

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[WebRtcDomeSceneBuilder] 已生成并打开 {ScenePath}。\n" +
                      "M0：编辑器 Play 用假帧源(左红/右蓝+移动渐变)验证 SBS 分眼+云台；" +
                      "M1+ 注入真实 WebRTC 源。标定 M0 用 RealLeft/RealRight，机器人真值(1280×720)留 M4。");
        }
    }
}
