// ============================================================================
// GIPoker MRステージ「島の侵蝕」: Shadow Catcher（パススルー用影受け）
//
// 仮想オブジェクトがメインライトで落とす影の「減衰量」だけを
// 半透明の黒として出力し、現実の床がその分だけ暗くなって見える。
// - 影のない画素は alpha=0（パススルーがそのまま見える）
// - framebuffer alpha にも影の濃度を書き込み、コンポジタが
//   premultiplied / straight のどちらの規約でも黒(0,0,0)は正しく合成される
// - オクルーダー（同一平面の床）との z-fight 回避に Offset -1,-1
// - シングルパスインスタンシング対応
// ============================================================================
Shader "GIPoker/Stage/ShadowCatcher"
{
    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0, 0, 0, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "ShadowCatcher"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            // メインライトの影のみ（追加ライト影はモバイルでは使わない）
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // SRP Batcher 互換: マテリアルプロパティは UnityPerMaterial に収める
            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _ShadowStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                half attenuation = MainLightRealtimeShadow(shadowCoord);
                half shadow = (1.0h - attenuation) * _ShadowStrength;
                return half4(_ShadowColor.rgb, shadow);
            }
            ENDHLSL
        }
    }
}
