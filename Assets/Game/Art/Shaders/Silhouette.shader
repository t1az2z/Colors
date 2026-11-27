// Силуэт игрока за блоками: рисуется только там, где игрок закрыт геометрией (ZTest Greater),
// ровной полупрозрачной заливкой. Заливка намеренно без подсветки по нормалям: иначе внутри
// силуэта читаются глаза и оружие — силуэт должен быть одним пятном.
// Проход 1 (StencilMark) рисуется после непрозрачных и метит стенсилом видимые пиксели игрока.
// Проход 0 (Silhouette) пропускает помеченные пиксели: глаза на задней грани и оружие за кубом
// закрыты самим игроком и не должны просвечивать сквозь него. Он же метит свои пиксели,
// чтобы перекрывающиеся части (куб, оружие) не накладывались друг на друга.
Shader "ColorsAction/Silhouette"
{
    Properties
    {
        _Color ("Color", Color) = (0.25, 0.27, 0.33, 0.55)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Cull Back
            Stencil
            {
                Ref 64
                ReadMask 64
                WriteMask 64
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Game/Art/Shaders/CurvedWorld.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(CurvedWorldBendOS(positionOS.xyz));
            }

            half4 frag() : SV_Target
            {
                return _Color;
            }
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
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Game/Art/Shaders/CurvedWorld.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(CurvedWorldBendOS(positionOS.xyz));
            }

            half4 frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
