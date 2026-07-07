Shader "TiB/BorderLine"
{
    Properties
    {
        _Color ("Line Color", Color) = (1, 1, 1, 1)
        _LineWidth ("Line Width", Range(0.001, 0.1)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            float _LineWidth;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // UV の端からの距離を計算
                float distLeft   = uv.x;
                float distRight  = 1.0 - uv.x;
                float distBottom = uv.y;
                float distTop    = 1.0 - uv.y;

                float minDist = min(min(distLeft, distRight), min(distBottom, distTop));

                // 線の内側ならば描画、それ以外は透明
                float alpha = step(minDist, _LineWidth) * _Color.a;

                return fixed4(_Color.rgb, alpha);
            }
            ENDCG
        }
    }
}
