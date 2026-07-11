Shader "GIPoker/Stage/MoteParticle"
{
    // AmbientMotes（木漏れ日・胞子風パーティクル）用の最小限Unlit加算シェーダー。
    // テクスチャを持たず、Shurikenのビルボードクアッド（UV 0-1）のUV中心からの
    // 距離だけで丸く柔らかいドットを描く（テクスチャアセット不要＝プロシージャル）。
    // 頂点カラー（Color over Lifetime等）をそのまま乗算するため、
    // フェードイン/アウトはパーティクル側のモジュールとマテリアル操作の両方で制御できる。
    //
    // 既存 BorderLine.shader と同じCGPROGRAM構成を踏襲（URPでもLightModeタグなしで
    // SRPDefaultUnlitとして描画される）。Soft Particles・Collision・Lights・Trailsは
    // 一切使用しないため、モバイルXRでのオーバードローコストは最小限。
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Softness ("Edge Softness", Range(0.05, 0.9)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }

        Pass
        {
            Blend One One
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
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR0;
            };

            fixed4 _Color;
            float _Softness;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 centered = i.uv - 0.5;
                float dist = length(centered) * 2.0; // 中心0 → 端1
                float falloff = saturate(1.0 - smoothstep(1.0 - _Softness, 1.0, dist));

                fixed4 col = i.color;
                col.rgb *= falloff;
                col.a *= falloff;
                return col;
            }
            ENDCG
        }
    }
}
