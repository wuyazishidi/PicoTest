// Assets/Main/Scripts/Rendering/IDepthSurface.cs
using UnityEngine;

namespace PicoTest.Rendering
{
    /// <summary>
    /// 深度面：给穹顶某方向的视线返回真实深度(米)。渲染器据此把顶点位移到 dirHat×深度，
    /// 从而得到视点重投影(视差)。M0 用常量(退化为无穷远穹顶)，M1 用 spatial mesh。
    /// </summary>
    public interface IDepthSurface
    {
        /// <summary>头中心出发、单位方向 dirLocalHat（穹顶局部坐标）上的深度(米)。</summary>
        float SampleDepth(Vector3 dirLocalHat);

        /// <summary>每帧刷新（spatial mesh 更新/位姿变化）；常量实现可空实现。</summary>
        void Tick(Transform head);
    }

    /// <summary>M0：所有方向同一深度（= 固定半径穹顶，仅远景 1:1，近景视差未解）。</summary>
    public sealed class ConstantDepthSurface : IDepthSurface
    {
        public float depth;
        public ConstantDepthSurface(float depthMeters) { depth = depthMeters; }
        public float SampleDepth(Vector3 dirLocalHat) => depth;
        public void Tick(Transform head) { }
    }
}
