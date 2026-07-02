// Assets/Editor/Rendering/FisheyeDemoSceneBuilder.cs
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PicoTest.Rendering;

namespace PicoTest.Editor.Rendering
{
    /// <summary>
    /// 构建鱼眼穹顶 demo 场景（宪法 #14：场景极简，内容由代码装配）。
    /// 菜单：PicoTest/Build Fisheye Demo Scene → 生成并打开 Assets/Main/Scenes/FisheyeDomeDemo.unity。
    /// 打开后按 Play 即看真实鱼眼帧在穹顶上的去畸变还原（相机自动扫视）。
    /// </summary>
    public static class FisheyeDemoSceneBuilder
    {
        private const string ScenePath = "Assets/Main/Scenes/FisheyeDomeDemo.unity";
        private const string CalDir = "Assets/Main/Settings/Calibration";

        [MenuItem("PicoTest/Build Fisheye Demo Scene")]
        public static void Build()
        {
            var left = AssetDatabase.LoadAssetAtPath<FisheyeCalibration>($"{CalDir}/RealLeft.asset");
            var right = AssetDatabase.LoadAssetAtPath<FisheyeCalibration>($"{CalDir}/RealRight.asset");
            if (left == null || right == null)
            {
                EditorUtility.DisplayDialog("缺标定",
                    "未找到 RealLeft/RealRight。先跑菜单 PicoTest/Import Factory Calibration。", "好");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("FisheyeDomeDemo");
            var demo = go.AddComponent<FisheyeDomeDemo>();
            demo.leftCalibration = left;
            demo.rightCalibration = right;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[FisheyeDemoSceneBuilder] 已生成并打开 {ScenePath}。按 Play 看效果" +
                      "（需先有 StreamingAssets/sbs_frame.png：跑 Tools\\extract-fisheye-frame.ps1）。");
        }
    }
}
