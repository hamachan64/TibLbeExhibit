// ============================================================================
// GIPoker チュートリアル「ゲーム開始の間」: ルーム壁面（RoomWall）
//
// プレイヤーを完全に囲む不透明ルーム（円筒＋床＋天井）の内面に描く、
// G.I.（グリードアイランド）具現化装置演出風の紺〜青黒ベース・水色/白発光の
// 迷路/回路状紋様（Truchet迷路）に、壁面のみ縦長の発光ウィンドウストライプを
// 加算合成する。パススルーを切らずに視界を覆ってVR化するため、以下を厳守する:
//   - 完全不透明（Blend なし＝上書き）。フラグメントは framebuffer alpha=1 を書き、
//     パススルー合成でMR映像を完全に隠す（.claude/agent-memory の MR知見に準拠）。
//   - ライティング非依存の Unlit 発光（ルーム内が暗く沈まない）。
//   - 紋様・ストライプともにテクスチャ不要のプロシージャル生成。
//   - half精度基本・シングルパスインスタンシング対応・PICOモバイルで軽量。
//
// _Reveal（0→1）で紋様・ストライプが hash 順にじわっと灯る登場演出をシェーダー側で行う
// （C# 側は DOTween で _Reveal を駆動するだけ）。
//
// ウィンドウストライプ（_StripeXxx）:
//   - UV.x（周方向・メートル）を _StripeDensity で列に分割し、hash21 で列ごとに
//     点灯有無・明滅位相をランダム化する（すべての列が光るわけではない）。
//   - UV.y（縦方向・メートル）も列ごとにランダムなオフセットを掛けた上で
//     区切り、点灯セグメントを作る（列間で高さが揃わない不規則な短冊にする）。
//   - 壁マテリアルのみ _StripeIntensity=1 で有効化し、床・天井は 0 のまま無効化する。
// ============================================================================
Shader "GIPoker/Tutorial/RoomWall"
{
    Properties
    {
        _BaseColor ("Base Color (Navy)", Color) = (0.03, 0.05, 0.10, 1)
        _GlowColor ("Glyph Glow Color", Color) = (0.55, 0.85, 1.0, 1)
        _Tiling ("Glyph Tiling (cells / UV meter)", Range(0.25, 8)) = 1.6
        _LineWidth ("Glyph Line Width", Range(0.01, 0.25)) = 0.06
        _Emission ("Glow Emission", Range(0, 4)) = 1.3
        _PulseSpeed ("Glow Pulse Speed", Range(0, 6)) = 1.1
        _ScrollSpeed ("Glyph Scroll Speed", Range(0, 1)) = 0.03
        _Reveal ("Reveal (ignite 0..1)", Range(0, 1)) = 1

        [Header(Window Stripe)]
        _StripeColor ("Window Stripe Color", Color) = (0.85, 0.97, 1.0, 1)
        _StripeIntensity ("Window Stripe Intensity (wall=1 / floor,ceiling=0)", Range(0, 1)) = 0
        _StripeDensity ("Window Stripe Column Density (cols / UV meter)", Range(0.1, 5)) = 1.1
        _StripeWidth ("Window Stripe Width Ratio (within a column)", Range(0.01, 1)) = 0.35
        _StripeRowDensity ("Window Stripe Row Density (segments / UV meter)", Range(0.1, 10)) = 2.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "TutorialRoomUnlit"

            // 完全不透明で上書き。ColorMask 既定(RGBA)のまま alpha=1 を書き込み、
            // パススルーコンポジタがどちらの合成規約でもMR映像を確実に隠す。
            // ルームは閉じた形状のため Cull Off（内向き/外向きの巻き方に依存せず
            // 必ず内面が描かれる。三角数はごく少数で overdraw コストは無視できる）。
            ZWrite On
            ZTest LEqual
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

            // SRP Batcher 互換: マテリアルプロパティは UnityPerMaterial に収める
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _GlowColor;
                half _Tiling;
                half _LineWidth;
                half _Emission;
                half _PulseSpeed;
                half _ScrollSpeed;
                half _Reveal;
                half4 _StripeColor;
                half _StripeIntensity;
                half _StripeDensity;
                half _StripeWidth;
                half _StripeRowDensity;
            CBUFFER_END

            // セル座標からの疑似乱数（float で計算し精度落ちを避ける）
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

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

                // UV は「メートル単位」で生成済み。_Tiling でセル密度を決める。
                // ごく僅かに縦スクロールさせて紋様に生命感を出す。
                float2 g = input.uv * _Tiling;
                g.y += _Time.y * _ScrollSpeed;

                float2 id = floor(g);
                float2 f = frac(g);
                float h = hash21(id);

                // Truchet 迷路: セルごとにX反転して2本の1/4円弧をランダム接続
                float2 ff = f;
                ff.x = h < 0.5 ? 1.0 - f.x : f.x;
                float d1 = abs(length(ff) - 0.5);
                float d2 = abs(length(ff - 1.0) - 0.5);
                float maze = min(d1, d2);

                half lw = _LineWidth;
                // 発光コア（中心ほど明るい線）。
                // smoothstep の降順引数（min > max）はHLSL仕様上undefinedで
                // GPUドライバによって結果が変わるため、昇順＋反転の形で書く
                half arcs = 1.0h - smoothstep(lw * 0.35h, lw, half(maze));

                // 回路ノード: 弧が接続するセル辺の中点で強めに光らせる
                float dn = min(
                    min(length(f - float2(0.5, 0.0)), length(f - float2(0.5, 1.0))),
                    min(length(f - float2(0.0, 0.5)), length(f - float2(1.0, 0.5))));
                half node = 1.0h - smoothstep(0.0h, lw * 2.2h, half(dn));

                half glyph = max(arcs, node * 0.85h);

                // セル単位のゆっくりした発光パルス（位相を hash と高さでずらす）
                half pulse = 0.62h + 0.38h * sin(_Time.y * _PulseSpeed + half(h) * 6.2831h + half(input.uv.y) * 0.6h);

                // 登場演出: _Reveal が上がるにつれ hash の小さいセルから順に灯る
                half ignite = smoothstep(0.0h, 0.28h, _Reveal * 1.35h - half(h) * 0.9h);

                half emission = glyph * pulse * _Emission * ignite;

                // --- 縦長の発光ウィンドウストライプ（壁のみ _StripeIntensity=1 で有効） ---
                // 列（周方向）ごとに hash で点灯有無を決め、すべての列が光らないようにする。
                float colCoord = input.uv.x * _StripeDensity;
                float colId = floor(colCoord);
                float colFrac = frac(colCoord);
                float colHash = hash21(float2(colId, 7.13));
                half colLit = step(0.4h, half(colHash));

                // 列内での幅（_StripeWidth 比率の帯）
                half stripeMask = step(half(colFrac), half(_StripeWidth));

                // 縦方向は列ごとに異なるオフセットを掛けて区切ることで、
                // 高さの異なる短冊が不規則に並んだ見た目にする。
                float rowCoord = input.uv.y * _StripeRowDensity + colHash * 17.0;
                float rowId = floor(rowCoord);
                float rowHash = hash21(float2(colId, rowId));
                half rowLit = step(0.45h, half(rowHash));

                // 列ごとに明滅位相・速度をずらす（すべてが同じタイミングで点滅しないように）
                half stripePulse = 0.65h + 0.35h * sin(_Time.y * (0.5h + half(colHash) * 1.5h) + half(rowHash) * 6.2831h);

                half stripe = colLit * stripeMask * rowLit * stripePulse * ignite;

                emission += stripe * _StripeIntensity;

                // 紺〜青黒の下地は常時見える（画面フェードイン直後から壁がある）。
                // その上に回路紋様の発光とウィンドウストライプの発光を加算。
                // alpha=1 を書いてパススルーを隠す。
                half3 col = _BaseColor.rgb + _GlowColor.rgb * emission + _StripeColor.rgb * stripe * _StripeIntensity;
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
