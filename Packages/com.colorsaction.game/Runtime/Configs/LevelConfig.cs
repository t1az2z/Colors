using System;
using System.Collections.Generic;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Уровень из столбиков кубов 1×1 (Configs/Levels/*.json).
    /// <para><c>rows</c> — карта высот: строка = ряд по Z (первая строка — z = 0), символ = столбик по X.
    /// Символ кодирует высоту в шагах <c>step</c>: '0'..'9' = 0..9, 'a'..'z' = 10..35, '.' — пусто (дыра).</para>
    /// Объекты (точка появления, предметы, манекены) задаются в клетках карты.
    /// </summary>
    [Preserve]
    public sealed class LevelConfig : IConfigPostLoad
    {
        public string Name { get; set; }

        /// <summary>Высота одного шага, в метрах. 0.25 — четверть куба.</summary>
        public float Step { get; set; } = 0.25f;

        /// <summary>Глубина столбиков под нулевой высотой.</summary>
        public float Depth { get; set; } = 1f;

        public List<string> Rows { get; set; } = new List<string>();

        public LevelPoint Spawn { get; set; } = new LevelPoint();
        public List<LevelItem> Items { get; set; } = new List<LevelItem>();
        public List<LevelPoint> Dummies { get; set; } = new List<LevelPoint>();
        public List<LevelHint> Hints { get; set; } = new List<LevelHint>();

        public int Width { get; private set; }
        public int Length { get; private set; }

        public const int Hole = -1;

        public void OnConfigLoaded()
        {
            if (Rows == null || Rows.Count == 0)
                throw new InvalidOperationException("rows are empty");
            Width = Rows[0].Length;
            Length = Rows.Count;
            for (int z = 0; z < Rows.Count; z++)
            {
                if (Rows[z].Length != Width)
                    throw new InvalidOperationException($"row {z} has {Rows[z].Length} cells, expected {Width}");
                foreach (var c in Rows[z])
                    Decode(c);
            }

            Items ??= new List<LevelItem>();
            Dummies ??= new List<LevelPoint>();
            Hints ??= new List<LevelHint>();
            Spawn ??= new LevelPoint();
        }

        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Length;

        /// <summary>Высота столбика в шагах или <see cref="Hole"/>.</summary>
        public int HeightSteps(int x, int z) => InBounds(x, z) ? Decode(Rows[z][x]) : Hole;

        /// <summary>Высота верхней грани столбика в метрах; для дыры — минус глубина.</summary>
        public float HeightAt(int x, int z)
        {
            var steps = HeightSteps(x, z);
            return steps == Hole ? -Depth : steps * Step;
        }

        public static int Decode(char c)
        {
            if (c == '.')
                return Hole;
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'a' && c <= 'z')
                return c - 'a' + 10;
            throw new InvalidOperationException($"unknown height symbol '{c}'");
        }

        public static char Encode(int steps)
        {
            if (steps < 0)
                return '.';
            if (steps < 10)
                return (char)('0' + steps);
            if (steps < 36)
                return (char)('a' + steps - 10);
            throw new ArgumentOutOfRangeException(nameof(steps), "max 35 steps");
        }
    }

    /// <summary>Клетка карты. Y берётся из высоты столбика.</summary>
    [Preserve]
    public class LevelPoint
    {
        public int X { get; set; }
        public int Z { get; set; }

        /// <summary>Поворот вокруг вертикали, в градусах.</summary>
        public float Yaw { get; set; }
    }

    [Preserve]
    public sealed class LevelItem : LevelPoint
    {
        public string Item { get; set; }
    }

    /// <summary>Подсказка на полу: иконка действия (jump, roll, interact...).</summary>
    [Preserve]
    public sealed class LevelHint : LevelPoint
    {
        public string Action { get; set; }
    }
}
