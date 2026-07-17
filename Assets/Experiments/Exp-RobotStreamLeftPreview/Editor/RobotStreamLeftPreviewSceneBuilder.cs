// Assets/Experiments/Exp-RobotStreamLeftPreview/Editor/RobotStreamLeftPreviewSceneBuilder.cs
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PicoTest.Experiments.RobotDsDome;

namespace PicoTest.Experiments.RobotStreamLeftPreview.Editor
{
    /// <summary>
    /// Robot Stream Left Preview Demo 场景生成 + 确保 RobotDsDome shader Always-Included（否则
    /// 真机剥离→黑屏，同 RobotDsDomeSceneBuilder 的防御）。极简（宪法 #14）：XR Origin + 挂
    /// RobotStreamLeftPreviewFeeder 的对象。打包/装机走中央注册表：菜单
    /// PicoTest/Build APK/Robot Stream Left Preview（Builder.SceneRegistry key=robotleft）。
    /// </summary>
    public static class RobotStreamLeftPreviewSceneBuilder
    {
        private const string ScenePath = "Assets/Experiments/Exp-RobotStreamLeftPreview/Scenes/RobotStreamLeftPreviewDemo.unity";
        private const string CalDir = "Assets/Experiments/Exp-RobotDsDome/Calibration";
        private const string ShaderName = "PicoTest/RobotDsDome";

        [MenuItem("PicoTest/Robot Stream Left Preview/Build Demo Scene")]
        public static void BuildScene()
        {
            EnsureShaderIncluded();

            var calib = AssetDatabase.LoadAssetAtPath<DsEyeCalibration>($"{CalDir}/RobotDsLeft.asset");
            if (calib == null)
            {
                EditorUtility.DisplayDialog("缺 DS 标定",
                    "未找到 RobotDsLeft。先跑 PicoTest/Robot DS Dome/Import Camchain。", "好");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!EditorApplication.ExecuteMenuItem("GameObject/XR/Device-based/XR Origin (VR)"))
            {
                Debug.LogError("[RobotLeftPreviewBuilder] 创建 XR Origin 失败——确认 XRI 已安装。");
                return;
            }

            var go = new GameObject("RobotStreamLeftPreviewFeeder");
            var feeder = go.AddComponent<RobotStreamLeftPreviewFeeder>();
            feeder.calibration = calib;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[RobotLeftPreviewBuilder] 已生成并打开 {ScenePath}。\n" +
                      "标定：机器人左目 Double Sphere 真实标定（RobotDsLeft，来自 3-camchain.yaml）。\n" +
                      "PC 环回：python Tools/run_stereo_left_viewer.py，serverUrl=http://127.0.0.1:8888；编辑器假源 useRealWebRtc=false。\n" +
                      "打包：菜单 PicoTest/Build APK/Robot Stream Left Preview。");
        }

        [MenuItem("PicoTest/Robot Stream Left Preview/Ensure Shader Included")]
        public static void EnsureShaderIncluded()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError($"[RobotLeftPreviewBuilder] shader '{ShaderName}' 未找到"); return; }
            var so = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            int idx = arr.arraySize;
            arr.InsertArrayElementAtIndex(idx);
            arr.GetArrayElementAtIndex(idx).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[RobotLeftPreviewBuilder] 已把 '{ShaderName}' 加入 Always Included Shaders");
        }
    }
}
