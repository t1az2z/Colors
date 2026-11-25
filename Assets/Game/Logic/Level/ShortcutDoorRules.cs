using System;

namespace ColorsAction.Level
{
    /// <summary>Результат попытки открыть дверь-срезку.</summary>
    public enum DoorOpenResult
    {
        /// <summary>Открыта сейчас.</summary>
        Opened,

        /// <summary>Уже была открыта.</summary>
        AlreadyOpen,

        /// <summary>Персонаж стоит с обратной стороны: «с этой стороны не открыть».</summary>
        WrongSide
    }

    /// <summary>
    /// Односторонняя дверь-срезка (шорткат в духе Dark Souls). Дверь стоит в клетке поперёк прохода;
    /// <c>yaw</c> смотрит в лицевую сторону — только оттуда её можно открыть. Открытая дверь не закрывается.
    /// Поворот как у объектов уровня: 0° — лицевая сторона в +Z, 90° — в +X, 180° — в −Z, 270° — в −X.
    /// </summary>
    public static class ShortcutDoorRules
    {
        /// <summary>
        /// Насколько персонаж должен быть впереди плоскости двери, чтобы считаться стоящим с лицевой стороны.
        /// Небольшой запас, чтобы точно сбоку от двери (вдоль стены) она не открывалась.
        /// </summary>
        public const float MinFrontDepth = 0.1f;

        /// <summary>Единичный вектор лицевой стороны (x, z) для поворота в градусах.</summary>
        public static (float x, float z) Front(float yaw)
        {
            var r = yaw * Math.PI / 180.0;
            return ((float)Math.Sin(r), (float)Math.Cos(r));
        }

        /// <summary>
        /// Расстояние от плоскости двери до точки вдоль лицевой стороны: больше нуля — спереди, меньше — сзади.
        /// </summary>
        public static float SideOf(float doorX, float doorZ, float yaw, float x, float z)
        {
            var (fx, fz) = Front(yaw);
            return (x - doorX) * fx + (z - doorZ) * fz;
        }

        /// <summary>Можно ли открыть дверь, стоя в точке (x, z).</summary>
        public static bool CanOpenFrom(float doorX, float doorZ, float yaw, float x, float z) =>
            SideOf(doorX, doorZ, yaw, x, z) > MinFrontDepth;
    }

    /// <summary>Состояние одной двери-срезки: закрыта → открыта, обратно не бывает.</summary>
    public sealed class ShortcutDoorState
    {
        public ShortcutDoorState(string id, float x, float z, float yaw, bool open = false)
        {
            Id = id;
            X = x;
            Z = z;
            Yaw = yaw;
            IsOpen = open;
        }

        public string Id { get; }

        /// <summary>Центр двери по X и Z (мировые координаты).</summary>
        public float X { get; }

        public float Z { get; }
        public float Yaw { get; }
        public bool IsOpen { get; private set; }

        public bool CanOpenFrom(float x, float z) => !IsOpen && ShortcutDoorRules.CanOpenFrom(X, Z, Yaw, x, z);

        public DoorOpenResult TryOpen(float x, float z)
        {
            if (IsOpen)
                return DoorOpenResult.AlreadyOpen;
            if (!ShortcutDoorRules.CanOpenFrom(X, Z, Yaw, x, z))
                return DoorOpenResult.WrongSide;
            IsOpen = true;
            return DoorOpenResult.Opened;
        }
    }
}
