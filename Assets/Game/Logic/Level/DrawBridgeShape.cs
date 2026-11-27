using System;

namespace ColorsAction.Level
{
    /// <summary>
    /// Форма разводного моста: 8 клеток в длину, 3 в ширину, поднятая створка — 4 в высоту. Клетка объекта — ближний край опущенной
    /// створки; длина идёт по локальной +X, повёрнутой на yaw (как у <see cref="CrateStackShape"/>: 0° — +X, 90° — −Z,
    /// 180° — −X, 270° — +Z), ширина — по локальной +Z (слева по ходу: при 0° это +Z, при 270° — −X).
    /// Ближняя створка (клетки длины 0–3) всегда опущена; дальняя (4–7) поднята на шарнире у дальнего края моста
    /// (с наклоном к ближней) и стоит стеной в клетке длины <see cref="RaisedColumn"/> высотой <see cref="Height"/>,
    /// пока цепи целы. Клетки длины 4–6 при этом пусты — под ними обычно пропасть.
    /// </summary>
    public static class DrawBridgeShape
    {
        /// <summary>Длина моста в клетках.</summary>
        public const int Length = 8;

        /// <summary>Ширина моста в клетках.</summary>
        public const int Width = 3;

        /// <summary>Длина одной створки в клетках (поднятая створка стоит на эту высоту).</summary>
        public const int LeafLength = Length / 2;

        /// <summary>Высота поднятой створки в клетках (≈ длина створки): выше прыжка, не перелезть.</summary>
        public const int Height = LeafLength;

        /// <summary>Индекс клетки длины, где стоит поднятая створка (шарнир на дальнем краю последней клетки).</summary>
        public const int RaisedColumn = Length - 1;

        /// <summary>Поворот, приведённый к 0/90/180/270.</summary>
        public static int SnapYaw(float yaw) => CrateStackShape.SnapYaw(yaw);

        /// <summary>Шаг вдоль моста (локальная +X).</summary>
        public static (int dx, int dz) Along(float yaw) => CrateStackShape.Step(yaw);

        /// <summary>Шаг поперёк моста (локальная +Z).</summary>
        public static (int dx, int dz) Across(float yaw)
        {
            switch (SnapYaw(yaw))
            {
                case 90: return (1, 0);
                case 180: return (0, -1);
                case 270: return (-1, 0);
                default: return (0, 1);
            }
        }

        /// <summary>Клетка моста: <paramref name="along"/> — 0..Length−1 по длине, <paramref name="across"/> — 0..Width−1.</summary>
        public static Int3 Cell(Int3 anchor, float yaw, int along, int across)
        {
            var (ax, az) = Along(yaw);
            var (cx, cz) = Across(yaw);
            return new Int3(anchor.X + ax * along + cx * across, anchor.Y, anchor.Z + az * along + cz * across);
        }

        /// <summary>Индексы (вдоль, поперёк) столбца (x, z) на мосту или null, если столбец не под мостом.</summary>
        public static (int along, int across)? Locate(Int3 anchor, float yaw, int x, int z)
        {
            for (int i = 0; i < Length; i++)
            for (int j = 0; j < Width; j++)
            {
                var c = Cell(anchor, yaw, i, j);
                if (c.X == x && c.Z == z)
                    return (i, j);
            }

            return null;
        }

        /// <summary>Занимает ли мост столбец (x, z).</summary>
        public static bool CoversColumn(Int3 anchor, float yaw, int x, int z) => Locate(anchor, yaw, x, z).HasValue;

        /// <summary>Закрывает ли поднятая створка клетку (стена в столбцах <see cref="RaisedColumn"/>, высотой <see cref="Height"/>).</summary>
        public static bool RaisedLeafCovers(Int3 anchor, float yaw, Int3 cell)
        {
            if (cell.Y < anchor.Y || cell.Y >= anchor.Y + Height)
                return false;
            var at = Locate(anchor, yaw, cell.X, cell.Z);
            return at.HasValue && at.Value.along == RaisedColumn;
        }

        /// <summary>Центр моста по X/Z (в метрах, клетка 1×1 начинается в целых координатах).</summary>
        public static (float x, float z) Center(int anchorX, int anchorZ, float yaw)
        {
            var (ax, az) = Along(yaw);
            var (cx, cz) = Across(yaw);
            const float halfLength = (Length - 1) * 0.5f;
            const float halfWidth = (Width - 1) * 0.5f;
            return (anchorX + 0.5f + ax * halfLength + cx * halfWidth, anchorZ + 0.5f + az * halfLength + cz * halfWidth);
        }
    }
}
