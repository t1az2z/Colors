using System;

namespace ColorsAction.Level
{
    /// <summary>
    /// Форма стопки ящиков: 2 клетки в ширину, 1 в глубину, N ящиков в высоту. Первая клетка — клетка объекта,
    /// вторая — соседняя по локальной оси +X, повёрнутой на yaw (как в Unity: 0° — +X, 90° — −Z, 180° — −X, 270° — +Z).
    /// В редакторах удобнее две ориентации: «вдоль X» (0°) и «вдоль Z» (270°).
    /// </summary>
    public static class CrateStackShape
    {
        /// <summary>Ширина стопки в клетках.</summary>
        public const int Width = 2;

        /// <summary>Высота стопки по умолчанию (ящиков): выше прыжка игрока, поэтому без меча не обойти.</summary>
        public const int DefaultHeight = 2;

        /// <summary>Поворот «вдоль X»: вторая клетка в +X.</summary>
        public const float AlongX = 0f;

        /// <summary>Поворот «вдоль Z»: вторая клетка в +Z.</summary>
        public const float AlongZ = 270f;

        /// <summary>Поворот, приведённый к 0/90/180/270.</summary>
        public static int SnapYaw(float yaw) => (((int)Math.Round(yaw / 90f) % 4) + 4) % 4 * 90;

        /// <summary>Шаг от первой клетки ко второй.</summary>
        public static (int dx, int dz) Step(float yaw)
        {
            switch (SnapYaw(yaw))
            {
                case 90: return (0, -1);
                case 180: return (-1, 0);
                case 270: return (0, 1);
                default: return (1, 0);
            }
        }

        /// <summary>Клетка <paramref name="index"/> (0..Width−1) стопки на уровне первой клетки.</summary>
        public static Int3 Cell(Int3 anchor, float yaw, int index)
        {
            var (dx, dz) = Step(yaw);
            return new Int3(anchor.X + dx * index, anchor.Y, anchor.Z + dz * index);
        }

        /// <summary>Занимает ли стопка столбец (x, z).</summary>
        public static bool CoversColumn(Int3 anchor, float yaw, int x, int z)
        {
            for (int i = 0; i < Width; i++)
            {
                var c = Cell(anchor, yaw, i);
                if (c.X == x && c.Z == z)
                    return true;
            }

            return false;
        }

        /// <summary>Центр основания стопки по X/Z (в метрах, клетка 1×1 начинается в целых координатах).</summary>
        public static (float x, float z) Center(int anchorX, int anchorZ, float yaw)
        {
            var (dx, dz) = Step(yaw);
            var half = (Width - 1) * 0.5f;
            return (anchorX + 0.5f + dx * half, anchorZ + 0.5f + dz * half);
        }
    }
}
