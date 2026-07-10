// Assets/Experiments/Exp-RobotDsDome/Editor/DsCamchainImporter.cs
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PicoTest.Experiments.RobotDsDome.Editor
{
    /// <summary>
    /// 从 StreamingAssets/3-camchain.yaml 导入 cam0/cam1 的 DS 标定 → RobotDsLeft/Right.asset。
    /// 菜单：PicoTest/Robot DS Dome/Import Camchain。解析走纯 C# DsCamchainParser（可单测）。
    /// </summary>
    public static class DsCamchainImporter
    {
        private const string YamlPath = "Assets/StreamingAssets/3-camchain.yaml";
        private const string OutDir = "Assets/Experiments/Exp-RobotDsDome/Calibration";

        [MenuItem("PicoTest/Robot DS Dome/Import Camchain")]
        public static void Import()
        {
            if (!File.Exists(YamlPath)) { Debug.LogError($"[DsImporter] 未找到 {YamlPath}"); return; }

            var chain = DsCamchainParser.Parse(File.ReadAllText(YamlPath));
            Directory.CreateDirectory(OutDir);
            WriteEye(chain.cam0, "RobotDsLeft");
            WriteEye(chain.cam1, "RobotDsRight");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[DsImporter] 写入 RobotDsLeft/Right（{chain.cam0.width}x{chain.cam0.height}, ds），" +
                      $"基线={chain.baselineM * 1000:F1}mm。cam0={FmtCam(chain.cam0)} cam1={FmtCam(chain.cam1)}");
        }

        private static void WriteEye(DsCam c, string name)
        {
            string path = $"{OutDir}/{name}.asset";
            var cal = AssetDatabase.LoadAssetAtPath<DsEyeCalibration>(path);
            bool isNew = cal == null;
            if (isNew) cal = ScriptableObject.CreateInstance<DsEyeCalibration>();
            cal.xi = (float)c.xi; cal.alpha = (float)c.alpha;
            cal.fx = (float)c.fx; cal.fy = (float)c.fy; cal.cx = (float)c.cx; cal.cy = (float)c.cy;
            cal.width = c.width; cal.height = c.height;
            cal.extrinsicRotation = Quaternion.identity;  // v1：两目近平行，无 cam→头外参
            if (isNew) AssetDatabase.CreateAsset(cal, path);
            else EditorUtility.SetDirty(cal);
        }

        private static string FmtCam(DsCam c) =>
            $"[xi={c.xi:F4} a={c.alpha:F4} f=({c.fx:F1},{c.fy:F1}) c=({c.cx:F1},{c.cy:F1})]";
    }
}
