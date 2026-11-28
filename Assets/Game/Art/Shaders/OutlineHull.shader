// Белый контур цели фиксации (LockOnOutline), проход 2 из 2: та же геометрия, раздутая в экранном пространстве
// на _Width пикселей (при высоте экрана 1080) вдоль сглаженной нормали (TEXCOORD3, её пишет LockOnOutline; нет — обычная
// нормаль), рисуется только вне силуэта (стенсил 128 не выставлен проходом OutlineMark) и поверх всего.
Shader "ColorsAction/OutlineHull"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Width ("Width (px at 1080p)", Float) = 4
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "OutlineHull"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            Stencil
            {
                Ref 128
                ReadMask 128
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Game/Art/Shaders/CurvedWorld.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 smoothNormal : TEXCOORD3;
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                float4 clip = TransformObjectToHClip(CurvedWorldBendOS(input.positionOS.xyz));
                float3 n = dot(input.smoothNormal, input.smoothNormal) > 0.01 ? input.smoothNormal : input.normalOS;
                float3 nWS = TransformObjectToWorldDir(n, true);
                float2 nCS = mul((float3x3)GetWorldToHClipMatrix(), nWS).xy;
                float len = length(nCS);
                if (len > 1e-5)
                {
                    // Пиксели → доли экрана в clip-пространстве (×w: смещение после перспективного деления постоянно).
                    float2 dir = nCS / len;
                    float px = _Width * _ScreenParams.y / 1080.0;
                    clip.xy += dir * px * 2.0 / _ScreenParams.xy * clip.w;
                }
                return clip;
            }

            half4 frag() : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
