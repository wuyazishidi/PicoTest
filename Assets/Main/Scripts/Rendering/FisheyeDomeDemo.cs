// Assets/Main/Scripts/Rendering/FisheyeDomeDemo.cs
using System.IO;
using UnityEngine;

namespace PicoTest.Rendering
{
    /// <summary>
    /// 编辑器 demo：把真实鱼眼帧(StreamingAssets/sbs_frame.png, SBS)投到静止穹顶，
    /// 相机在穹顶内缓慢扫视，肉眼看整个 FOV 的去畸变还原。按 Play 即可。
    /// 真机上换成头部/XR 驱动相机即同款体验。
    /// </summary>
    public sealed class FisheyeDomeDemo : MonoBehaviour
    {
        [Header("真实标定（左右各一，场景里已连 RealLeft/RealRight）")]
        public FisheyeCalibration leftCalibration, rightCalibration;
        [Header("鱼眼帧文件名（StreamingAssets 下）")]
        public string frameFileName = "sbs_frame.png";
        [Header("穹顶覆盖角 / 相机 FOV / 扫视幅度(度)")]
        public float coverageDeg = 150f;
        public float cameraFov = 70f;
        public float sweepAmplitudeDeg = 35f;
        public float sweepSpeed = 0.3f;

        private Camera _cam;

        private void Start()
        {
            // 1) 载入真实鱼眼帧（编辑器/PC：StreamingAssets 是裸文件，直接读）
            var path = Path.Combine(Application.streamingAssetsPath, frameFileName);
            Texture2D tex = null;
            if (File.Exists(path))
            {
                tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
                tex.LoadImage(File.ReadAllBytes(path));
            }
            else
            {
                Debug.LogWarning($"[FisheyeDomeDemo] 帧不存在：{path}\n" +
                                 "跑 Tools\\extract-fisheye-frame.ps1 从 camera.mp4 抽帧（真人数据不入库）。");
            }

            if (leftCalibration == null || rightCalibration == null)
            {
                Debug.LogError("[FisheyeDomeDemo] 未连标定。场景应连 RealLeft/RealRight；" +
                               "或先跑菜单 PicoTest/Import Factory Calibration。");
                return;
            }

            // 2) 静止穹顶（挂在本物体，本物体不动；相机另建并扫视）
            var dome = gameObject.AddComponent<FisheyeDomeRenderer>();
            dome.frame = FisheyeDomeRenderer.RenderFrame.WorldLocked;
            dome.robotHeadAnchor = transform;      // 静止锚点
            dome.leftCalibration = leftCalibration;
            dome.rightCalibration = rightCalibration;
            dome.leftTex = tex; dome.rightTex = tex;
            dome.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);    // SBS 左半
            dome.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f); // SBS 右半
            dome.coverageDeg = coverageDeg; dome.radius = 10f; dome.segments = 64;
            dome.Initialize();
            dome.PushParameters();

            // 3) 相机（在穹顶内，扫视）
            var camGo = new GameObject("DemoCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = Vector3.zero;
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.fieldOfView = cameraFov;
            _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 100f;
        }

        private void Update()
        {
            if (_cam == null) return;
            float yaw = Mathf.Sin(Time.time * sweepSpeed) * sweepAmplitudeDeg;
            _cam.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
