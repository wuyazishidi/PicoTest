// Assets/Experiments/Exp-RobotDsDome/Scripts/DsDomeRenderer.cs
using UnityEngine;
using PicoTest.Rendering;   // InvertedSphereMesh（Main，穹顶网格复用）

namespace PicoTest.Experiments.RobotDsDome
{
    /// <summary>
    /// Double Sphere 穹顶渲染器：装配反法线穹顶 + RobotDsDome 材质，把 DS 标定/纹理推到 MPB。
    /// 结构同 Main 的 FisheyeDomeRenderer，只把等距鱼眼参数换成 DS（xi/alpha）。不改 Main。
    /// </summary>
    public sealed class DsDomeRenderer : MonoBehaviour
    {
        public enum RenderFrame { WorldLocked, HeadLocked }
        public RenderFrame frame = RenderFrame.WorldLocked;

        [Header("DS 标定（左右各一）")] public DsEyeCalibration leftCalibration, rightCalibration;
        [Header("纹理")] public Texture leftTex, rightTex;
        [Header("SBS UV 子区")]
        public Vector4 leftUVRect = new Vector4(0, 0, 1, 1);
        public Vector4 rightUVRect = new Vector4(0, 0, 1, 1);
        [Header("WorldLocked 锚点")] public Transform robotHeadAnchor;
        [Header("穹顶")] public float coverageDeg = 190f; public int segments = 64; public float radius = 2f;
        public float edgeFeatherDeg = 8f;
        public float boundsFeatherUV = 0.02f;
        public float bottomCutoffDeg = -90f;
        public float bottomFeatherDeg = 0f;
        [Range(0, 1)] public float flipV = 1f, mirror = 0f;
        public Shader domeShader; // 指 PicoTest/RobotDsDome；空则 Shader.Find

        public Transform DomeTransform { get; private set; }
        public MeshRenderer DomeRenderer { get; private set; }
        private MaterialPropertyBlock _mpb;

        public void Initialize()
        {
            var dome = new GameObject("RobotDsDome");
            DomeTransform = dome.transform;
            bool worldLocked = frame == RenderFrame.WorldLocked && robotHeadAnchor != null;
            DomeTransform.SetParent(worldLocked ? robotHeadAnchor : transform, false);
            DomeTransform.localScale = Vector3.one * radius;

            dome.AddComponent<MeshFilter>().sharedMesh = InvertedSphereMesh.Create(coverageDeg, segments);
            DomeRenderer = dome.AddComponent<MeshRenderer>();
            var shader = domeShader != null ? domeShader : Shader.Find("PicoTest/RobotDsDome");
            DomeRenderer.sharedMaterial = new Material(shader);
            _mpb = new MaterialPropertyBlock();
        }

        public void PushParameters()
        {
            if (DomeRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            DomeRenderer.GetPropertyBlock(_mpb);

            SetEye(_mpb, "_Left", leftCalibration, leftTex, "_LeftTex");
            SetEye(_mpb, "_Right", rightCalibration, rightTex, "_RightTex");
            _mpb.SetVector("_ImgSize", new Vector4(leftCalibration.width, leftCalibration.height, 0, 0));
            _mpb.SetVector("_LeftUVRect", leftUVRect);
            _mpb.SetVector("_RightUVRect", rightUVRect);

            // 覆盖角羽化用 cos(前向夹角)：dir.z 从 coverCos(边缘) 到 coverCos+feather(内) 渐入
            float half = Mathf.Deg2Rad * coverageDeg * 0.5f;
            float coverCos = Mathf.Cos(half);
            float featherCos = Mathf.Cos(Mathf.Max(0f, half - Mathf.Deg2Rad * edgeFeatherDeg)) - coverCos;
            _mpb.SetFloat("_CoverCos", coverCos);
            _mpb.SetFloat("_EdgeFeather", Mathf.Max(1e-4f, featherCos));
            _mpb.SetFloat("_BoundsFeather", boundsFeatherUV);
            float botCut = Mathf.Sin(Mathf.Deg2Rad * bottomCutoffDeg);
            float botTop = Mathf.Sin(Mathf.Deg2Rad * (bottomCutoffDeg + bottomFeatherDeg));
            _mpb.SetFloat("_BottomCut", botCut);
            _mpb.SetFloat("_BottomFeat", Mathf.Max(0f, botTop - botCut));
            _mpb.SetFloat("_FlipV", flipV);
            _mpb.SetFloat("_Mirror", mirror);

            DomeRenderer.SetPropertyBlock(_mpb);
        }

        private static void SetEye(MaterialPropertyBlock mpb, string prefix, DsEyeCalibration c, Texture tex, string texProp)
        {
            mpb.SetVector(prefix + "Intrin", new Vector4(c.fx, c.fy, c.cx, c.cy));
            mpb.SetVector(prefix + "Ds", new Vector4(c.xi, c.alpha, c.ComputeW2(), 0));
            mpb.SetMatrix(prefix + "Rot", c.ExtrinsicMatrix());
            if (tex != null) mpb.SetTexture(texProp, tex);
        }
    }
}
