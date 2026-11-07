// ============================================================================
// Treasure Haul: 亡霊（墓守の霊）の視界の扇（床に落ちる灯りの光 / Unlit・アルファブレンド）
//
// UV: u = 角度（左端 0 〜 右端 1）・v = 要からの距離（要 0 〜 弧 1）（PhantomView.BuildFan が作る）
// - 左右の縁（u の両端）と先端の弧（v → 1）でやわらかく消える
// - 弧の少し内側（v ≈ 0.9）が明るい線
// - 外へゆっくり流れる波紋（時間）
// - 要の近く（v ≈ 0）も少し薄い
// - 色と不透明度は _BaseColor（PhantomView が MaterialPropertyBlock で _BaseColor と _Color を塗る）
// - ZWrite Off・両面・描画順 2899（体の 2900 より前）。PICO のシングルパス・インスタンシング（ステレオ）対応
// ============================================================================
Shader "TIB/TreasureHaul/PhantomFan"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color (A = Opacity)", Color) = (0.6, 0.95, 1, 0.12)
        _EdgeSoftness ("Side Edge Softness (u)", Range(0.01, 0.5)) = 0.18
        _ArcSoftness ("Arc Edge Softness (v)", Range(0.01, 0.5)) = 0.12
        _ArcLineV ("Arc Line Position (v)", Range(0.5, 0.99)) = 0.9
        _ArcLineWidth ("Arc Line Width (v)", Range(0.005, 0.3)) = 0.04
        _ArcLineBoost ("Arc Line Brightness", Range(0, 6)) = 2.5
        _RippleCount ("Ripple Count", Float) = 4
        _RippleSpeed ("Ripple Speed", Float) = 0.35
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.35
        _NearFadeV ("Near Apex Fade (v)", Range(0.01, 0.5)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-101"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "PhantomFan"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _EdgeSoftness;
                half _ArcSoftness;
                half _ArcLineV;
                half _ArcLineWidth;
                half _ArcLineBoost;
                half _RippleCount;
                half _RippleSpeed;
                half _RippleStrength;
                half _NearFadeV;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half u = input.uv.x;
                half v = input.uv.y;

                // 左右の縁と先端の弧でやわらかく消える・要の近くも少し薄い
                half side = smoothstep(0.0h, _EdgeSoftness, u) * smoothstep(0.0h, _EdgeSoftness, 1.0h - u);
                half arc = 1.0h - smoothstep(1.0h - _ArcSoftness, 1.0h, v);
                half apex = lerp(0.35h, 1.0h, smoothstep(0.0h, _NearFadeV, v));
                half mask = side * arc * apex;

                // 弧の少し内側の明るい線
                half ln = 1.0h - saturate(abs(v - _ArcLineV) / _ArcLineWidth);
                ln *= ln;

                // 外へ流れる波紋（時間とともに v が増える向きへ進む）
                half ripple = 0.5h + 0.5h * sin((v * _RippleCount - _Time.y * _RippleSpeed) * 6.2831853h);
                half body = lerp(1.0h - _RippleStrength, 1.0h, ripple);

                half intensity = body + ln * _ArcLineBoost;
                half alpha = _BaseColor.a * mask * intensity;
                half3 col = lerp(_BaseColor.rgb, half3(1, 1, 1), saturate(ln * 0.35h));
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
