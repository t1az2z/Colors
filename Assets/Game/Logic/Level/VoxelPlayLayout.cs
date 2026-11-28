using System;
using System.Collections.Generic;
using ColorsAction.Configs;

namespace ColorsAction.Level
{
    /// <summary>
    /// Воксельный уровень (формат веб-редактора, Configs/VoxelLevels) для игры: объекты переводятся в списки
    /// <see cref="LevelConfig"/> (точка появления, оружие, враги с ключами, манекены, подсказки, двери-срезки, двери на ключ, костры, ящики, сундуки, мосты) с высотой опоры
    /// <see cref="LevelPoint.Y"/>. Геометрию строит сам воксельный уровень. Барьеры и NPC игра пока не умеет — пропускаются.
    /// </summary>
    public static class VoxelPlayLayout
    {
        /// <summary>Тип врага, который ставит манекен для битья, а не врага из Enemies.json.</summary>
        public const string DummyType = "dummy";

        /// <param name="skipped">Описание объектов, которые игра пока не поддерживает (для предупреждения).</param>
        public static LevelConfig ToLevelConfig(VoxelLevel level, List<string> skipped = null)
        {
            var config = new LevelConfig { Name = level.Name };
            var hasSpawn = false;
            foreach (var o in level.Objects)
            {
                var y = StandHeight(level, o.Cell);
                switch (o.Kind)
                {
                    case LevelObjectKind.PlayerSpawn:
                        config.Spawn = Point(new LevelPoint(), o, y);
                        hasSpawn = true;
                        break;
                    case LevelObjectKind.WeaponPedestal:
                    case LevelObjectKind.Item:
                        if (string.IsNullOrEmpty(o.Item))
                            skipped?.Add(o.Kind == LevelObjectKind.Item ? $"{o.Id}: предмет не выбран" : $"{o.Id}: оружие не выбрано");
                        else
                            config.Items.Add(Point(new LevelItem { Item = o.Item }, o, y));
                        break;
                    case LevelObjectKind.EnemySpawn:
                        if (string.IsNullOrEmpty(o.Item) || o.Item == DummyType)
                        {
                            config.Dummies.Add(Point(new LevelPoint(), o, y));
                            if (!string.IsNullOrEmpty(o.Key))
                                skipped?.Add($"{o.Id}: манекен не роняет ключ от {o.Key}");
                        }
                        else
                        {
                            config.Enemies.Add(Point(new LevelEnemy
                            {
                                Type = o.Item, Color = string.IsNullOrEmpty(o.Color) ? null : o.Color, AggroRadius = o.AggroRadius, Key = string.IsNullOrEmpty(o.Key) ? null : o.Key
                            }, o, y));
                        }
                        break;
                    case LevelObjectKind.Hint:
                        config.Hints.Add(Point(new LevelHint { Id = o.Id, Action = string.IsNullOrEmpty(o.Item) ? "interact" : o.Item }, o, y));
                        break;
                    case LevelObjectKind.ShortcutDoor:
                        config.Doors.Add(Point(new LevelDoor { Id = o.Id, Height = Math.Max(1, o.Height) }, o, y));
                        break;
                    case LevelObjectKind.KeyDoor:
                        config.KeyDoors.Add(Point(new LevelKeyDoor { Id = o.Id, Height = Math.Max(1, o.Height) }, o, y));
                        break;
                    case LevelObjectKind.Bonfire:
                        config.Bonfires.Add(Point(new LevelBonfire { Id = o.Id }, o, y));
                        break;
                    case LevelObjectKind.CrateStack:
                        config.Crates.Add(Point(new LevelCrate { Id = o.Id, Height = Math.Max(1, o.Height) }, o, y));
                        break;
                    case LevelObjectKind.Chest:
                        if (string.IsNullOrEmpty(o.Item))
                            skipped?.Add($"{o.Id}: в сундуке не выбран ресурс — будет пустым");
                        config.Chests.Add(Point(new LevelChest { Id = o.Id, Item = o.Item, Count = Math.Max(1, o.Count) }, o, y));
                        break;
                    case LevelObjectKind.DrawBridge:
                    {
                        // В карте — якорь на берегу за дальним краем, игре нужно начало моста; высота настила — опора якоря.
                        var bridge = Point(new LevelBridge { Id = o.Id }, o, y);
                        var origin = DrawBridgeShape.Origin(o.Cell, o.Yaw);
                        bridge.X = origin.X;
                        bridge.Z = origin.Z;
                        config.Bridges.Add(bridge);
                        break;
                    }
                    default:
                        skipped?.Add($"{o.Id}: {o.Kind} пока не поддерживается в игре");
                        break;
                }
            }

            if (!hasSpawn)
            {
                skipped?.Add("нет точки старта игрока — игрок появится над серединой уровня");
                config.Spawn = DefaultSpawn(level);
            }

            return config;
        }

        private static T Point<T>(T p, LevelObject o, float y) where T : LevelPoint
        {
            p.X = o.Cell.X;
            p.Z = o.Cell.Z;
            p.Y = y;
            p.Yaw = o.Yaw;
            return p;
        }

        /// <summary>
        /// Высота опоры объекта в клетке (в метрах): верх неполного блока в самой клетке (полублок, ступенька, слоп)
        /// или верх блока под ней; без опоры — низ клетки.
        /// </summary>
        public static float StandHeight(VoxelLevel level, Int3 cell)
        {
            if (level.TryGet(cell, out var own) && BlockShapes.IsSolid(own.Shape) && !BlockShapes.FillsCell(own.Shape))
                return cell.Y + BlockShapes.TopAt(own.Shape, own.Rotation, 0.5f, 0.5f);
            if (level.TryGet(cell.Below, out var below) && BlockShapes.IsSolid(below.Shape))
                return cell.Y - 1 + BlockShapes.TopAt(below.Shape, below.Rotation, 0.5f, 0.5f);
            return cell.Y;
        }

        /// <summary>
        /// Есть ли в колонке (x, z) поверхность, на которую можно встать, не дальше <paramref name="maxDelta"/> по высоте
        /// от <paramref name="nearY"/>, и над ней свободно (две клетки). Возвращает ближайшую такую.
        /// </summary>
        public static bool TryStandNear(VoxelLevel level, int x, int z, float nearY, float maxDelta, out float height)
        {
            height = 0f;
            var best = float.MaxValue;
            var from = (int)Math.Floor(nearY - maxDelta) - 1;
            var to = (int)Math.Ceiling(nearY + maxDelta) + 1;
            for (int y = from; y <= to; y++)
            {
                var cell = new Int3(x, y, z);
                if (!level.TryGet(cell, out var b) || !BlockShapes.IsSolid(b.Shape))
                    continue;
                var top = y + BlockShapes.TopAt(b.Shape, b.Rotation, 0.5f, 0.5f);
                // Над опорой нужно место под персонажа (куб 1×1).
                if (level.IsSolid(new Int3(x, y + 1, z)) || level.IsFull(new Int3(x, y + 2, z)))
                    continue;
                var d = Math.Abs(top - nearY);
                if (d <= maxDelta + 1e-4f && d < best)
                {
                    best = d;
                    height = top;
                }
            }

            return best < float.MaxValue;
        }

        /// <summary>Точка появления по умолчанию: верх самой высокой опоры в середине уровня.</summary>
        private static LevelPoint DefaultSpawn(VoxelLevel level)
        {
            var (min, max) = level.Bounds();
            var x = (min.X + max.X) / 2;
            var z = (min.Z + max.Z) / 2;
            float y = max.Y + 1;
            for (int cy = max.Y; cy >= min.Y; cy--)
            {
                if (level.TryGet(new Int3(x, cy, z), out var b) && BlockShapes.IsSolid(b.Shape))
                {
                    y = cy + BlockShapes.TopAt(b.Shape, b.Rotation, 0.5f, 0.5f);
                    break;
                }
            }

            return new LevelPoint { X = x, Z = z, Y = y };
        }
    }
}
