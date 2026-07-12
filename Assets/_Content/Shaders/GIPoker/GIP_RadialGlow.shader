Shader "GIPoker/Stage/RadialGlow"
{
    // 台座の接地光輪（RuinPillar/FloatingRock）と境界魔法陣（BorderCircle）が共用する
    // 最小限のUnlitシェーダー。Quad（UV 0-1）のUV中心からの距離で
    // 「塗りつぶした円（ハロー）」または「リング（魔法陣）」を描く。
    // _InnerRadius を 0 にすると内側まで塗りつぶされ光輪になり、
    // 0より大きくするとリング状になり魔法陣として使える。
    //
    // 既存の Assets/_Content/Shaders/BorderLine.shader と同じ構成
    // （CGPROGRAM + UnityCG.cginc、LightModeタグなし＝SRPDefaultUnlitとしてURPでも描画される）
    // を踏襲し、モバイルXRでのコンパイル・描画リスクを最小化している。
    // _Segments を 1 以上にするとリングが破線状（ルーンの刻み風）になり、
    // Transform の回転が視認できるようになる（BorderCircle の回転演出用）。
    Properties
    {
        _Color ("Color", Color) = (1, 0.85, 0.55, 0.5)
        _InnerRadius ("Inner Radius (0=塗りつぶし)", Range(0, 0.5)) = 0.0
        _OuterRadius ("Outer Radius", Range(0, 0.5)) = 0.45
        _Softness ("Edge Softness", Range(0.001, 0.3)) = 0.05
        _Intensity ("Intensity Multiplier", Range(0, 2)) = 1.0
        _Segments ("Segments (0=実線)", Range(0, 64)) = 0
        _SegmentFill ("Segment Fill Ratio", Range(0.1, 1)) = 0.65
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            float _InnerRadius;
            float _OuterRadius;
            float _Softness;
            float _Intensity;
            float _Segments;
            float _SegmentFill;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 centered = i.uv - 0.5;
                float dist = length(centered);

                // 外周: _OuterRadius で外側にフェードアウト
                float outerMask = smoothstep(_OuterRadius, _OuterRadius - _Softness, dist);
                // 内周: _InnerRadius が実質0なら常に1（塗りつぶし＝光輪）、
                //       それ以上ならリング状にくり抜く（魔法陣の線）
                float innerMask = _InnerRadius <= 0.001 ? 1.0 : smoothstep(_InnerRadius, _InnerRadius + _Softness, dist);

                float mask = saturate(outerMask * innerMask);

                // 破線（ルーン刻み）: 周方向を _Segments 分割し、各区間の
                // _SegmentFill 割合だけを描画する（0 なら実線のまま）
                if (_Segments >= 1.0)
                {
                    float angle01 = atan2(centered.y, centered.x) / 6.2831853 + 0.5;
                    float seg = frac(angle01 * _Segments);
                    float halfFill = _SegmentFill * 0.5;
                    float dash = smoothstep(0.0, 0.08, halfFill - abs(seg - 0.5));
                    mask *= dash;
                }

                float alpha = mask * _Color.a * _Intensity;

                return fixed4(_Color.rgb * _Intensity, alpha);
            }
            ENDCG
        }
    }
}
