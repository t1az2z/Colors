using System;
using System.Collections.Generic;

namespace ColorsAction.Level
{
    /// <summary>Форма блока. Все блоки занимают одну клетку 1×1×1, но верх у них разный.</summary>
    public enum BlockShape
    {
        /// <summary>Полный куб, верх 1.</summary>
        Full,

        /// <summary>Полублок, верх 0.5.</summary>
        Half,

        /// <summary>Ступенька, верх 0.25 (по метрике шага уровня; персонаж заходит без прыжка).</summary>
        Step,

        /// <summary>Пологий слоп: от 0 до 0.5 по направлению поворота. Проходим пешком.</summary>
        Slope,

        /// <summary>Верхняя половина пологого слопа: от 0.5 до 1. Вместе со <see cref="Slope"/> даёт подъём на куб за 2 клетки.</summary>
        SlopeTop,

        /// <summary>Крутой слоп: от 0 до 1 за одну клетку. Пешком не проходим, по нему съезжают на щите.</summary>
        SteepSlope,

        /// <summary>Декоративный блок: рисуется, но не мешает ходьбе и не держит персонажа.</summary>
        Decor,

        /// <summary>Блок в три четверти, верх 0.75 (шаг высоты столбца 0.25). Код в файле — Q, добавлен в конец, чтобы не сдвигать старые коды.</summary>
        ThreeQuarter
    }

    /// <summary>Блок в клетке: форма, поворот (0..3, куда поднимается слоп: +Z, +X, −Z, −X) и цвет из палитры.</summary>
    public readonly struct Block : IEquatable<Block>
    {
        public readonly BlockShape Shape;
        public readonly byte Rotation;

        /// <summary>Id цвета из Colors.json; null — цвет мира по умолчанию (белый с серым оттенком).</summary>
        public readonly string Color;

        public Block(BlockShape shape, int rotation = 0, string color = null)
        {
            Shape = shape;
            Rotation = (byte)(((rotation % 4) + 4) % 4);
            Color = string.IsNullOrEmpty(color) ? null : color;
        }

        public static Block Cube(string color = null) => new Block(BlockShape.Full, 0, color);

        public Block WithColor(string color) => new Block(Shape, Rotation, color);

        public bool Equals(Block other) => Shape == other.Shape && Rotation == other.Rotation && Color == other.Color;
        public override bool Equals(object obj) => obj is Block other && Equals(other);
        public override int GetHashCode() => ((int)Shape * 397) ^ (Rotation * 31) ^ (Color?.GetHashCode() ?? 0);
        public override string ToString() => $"{Shape}/{Rotation}/{Color ?? "-"}";
    }

    /// <summary>Геометрия форм: высоты верхней грани относительно низа клетки.</summary>
    public static class BlockShapes
    {
        public const float StepTop = 0.25f;

        /// <summary>Направление подъёма для поворота 0..3.</summary>
        public static (int dx, int dz) RiseDirection(int rotation)
        {
            switch (rotation & 3)
            {
                case 0: return (0, 1);
                case 1: return (1, 0);
                case 2: return (0, -1);
                default: return (-1, 0);
            }
        }

        /// <summary>Держит ли блок персонажа (есть ли у него верх, на который можно встать).</summary>
        public static bool IsSolid(BlockShape shape) => shape != BlockShape.Decor;

        /// <summary>Закрывает ли блок клетку целиком (для отсечения граней и проверки свободного места).</summary>
        public static bool FillsCell(BlockShape shape) => shape == BlockShape.Full;

        /// <summary>Низ и верх слопа вдоль направления подъёма.</summary>
        private static (float low, float high) SlopeRange(BlockShape shape)
        {
            switch (shape)
            {
                case BlockShape.Slope: return (0f, 0.5f);
                case BlockShape.SlopeTop: return (0.5f, 1f);
                case BlockShape.SteepSlope: return (0f, 1f);
                default: return (0f, 0f);
            }
        }

        public static bool IsSlope(BlockShape shape) =>
            shape == BlockShape.Slope || shape == BlockShape.SlopeTop || shape == BlockShape.SteepSlope;

        /// <summary>Высота верха в центре клетки.</summary>
        public static float CenterTop(BlockShape shape)
        {
            switch (shape)
            {
                case BlockShape.Full: return 1f;
                case BlockShape.ThreeQuarter: return 0.75f;
                case BlockShape.Half: return 0.5f;
                case BlockShape.Step: return StepTop;
                case BlockShape.Decor: return 0f;
                default:
                    var (low, high) = SlopeRange(shape);
                    return (low + high) * 0.5f;
            }
        }

        /// <summary>Наибольшая высота верха (для проверки свободного места над блоком).</summary>
        public static float MaxTop(BlockShape shape) => IsSlope(shape) ? SlopeRange(shape).high : CenterTop(shape);

        /// <summary>Высота верха на краю клетки в сторону (dx, dz) — по ней считается перепад при шаге к соседу.</summary>
        public static float EdgeTop(BlockShape shape, int rotation, int dx, int dz)
        {
            if (!IsSlope(shape))
                return CenterTop(shape);
            var (low, high) = SlopeRange(shape);
            var (rx, rz) = RiseDirection(rotation);
            var along = dx * rx + dz * rz;
            if (along > 0)
                return high;
            if (along < 0)
                return low;
            return (low + high) * 0.5f;
        }

        /// <summary>Высота верха в точке (u, v) внутри клетки, u,v ∈ [0,1] по X и Z.</summary>
        public static float TopAt(BlockShape shape, int rotation, float u, float v)
        {
            if (!IsSlope(shape))
                return CenterTop(shape);
            var (low, high) = SlopeRange(shape);
            float t;
            switch (rotation & 3)
            {
                case 0: t = v; break;
                case 1: t = u; break;
                case 2: t = 1 - v; break;
                default: t = 1 - u; break;
            }

            return low + (high - low) * t;
        }
    }

    /// <summary>Этаж: горизонтальный пласт уровня. Высота H в редакторе считается от <see cref="BaseY"/>.</summary>
    public sealed class Floor
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Нижняя клетка этажа (абсолютная Y).</summary>
        public int BaseY { get; set; }

        public Floor Clone() => (Floor)MemberwiseClone();
    }

    public enum LevelObjectKind
    {
        PlayerSpawn,
        Barrier,
        WeaponPedestal,
        Bonfire,
        EnemySpawn,
        Npc,
        Hint,

        /// <summary>
        /// Односторонняя дверь-срезка: <see cref="LevelObject.Yaw"/> смотрит в сторону, с которой её открывают,
        /// <see cref="LevelObject.Height"/> — высота в клетках. Закрытая блокирует проход, как барьер.
        /// </summary>
        ShortcutDoor,

        /// <summary>
        /// Стопка ломаемых ящиков (<see cref="CrateStackShape"/>): 2 клетки в ширину (от <see cref="LevelObject.Cell"/>
        /// в сторону <see cref="LevelObject.Yaw"/>), 1 в глубину, <see cref="LevelObject.Height"/> ящиков в высоту.
        /// Закрывает проход, пока не разбита; чем ломается и сколько выдерживает — Configs/Crates.json.
        /// </summary>
        CrateStack,

        /// <summary>
        /// Сундук в одной клетке: открывается на E / X, из него вылетает ресурс <see cref="LevelObject.Item"/>
        /// (id из Items.json, kind Resource) в количестве <see cref="LevelObject.Count"/>. Вид — Configs/Chests.json.
        /// </summary>
        Chest,

        /// <summary>
        /// Разводной мост (<see cref="DrawBridgeShape"/>): 4 клетки в длину от <see cref="LevelObject.Cell"/> в сторону
        /// <see cref="LevelObject.Yaw"/>, 2 в ширину (вторая — слева по ходу), 2 в высоту. Ближняя створка опущена,
        /// дальняя поднята у дальнего края и держится на красных цепях; выстрел из дробовика рвёт цепи, створка падает
        /// и открывает проход.
        /// Параметры — Configs/Bridges.json.
        /// </summary>
        DrawBridge
    }

    /// <summary>Тип цветного барьера (раздел 8 GDD).</summary>
    public enum BarrierKind
    {
        Wall,
        Crate,
        Membrane,
        Grate
    }

    /// <summary>
    /// Объект поверх сетки. <see cref="Cell"/> — клетка, в которой объект стоит (над блоком-опорой).
    /// Значение <see cref="Item"/> зависит от вида: оружие на постаменте, тип врага, id NPC, действие подсказки, ресурс в сундуке.
    /// </summary>
    public sealed class LevelObject
    {
        public string Id { get; set; }
        public LevelObjectKind Kind { get; set; }
        public Int3 Cell { get; set; }
        public float Yaw { get; set; }
        public string Color { get; set; }
        public BarrierKind Barrier { get; set; }

        /// <summary>Высота барьера или двери в клетках (сколько клеток вверх он закрывает).</summary>
        public int Height { get; set; } = 2;

        public string Item { get; set; }

        /// <summary>Сколько штук <see cref="Item"/> (сундук: количество ресурса).</summary>
        public int Count { get; set; } = 1;

        public bool IsBarrier => Kind == LevelObjectKind.Barrier;

        public bool IsChest => Kind == LevelObjectKind.Chest;

        public bool IsDoor => Kind == LevelObjectKind.ShortcutDoor;

        public bool IsCrate => Kind == LevelObjectKind.CrateStack;

        public bool IsBridge => Kind == LevelObjectKind.DrawBridge;

        /// <summary>Перекрывает ли проход, пока не открыт (барьер, дверь-срезка, неразбитые ящики, поднятый мост).</summary>
        public bool BlocksPath => IsBarrier || IsDoor || IsCrate || IsBridge;

        public LevelObject Clone() => (LevelObject)MemberwiseClone();

        /// <summary>Закрывает ли барьер, дверь, стопка ящиков или поднятая створка моста клетку.</summary>
        public bool Covers(Int3 cell)
        {
            if (IsBridge)
                return DrawBridgeShape.RaisedLeafCovers(Cell, Yaw, cell);
            if (!BlocksPath || cell.Y < Cell.Y || cell.Y >= Cell.Y + Math.Max(1, Height))
                return false;
            if (cell.X == Cell.X && cell.Z == Cell.Z)
                return true;
            return IsCrate && CrateStackShape.CoversColumn(Cell, Yaw, cell.X, cell.Z);
        }
    }

    /// <summary>Действие точки взаимодействия.</summary>
    public enum InteractionAction
    {
        /// <summary>Спуск на щите по крутому слопу: связь A → B, нужен щит.</summary>
        ShieldSlide,

        /// <summary>Лифт между A и B (в обе стороны).</summary>
        Lift,

        /// <summary>Якорь крюка-кошки: притягивает из A в B.</summary>
        GrappleAnchor,

        /// <summary>Кольцо кистеня: раскачка над пропастью из A в B.</summary>
        FlailRing,

        /// <summary>Рычаг: открывает объект-цель (барьер, дверь). Связи пути не создаёт.</summary>
        Lever,

        /// <summary>
        /// Связь через дверь-срезку для карты пути (A — лицевая сторона). Сама дверь — объект
        /// <see cref="LevelObjectKind.ShortcutDoor"/>; эта точка нужна, только чтобы описать проход явно.
        /// </summary>
        ShortcutDoor,

        /// <summary>Переход между этажами (лестница, люк): связь A ↔ B.</summary>
        FloorTransition
    }

    /// <summary>Условие точки или связи: нужный предмет и/или открытый барьер.</summary>
    public sealed class NavCondition
    {
        /// <summary>Id предмета из Items.json, который нужно иметь (например "shield").</summary>
        public string RequiredItem { get; set; }

        /// <summary>Id объекта-барьера, который должен быть открыт.</summary>
        public string RequiredOpen { get; set; }

        public bool IsEmpty => string.IsNullOrEmpty(RequiredItem) && string.IsNullOrEmpty(RequiredOpen);

        public NavCondition Clone() => (NavCondition)MemberwiseClone();

        public override string ToString()
        {
            if (IsEmpty)
                return "—";
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(RequiredItem))
                parts.Add("нужен " + RequiredItem);
            if (!string.IsNullOrEmpty(RequiredOpen))
                parts.Add("открыт " + RequiredOpen);
            return string.Join(", ", parts);
        }
    }

    /// <summary>Точка взаимодействия на клетке с привязанным действием, условием и целью.</summary>
    public sealed class InteractionPoint
    {
        public string Id { get; set; }
        public InteractionAction Action { get; set; }

        /// <summary>Клетка, в которой стоит персонаж, чтобы воспользоваться точкой.</summary>
        public Int3 Cell { get; set; }

        public NavCondition Condition { get; set; } = new NavCondition();

        /// <summary>Точка B для связей (спуск, лифт, крюк, переход).</summary>
        public string TargetPoint { get; set; }

        /// <summary>Объект-цель (рычаг → барьер или дверь).</summary>
        public string TargetObject { get; set; }

        /// <summary>Этаж-цель (для перехода между этажами; для подсказки редактору).</summary>
        public string TargetFloor { get; set; }

        /// <summary>Можно ли пройти связь в обратную сторону, B → A.</summary>
        public bool TwoWay { get; set; }

        public InteractionPoint Clone()
        {
            var c = (InteractionPoint)MemberwiseClone();
            c.Condition = Condition?.Clone() ?? new NavCondition();
            return c;
        }

        /// <summary>Создаёт ли действие связь на карте пути (при заданной точке B).</summary>
        public static bool CreatesLink(InteractionAction action) => action != InteractionAction.Lever;

        /// <summary>Значения по умолчанию для нового действия: условие и двусторонность.</summary>
        public static InteractionPoint Create(string id, InteractionAction action, Int3 cell)
        {
            var p = new InteractionPoint { Id = id, Action = action, Cell = cell };
            switch (action)
            {
                case InteractionAction.ShieldSlide:
                    p.Condition.RequiredItem = "shield";
                    break;
                case InteractionAction.GrappleAnchor:
                    p.Condition.RequiredItem = "hook";
                    break;
                case InteractionAction.FlailRing:
                    p.Condition.RequiredItem = "flail";
                    break;
                case InteractionAction.Lift:
                case InteractionAction.FloorTransition:
                case InteractionAction.ShortcutDoor:
                    p.TwoWay = true;
                    break;
            }

            return p;
        }
    }
}
