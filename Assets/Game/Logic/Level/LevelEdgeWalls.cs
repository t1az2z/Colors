using System;
using System.Collections.Generic;
using ColorsAction.Configs;

namespace ColorsAction.Level
{
    /// <summary>Коробка невидимой стены: центр и размер в метрах (мир уровня, клетка x — от x до x+1).</summary>
    public readonly struct WallBox
    {
        public readonly float CenterX, CenterY, CenterZ;
        public readonly float SizeX, SizeY, SizeZ;

        public WallBox(float cx, float cy, float cz, float sx, float sy, float sz)
        {
            CenterX = cx;
            CenterY = cy;
            CenterZ = cz;
            SizeX = sx;
            SizeY = sy;
            SizeZ = sz;
        }

        public bool Contains(float x, float y, float z) =>
            Math.Abs(x - CenterX) <= SizeX * 0.5f && Math.Abs(y - CenterY) <= SizeY * 0.5f && Math.Abs(z - CenterZ) <= SizeZ * 0.5f;
    }

    /// <summary>
    /// Невидимые стены по внешнему краю уровня, чтобы ни игрок, ни враги не упали за границу карты.
    /// Стоят вплотную снаружи прямоугольника карты (внутренние дыры и спуски не трогают), высотой выше самой
    /// высокой точки уровня на <see cref="Headroom"/> — не перепрыгнуть.
    /// </summary>
    public static class LevelEdgeWalls
    {
        public const float Thickness = 1f;
        public const float Headroom = 6f;

        /// <summary>Насколько ниже самой нижней точки уровня игрок погибает от падения, метры.</summary>
        public const float FallKillDepth = 6f;

        /// <summary>Высота, ниже которой упавший игрок погибает: дно уровня минус <see cref="FallKillDepth"/>.</summary>
        public static float KillHeight(float levelBottom) => levelBottom - FallKillDepth;

        /// <summary>Высота смерти от падения для уровня-карты высот: дно — глубина дыр.</summary>
        public static float KillHeight(LevelConfig level) => KillHeight(-level.Depth);

        public static List<WallBox> Build(LevelConfig level)
        {
            var top = 0f;
            for (int z = 0; z < level.Length; z++)
            for (int x = 0; x < level.Width; x++)
                top = Math.Max(top, level.HeightAt(x, z));
            return Build(level.Width, level.Length, -level.Depth - 2f, top + Headroom);
        }

        /// <summary>Четыре стены вокруг прямоугольника [0, width] × [0, length] от <paramref name="bottom"/> до <paramref name="top"/>.</summary>
        public static List<WallBox> Build(float width, float length, float bottom, float top)
        {
            var h = top - bottom;
            var cy = (top + bottom) * 0.5f;
            const float t = Thickness;
            // Длинные стены по X с запасом на углы, короткие по Z — только вдоль карты.
            return new List<WallBox>
            {
                new WallBox(width * 0.5f, cy, -t * 0.5f, width + 2 * t, h, t),
                new WallBox(width * 0.5f, cy, length + t * 0.5f, width + 2 * t, h, t),
                new WallBox(-t * 0.5f, cy, length * 0.5f, t, h, length),
                new WallBox(width + t * 0.5f, cy, length * 0.5f, t, h, length)
            };
        }
    }
}
