// Assets/Editor/Rendering/FisheyeXRLiveSceneBuilder.cs
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PicoTest.Rendering;
using PicoTest.Vst;

namespace PicoTest.Editor.Rendering
{
    /// <summary>
    /// 构建 VST 实时鱼眼穹顶测试场景：XR Origin + PICO VST 相机实时流喂穹顶。
    /// 菜单：PicoTest/Build Fisheye XR Live Scene → 生成并打开 FisheyeDomeXRLive.unity。
    /// 仅真机有效（Enterprise 相机需 PICO 4U + 激活）。编辑器里相机不出帧，穹顶黑屏。
    /// </summary>
    public static class FisheyeXRLiveSceneBuilder
    {
        private const string ScenePath = "Assets/Main/Scenes/FisheyeDomeXRLive.unity";
        private const string CalDir = "Assets/Main/Settings/Calibration";

        [MenuItem("PicoTest/Build Fisheye XR Live Scene")]
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
                Debug.LogError("[FisheyeXRLiveSceneBuilder] 创建 XR Origin 失败——确认 XRI 已安装。");
                return;
            }

            var rigGo = new GameObject("VstDomeFeeder");
            var feeder = rigGo.AddComponent<VstCameraDomeFeeder>();
            feeder.leftCalibration = left;
            feeder.rightCalibration = right;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[FisheyeXRLiveSceneBuilder] 已生成并打开 {ScenePath}。\n" +
                      "仅真机有效：构建 APK 部署 PICO 4U（需 Enterprise 激活）。\n" +
                      "标定用本机 A9410（RealLeft/RealRight）；若测试设备不同型号，畸变会近似。");
        }
    }
}
