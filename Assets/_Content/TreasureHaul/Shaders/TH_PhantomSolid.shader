// ============================================================================
// Treasure Haul: 亡霊（墓守の霊）の不透明な部品（目・灯りの芯・顔の闇 / Unlit・不透明）
//
// - 色は _BaseColor（PhantomView が MaterialPropertyBlock で _BaseColor と _Color を塗る。普段は青、見つかったら赤）
// - カメラから _NearFadeStart m より近いと、画面のディザで穴が開き、_NearFadeEnd m で全部消える（clip）
//   → 体験者が亡霊をすり抜けて頭の位置に入ったとき、不透明な目・闇が視界をふさがない（体の PhantomGhost と同じ距離）
// - 不透明のまま（透明にしない）。PICO のシングルパス・インスタンシング（ステレオ）対応
// ============================================================================
Shader "TIB/TreasureHaul/PhantomSolid"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (0.2, 0.7, 0.9, 1)
        _NearFadeStart ("Near Fade Start (m)", Float) = 0.6
        _NearFadeEnd ("Near Fade End (m)", Float) = 0.25
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
            Name "PhantomSolid"
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
                half4 _BaseColor;
                float _NearFadeStart;
                float _NearFadeEnd;
            CBUFFER_END

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

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            // 4x4 のディザ（0〜1）。画面の位置で決まるので、近づくほど穴が増えて消えていく
            half Dither4x4(float2 pixel)
            {
                static const float m[16] = { 0.0625, 0.5625, 0.1875, 0.6875,
                                             0.8125, 0.3125, 0.9375, 0.4375,
                                             0.25,   0.75,   0.125,  0.625,
                                             1.0,    0.5,    0.875,  0.375 };
                int2 p = int2(fmod(pixel, 4.0));
                return m[p.y * 4 + p.x];
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float dist = distance(GetCameraPositionWS(), input.positionWS);
                half fade = smoothstep(_NearFadeEnd, max(_NearFadeStart, _NearFadeEnd + 1e-3), dist);   // 1 = 全部見える、0 = 消える
                clip(fade - Dither4x4(input.positionHCS.xy) + 1e-4);
                return half4(_BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
