// Assets/Main/Scripts/Rendering/FisheyeDomeXRRig.cs
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace PicoTest.Rendering
{
    /// <summary>
    /// XR 版鱼眼穹顶装配：穹顶跟随头显位置、世界锁朝向（WorldLocked），由 XR 相机渲染
    /// → 真机上单通道立体实例化 shader 让左右眼各采 SBS 半幅 = 真立体。
    /// 相机由场景里的 XR Origin 提供（Camera.main）；本组件只碰 Camera/Transform/Texture，不引 XR 包。
    /// 帧用 UnityWebRequest 加载（编辑器/PC/Android 通吃）；真机最终换成双目流实时纹理。
    /// </summary>
    public sealed class FisheyeDomeXRRig : MonoBehaviour
    {
        [Header("真实标定（左右各一）")]
        public FisheyeCalibration leftCalibration, rightCalibration;
        [Header("鱼眼帧（StreamingAssets 下）")]
        public string frameFileName = "sbs_frame.png";
        [Header("穹顶覆盖角 / 半径")]
        public float coverageDeg = 160f;
        public float radius = 20f;

        private FisheyeDomeRenderer _dome;
        private Transform _anchor;
        private Camera _xrCam;

        private void Start()
        {
            if (leftCalibration == null || rightCalibration == null)
            {
                Debug.LogError("[FisheyeDomeXRRig] 未连标定（RealLeft/RealRight）。");
                return;
            }

            // 跟随头显位置、世界锁朝向的锚点；穹顶挂其下
            _anchor = new GameObject("DomeAnchor").transform;
            _anchor.SetParent(transform, false);

            _dome = gameObject.AddComponent<FisheyeDomeRenderer>();
            _dome.frame = FisheyeDomeRenderer.RenderFrame.WorldLocked;
            _dome.robotHeadAnchor = _anchor;
            _dome.leftCalibration = leftCalibration;
            _dome.rightCalibration = rightCalibration;
            _dome.leftUVRect = new Vector4(0f, 0f, 0.5f, 1f);    // SBS 左半 → 左眼
            _dome.rightUVRect = new Vector4(0.5f, 0f, 0.5f, 1f); // SBS 右半 → 右眼
            _dome.coverageDeg = coverageDeg; _dome.radius = radius; _dome.segments = 64;
            _dome.Initialize();

            StartCoroutine(LoadFrameThenPush());
        }

        private IEnumerator LoadFrameThenPush()
        {
            string url = Application.streamingAssetsPath + "/" + frameFileName;
            if (!url.Contains("://")) url = "file://" + url; // 非 Android 需 file:// 前缀

            using (var uwr = UnityWebRequestTexture.GetTexture(url))
            {
                yield return uwr.SendWebRequest();
                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    var tex = DownloadHandlerTexture.GetContent(uwr);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    _dome.leftTex = tex; _dome.rightTex = tex;
                }
                else
                {
                    Debug.LogWarning($"[FisheyeDomeXRRig] 帧加载失败 {url}: {uwr.error}\n" +
                                     "真机最终用双目流实时纹理替代静态帧。");
                }
            }
            _dome.PushParameters();
        }

        private void LateUpdate()
        {
            // 穹顶跟头显位置（始终罩住眼点），但不跟头转 → 转头在静止穹顶内环顾
            if (_xrCam == null)
            {
                _xrCam = Camera.main;
                if (_xrCam != null)
                {
                    // 穹顶 ZWrite Off 是背景，必须关天空盒否则被盖住（遥操作背景=黑）
                    _xrCam.clearFlags = CameraClearFlags.SolidColor;
                    _xrCam.backgroundColor = Color.black;
                }
            }
            if (_xrCam != null && _anchor != null)
                _anchor.position = _xrCam.transform.position;
        }
    }
}
