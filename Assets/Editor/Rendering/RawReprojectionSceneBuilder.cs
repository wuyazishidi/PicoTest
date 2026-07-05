// Assets/Editor/Rendering/RawReprojectionSceneBuilder.cs
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PicoTest.Rendering;
using PicoTest.Vst;

namespace PicoTest.Editor.Rendering
{
    /// <summary>
    /// 构建纯 raw 视点重投影 Demo 场景：XR Origin + PICO VST 实时流 → 重投影穹顶。
    /// 菜单：PicoTest/Build Raw Reprojection Demo → 生成并打开 RawReprojectionDemo.unity。
    /// 仅真机有效（Enterprise 相机需 PICO 4U + 激活）。编辑器里相机不出帧，穹顶黑屏。
    /// 与 FisheyeDomeXRLive（远端云台+透视方案）互不影响，是并行的独立 Demo。
    /// </summary>
    public static class RawReprojectionSceneBuilder
    {
        private const string ScenePath = "Assets/Main/Scenes/RawReprojectionDemo.unity";
        private const string CalDir = "Assets/Main/Settings/Calibration";

        [MenuItem("PicoTest/Build Raw Reprojection Demo")]
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
                Debug.LogError("[RawReprojectionSceneBuilder] 创建 XR Origin 失败——确认 XRI 已安装。");
                return;
            }

            var rigGo = new GameObject("RawReprojectionFeeder");
            var feeder = rigGo.AddComponent<RawReprojectionFeeder>();
            feeder.leftCalibration = left;
            feeder.rightCalibration = right;
            feeder.depthMode = RawReprojectionFeeder.DepthMode.Constant; // 首版 M0 常量深度

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[RawReprojectionSceneBuilder] 已生成并打开 {ScenePath}。\n" +
                      "纯 raw 视点重投影 Demo（超 FOV=黑，不开系统透视）。\n" +
                      "真机验证：远景 1:1 / 转头稳定 / 倾斜消失；切 SpatialMesh 深度模式看近景视差。");
        }
    }
}
