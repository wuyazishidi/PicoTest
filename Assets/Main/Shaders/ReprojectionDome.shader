Shader "PicoTest/ReprojectionDome"
{
    // 纯 raw 视点重投影穹顶：顶点位置 = 视线方向 × 真实深度（深度烤进网格）。
    // frag：d = posOS − camOff（= EyeReprojection.CameraRayForEyeRay，米制）→ 鱼眼正投影 → 采样。
    // 眼视差来自「几何在真实深度 + XR 眼相机从各自眼点渲染」；采样正确来自 camOff。
    // 纯 raw：超 FOV / 出图 → 黑（不透明），不露系统透视。
    Properties
    {
        _LeftTex ("Left Eye", 2D) = "black" {}
        _RightTex ("Right Eye", 2D) = "black" {}
        _ThetaMax ("Theta Max (rad)", Float) = 1.91986
        _FlipV ("Flip V", Float) = 0
        _Mirror ("Mirror U", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        Cull Front      // 看穹顶内壁
        ZWrite Off
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 左右各一套：内参(fx,fy,cx,cy)、畸变(k1..k6)、外参 3x3、图像尺寸、眼→相机平移
            float4 _LeftIntrin, _RightIntrin;   // xy=f, zw=c
            float4 _LeftDist, _RightDist;       // k1..k4
            float4 _LeftDist2, _RightDist2;     // k5,k6,_,_
            float4x4 _LeftRot, _RightRot;       // 3x3 置于左上
            float4 _ImgSize;                    // xy = (w,h)
            float4 _LeftUVRect, _RightUVRect;   // 眼图 [0,1] → 图集子区: uv*zw + xy（SBS 分半）
            float4 _LeftCamOffset, _RightCamOffset; // 眼→相机光心平移 t (m)，xyz
            float _ThetaMax, _FlipV, _Mirror;
            TEXTURE2D(_LeftTex);  SAMPLER(sampler_LeftTex);
            TEXTURE2D(_RightTex); SAMPLER(sampler_RightTex);

            struct Attributes
            {
                float4 positionOS : POSITION;   // 已烤入深度：|pos| = 该方向的真实深度(m)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posOS : TEXCOORD0;        // 传原始位置（含深度），非归一化
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.posOS = v.positionOS.xyz;      // 顶点位置 = dirHat × 深度(m)
                return o;
            }

            // === 视点重投影：与 EyeReprojection.CameraRayForEyeRay 逐行一致 ===
            // posOS = depth·eHat（米制）；相机采样方向 = posOS − camOff = P − C。
            float3 CameraRay(float3 posOS, float3 camOff) { return posOS - camOff; }

            // === 与 FisheyeProjection.ProjectDirection 逐行一致（径向 k1..k6，Horner） ===
            float2 ProjectUV(float3 d, float4 intrin, float4 k, float4 k2c, float4x4 R, out bool inFov)
            {
                float3 c = mul((float3x3)R, d);
                float rxy = length(c.xy);
                float theta = atan2(rxy, c.z);
                inFov = theta <= _ThetaMax;
                float t2 = theta * theta;
                float thetaD = theta * (1 + t2*(k.x + t2*(k.y + t2*(k.z + t2*(k.w + t2*(k2c.x + t2*k2c.y))))));
                float2 phi = (rxy < 1e-6) ? float2(0, 0) : c.xy / rxy;
                float u = intrin.x * (thetaD * phi.x) + intrin.z;
                float v = intrin.y * (thetaD * phi.y) + intrin.w;
                float2 uv = float2(u / _ImgSize.x, v / _ImgSize.y);
                if (_Mirror > 0.5) uv.x = 1 - uv.x;
                if (_FlipV > 0.5)  uv.y = 1 - uv.y;
                return uv;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                bool isRight = unity_StereoEyeIndex == 1;
                bool inFov;
                float3 camOff = isRight ? _RightCamOffset.xyz : _LeftCamOffset.xyz;
                float3 d = CameraRay(i.posOS, camOff);   // 视点重投影
                float2 uv = isRight
                    ? ProjectUV(d, _RightIntrin, _RightDist, _RightDist2, _RightRot, inFov)
                    : ProjectUV(d, _LeftIntrin,  _LeftDist,  _LeftDist2,  _LeftRot,  inFov);
                if (!inFov) return half4(0, 0, 0, 1);            // 纯 raw：超 FOV → 黑
                float4 rect = isRight ? _RightUVRect : _LeftUVRect;
                float2 uva = uv * rect.zw + rect.xy;
                // 出图（含 SBS 子区外）→ 黑，避免边缘拉丝
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return half4(0, 0, 0, 1);
                return isRight
                    ? SAMPLE_TEXTURE2D(_RightTex, sampler_RightTex, uva)
                    : SAMPLE_TEXTURE2D(_LeftTex,  sampler_LeftTex,  uva);
            }
            ENDHLSL
        }
    }
}
