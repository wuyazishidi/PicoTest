// Assets/Editor/Rendering/FactoryCalibrationImporter.cs
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using PicoTest.Rendering;

namespace PicoTest.Editor.Rendering
{
    /// <summary>
    /// 从 StreamingAssets/metadata.json 的 factory_calibration(model=equiDis62) 导入左右
    /// 鱼眼标定为 FisheyeCalibration 资产。取 raw 鱼眼 K + D 前 6 径向（切向 p1/p2 按设计简化丢弃）。
    /// 菜单：PicoTest/Import Factory Calibration (from metadata.json)。
    /// </summary>
    public static class FactoryCalibrationImporter
    {
        private const string MetaPath = "Assets/StreamingAssets/metadata.json";
        private const string OutDir = "Assets/Main/Settings/Calibration";

        [MenuItem("PicoTest/Import Factory Calibration (from metadata.json)")]
        public static void Import()
        {
            if (!File.Exists(MetaPath)) { Debug.LogError($"metadata not found: {MetaPath}"); return; }
            var json = JObject.Parse(File.ReadAllText(MetaPath));
            var fc = json["streams"]?["camera"]?["factory_calibration"];
            if (fc == null) { Debug.LogError("factory_calibration 缺失"); return; }

            var res = fc["resolution_per_eye_wh"];
            int w = (int)res[0], h = (int)res[1];
            Directory.CreateDirectory(OutDir);
            ImportEye(fc["left"], "RealLeft", w, h);
            ImportEye(fc["right"], "RealRight", w, h);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[FactoryCalibrationImporter] wrote RealLeft/RealRight ({w}x{h}), " +
                      $"model={fc["model"]}, baseline={fc["stereo_baseline_m"]}m");
        }

        private static void ImportEye(JToken eye, string name, int w, int h)
        {
            var K = eye["K"]; var D = eye["D"];
            var path = $"{OutDir}/{name}.asset";
            var cal = AssetDatabase.LoadAssetAtPath<FisheyeCalibration>(path);
            bool isNew = cal == null;
            if (isNew) cal = ScriptableObject.CreateInstance<FisheyeCalibration>();

            cal.fx = (float)K[0][0]; cal.cx = (float)K[0][2];
            cal.fy = (float)K[1][1]; cal.cy = (float)K[1][2];
            cal.k1 = (float)D[0]; cal.k2 = (float)D[1]; cal.k3 = (float)D[2];
            cal.k4 = (float)D[3]; cal.k5 = (float)D[4]; cal.k6 = (float)D[5];
            // 切向 p1=D[6], p2=D[7] 按设计简化丢弃（量级 ~1e-3）
            cal.width = w; cal.height = h;
            cal.extrinsicRotation = Quaternion.identity; // 首测：相机光轴=视线前方（双相机近平行，视差来自图像内容）

            if (isNew) AssetDatabase.CreateAsset(cal, path);
            else EditorUtility.SetDirty(cal);
        }
    }
}
