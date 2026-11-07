// ============================================================================
// Treasure Haul: 納品場所の表示板の 7 セグメント表示（Unlit・不透明）
//
// - 頂点色 R で点灯（1）／消灯（0）を選ぶ: color = lerp(_OffColor, _OnColor * _Intensity, R)
// - _Flash（0〜1）: 納品で一瞬明るくする。点灯側を中心に明るさを足す（透明は使わない）
// - PICO のシングルパス・インスタンシング（ステレオ）対応
// ============================================================================
Shader "TIB/TreasureHaul/SegmentDisplay"
{
    Properties
    {
        _OnColor ("On Color", Color) = (1.0, 0.678, 0.18, 1)
        _OffColor ("Off Color", Color) = (0.07, 0.047, 0.013, 1)
        _Intensity ("On Intensity", Float) = 1.0
        _Flash ("Flash (0-1)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "SegmentDisplay"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OnColor;
                half4 _OffColor;
                float _Intensity;
                float _Flash;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half lit : TEXCOORD0;
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
                output.lit = input.color.r;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 col = lerp(_OffColor.rgb, _OnColor.rgb * _Intensity, input.lit);
                col += _OnColor.rgb * _Flash * (0.25h + 0.75h * input.lit);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
