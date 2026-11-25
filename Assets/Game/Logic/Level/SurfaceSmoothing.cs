using System;

namespace ColorsAction.Level
{
    /// <summary>
    /// Плавные перепады: если соседняя поверхность выше не больше чем на шаг (по умолчанию 0.3), верх клетки
    /// наклоняется к ней — получается слоп вместо ступеньки, и камера с персонажем идут по нему без рывков.
    /// Правило считает высоты четырёх углов верха клетки: угол поднимается до самого высокого из соседних верхов
    /// (по двум сторонам и диагонали), которые выше своего верха не больше чем на шаг. Крупные перепады остаются стенками.
    /// <para>Сглаживаются только плоские блоки (куб, полублок, ступенька), над которыми свободно. Явные слопы не трогаются.</para>
    /// </summary>
    public static class SurfaceSmoothing
    {
        /// <summary>Перепад, который сглаживается слопом (как stepHeight игрока).</summary>
        public const float DefaultMaxStep = 0.3f;

        private const float Eps = 1e-4f;

        /// <summary>Высоты углов верха клетки, абсолютные. Порядок: (x0,z0), (x0,z1), (x1,z1), (x1,z0).</summary>
        public readonly struct Corners
        {
            public readonly float X0Z0;
            public readonly float X0Z1;
            public readonly float X1Z1;
            public readonly float X1Z0;

            public Corners(float x0z0, float x0z1, float x1z1, float x1z0)
            {
                X0Z0 = x0z0;
                X0Z1 = x0z1;
                X1Z1 = x1z1;
                X1Z0 = x1z0;
            }

            public float Max => Math.Max(Math.Max(X0Z0, X0Z1), Math.Max(X1Z1, X1Z0));
            public float Center => (X0Z0 + X0Z1 + X1Z1 + X1Z0) * 0.25f;

            /// <summary>Наибольшая высота на стороне клетки в направлении (dx, dz).</summary>
            public float EdgeMax(int dx, int dz)
            {
                if (dx < 0) return Math.Max(X0Z0, X0Z1);
                if (dx > 0) return Math.Max(X1Z0, X1Z1);
                if (dz < 0) return Math.Max(X0Z0, X1Z0);
                return Math.Max(X0Z1, X1Z1);
            }
        }

        public static bool IsSmoothable(BlockShape shape) =>
            shape == BlockShape.Full || shape == BlockShape.ThreeQuarter || shape == BlockShape.Half || shape == BlockShape.Step;

        /// <summary>
        /// Углы верха клетки с учётом сглаживания. false — клетка не сглаживается (не плоский блок, над ним что-то
        /// стоит или рядом нет подходящего перепада): верх плоский.
        /// </summary>
        public static bool TryGetCorners(VoxelLevel level, Int3 cell, out Corners corners, float maxStep = DefaultMaxStep)
        {
            corners = default;
            if (!level.TryGet(cell, out var block) || !IsSmoothable(block.Shape) || level.IsSolid(cell.Above))
                return false;
            var own = cell.Y + BlockShapes.CenterTop(block.Shape);
            var x0z0 = Corner(level, cell, own, -1, -1, maxStep);
            var x0z1 = Corner(level, cell, own, -1, 1, maxStep);
            var x1z1 = Corner(level, cell, own, 1, 1, maxStep);
            var x1z0 = Corner(level, cell, own, 1, -1, maxStep);
            corners = new Corners(x0z0, x0z1, x1z1, x1z0);
            return corners.Max > own + Eps;
        }

        private static float Corner(VoxelLevel level, Int3 cell, float own, int sx, int sz, float maxStep)
        {
            var h = own;
            h = Math.Max(h, Raise(level, cell.X + sx, cell.Y, cell.Z, own, maxStep));
            h = Math.Max(h, Raise(level, cell.X, cell.Y, cell.Z + sz, own, maxStep));
            h = Math.Max(h, Raise(level, cell.X + sx, cell.Y, cell.Z + sz, own, maxStep));
            return h;
        }

        /// <summary>Верх соседнего столбца, если он выше <paramref name="own"/> не больше чем на шаг; иначе own.</summary>
        private static float Raise(VoxelLevel level, int x, int y, int z, float own, float maxStep)
        {
            var best = own;
            for (int dy = 0; dy <= 1; dy++)
            {
                var c = new Int3(x, y + dy, z);
                if (!level.TryGet(c, out var b) || !IsSmoothable(b.Shape) || level.IsSolid(c.Above))
                    continue;
                var top = c.Y + BlockShapes.CenterTop(b.Shape);
                if (top > own + Eps && top <= own + maxStep + Eps && top > best)
                    best = top;
            }

            return best;
        }

        /// <summary>Высота верха в точке (u, v) клетки, u,v ∈ [0,1]: билинейно по углам.</summary>
        public static float HeightAt(in Corners c, float u, float v)
        {
            var a = c.X0Z0 + (c.X1Z0 - c.X0Z0) * u;
            var b = c.X0Z1 + (c.X1Z1 - c.X0Z1) * u;
            return a + (b - a) * v;
        }
    }
}
