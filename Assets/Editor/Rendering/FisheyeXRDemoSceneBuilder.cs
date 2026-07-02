// Assets/Editor/Rendering/FisheyeXRDemoSceneBuilder.cs
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PicoTest.Rendering;

namespace PicoTest.Editor.Rendering
{
    /// <summary>
    /// 构建 XR 版鱼眼穹顶 demo 场景：XRI 的 XR Origin(头显追踪相机) + 鱼眼穹顶 rig。
    /// 菜单：PicoTest/Build Fisheye XR Demo Scene → 生成并打开 FisheyeDomeXRDemo.unity。
    /// 真机(PICO)上跑出真立体；编辑器无设备只能单眼预览(相机静止看前方)。
    /// </summary>
    public static class FisheyeXRDemoSceneBuilder
    {
        private const string ScenePath = "Assets/Main/Scenes/FisheyeDomeXRDemo.unity";
        private const string CalDir = "Assets/Main/Settings/Calibration";

        [MenuItem("PicoTest/Build Fisheye XR Demo Scene")]
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

            // XRI 自带的 XR Origin（已知良好配置：相机 + TrackedPoseDriver + MainCamera tag）
            if (!EditorApplication.ExecuteMenuItem("GameObject/XR/Device-based/XR Origin (VR)"))
            {
                Debug.LogError("[FisheyeXRDemoSceneBuilder] 创建 XR Origin 菜单失败——确认 XRI 已安装。");
                return;
            }

            // 鱼眼穹顶 rig
            var rigGo = new GameObject("FisheyeDomeXRRig");
            var rig = rigGo.AddComponent<FisheyeDomeXRRig>();
            rig.leftCalibration = left;
            rig.rightCalibration = right;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[FisheyeXRDemoSceneBuilder] 已生成并打开 {ScenePath}。\n" +
                      "编辑器 Play：单眼预览(相机静止)。真机：构建 APK 部署 PICO 看真立体。\n" +
                      "需 StreamingAssets/sbs_frame.png（跑 Tools\\extract-fisheye-frame.ps1）。");
        }
    }
}
