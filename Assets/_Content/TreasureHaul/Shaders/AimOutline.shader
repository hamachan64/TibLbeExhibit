// ============================================================================
// Treasure Haul: ビームが当たっているお宝の輪郭（背面法 / Unlit）
//
// お宝と同じメッシュをこのマテリアルで重ね描きする（Treasure.BuildOutlines が子を作る）。
// Cull Front で裏側だけを描き、頂点を外へ押し出すと、本体の外側に縁取りだけが見える。
// - 押し出す向き: 既定は「メッシュの中心 → 頂点」（_PUSH_NORMAL オフ）。角張った低ポリゴンのモデルは
//   面ごとに法線が割れているので、法線で押し出すと角に隙間が出る。中心からの向きなら隙間が出にくい。
//   法線で押し出したいなめらかなメッシュ用に _PUSH_NORMAL を切り替えられる。
// - 太さはワールド空間のメートル（スケールに依らない）
// - _PushCenter はメッシュの中心（オブジェクト空間）。Treasure が MaterialPropertyBlock で渡す
// - URP・モバイル（PICO）・シングルパスインスタンシング対応
// ============================================================================
Shader "TIB/TreasureHaul/AimOutline"
{
    Properties
    {
        _Color ("Outline Color", Color) = (1.0, 0.85, 0.45, 1)
        _Width ("Outline Width (m)", Float) = 0.006
        [Toggle(_PUSH_NORMAL)] _PushNormal ("Push Along Normal (off = Center To Vertex)", Float) = 0
        _PushCenter ("Push Center (Object Space)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+1"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "AimOutline"

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _PUSH_NORMAL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float4 _PushCenter;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            #if defined(_PUSH_NORMAL)
                float3 dirWS = TransformObjectToWorldNormal(input.normalOS);
            #else
                float3 dirWS = positionWS - TransformObjectToWorld(_PushCenter.xyz);
            #endif
                float len = max(length(dirWS), 1e-5);
                positionWS += dirWS / len * _Width;

                output.positionHCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(_Color.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
