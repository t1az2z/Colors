// Стилизованная лоу-поли вода вокруг уровня (Configs/Water.json, сетку строит Level/Water.cs).
// Волны — сумма трёх синусоид по мировым X/Z в вершинном шейдере; грани плоские: нормаль считается в пикселе
// по производным позиции (ddx/ddy), поэтому каждый треугольник освещён ровно и вода «гранёная».
// Цвет: мелководье → глубина по разнице глубин с непрозрачной сценой (_CameraDepthTexture), у берега — полоса пены.
// Изгиб мира (CurvedWorld.hlsl) — после волн, как у остального мира: вода уходит за горизонт вместе с уровнем.
Shader "ColorsAction/Water"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.56, 0.85, 0.89, 1)
        _DeepColor ("Deep Color", Color) = (0.25, 0.56, 0.71, 1)
        _FoamColor ("Foam Color", Color) = (0.96, 0.98, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.9
        _DepthFade ("Depth Fade (m)", Float) = 2.5
        _FoamWidth ("Foam Width (m)", Float) = 0.35
        _WaveHeight ("Wave Height (m)", Float) = 0.12
        _WaveLength ("Wave Length (m)", Float) = 7
        _WaveSpeed ("Wave Speed (m/s)", Float) = 1.1
        _WaveDirection ("Wave Direction (deg)", Float) = 30
        _Specular ("Specular", Range(0, 1)) = 0.35
        _Facets ("Facets", Range(0, 1)) = 0.8
    }

    SubShader
    {
        // Сразу после непрозрачных: глубина сцены уже скопирована, частицы и силуэт рисуются поверх.
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Assets/Game/Art/Shaders/CurvedWorld.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _FoamColor;
                half _Opacity;
                float _DepthFade;
                float _FoamWidth;
                float _WaveHeight;
                float _WaveLength;
                float _WaveSpeed;
                float _WaveDirection;
                half _Specular;
                half _Facets;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0; // с волнами и изгибом — то, что видно на экране
                float4 screenPos : TEXCOORD1;
                float wave : TEXCOORD2;        // высота волны -1..1: гребни чуть светлее
                float fog : TEXCOORD3;
            };

            float Wave(float2 p, float2 dir, float length, float speed, float time)
            {
                float k = TWO_PI / length;
                return sin(dot(p, dir) * k - time * speed * k);
            }

            // Три волны под разными углами и длинами — без заметного повтора, но крупные и мягкие.
            float WaveHeight(float2 p)
            {
                float a = radians(_WaveDirection);
                float2 d0 = float2(cos(a), sin(a));
                float2 d1 = float2(cos(a + 1.1), sin(a + 1.1));
                float2 d2 = float2(cos(a - 0.8), sin(a - 0.8));
                float t = _Time.y;
                return Wave(p, d0, _WaveLength, _WaveSpeed, t) * 0.55
                     + Wave(p, d1, _WaveLength * 0.61, _WaveSpeed * 0.8, t) * 0.3
                     + Wave(p, d2, _WaveLength * 0.37, _WaveSpeed * 1.3, t) * 0.15;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                float h = WaveHeight(ws.xz);
                ws.y += h * _WaveHeight;
                ws = CurvedWorldBendWS(ws);
                o.positionWS = ws;
                o.wave = h;
                o.positionCS = TransformWorldToHClip(ws);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Плоская нормаль грани по производным позиции; смотрим, чтобы глядела вверх.
                float3 n = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                n *= sign(n.y + 1e-5);
                // Волны пологие: наклон граней усиливаем, чтобы треугольники читались (0 — ровно, 1 — ×6).
                n = normalize(float3(n.x * _Facets * 6.0, n.y, n.z * _Facets * 6.0));

                // Глубина воды в этой точке: сколько метров до непрозрачной сцены за поверхностью.
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surfaceDepth = i.screenPos.w;
                float depth = max(0.0, sceneDepth - surfaceDepth);

                half3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depth / _DepthFade));
                color *= 1.0 + i.wave * 0.06;

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = lerp(0.55, 1.0, light.shadowAttenuation);
                half ndl = saturate(dot(n, light.direction));
                half3 lit = color * (0.55 + 0.45 * ndl) * shadow * lerp(half3(1, 1, 1), light.color, 0.5);

                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 halfDir = normalize(light.direction + view);
                half spec = pow(saturate(dot(n, halfDir)), 48.0) * _Specular * light.shadowAttenuation;
                lit += spec * light.color;

                // Пена: ровная полоса у берега, край чуть «дышит», чтобы не была мёртвой линией.
                half foam = 0;
                if (_FoamWidth > 0)
                {
                    float wobble = 0.75 + 0.25 * sin(_Time.y * 1.7 + i.positionWS.x * 0.9 + i.positionWS.z * 0.7);
                    foam = step(depth, _FoamWidth * wobble);
                }
                lit = lerp(lit, _FoamColor.rgb, foam);
                half alpha = lerp(_Opacity, 1.0, foam);
                // На глубине вода непрозрачна: под ней только низ уровня и пустота.
                alpha = lerp(alpha, 1.0, saturate(depth / (_DepthFade * 2.0)));

                lit = MixFog(lit, i.fog);
                return half4(lit, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
