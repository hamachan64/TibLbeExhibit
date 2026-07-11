// ============================================================================
// GIPoker MRステージ「島の侵蝕」: 侵蝕パッチ（床用 radial dissolve）
//
// グローバル _GIP_Erosion（0〜1、StageDirector が配信）に応じて、
// パッチ中心から円形に「草地＋発光ルーン模様」が広がる。
// - 侵蝕の最前線（フロンティア）はノイズで縁が揺らぎ、発光バンドが乗る
// - グローバル _GIP_Unstable（0〜1）でフロンティアが金色に変化＋明滅（Unstable演出）
// - URP Unlit ベース / シングルパスインスタンシング対応 / half精度基本
// - パススルーMR前提: 過剰発光禁止。アルファも書き込み（コンポジタ合成用）
// ============================================================================
Shader "GIPoker/Stage/ErosionPatch"
{
    Properties
    {
        [Header(Grass)]
        _GrassColorA ("Grass Color A (bright)", Color) = (0.28, 0.52, 0.25, 1)
        _GrassColorB ("Grass Color B (dark)", Color) = (0.14, 0.33, 0.16, 1)
        _BaseOpacity ("Grass Opacity", Range(0, 1)) = 0.92

        [Header(Rune)]
        _RuneTex ("Rune Pattern (R)", 2D) = "black" {}
        _RuneColor ("Rune Emission Color", Color) = (0.45, 0.95, 0.65, 1)
        _RuneIntensity ("Rune Intensity", Range(0, 4)) = 1.2
        _RunePulseSpeed ("Rune Pulse Speed", Range(0, 10)) = 1.5

        [Header(Erosion Edge)]
        _NoiseTex ("Noise (R)", 2D) = "gray" {}
        _NoiseTiling ("Noise Tiling", Range(0.1, 16)) = 3
        _NoiseAmplitude ("Edge Noise Amplitude", Range(0, 1)) = 0.25
        _EdgeColor ("Edge Glow Color", Color) = (0.5, 0.9, 0.6, 1)
        _GoldColor ("Edge Gold Color (Unstable)", Color) = (1.0, 0.76, 0.32, 1)
        _EdgeWidth ("Edge Glow Width", Range(0.01, 0.5)) = 0.12
        _EdgeSoftness ("Edge Softness", Range(0.005, 0.3)) = 0.06
        _EdgeIntensity ("Edge Intensity", Range(0, 4)) = 1.4

        [Header(Erosion Remap)]
        // パッチごとに侵蝕の開始タイミングをずらすためのローカル再マップ。
        // local = saturate((_GIP_Erosion - Delay) / Span)
        _ErosionDelay ("Erosion Delay", Range(0, 0.9)) = 0
        _ErosionSpan ("Erosion Span", Range(0.05, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-50"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "ErosionPatchUnlit"

            // 通常アルファ合成。アルファチャンネルは加算的に蓄積して
            // パススルーコンポジタに「ここに仮想物がある」ことを伝える
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

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

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_RuneTex);
            SAMPLER(sampler_RuneTex);

            // SRP Batcher 互換: マテリアルプロパティは UnityPerMaterial に収める
            CBUFFER_START(UnityPerMaterial)
                half4 _GrassColorA;
                half4 _GrassColorB;
                half _BaseOpacity;
                half4 _RuneColor;
                half _RuneIntensity;
                half _RunePulseSpeed;
                half _NoiseTiling;
                half _NoiseAmplitude;
                half4 _EdgeColor;
                half4 _GoldColor;
                half _EdgeWidth;
                half _EdgeSoftness;
                half _EdgeIntensity;
                half _ErosionDelay;
                half _ErosionSpan;
            CBUFFER_END

            // グローバル（StageDirector が Shader.SetGlobalFloat で配信）
            // ※ UnityPerMaterial に入れないこと（グローバルはSRP Batcherの対象外）
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

                // ローカル侵蝕度（パッチごとの遅延・スパン再マップ）
                half erosion = saturate((half(_GIP_Erosion) - _ErosionDelay) / max(_ErosionSpan, 0.05h));
                half unstable = saturate(half(_GIP_Unstable));

                // 侵蝕ゼロなら完全不可視（Reality フェーズで中心に光点が残らないように）
                clip(erosion - 0.001h);

                // 中心からの正規化距離: 1.0 = クアッドに内接する円の縁
                half2 centered = half2(input.uv - 0.5);
                half dist = length(centered) * 2.0h;

                // 侵蝕フロンティア（>0 が侵蝕済み領域）。ノイズで縁を揺らす
                half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, input.uv * _NoiseTiling).r;
                half reveal = erosion * 0.95h;
                half frontier = reveal - dist - (noise - 0.5h) * _NoiseAmplitude;

                // 発光バンドの外側は完全に不可視 → 破棄してブレンドコストを抑える
                clip(frontier + _EdgeWidth);

                // 草地: ノイズで2色をまだら模様に
                half mask = smoothstep(0.0h, max(_EdgeSoftness, 0.005h), frontier);
                half3 grass = lerp(_GrassColorB.rgb, _GrassColorA.rgb, noise);

                // ルーン: 内側で淡く脈動（過剰発光禁止 → 控えめな振幅）
                half rune = SAMPLE_TEXTURE2D(_RuneTex, sampler_RuneTex, input.uv).r;
                half pulse = 0.75h + 0.25h * sin(_Time.y * _RunePulseSpeed + dist * 6.2832h);
                half3 emission = _RuneColor.rgb * (rune * pulse * _RuneIntensity);

                // フロンティアの発光バンド。Unstable では金色化＋明滅
                half band = saturate(1.0h - abs(frontier) / _EdgeWidth);
                band *= band;
                half flicker = 1.0h + unstable * 0.35h * sin(_Time.y * 9.0h + dist * 12.0h);
                half3 edgeColor = lerp(_EdgeColor.rgb, _GoldColor.rgb, unstable);
                half3 edge = edgeColor * (band * _EdgeIntensity * flicker);

                // 出現・消滅の瞬間のポップを避けるため、極小侵蝕では全体をフェードする
                half emergence = saturate(erosion * 8.0h);
                half3 color = (grass * mask + emission * mask + edge) * emergence;
                half alpha = saturate(mask * _BaseOpacity + band * 0.85h) * emergence;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
