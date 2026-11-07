// ============================================================================
// Treasure Haul: 亡霊（墓守の霊）の体（光を受けない半透明 / Unlit）
//
// - 縁ほど明るい（フレネル）。中心は薄く、縁は濃い
// - 頂点色 RGB を色に掛ける（_VertexColorShade 0〜1。ひだの陰）
// - 裾が消える: オブジェクト空間の高さ（_FadeBottom で完全に消え、_FadeTop で不透明度が最大）。頂点色 A も掛ける（袖の先をぼかす）
// - 目の前が近いと薄くなる: カメラから _NearFadeStart m より近いと薄くなり、_NearFadeEnd m で 0（体がすり抜けたとき視界をふさがない）
// - 裾の頂点を時間でゆっくり揺らす（下ほど大きく、上 _SwayTop m より上は動かさない）
// - 自分自身の重なりを 1 層にする 2 パス:
//     1 つ目（LightMode SRPDefaultUnlit）= 深度だけ書く（ColorMask 0）
//     2 つ目（LightMode UniversalForward）= 色（ZTest LEqual・ZWrite Off・アルファブレンド）
//   URP は SRPDefaultUnlit・UniversalForward・UniversalForwardOnly を同じ描画呼び出しで描く（DrawObjectsPass）ので、両方とも描かれる
// - 描画順 3000（Transparent。後ろの値札・ビームは先に描かれて体が上に重なり、手前の物は後に描かれて深度に通る）。PICO のシングルパス・インスタンシング（ステレオ）対応
// ============================================================================
Shader "TIB/TreasureHaul/PhantomGhost"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color (A = Opacity)", Color) = (0.55, 0.75, 0.9, 0.55)
        _RimColor ("Rim Color", Color) = (0.8, 0.95, 1.0, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimIntensity ("Rim Intensity", Range(0, 3)) = 1.2
        _CenterAlpha ("Center Alpha (0 = Transparent Center)", Range(0, 1)) = 0.5
        _FadeBottom ("Fade Bottom Height (m, Object Space)", Float) = 0.15
        _FadeTop ("Fade Top Height (m, Object Space)", Float) = 0.7
        _NearFadeStart ("Near Fade Start (m)", Float) = 0.6
        _NearFadeEnd ("Near Fade End (m)", Float) = 0.25
        _SwayAmount ("Sway Amount (m)", Float) = 0.04
        _SwaySpeed ("Sway Speed", Float) = 1.2
        _SwayTop ("Sway Top Height (m, No Sway Above)", Float) = 1.0
        _VertexColorShade ("Vertex Color RGB Shade (Fold Shadows)", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        HLSLINCLUDE
        #pragma target 2.0
        #pragma multi_compile_instancing
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _RimColor;
            half _RimPower;
            half _RimIntensity;
            half _CenterAlpha;
            float _FadeBottom;
            float _FadeTop;
            float _NearFadeStart;
            float _NearFadeEnd;
            float _SwayAmount;
            float _SwaySpeed;
            float _SwayTop;
            half _VertexColorShade;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float heightOS : TEXCOORD2;   // 揺らす前の高さ（裾の消え方が揺れで動かないように）
            half vertexAlpha : TEXCOORD3;
            half3 vertexShade : TEXCOORD4;   // 頂点色 RGB = 折り目の陰（1 = 陰なし）
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        // 裾の揺れ（オブジェクト空間。下ほど大きく、_SwayTop より上は 0）。2 つのパスで同じ式を使う（深度が一致するように）
        float3 SwayOffset(float3 positionOS)
        {
            float w = saturate(1.0 - positionOS.y / max(_SwayTop, 1e-3));
            w *= w;
            float t = _Time.y * _SwaySpeed;
            float phase = positionOS.y * 3.0 + positionOS.x * 1.7 + positionOS.z * 2.3;
            return float3(sin(t + phase), 0.0, cos(t * 0.83 + phase * 1.3)) * (_SwayAmount * w);
        }

        Varyings vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float3 posOS = input.positionOS.xyz + SwayOffset(input.positionOS.xyz);
            output.positionWS = TransformObjectToWorld(posOS);
            output.positionHCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.heightOS = input.positionOS.y;
            output.vertexAlpha = input.color.a;
            output.vertexShade = lerp(half3(1, 1, 1), input.color.rgb, _VertexColorShade);
            return output;
        }

        // 裾・頂点色 A・目の前の近さによる不透明度の係数（0〜1）。深度の先行パスの切り抜きにも使う
        half FadeFactor(Varyings input)
        {
            half hFade = smoothstep(_FadeBottom, max(_FadeTop, _FadeBottom + 1e-3), input.heightOS);
            float dist = distance(GetCameraPositionWS(), input.positionWS);
            half nearFade = smoothstep(_NearFadeEnd, max(_NearFadeStart, _NearFadeEnd + 1e-3), dist);
            return hFade * saturate(input.vertexAlpha) * nearFade;
        }
        ENDHLSL

        // 1: 深度だけ書く。自分自身の奥の面を、色のパスの ZTest で落として 1 層にする
        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth

            half4 fragDepth(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // ほぼ消えている所は深度も書かない（裾の向こうの物を隠さない・近づいたとき視界をふさがない）
                clip(FadeFactor(input) - 0.1);
                return 0;
            }
            ENDHLSL
        }

        // 2: 色
        Pass
        {
            Name "GhostColor"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1   // 先行パスと同じ位置の深度でも、浮動小数の誤差でちらつかないよう、色のパスをわずかに手前へ
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragColor

            half4 fragColor(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS);
                half3 v = normalize(GetCameraPositionWS() - input.positionWS);
                half rim = pow(saturate(1.0h - abs(dot(n, v))), _RimPower);
                half3 col = (_BaseColor.rgb + _RimColor.rgb * (rim * _RimIntensity)) * input.vertexShade;
                half alpha = _BaseColor.a * lerp(_CenterAlpha, 1.0h, rim) * FadeFactor(input);
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
