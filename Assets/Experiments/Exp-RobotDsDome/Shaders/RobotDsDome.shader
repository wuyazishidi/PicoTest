Shader "PicoTest/RobotDsDome"
{
    Properties
    {
        _LeftTex ("Left Eye", 2D) = "black" {}
        _RightTex ("Right Eye", 2D) = "black" {}
        _EdgeFeather ("Edge Feather (rad)", Float) = 0
        _BoundsFeather ("Bounds Feather (uv)", Float) = 0
        _BottomCut ("Bottom Cut (sin elev)", Float) = -1
        _BottomFeat ("Bottom Feather (sin)", Float) = 0
        _FlipV ("Flip V", Float) = 1
        _Mirror ("Mirror U", Float) = 0
        _CoverCos ("Coverage cos(half)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        Cull Front
        ZWrite Off
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 左右各一套：内参(fx,fy,cx,cy)、DS(xi,alpha,w2,_)、外参 3x3、图像尺寸、SBS 子区
            float4 _LeftIntrin, _RightIntrin;   // xy=(fx,fy), zw=(cx,cy)
            float4 _LeftDs, _RightDs;           // (xi, alpha, w2, _)
            float4x4 _LeftRot, _RightRot;       // 3x3 于左上（相机→头 的逆，作用于 dir）
            float4 _ImgSize;                    // xy = (w,h)（标定分辨率）
            float4 _LeftUVRect, _RightUVRect;   // 眼图 [0,1] → 图集子区（SBS 分半）
            float _EdgeFeather, _BoundsFeather, _BottomCut, _BottomFeat, _FlipV, _Mirror, _CoverCos;
            TEXTURE2D(_LeftTex);  SAMPLER(sampler_LeftTex);
            TEXTURE2D(_RightTex); SAMPLER(sampler_RightTex);

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
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
                o.dirOS = normalize(v.positionOS.xyz);
                return o;
            }

            // === 与 DoubleSphereProjection.ProjectDirection 逐行一致（DS 前向投影）===
            // 输入 dir 为 dome 世界系(y-up)；此处 c.y=-c.y 转相机系(y-down)后照抄 ds_project。
            float2 ProjectUV(float3 d, float4 intrin, float4 ds, float4x4 R, out bool valid)
            {
                float3 c = mul((float3x3)R, d);
                c.y = -c.y;                       // dome y-up → 相机 y-down（投影公式本身不含此翻转）
                float X = c.x, Y = c.y, Z = c.z;
                float xi = ds.x, alpha = ds.y, w2 = ds.z;
                float d1 = length(c);
                float k = xi * d1 + Z;
                float d2 = sqrt(X * X + Y * Y + k * k);
                float nrm = alpha * d2 + (1.0 - alpha) * k;
                valid = (nrm > 1e-6) && (Z > -w2 * d1);
                float ns = valid ? nrm : 1.0;
                float u = intrin.x * X / ns + intrin.z;
                float v = intrin.y * Y / ns + intrin.w;
                float2 uv = float2(u / _ImgSize.x, v / _ImgSize.y);
                if (_Mirror > 0.5) uv.x = 1 - uv.x;
                if (_FlipV > 0.5)  uv.y = 1 - uv.y;
                return uv;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                bool isRight = unity_StereoEyeIndex == 1;
                bool valid;
                float3 dir = normalize(i.dirOS);
                float2 uvEye = isRight
                    ? ProjectUV(dir, _RightIntrin, _RightDs, _RightRot, valid)
                    : ProjectUV(dir, _LeftIntrin,  _LeftDs,  _LeftRot,  valid);
                if (!valid) return half4(0, 0, 0, 0);           // DS 无效域 → 透明，露原生透视

                float4 rect = isRight ? _RightUVRect : _LeftUVRect;
                float2 uv = uvEye * rect.zw + rect.xy;
                half4 col = isRight
                    ? SAMPLE_TEXTURE2D(_RightTex, sampler_RightTex, uv)
                    : SAMPLE_TEXTURE2D(_LeftTex,  sampler_LeftTex,  uv);

                // (a) 角度边缘羽化：穹顶覆盖角（cos 与前向 dir.z 比）内渐隐
                half aCover = (_EdgeFeather > 1e-4) ? (half)saturate((dir.z - _CoverCos) / _EdgeFeather) : 1.0h;
                // (b) 图像边界羽化：采样 UV 出眼图 [0,1] → 透明并羽化
                float2 dEdge = min(uvEye, 1.0 - uvEye);
                float md = min(dEdge.x, dEdge.y);
                half aBounds = (_BoundsFeather > 1e-5) ? (half)saturate(md / _BoundsFeather) : (half)step(0.0, md);
                // (c) 底部水平截断（世界仰角 dir.y）
                float elev = dir.y;
                half aBottom = (_BottomFeat > 1e-5) ? (half)saturate((elev - _BottomCut) / _BottomFeat) : 1.0h;
                col.a = min(min(aCover, aBounds), aBottom);
                return col;
            }
            ENDHLSL
        }
    }
}
