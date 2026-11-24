// Силуэт игрока за блоками: рисуется только там, где куб закрыт геометрией (ZTest Greater),
// полупрозрачной заливкой с краевой подсветкой. Стенсил — чтобы перекрывающиеся части (куб, оружие)
// не накладывались друг на друга.
Shader "ColorsAction/Silhouette"
{
    Properties
    {
        _Color ("Color", Color) = (0.25, 0.27, 0.33, 0.55)
        _RimColor ("Rim Color", Color) = (1, 1, 1, 0.9)
        _RimPower ("Rim Power", Float) = 2.5
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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                half _RimPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewDirWS = GetWorldSpaceViewDir(positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half rim = pow(1.0 - saturate(dot(normalize(i.normalWS), normalize(i.viewDirWS))), _RimPower);
                half3 color = lerp(_Color.rgb, _RimColor.rgb, rim * _RimColor.a);
                half alpha = saturate(_Color.a + rim * _RimColor.a * 0.5);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
