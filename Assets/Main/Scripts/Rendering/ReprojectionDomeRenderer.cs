// Assets/Main/Scripts/Rendering/ReprojectionDomeRenderer.cs
using UnityEngine;

namespace PicoTest.Rendering
{
    /// <summary>
    /// 纯 raw 视点重投影穹顶渲染器（独立于 FisheyeDomeRenderer）。
    /// 建单位反法线穹顶 → 每帧按 <see cref="IDepthSurface"/> 把顶点位移到 dirHat×深度(米)
    /// → shader 用顶点位置(含深度)做重投影采样 + XR 眼相机渲染出视差。
    /// 深度=常量时退化为无穷远穹顶(M0)；深度=spatial mesh 时得近景视差(M1)。
    /// 坐标系：穹顶挂 headAnchor（跟头位置+朝向，HeadLocked），localScale=1，顶点单位=米。
    /// </summary>
    public sealed class ReprojectionDomeRenderer : MonoBehaviour
    {
        [Header("标定（左右各一）")] public FisheyeCalibration leftCalibration, rightCalibration;
        [Header("纹理")] public Texture leftTex, rightTex;
        [Header("UV 子区；SBS 整图 左=(0,0,.5,1) 右=(.5,0,.5,1)")]
        public Vector4 leftUVRect = new Vector4(0, 0, 1, 1);
        public Vector4 rightUVRect = new Vector4(0.5f, 0, 0.5f, 1);
        [Header("HeadLocked 锚点")] public Transform headAnchor;
        [Header("穹顶")] public float coverageDeg = 160f; public int segments = 64;
        [Range(0, 1)] public float flipV = 0, mirror = 0;
        public Shader domeShader; // 指 PicoTest/ReprojectionDome；空则 Shader.Find

        public IDepthSurface DepthSurface { get; set; } = new ConstantDepthSurface(20f);

        public Transform DomeTransform { get; private set; }
        public MeshRenderer DomeRenderer { get; private set; }
        private Mesh _mesh;
        private Vector3[] _baseDirs;   // 单位方向（穹顶几何，不变）
        private Vector3[] _verts;      // 位移后的顶点（dir×深度），每帧写
        private MaterialPropertyBlock _mpb;

        public void Initialize()
        {
            var dome = new GameObject("ReprojectionDome");
            DomeTransform = dome.transform;
            DomeTransform.SetParent(headAnchor != null ? headAnchor : transform, false);
            DomeTransform.localScale = Vector3.one; // 顶点已是米制真实深度

            _mesh = InvertedSphereMesh.Create(coverageDeg, segments);
            _baseDirs = _mesh.vertices;                 // 单位球顶点 = 方向
            _verts = new Vector3[_baseDirs.Length];

            dome.AddComponent<MeshFilter>().sharedMesh = _mesh;
            DomeRenderer = dome.AddComponent<MeshRenderer>();
            var shader = domeShader != null ? domeShader : Shader.Find("PicoTest/ReprojectionDome");
            DomeRenderer.sharedMaterial = new Material(shader);
            _mpb = new MaterialPropertyBlock();

            ApplyDepth();      // 首次位移
            PushParameters();
        }

        /// <summary>按深度面位移顶点（M0 常量一次即可；M1 每帧调）。</summary>
        public void ApplyDepth()
        {
            if (_mesh == null) return;
            DepthSurface?.Tick(headAnchor != null ? headAnchor : transform);
            Displace(_baseDirs, DepthSurface, _verts);
            _mesh.vertices = _verts;
            _mesh.RecalculateBounds();
        }

        /// <summary>纯函数：把单位方向按深度面位移到 dir×深度（可测，不依赖 MonoBehaviour）。</summary>
        public static void Displace(Vector3[] baseDirs, IDepthSurface surface, Vector3[] outVerts)
        {
            for (int i = 0; i < baseDirs.Length; i++)
            {
                Vector3 dir = baseDirs[i];             // 已是单位（InvertedSphereMesh 单位球）
                float depth = surface != null ? surface.SampleDepth(dir) : 20f;
                outVerts[i] = dir * depth;
            }
        }

        public void PushParameters()
        {
            if (DomeRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            DomeRenderer.GetPropertyBlock(_mpb);

            float thetaMax = Mathf.Deg2Rad * coverageDeg * 0.5f;
            SetEye(_mpb, "_Left", leftCalibration, leftTex, "_LeftTex");
            SetEye(_mpb, "_Right", rightCalibration, rightTex, "_RightTex");
            _mpb.SetVector("_ImgSize", new Vector4(leftCalibration.width, leftCalibration.height, 0, 0));
            _mpb.SetVector("_LeftUVRect", leftUVRect);
            _mpb.SetVector("_RightUVRect", rightUVRect);
            _mpb.SetVector("_LeftCamOffset", leftCalibration.extrinsicTranslation);
            _mpb.SetVector("_RightCamOffset", rightCalibration.extrinsicTranslation);
            _mpb.SetFloat("_ThetaMax", thetaMax);
            _mpb.SetFloat("_FlipV", flipV);
            _mpb.SetFloat("_Mirror", mirror);

            DomeRenderer.SetPropertyBlock(_mpb);
        }

        private static void SetEye(MaterialPropertyBlock mpb, string prefix, FisheyeCalibration c, Texture tex, string texProp)
        {
            mpb.SetVector(prefix + "Intrin", new Vector4(c.fx, c.fy, c.cx, c.cy));
            mpb.SetVector(prefix + "Dist", new Vector4(c.k1, c.k2, c.k3, c.k4));
            mpb.SetVector(prefix + "Dist2", new Vector4(c.k5, c.k6, 0, 0));
            mpb.SetMatrix(prefix + "Rot", c.ExtrinsicMatrix());
            if (tex != null) mpb.SetTexture(texProp, tex);
        }
    }
}
