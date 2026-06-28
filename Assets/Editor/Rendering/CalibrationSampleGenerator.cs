// Assets/Editor/Rendering/CalibrationSampleGenerator.cs
using System.IO;
using UnityEditor;
using UnityEngine;
using PicoTest.Rendering;

namespace PicoTest.Editor.Rendering
{
    /// <summary>
    /// 生成左右占位标定资产（220° 等距鱼眼合理初值）。真实标定到位后直接覆盖字段。
    /// 菜单：PicoTest/Create Sample Calibrations。
    /// </summary>
    public static class CalibrationSampleGenerator
    {
        private const string Dir = "Assets/Main/Settings/Calibration";

        [MenuItem("PicoTest/Create Sample Calibrations")]
        public static void Create()
        {
            Directory.CreateDirectory(Dir);
            // 220° 视场：thetaMax=110°，等距 fx=(width/2)/thetaMax
            const int size = 1600;
            float thetaMax = 110f * Mathf.Deg2Rad;
            float f = (size / 2f) / thetaMax; // ≈416.7

            CreateOne("SampleLeft", f, size);
            CreateOne("SampleRight", f, size);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CalibrationSampleGenerator] wrote SampleLeft/SampleRight to {Dir} (placeholder, replace with real calibration)");
        }

        private static void CreateOne(string name, float f, int size)
        {
            var path = $"{Dir}/{name}.asset";
            var cal = AssetDatabase.LoadAssetAtPath<FisheyeCalibration>(path);
            bool isNew = cal == null;
            if (isNew) cal = ScriptableObject.CreateInstance<FisheyeCalibration>();

            cal.fx = f; cal.fy = f;
            cal.cx = size / 2f; cal.cy = size / 2f;
            cal.k1 = cal.k2 = cal.k3 = cal.k4 = 0f; // 占位，真实畸变待标定
            cal.width = size; cal.height = size;
            cal.extrinsicRotation = Quaternion.identity; // 占位，待相对机器人头外参

            if (isNew) AssetDatabase.CreateAsset(cal, path);
            else EditorUtility.SetDirty(cal);
        }
    }
}
