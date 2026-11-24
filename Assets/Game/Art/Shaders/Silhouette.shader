// Силуэт игрока за блоками. Показывает ближайшую к камере часть игрока, которая закрыта геометрией:
// оружие перед кубом видно поверх куба, оружие за кубом — нет. Оружие красится своим цветом
// (_SilhouetteTint из MaterialPropertyBlock), сам куб — ровной заливкой _Color без деталей.
//
// Проходы (индексы задают фичи RenderObjects в рендерере):
//   1 StencilMark   — после непрозрачных: метит бит 64 у видимых пикселей игрока.
//   2 OccludedReset — ZTest Greater: пиксели, где игрок закрыт геометрией (и не виден сам), метит
//                     битом 32 и сбрасывает в них глубину на дальнюю плоскость.
//   3 OccludedDepth — пишет глубину игрока в помеченные пиксели обычным тестом: остаётся ближайшая часть.
//   0 Silhouette    — красит ближайшую часть (ZTest Equal) и снимает бит 32, чтобы не красить дважды.
// Глубина в закрытых пикселях портится только после прозрачных, когда она уже никому не нужна.
Shader "ColorsAction/Silhouette"
{
    Properties
    {
        _Color ("Color", Color) = (0.25, 0.27, 0.33, 0.55)
        _TintStrength ("Weapon Tint Strength", Range(0, 1)) = 0.65
        [HideInInspector] _SilhouetteTint ("Tint (per renderer)", Color) = (0, 0, 0, 0)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _Color;
        half _TintStrength;
        half4 _SilhouetteTint;
    CBUFFER_END

    #if UNITY_REVERSED_Z
        #define SILHOUETTE_FAR_DEPTH 0.0
    #else
        #define SILHOUETTE_FAR_DEPTH 1.0
    #endif

    float4 vert(float4 positionOS : POSITION) : SV_POSITION
    {
        return TransformObjectToHClip(positionOS.xyz);
    }

    half4 fragColor() : SV_Target
    {
        half3 rgb = lerp(_Color.rgb, _SilhouetteTint.rgb, _SilhouetteTint.a * _TintStrength);
        return half4(rgb, _Color.a);
    }

    half4 fragMask() : SV_Target
    {
        return 0;
    }

    half4 fragFar(out float depth : SV_Depth) : SV_Target
    {
        depth = SILHOUETTE_FAR_DEPTH;
        return 0;
    }
    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Equal
            Cull Back
            Stencil
            {
                Ref 32
                ReadMask 32
                WriteMask 32
                Comp Equal
                Pass Zero
            }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragColor
            ENDHLSL
        }

        Pass
        {
            Name "StencilMark"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Cull Back
            Stencil
            {
                Ref 64
                WriteMask 64
                Comp Always
                Pass Replace
            }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragMask
            ENDHLSL
        }

        Pass
        {
            Name "OccludedReset"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite On
            ZTest Greater
            Cull Back
            Stencil
            {
                // Проходит, только если бит 64 (игрок виден) не стоит; ставит бит 32.
                Ref 96
                ReadMask 64
                WriteMask 32
                Comp NotEqual
                Pass Replace
            }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragFar
            ENDHLSL
        }

        Pass
        {
            Name "OccludedDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Back
            Stencil
            {
                Ref 32
                ReadMask 32
                Comp Equal
                Pass Keep
            }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragMask
            ENDHLSL
        }
    }
}
