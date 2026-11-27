using System;
using ColorsAction.Configs;

namespace ColorsAction.Level
{
    /// <summary>
    /// Где лежит вода вокруг уровня (<see cref="WaterConfig"/>): высота поверхности над дном, прямоугольник плоскости
    /// и высота, ниже которой упавший игрок тонет. Вода только картинка: коллайдера у неё нет, а смерть — та же
    /// <c>KillHeight</c>, что при падении ниже дна, только выше: игрок гибнет, как только скрылся под водой.
    /// </summary>
    public readonly struct WaterLayout
    {
        /// <summary>Высота поверхности воды (мир).</summary>
        public readonly float Surface;
        /// <summary>Прямоугольник плоскости воды по X/Z (мир).</summary>
        public readonly float MinX, MinZ, MaxX, MaxZ;
        /// <summary>Ниже этой высоты игрок утонул.</summary>
        public readonly float DrownHeight;

        private WaterLayout(float surface, float minX, float minZ, float maxX, float maxZ, float drownHeight)
        {
            Surface = surface;
            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
            DrownHeight = drownHeight;
        }

        /// <summary>
        /// Вода для уровня с прямоугольником карты [<paramref name="minX"/>, <paramref name="maxX"/>] ×
        /// [<paramref name="minZ"/>, <paramref name="maxZ"/>] и дном <paramref name="levelBottom"/> (низ самого нижнего блока).
        /// </summary>
        public static WaterLayout Build(WaterConfig config, float minX, float minZ, float maxX, float maxZ, float levelBottom)
        {
            var surface = levelBottom + config.SurfaceAboveBottom;
            var e = config.Extent;
            return new WaterLayout(surface, minX - e, minZ - e, maxX + e, maxZ + e, surface - config.DrownDepth);
        }

        /// <summary>
        /// Высота смерти с водой: выше из прежней (дно минус <see cref="LevelEdgeWalls.FallKillDepth"/>) и «утонул».
        /// Ниже дна и так погибали, поэтому вода смерть только приближает и никогда не убивает на суше:
        /// поверхность ниже верха самых нижних блоков.
        /// </summary>
        public static float KillHeight(float fallKillHeight, WaterLayout water) => Math.Max(fallKillHeight, water.DrownHeight);
    }
}
