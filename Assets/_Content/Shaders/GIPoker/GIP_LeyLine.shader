// ============================================================================
// GIPoker MRステージ「島の侵蝕」: レイライン（床上を這う光の筋）
//
// UV.x をラインの長手方向として、グローバル _GIP_Erosion に連動して
// 根本（uv.x=0）から先端へ光が「伸びていく」＋流れのスクロール発光。
// - 加算系合成（パススルーで映える）。framebuffer alpha も蓄積する
// - グローバル _GIP_Unstable で金色化＋明滅（Unstable演出）
// - URP Unlit ベース / シングルパスインスタンシング対応 / half精度基本
// ============================================================================
Shader "GIPoker/Stage/LeyLine"
{
    Properties
    {
        _FlowTex ("Flow Pattern (R)", 2D) = "white" {}
        _LineColor ("Line Color", Color) = (0.4, 0.9, 0.65, 1)
        _GoldColor ("Gold Color (Unstable)", Color) = (1.0, 0.76, 0.32, 1)
        _ScrollSpeed ("Flow Scroll Speed", Range(0, 5)) = 0.8
        _FlowTiling ("Flow Tiling (along length)", Range(0.1, 16)) = 2
        _CoreSharpness ("Core Sharpness", Range(1, 16)) = 6
        _Intensity ("Emission Intensity", Range(0, 4)) = 1.2

        [Header(Erosion Remap)]
        _ErosionDelay ("Erosion Delay", Range(0, 0.9)) = 0
        _ErosionSpan ("Erosion Span", Range(0.05, 1)) = 0.8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-40"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "LeyLineUnlit"

            // 加算発光。alpha も加算蓄積し、パススルーコンポジタの
            // どちらの合成規約（premultiplied / straight）でも視認できるようにする
            Blend One One, One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            TEXTURE2D(_FlowTex);
            SAMPLER(sampler_FlowTex);

            // SRP Batcher 互換: マテリアルプロパティは UnityPerMaterial に収める
            CBUFFER_START(UnityPerMaterial)
                half4 _LineColor;
                half4 _GoldColor;
                half _ScrollSpeed;
                half _FlowTiling;
                half _CoreSharpness;
                half _Intensity;
                half _ErosionDelay;
                half _ErosionSpan;
            CBUFFER_END

            // グローバル（StageDirector が配信。UnityPerMaterial に入れない）
            float _GIP_Erosion;
            float _GIP_Unstable;

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
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half erosion = saturate((half(_GIP_Erosion) - _ErosionDelay) / max(_ErosionSpan, 0.05h));
                half unstable = saturate(half(_GIP_Unstable));

                // 根本から先端へ伸びる（uv.x が長手方向）。未到達領域は破棄
                half head = erosion * 1.25h - half(input.uv.x);
                clip(head);
                half grow = saturate(head / 0.2h);

                // 中心線ほど明るいコア + 流れる模様
                half offCenter = abs(half(input.uv.y) - 0.5h) * 2.0h;
                half core = pow(saturate(1.0h - offCenter), _CoreSharpness);
                half flow = SAMPLE_TEXTURE2D(_FlowTex, sampler_FlowTex,
                    float2(input.uv.x * _FlowTiling - _Time.y * _ScrollSpeed, input.uv.y)).r;

                // Unstable: 金色化＋明滅（振幅は控えめに）
                half flicker = 1.0h + unstable * 0.3h * sin(_Time.y * 8.0h + input.uv.x * 10.0h);
                half3 color = lerp(_LineColor.rgb, _GoldColor.rgb, unstable);

                half intensity = core * (0.35h + 0.65h * flow) * grow * _Intensity * flicker;
                return half4(color * intensity, saturate(intensity) * 0.6h);
            }
            ENDHLSL
        }
    }
}
