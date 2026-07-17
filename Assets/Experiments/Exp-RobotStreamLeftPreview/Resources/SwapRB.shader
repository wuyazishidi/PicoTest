// Assets/Experiments/Exp-RobotStreamLeftPreview/Resources/SwapRB.shader
// run_stereo_left_viewer.py 的服务端对 RGB ndarray 直接 cv2.imencode（未转 BGR），
// 送到浏览器天生 R/B 互换（该工具默认用 feColorMatrix 补偿）。这里在 Unity 侧用一次
// Graphics.Blit 复现同一补偿。放在 Resources/ 下按名 Resources.Load 加载，
// 免于依赖 ProjectSettings 的 Always Included Shaders 手工登记（新建资产还没有 GUID）。
Shader "Hidden/PicoTest/SwapRB"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                return fixed4(c.b, c.g, c.r, c.a);
            }
            ENDCG
        }
    }
}
