using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Ближайшая к точке (x, z) точка взаимодействия на двери шириной <paramref name="width"/> клеток: на оси плиты
        /// между центрами крайних клеток. У двери в одну клетку — её центр.
        /// </summary>
        public static (float x, float z) NearestPoint(float doorX, float doorZ, float yaw, int width, float x, float z)
        {
            var (fx, fz) = Front(yaw);
            // Вдоль плиты: лицевая сторона, повёрнутая на 90°.
            float ax = fz, az = -fx;
            var half = Math.Max(0, width - 1) * 0.5f;
            var t = (x - doorX) * ax + (z - doorZ) * az;
            t = Math.Max(-half, Math.Min(half, t));
            return (doorX + ax * t, doorZ + az * t);
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

namespace ColorsAction.Level
{
    /// <summary>
    /// Несколько дверей, поставленных вплотную в одну линию: в игре это одна широкая дверь — одна плита,
    /// одно взаимодействие, открываются вместе. Формат карты не меняется: каждая клетка — своя дверь со своим id,
    /// группа собирается при загрузке (<see cref="ShortcutDoorGroups.Build"/> для срезок, <see cref="KeyDoorGroups"/>
    /// для дверей на ключ).
    /// </summary>
    public sealed class DoorGroup
    {
        internal DoorGroup(int[] doors, float centerX, float centerZ, float yaw, float? y, float height)
        {
            Doors = doors;
            CenterX = centerX;
            CenterZ = centerZ;
            Yaw = yaw;
            Y = y;
            Height = height;
        }

        /// <summary>Индексы дверей в исходном списке, по порядку вдоль плиты.</summary>
        public int[] Doors { get; }

        /// <summary>Ширина в клетках.</summary>
        public int Width => Doors.Length;

        /// <summary>Середина группы в координатах клеток (как <see cref="Configs.LevelPoint.X"/>; у одной двери — её клетка).</summary>
        public float CenterX { get; }

        public float CenterZ { get; }

        /// <summary>Поворот первой двери, приведённый к 0/90/180/270.</summary>
        public float Yaw { get; }

        public float? Y { get; }
        public float Height { get; }
    }

    /// <summary>Клетка двери для сборки групп: координаты, этаж, поворот и высота.</summary>
    internal readonly struct DoorCell
    {
        public DoorCell(int x, int z, float? y, float yaw, float height)
        {
            X = x;
            Z = z;
            Y = y;
            Yaw = yaw;
            Height = height;
        }

        public int X { get; }
        public int Z { get; }
        public float? Y { get; }
        public float Yaw { get; }
        public float Height { get; }
    }

    public static class ShortcutDoorGroups
    {
        /// <summary>Поворот, приведённый к 0/90/180/270.</summary>
        public static int SnapYaw(float yaw) => (int)((((Math.Round(yaw / 90.0) % 4) + 4) % 4) * 90);

        /// <summary>
        /// Соседние двери объединяются, если они смотрят в одну сторону, стоят на одной высоте, одной высоты
        /// и соприкасаются вдоль плиты (лицом в ±Z — соседи по X, лицом в ±X — соседи по Z).
        /// Двери, повёрнутые в разные стороны, остаются отдельными.
        /// </summary>
        public static List<DoorGroup> Build(IReadOnlyList<Configs.LevelDoor> doors) =>
            Build(doors, d => new DoorCell(d.X, d.Z, d.Y, d.Yaw, d.Height), false);

        /// <summary>
        /// Сборка групп. <paramref name="twoSided"/> — у двери нет лицевой стороны (дверь на ключ): 0° и 180°
        /// (90° и 270°) считаются одним поворотом.
        /// </summary>
        internal static List<DoorGroup> Build<T>(IReadOnlyList<T> source, Func<T, DoorCell> cell, bool twoSided)
        {
            var groups = new List<DoorGroup>();
            if (source == null)
                return groups;
            var doors = new DoorCell[source.Count];
            for (int i = 0; i < doors.Length; i++)
                doors[i] = cell(source[i]);
            var used = new bool[doors.Length];
            var members = new List<int>();
            for (int start = 0; start < doors.Length; start++)
            {
                if (used[start])
                    continue;
                members.Clear();
                members.Add(start);
                used[start] = true;
                for (int k = 0; k < members.Count; k++)
                {
                    var a = doors[members[k]];
                    for (int j = 0; j < doors.Length; j++)
                    {
                        if (!used[j] && Joins(a, doors[j], twoSided))
                        {
                            used[j] = true;
                            members.Add(j);
                        }
                    }
                }

                var first = doors[start];
                var side = SnapYaw(first.Yaw);
                var alongX = side % 180 == 0;
                members.Sort((l, r) => alongX ? doors[l].X.CompareTo(doors[r].X) : doors[l].Z.CompareTo(doors[r].Z));
                float cx = 0f, cz = 0f;
                foreach (var m in members)
                {
                    cx += doors[m].X;
                    cz += doors[m].Z;
                }

                groups.Add(new DoorGroup(members.ToArray(), cx / members.Count, cz / members.Count, side, first.Y,
                    first.Height));
            }

            return groups;
        }

        private static bool Joins(DoorCell a, DoorCell b, bool twoSided)
        {
            var side = SnapYaw(a.Yaw);
            var other = SnapYaw(b.Yaw);
            if (twoSided ? side % 180 != other % 180 : side != other)
                return false;
            if (Math.Abs(a.Height - b.Height) > 0.01f)
                return false;
            if (a.Y.HasValue != b.Y.HasValue || (a.Y.HasValue && Math.Abs(a.Y.Value - b.Y.Value) > 0.01f))
                return false;
            return side % 180 == 0
                ? a.Z == b.Z && Math.Abs(a.X - b.X) == 1
                : a.X == b.X && Math.Abs(a.Z - b.Z) == 1;
        }
    }

    /// <summary>
    /// Двери на ключ, стоящие вплотную в линию (одна ось прохода, один этаж, одна высота), — одна широкая дверь:
    /// открывается одним ключом от любой её клетки, ключ тратится один, открытыми помечаются все клетки.
    /// </summary>
    public static class KeyDoorGroups
    {
        public static List<DoorGroup> Build(IReadOnlyList<Configs.LevelKeyDoor> doors) =>
            ShortcutDoorGroups.Build(doors, d => new DoorCell(d.X, d.Z, d.Y, d.Yaw, d.Height), true);

        /// <summary>Группы дверей на ключ воксельной карты (для редакторов): id клеток каждой двери по порядку вдоль плиты.</summary>
        public static List<string[]> Build(VoxelLevel level)
        {
            var result = new List<string[]>();
            if (level == null)
                return result;
            var doors = new List<LevelObject>();
            foreach (var o in level.Objects)
                if (o.IsKeyDoor)
                    doors.Add(o);
            foreach (var g in ShortcutDoorGroups.Build(doors,
                         d => new DoorCell(d.Cell.X, d.Cell.Z, d.Cell.Y, d.Yaw, Math.Max(1, d.Height)), true))
            {
                var ids = new string[g.Width];
                for (int k = 0; k < ids.Length; k++)
                    ids[k] = doors[g.Doors[k]].Id;
                result.Add(ids);
            }

            return result;
        }
    }
}
