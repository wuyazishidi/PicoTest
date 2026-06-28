Shader "PicoTest/FisheyeDome"
{
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
        Cull Front      // 看球内壁
        ZWrite Off
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 左右各一套：内参(fx,fy,cx,cy)、畸变(k1..k4)、外参 3x3、图像尺寸
            float4 _LeftIntrin, _RightIntrin;   // xy=f, zw=c
            float4 _LeftDist, _RightDist;       // k1..k4
            float4 _LeftDist2, _RightDist2;     // k5,k6,_,_
            float4x4 _LeftRot, _RightRot;       // 3x3 置于左上
            float4 _ImgSize;                    // xy = (w,h)
            float4 _LeftUVRect, _RightUVRect;   // 眼图 [0,1] → 图集子区: uv*zw + xy（SBS 分半）
            float _ThetaMax, _FlipV, _Mirror;
            TEXTURE2D(_LeftTex);  SAMPLER(sampler_LeftTex);
            TEXTURE2D(_RightTex); SAMPLER(sampler_RightTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dirOS = normalize(v.positionOS.xyz);  // 视线方向 = 顶点方向（穹顶居中眼点）
                return o;
            }

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
                float2 uv = isRight
                    ? ProjectUV(i.dirOS, _RightIntrin, _RightDist, _RightDist2, _RightRot, inFov)
                    : ProjectUV(i.dirOS, _LeftIntrin,  _LeftDist,  _LeftDist2,  _LeftRot,  inFov);
                if (!inFov) return half4(0, 0, 0, 1);            // FOV 裁剪
                // 眼图 [0,1] → 图集子区（SBS：左半/右半）
                float4 rect = isRight ? _RightUVRect : _LeftUVRect;
                uv = uv * rect.zw + rect.xy;
                return isRight
                    ? SAMPLE_TEXTURE2D(_RightTex, sampler_RightTex, uv)
                    : SAMPLE_TEXTURE2D(_LeftTex,  sampler_LeftTex,  uv);
            }
            ENDHLSL
        }
    }
}
