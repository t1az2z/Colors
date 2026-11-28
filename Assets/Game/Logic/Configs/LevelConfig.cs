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

        /// <summary>
        /// Перепады до этой высоты (включительно) строятся слопом, а не ступенькой — чтобы ходьба и камера шли плавно.
        /// Правило то же, что у воксельного редактора (SurfaceSmoothing): угол верха клетки поднимается до самого
        /// высокого соседнего верха (по сторонам и диагонали), который выше не больше чем на этот шаг. 0 — без слопов.
        /// </summary>
        public float SlopeMaxStep { get; set; } = 0.3f;

        public List<string> Rows { get; set; } = new List<string>();

        public LevelPoint Spawn { get; set; } = new LevelPoint();
        public List<LevelItem> Items { get; set; } = new List<LevelItem>();
        public List<LevelPoint> Dummies { get; set; } = new List<LevelPoint>();
        public List<LevelEnemy> Enemies { get; set; } = new List<LevelEnemy>();
        public List<LevelHint> Hints { get; set; } = new List<LevelHint>();
        public List<LevelDoor> Doors { get; set; } = new List<LevelDoor>();
        public List<LevelKeyDoor> KeyDoors { get; set; } = new List<LevelKeyDoor>();
        public List<LevelBonfire> Bonfires { get; set; } = new List<LevelBonfire>();
        public List<LevelCrate> Crates { get; set; } = new List<LevelCrate>();
        public List<LevelChest> Chests { get; set; } = new List<LevelChest>();
        public List<LevelBridge> Bridges { get; set; } = new List<LevelBridge>();

        public int Width { get; private set; }
        public int Length { get; private set; }

        public const int Hole = -1;

        public void OnConfigLoaded()
        {
            if (Rows == null || Rows.Count == 0)
                throw new InvalidOperationException(
                    "rows are empty (a voxel map from the level editor belongs in Configs/VoxelLevels, not Configs/Levels)");
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
            Enemies ??= new List<LevelEnemy>();
            Hints ??= new List<LevelHint>();
            Doors ??= new List<LevelDoor>();
            for (int i = 0; i < Doors.Count; i++)
            {
                if (string.IsNullOrEmpty(Doors[i].Id))
                    Doors[i].Id = $"door_{Doors[i].X}_{Doors[i].Z}";
            }
            KeyDoors ??= new List<LevelKeyDoor>();
            foreach (var d in KeyDoors)
            {
                if (string.IsNullOrEmpty(d.Id))
                    d.Id = $"keydoor_{d.X}_{d.Z}";
            }

            foreach (var e in Enemies)
            {
                if (string.IsNullOrEmpty(e.Key))
                    e.Key = null;
            }

            Bonfires ??= new List<LevelBonfire>();
            Crates ??= new List<LevelCrate>();
            foreach (var c in Crates)
            {
                if (string.IsNullOrEmpty(c.Id))
                    c.Id = $"crates_{c.X}_{c.Z}";
            }

            Chests ??= new List<LevelChest>();
            foreach (var c in Chests)
            {
                if (string.IsNullOrEmpty(c.Id))
                    c.Id = $"chest_{c.X}_{c.Z}";
                c.Count = Math.Max(1, c.Count);
            }

            Bridges ??= new List<LevelBridge>();
            foreach (var b in Bridges)
            {
                if (string.IsNullOrEmpty(b.Id))
                    b.Id = $"bridge_{b.X}_{b.Z}";
            }

            foreach (var b in Bonfires)
            {
                if (string.IsNullOrEmpty(b.Id))
                    b.Id = $"bonfire_{b.X}_{b.Z}";
            }

            foreach (var h in Hints)
            {
                if (string.IsNullOrEmpty(h.Id))
                    h.Id = $"hint_{h.X}_{h.Z}";
            }
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

        /// <summary>
        /// Высоты четырёх углов верха клетки с учётом слопов, в метрах.
        /// Порядок: (x0,z0), (x0,z1), (x1,z1), (x1,z0). Для дыры все углы равны -Depth.
        /// </summary>
        public void TopCorners(int x, int z, out float x0z0, out float x0z1, out float x1z1, out float x1z0)
        {
            var own = HeightAt(x, z);
            if (HeightSteps(x, z) == Hole)
            {
                x0z0 = x0z1 = x1z1 = x1z0 = own;
                return;
            }

            x0z0 = Corner(x, z, own, -1, -1);
            x0z1 = Corner(x, z, own, -1, 1);
            x1z1 = Corner(x, z, own, 1, 1);
            x1z0 = Corner(x, z, own, 1, -1);
        }

        /// <summary>Высота поверхности в точке клетки (u, v ∈ [0,1]), билинейно по углам.</summary>
        public float SurfaceHeight(int x, int z, float u, float v)
        {
            TopCorners(x, z, out var a00, out var a01, out var a11, out var a10);
            var a = a00 + (a10 - a00) * u;
            var b = a01 + (a11 - a01) * u;
            return a + (b - a) * v;
        }

        private float Corner(int x, int z, float own, int sx, int sz)
        {
            var h = own;
            h = Math.Max(h, Raise(x + sx, z, own));
            h = Math.Max(h, Raise(x, z + sz, own));
            h = Math.Max(h, Raise(x + sx, z + sz, own));
            return h;
        }

        private float Raise(int x, int z, float own)
        {
            const float eps = 1e-4f;
            if (SlopeMaxStep <= 0f || HeightSteps(x, z) == Hole)
                return own;
            var top = HeightAt(x, z);
            return top > own + eps && top <= own + SlopeMaxStep + eps ? top : own;
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

        /// <summary>
        /// Высота опоры в метрах. Пусто — берётся с карты высот; задаётся для воксельных уровней
        /// (<see cref="ColorsAction.Level.VoxelPlayLayout"/>), где в одной клетке бывает несколько этажей.
        /// </summary>
        public float? Y { get; set; }

        /// <summary>Поворот вокруг вертикали, в градусах.</summary>
        public float Yaw { get; set; }
    }

    [Preserve]
    public sealed class LevelItem : LevelPoint
    {
        public string Item { get; set; }
    }

    /// <summary>Враг на карте: тип из Enemies.json и радиус агро этого врага.</summary>
    [Preserve]
    public sealed class LevelEnemy : LevelPoint
    {
        /// <summary>Радиус агро врага, у которого в уровне он не задан (новые враги в редакторах, старые файлы).</summary>
        public const float DefaultAggroRadius = 7f;

        public string Type { get; set; }

        /// <summary>
        /// Цвет этого врага (один из круга Combat.json <c>colorWheel</c>): бонус урона оружием того же цвета.
        /// null — цвет типа по умолчанию (<see cref="EnemyConfig.Color"/>).
        /// </summary>
        public string Color { get; set; }

        /// <summary>Зона агро, м: игрок ближе — враг замечает его (если может до него дойти).</summary>
        public float AggroRadius { get; set; } = DefaultAggroRadius;

        /// <summary>
        /// Id двери на ключ (<see cref="LevelKeyDoor"/>): при смерти враг роняет ключ от неё, ключ лежит, пока игрок его
        /// не подберёт. null — без ключа.
        /// </summary>
        public string Key { get; set; }
    }

    /// <summary>
    /// Подсказка на полу: иконка действия (jump, roll, interact...).
    /// Подсказка, привязанная к предмету или двери, после подбора / открытия больше не показывается
    /// (см. <see cref="ColorsAction.Level.HintTargets"/>).
    /// </summary>
    [Preserve]
    public sealed class LevelHint : LevelPoint
    {
        /// <summary>Id подсказки (для запоминания выполненных). Пусто — "hint_x_z".</summary>
        public string Id { get; set; }

        public string Action { get; set; }

        /// <summary>
        /// К чему привязана: id двери или id предмета (ближайший такой предмет на карте). Пусто — подсказка
        /// "interact" сама привязывается к предметам и дверям рядом с ней.
        /// </summary>
        public string Target { get; set; }
    }

    /// <summary>
    /// Костёр: отдых восстанавливает здоровье и выносливость, возрождает врагов, становится точкой возрождения;
    /// у костра — меню апгрейдов оружия. <see cref="LevelPoint.Yaw"/> — куда смотрит место отдыха (там появляется игрок).
    /// </summary>
    [Preserve]
    public sealed class LevelBonfire : LevelPoint
    {
        /// <summary>Id костра. Пусто — "bonfire_x_z".</summary>
        public string Id { get; set; }
    }

    /// <summary>
    /// Стопка ломаемых ящиков (<see cref="ColorsAction.Level.CrateStackShape"/>): клетка (X, Z) — первая из двух,
    /// вторая — в сторону <see cref="LevelPoint.Yaw"/> (0 — +X, 270 — +Z). Параметры — Configs/Crates.json.
    /// </summary>
    [Preserve]
    public sealed class LevelCrate : LevelPoint
    {
        /// <summary>Id стопки (для запоминания разбитых). Пусто — "crates_x_z".</summary>
        public string Id { get; set; }

        /// <summary>Сколько ящиков в высоту.</summary>
        public int Height { get; set; } = 2;
    }

    /// <summary>
    /// Разводной мост 8×3 (<see cref="ColorsAction.Level.DrawBridgeShape"/>): клетка (X, Z) — ближний край опущенной
    /// створки, мост тянется на 8 клеток в сторону <see cref="LevelPoint.Yaw"/> (0 — +X, 90 — −Z, 180 — −X, 270 — +Z)
    /// и на 3 клетки в ширину (остальные — слева по ходу). Дальняя створка поднята и держится на цепях: выстрел из дробовика
    /// рвёт цепи, створка падает и открывает проход навсегда (<see cref="GameSession.OpenedBridges"/>).
    /// Параметры — Configs/Bridges.json. В воксельной карте у объекта моста другая клетка — якорь на берегу за дальним краем
    /// (<see cref="ColorsAction.Level.DrawBridgeShape.Origin"/> переводит её сюда).
    /// </summary>
    [Preserve]
    public sealed class LevelBridge : LevelPoint
    {
        /// <summary>Id моста (для запоминания опущенных). Пусто — "bridge_x_z".</summary>
        public string Id { get; set; }
    }

    /// <summary>
    /// Сундук в клетке: на E / X открывается, ресурс <see cref="Item"/> (id из Items.json) в количестве
    /// <see cref="Count"/> вылетает и летит к игроку. Открытый сундук остаётся открытым (<see cref="GameSession.OpenedChests"/>).
    /// <see cref="LevelPoint.Yaw"/> — куда смотрит лицевая сторона (0 — +Z).
    /// </summary>
    [Preserve]
    public sealed class LevelChest : LevelPoint
    {
        /// <summary>Id сундука (для запоминания открытых). Пусто — "chest_x_z".</summary>
        public string Id { get; set; }

        /// <summary>Ресурс внутри (id из Items.json). Пусто — сундук пустой.</summary>
        public string Item { get; set; }

        public int Count { get; set; } = 1;
    }

    /// <summary>
    /// Односторонняя дверь-срезка в клетке: <see cref="LevelPoint.Yaw"/> — лицевая сторона, только оттуда дверь
    /// открывается (0 — +Z, 90 — +X, 180 — −Z, 270 — −X). Ставится поперёк прохода в стене.
    /// </summary>
    [Preserve]
    public sealed class LevelDoor : LevelPoint
    {
        /// <summary>Id двери (для сохранения открытых срезок). Пусто — "door_x_z".</summary>
        public string Id { get; set; }

        /// <summary>Высота двери в метрах (кубах).</summary>
        public float Height { get; set; } = 3f;
    }

    /// <summary>
    /// Дверь на ключ в клетке (<see cref="ColorsAction.Level.KeyDoorRules"/>): плита поперёк прохода
    /// (<see cref="LevelPoint.Yaw"/> 0 — плита вдоль X, проход по Z; 90 — вдоль Z). Открывается с любой стороны, если у игрока
    /// есть ключ от неё (выпадает из врага с <see cref="LevelEnemy.Key"/> = <see cref="Id"/>); ключ тратится, дверь остаётся
    /// открытой навсегда (<see cref="GameSession.OpenedKeyDoors"/>).
    /// </summary>
    [Preserve]
    public sealed class LevelKeyDoor : LevelPoint
    {
        /// <summary>Id двери: на него ссылается ключ врага. Пусто — "keydoor_x_z".</summary>
        public string Id { get; set; }

        /// <summary>Высота двери в метрах (кубах).</summary>
        public float Height { get; set; } = 3f;
    }
}
