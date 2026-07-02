// Assets/Main/Scripts/Rendering/FisheyeDomeRenderer.cs
using UnityEngine;

namespace PicoTest.Rendering
{
    /// <summary>
    /// 双目鱼眼穹顶渲染器：装配反法线穹顶 + FisheyeDome 材质，按 RenderFrame 挂载，
    /// 把左右标定/纹理推到 MaterialPropertyBlock。决策 1：默认 WorldLocked（转头本地瞬时）。
    /// </summary>
    public sealed class FisheyeDomeRenderer : MonoBehaviour
    {
        public enum RenderFrame { WorldLocked, HeadLocked }
        public RenderFrame frame = RenderFrame.WorldLocked;

        [Header("标定（左右各一）")] public FisheyeCalibration leftCalibration, rightCalibration;
        [Header("纹理")] public Texture leftTex, rightTex;
        [Header("UV 子区 (x,y,scaleX,scaleY)；SBS 整图时左=(0,0,.5,1) 右=(.5,0,.5,1)")]
        public Vector4 leftUVRect = new Vector4(0, 0, 1, 1);
        public Vector4 rightUVRect = new Vector4(0, 0, 1, 1);
        [Header("WorldLocked 锚点")] public Transform robotHeadAnchor;
        [Header("穹顶")] public float coverageDeg = 220f; public int segments = 48; public float radius = 20f;
        [Tooltip("边缘羽化角(度)：最后这几度 alpha 渐隐，硬边圆弧柔化过渡到透视；0=硬切")]
        public float edgeFeatherDeg = 0f;
        [Range(0, 1)] public float flipV = 0, mirror = 0;
        public Shader domeShader; // 指 PicoTest/FisheyeDome；空则 Shader.Find

        public Transform DomeTransform { get; private set; }
        public MeshRenderer DomeRenderer { get; private set; }
        private MaterialPropertyBlock _mpb;

        public void Initialize()
        {
            var dome = new GameObject("FisheyeDome");
            DomeTransform = dome.transform;
            bool worldLocked = frame == RenderFrame.WorldLocked && robotHeadAnchor != null;
            DomeTransform.SetParent(worldLocked ? robotHeadAnchor : transform, false);
            DomeTransform.localScale = Vector3.one * radius;

            dome.AddComponent<MeshFilter>().sharedMesh = InvertedSphereMesh.Create(coverageDeg, segments);
            DomeRenderer = dome.AddComponent<MeshRenderer>();
            var shader = domeShader != null ? domeShader : Shader.Find("PicoTest/FisheyeDome");
            DomeRenderer.sharedMaterial = new Material(shader);
            _mpb = new MaterialPropertyBlock();
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
            _mpb.SetFloat("_ThetaMax", thetaMax);
            _mpb.SetFloat("_EdgeFeather", Mathf.Deg2Rad * edgeFeatherDeg);
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

        // 首版 WorldLocked：穹顶不随头转；HeadLocked / 低速云台伺服见 Task 7。
    }
}
