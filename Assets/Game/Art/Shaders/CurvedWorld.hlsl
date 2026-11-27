// Искривление мира к горизонту (как в No Rest for the Wicked): вершины опускаются тем ниже, чем дальше они
// от центра изгиба (игрока) по горизонтали. Изгиб радиальный — одинаковый при любом повороте камеры.
// Геометрия, физика и логика остаются плоскими: это только картинка. Та же формула в C# — CurvedWorld.Bend.
//   drop = curvature * max(0, distanceXZ - flatRadius)^2
// Глобальные параметры выставляет CurvedWorld (C#) перед рендером игровой камеры; у остальных камер
// (превью персонажа, окно сцены) кривизна 0 — изгиба нет.
#ifndef COLORSACTION_CURVED_WORLD_INCLUDED
#define COLORSACTION_CURVED_WORLD_INCLUDED

float4 _CurvedWorldCenter; // xyz — центр изгиба (мир)
float4 _CurvedWorldParams; // x — кривизна (1/м), y — плоский радиус (м)

float3 CurvedWorldBendWS(float3 positionWS)
{
    float2 d = positionWS.xz - _CurvedWorldCenter.xz;
    float r = max(0.0, length(d) - _CurvedWorldParams.y);
    positionWS.y -= _CurvedWorldParams.x * r * r;
    return positionWS;
}

float3 CurvedWorldBendOS(float3 positionOS)
{
    // Без кривизны — ничего не трогаем (и не теряем точность на двойном преобразовании).
    if (_CurvedWorldParams.x <= 0.0)
        return positionOS;
    float3 positionWS = TransformObjectToWorld(positionOS);
    return TransformWorldToObject(CurvedWorldBendWS(positionWS));
}

#endif
