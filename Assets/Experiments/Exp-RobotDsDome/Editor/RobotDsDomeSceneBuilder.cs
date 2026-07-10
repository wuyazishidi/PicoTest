// Assets/Experiments/Exp-RobotDsDome/Editor/RobotDsDomeSceneBuilder.cs
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PicoTest.Experiments.RobotDsDome.Editor
{
    /// <summary>
    /// Robot DS Dome Demo 场景生成 + 确保 RobotDsDome shader Always-Included（否则真机剥离→黑屏）。
    /// 打包/装机走中央注册表：PicoTest/Build APK/Robot DS Dome（Builder.SceneRegistry key=robotds）。
    /// </summary>
    public static class RobotDsDomeSceneBuilder
    {
        private const string ScenePath = "Assets/Experiments/Exp-RobotDsDome/Scenes/RobotDsDomeDemo.unity";
        private const string CalDir = "Assets/Experiments/Exp-RobotDsDome/Calibration";
        private const string ShaderName = "PicoTest/RobotDsDome";

        [MenuItem("PicoTest/Robot DS Dome/Build Demo Scene")]
        public static void BuildScene()
        {
            EnsureShaderIncluded();

            var left = AssetDatabase.LoadAssetAtPath<DsEyeCalibration>($"{CalDir}/RobotDsLeft.asset");
            var right = AssetDatabase.LoadAssetAtPath<DsEyeCalibration>($"{CalDir}/RobotDsRight.asset");
            if (left == null || right == null)
            {
                EditorUtility.DisplayDialog("缺 DS 标定",
                    "未找到 RobotDsLeft/Right。先跑 PicoTest/Robot DS Dome/Import Camchain。", "好");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!EditorApplication.ExecuteMenuItem("GameObject/XR/Device-based/XR Origin (VR)"))
            {
                Debug.LogError("[RobotDsBuilder] 创建 XR Origin 失败——确认 XRI 已安装。");
                return;
            }

            var go = new GameObject("RobotDsDomeFeeder");
            var feeder = go.AddComponent<RobotDsDomeFeeder>();
            feeder.leftCalibration = left;
            feeder.rightCalibration = right;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[RobotDsBuilder] 已生成并打开 {ScenePath}。\n" +
                      "视频：StreamingAssets/episode_000000.mp4（h264，编辑器 Play 可肉眼验证去畸变）。\n" +
                      "打包：菜单 PicoTest/Build APK/Robot DS Dome。");
        }

        [MenuItem("PicoTest/Robot DS Dome/Ensure Shader Included")]
        public static void EnsureShaderIncluded()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError($"[RobotDsBuilder] shader '{ShaderName}' 未找到"); return; }
            var so = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            int idx = arr.arraySize;
            arr.InsertArrayElementAtIndex(idx);
            arr.GetArrayElementAtIndex(idx).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[RobotDsBuilder] 已把 '{ShaderName}' 加入 Always Included Shaders");
        }
    }
}
