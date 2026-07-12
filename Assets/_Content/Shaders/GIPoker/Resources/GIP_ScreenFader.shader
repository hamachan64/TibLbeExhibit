// ============================================================================
// GIPoker MR⇔VR遷移（Issue #14）: 白フェード用スクリーンフェーダー
//
// ScreenFader.cs がプレイヤー（Camera.main）を包む球体に適用する、
// 全天フェード専用の Unlit シェーダー。_Color の RGBA をそのままピクセル出力するだけの
// 単純な構成だが、Blend の第2指定（RGBとAlphaを別式にする）で
// framebuffer alpha にもフェード量を正しく蓄積させることが肝要:
//   - RGB   : SrcAlpha OneMinusSrcAlpha （通常のstraight-alpha over）
//   - Alpha : One      OneMinusSrcAlpha （alphaチャンネルもover式で積む）
// これにより MR パススルーコンポジタが premultiplied / straight どちらの規約でも
// 白へのフェードアウト・パススルーへのフェードインを正しく合成できる
// （Assets/_Content/Shaders/GIPoker/GIP_ShadowCatcher.shader と同じ手法。
//   .claude/agent-memory/lbe-learnings.md の MRパススルー知見に準拠）。
//
// カメラは常に球の内側にいる前提のため Cull Front（内面のみ描画）。
// ZTest Always・ZWrite Off・Queue=Overlay ですべての描画の最前面に必ず重なる。
// ============================================================================
Shader "GIPoker/Transition/ScreenFader"
{
    Properties
    {
        _Color ("Fade Color (RGBA)", Color) = (1, 1, 1, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "ScreenFader"

            // RGBとAlphaで別のブレンド式を使い、framebuffer alphaにも
            // フェード量を正しく積む（MRパススルー合成規約に依存しない）
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // SRP Batcher 互換: マテリアルプロパティは UnityPerMaterial に収める
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                return _Color;
            }
            ENDHLSL
        }
    }
}
