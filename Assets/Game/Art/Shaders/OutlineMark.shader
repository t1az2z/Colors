// Белый контур цели фиксации (LockOnOutline), проход 1 из 2: метит стенсилом (бит 128) все пиксели силуэта цели,
// в том числе закрытые стенами, ничего не рисуя. Очередь — сразу после непрозрачных, раньше проходов контура
// (OutlineHull), чтобы оружие врага и его тело метились до того, как рисуется контур любого из них.
Shader "ColorsAction/OutlineMark"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+460" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "OutlineMark"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            ZTest Always
            Cull Off
            Stencil
            {
                Ref 128
                WriteMask 128
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Game/Art/Shaders/CurvedWorld.hlsl"

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
