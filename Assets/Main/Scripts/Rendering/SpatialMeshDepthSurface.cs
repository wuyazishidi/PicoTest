// Assets/Main/Scripts/Rendering/SpatialMeshDepthSurface.cs
using UnityEngine;

namespace PicoTest.Rendering
{
    /// <summary>
    /// M1 深度面：对 PICO spatial mesh（世界几何，两眼共用）做射线投射取深度。
    /// 依赖场景里有带 MeshCollider 的空间网格（PICO PXR_SpatialMeshManager 会生成可碰撞网格 GO）。
    /// 命中 → hit.distance 为深度；未命中（编辑器无网格 / 望向空处）→ fallbackDepth（远景）。
    /// 纯几何射线，无 PICO 程序集编译期依赖 → 编辑器可编译、可跑（无网格时退化为常量）。
    /// </summary>
    public sealed class SpatialMeshDepthSurface : IDepthSurface
    {
        public LayerMask meshLayers = ~0;   // 空间网格所在层（默认全部）
        public float fallbackDepth = 20f;   // 未命中时的远景深度
        public float minDepth = 0.15f;      // 深度下限（避免贴脸自遮挡）
        public float maxDepth = 30f;

        private Transform _head;

        public void Tick(Transform head) { _head = head; }

        public float SampleDepth(Vector3 dirLocalHat)
        {
            if (_head == null) return fallbackDepth;
            Vector3 origin = _head.position;
            Vector3 dirWorld = _head.TransformDirection(dirLocalHat);
            if (Physics.Raycast(origin, dirWorld, out RaycastHit hit, maxDepth, meshLayers, QueryTriggerInteraction.Ignore))
                return Mathf.Clamp(hit.distance, minDepth, maxDepth);
            return fallbackDepth;
        }
    }
}
