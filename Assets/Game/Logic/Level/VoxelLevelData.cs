using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ColorsAction.Configs;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Level
{
    /// <summary>
    /// Файл воксельного уровня: Configs/VoxelLevels/{name}.json. Читается <c>ConfigService</c>, как остальные конфиги.
    /// <para>Блок — строка <c>"x,y,z,форма,поворот,цвет"</c>, форма — буква:
    /// F полный, H полублок, S ступенька, L слоп, T верх слопа, X крутой слоп, D декор. Цвет можно опустить.</para>
    /// Клетки объектов и точек — строки <c>"x,y,z"</c>.
    /// </summary>
    [Preserve]
    public sealed class VoxelLevelData : IConfigPostLoad
    {
        public int Format { get; set; } = 1;
        public string Name { get; set; }
        public List<FloorData> Floors { get; set; } = new List<FloorData>();
        public List<string> Blocks { get; set; } = new List<string>();
        public List<ObjectData> Objects { get; set; } = new List<ObjectData>();
        public List<PointData> Points { get; set; } = new List<PointData>();

        public void OnConfigLoaded()
        {
            Floors ??= new List<FloorData>();
            Blocks ??= new List<string>();
            Objects ??= new List<ObjectData>();
            Points ??= new List<PointData>();
            foreach (var b in Blocks)
                ParseBlock(b);
        }

        [Preserve]
        public sealed class FloorData
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public int BaseY { get; set; }
        }

        [Preserve]
        public sealed class ObjectData
        {
            public string Id { get; set; }
            public LevelObjectKind Kind { get; set; }
            public string Cell { get; set; }
            public float Yaw { get; set; }
            public string Color { get; set; }
            public BarrierKind Barrier { get; set; }
            public int Height { get; set; } = 2;
            public string Item { get; set; }

            /// <summary>Количество (сундук: сколько ресурса). Не задано — 1.</summary>
            public int Count { get; set; } = 1;

            /// <summary>Радиус агро врага, м. Пишется только у врагов; не задан — <see cref="LevelEnemy.DefaultAggroRadius"/>.</summary>
            public float? AggroRadius { get; set; }

            /// <summary>Враг: id двери на ключ, ключ от которой он роняет. Пишется только у врагов с ключом.</summary>
            public string Key { get; set; }
        }

        [Preserve]
        public sealed class PointData
        {
            public string Id { get; set; }
            public InteractionAction Action { get; set; }
            public string Cell { get; set; }
            public string RequiredItem { get; set; }
            public string RequiredOpen { get; set; }
            public string TargetPoint { get; set; }
            public string TargetObject { get; set; }
            public string TargetFloor { get; set; }
            public bool TwoWay { get; set; }
        }

        // ------------------------------------------------------------------ формат блока

        private const string ShapeCodes = "FHSLTXDQ";

        public static string FormatBlock(Int3 cell, Block block)
        {
            var s = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
                cell.X, cell.Y, cell.Z, ShapeCodes[(int)block.Shape], block.Rotation);
            return block.Color == null ? s : s + "," + block.Color;
        }

        public static KeyValuePair<Int3, Block> ParseBlock(string text)
        {
            var p = text.Split(',');
            if (p.Length < 4)
                throw new FormatException($"block '{text}' must be 'x,y,z,shape[,rotation[,color]]'");
            var cell = new Int3(int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture),
                int.Parse(p[2], CultureInfo.InvariantCulture));
            var code = p[3].Trim();
            var shapeIndex = code.Length == 1 ? ShapeCodes.IndexOf(code[0]) : -1;
            if (shapeIndex < 0)
                throw new FormatException($"block '{text}': unknown shape '{code}'");
            var rotation = p.Length > 4 ? int.Parse(p[4], CultureInfo.InvariantCulture) : 0;
            var color = p.Length > 5 ? p[5].Trim() : null;
            return new KeyValuePair<Int3, Block>(cell, new Block((BlockShape)shapeIndex, rotation, color));
        }

        // ------------------------------------------------------------------ преобразование

        public VoxelLevel ToLevel()
        {
            var level = new VoxelLevel();
            Apply(level);
            return level;
        }

        public void Apply(VoxelLevel level)
        {
            OnConfigLoaded();
            level.Reset(Name ?? "Уровень",
                Blocks.Select(ParseBlock),
                Floors.Select(f => new Floor { Id = f.Id, Name = f.Name, BaseY = f.BaseY }),
                Objects.Select(o => new LevelObject
                {
                    Id = o.Id, Kind = o.Kind, Cell = Int3.Parse(o.Cell), Yaw = o.Yaw, Color = o.Color,
                    Barrier = o.Barrier, Height = o.Height, Item = o.Item, Count = Math.Max(1, o.Count),
                    AggroRadius = Math.Max(0f, o.AggroRadius ?? LevelEnemy.DefaultAggroRadius),
                    Key = string.IsNullOrEmpty(o.Key) ? null : o.Key
                }),
                Points.Select(p => new InteractionPoint
                {
                    Id = p.Id, Action = p.Action, Cell = Int3.Parse(p.Cell),
                    Condition = new NavCondition { RequiredItem = p.RequiredItem, RequiredOpen = p.RequiredOpen },
                    TargetPoint = p.TargetPoint, TargetObject = p.TargetObject, TargetFloor = p.TargetFloor, TwoWay = p.TwoWay
                }));
        }

        /// <summary>Снимок уровня для сохранения. Блоки упорядочены (y, z, x), чтобы diff файла был читаемым.</summary>
        public static VoxelLevelData From(VoxelLevel level)
        {
            return new VoxelLevelData
            {
                Name = level.Name,
                Floors = level.Floors.Select(f => new FloorData { Id = f.Id, Name = f.Name, BaseY = f.BaseY }).ToList(),
                Blocks = level.Blocks
                    .OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.Z).ThenBy(kv => kv.Key.X)
                    .Select(kv => FormatBlock(kv.Key, kv.Value)).ToList(),
                Objects = level.Objects.Select(o => new ObjectData
                {
                    Id = o.Id, Kind = o.Kind, Cell = o.Cell.ToString(), Yaw = o.Yaw, Color = o.Color,
                    Barrier = o.Barrier, Height = o.Height, Item = o.Item, Count = o.Count,
                    AggroRadius = IsEnemy(o) ? o.AggroRadius : (float?)null,
                    Key = IsEnemy(o) && !string.IsNullOrEmpty(o.Key) ? o.Key : null
                }).ToList(),
                Points = level.Points.Select(p => new PointData
                {
                    Id = p.Id, Action = p.Action, Cell = p.Cell.ToString(),
                    RequiredItem = p.Condition?.RequiredItem, RequiredOpen = p.Condition?.RequiredOpen,
                    TargetPoint = p.TargetPoint, TargetObject = p.TargetObject, TargetFloor = p.TargetFloor, TwoWay = p.TwoWay
                }).ToList()
            };
        }

        /// <summary>Настоящий враг (не манекен): у него есть радиус агро.</summary>
        public static bool IsEnemy(LevelObject o) =>
            o.Kind == LevelObjectKind.EnemySpawn && !string.IsNullOrEmpty(o.Item) && o.Item != VoxelPlayLayout.DummyType;

        /// <summary>
        /// Импорт карты высот прототипа (<see cref="LevelConfig"/>): каждый столбик превращается в полные кубы
        /// и верхушку из полублока/ступенек по остатку высоты. Точка появления, предметы, манекены, подсказки,
        /// двери и костры переносятся объектами.
        /// </summary>
        public static VoxelLevel FromHeightmap(LevelConfig config, int depthCells = 1)
        {
            var level = new VoxelLevel { Name = config.Name ?? "Импорт" };
            var blocks = new List<KeyValuePair<Int3, Block?>>();
            var stepsPerCube = Math.Max(1, (int)Math.Round(1f / config.Step));
            for (int z = 0; z < config.Length; z++)
            for (int x = 0; x < config.Width; x++)
            {
                var steps = config.HeightSteps(x, z);
                if (steps == LevelConfig.Hole)
                    continue;
                var full = steps / stepsPerCube;
                var rest = steps % stepsPerCube;
                for (int y = -depthCells; y < full; y++)
                    blocks.Add(new KeyValuePair<Int3, Block?>(new Int3(x, y, z), Block.Cube()));
                if (rest > 0)
                {
                    var top = rest * config.Step;
                    var shape = top >= 0.75f ? BlockShape.ThreeQuarter : top >= 0.5f ? BlockShape.Half : BlockShape.Step;
                    blocks.Add(new KeyValuePair<Int3, Block?>(new Int3(x, full, z), new Block(shape)));
                }
            }

            level.SetMany(blocks);

            Int3 StandCell(int x, int z)
            {
                var steps = Math.Max(0, config.HeightSteps(x, z));
                var full = steps / stepsPerCube;
                return new Int3(x, full, z);
            }

            level.PutObject(new LevelObject
            {
                Id = "spawn", Kind = LevelObjectKind.PlayerSpawn, Cell = StandCell(config.Spawn.X, config.Spawn.Z),
                Yaw = config.Spawn.Yaw
            });
            foreach (var item in config.Items)
            {
                level.PutObject(new LevelObject
                {
                    Id = level.NewObjectId(LevelObjectKind.WeaponPedestal), Kind = LevelObjectKind.WeaponPedestal,
                    Cell = StandCell(item.X, item.Z), Yaw = item.Yaw, Item = item.Item
                });
            }

            foreach (var d in config.Dummies)
            {
                level.PutObject(new LevelObject
                {
                    Id = level.NewObjectId(LevelObjectKind.EnemySpawn), Kind = LevelObjectKind.EnemySpawn,
                    Cell = StandCell(d.X, d.Z), Yaw = d.Yaw, Item = "dummy"
                });
            }

            foreach (var e in config.Enemies)
            {
                level.PutObject(new LevelObject
                {
                    Id = level.NewObjectId(LevelObjectKind.EnemySpawn), Kind = LevelObjectKind.EnemySpawn,
                    Cell = StandCell(e.X, e.Z), Yaw = e.Yaw, Item = e.Type, Color = e.Color, AggroRadius = e.AggroRadius, Key = e.Key
                });
            }

            foreach (var h in config.Hints)
            {
                level.PutObject(new LevelObject
                {
                    Id = level.NewObjectId(LevelObjectKind.Hint), Kind = LevelObjectKind.Hint,
                    Cell = StandCell(h.X, h.Z), Yaw = h.Yaw, Item = h.Action
                });
            }

            foreach (var d in config.Doors)
            {
                level.PutObject(new LevelObject
                {
                    Id = string.IsNullOrEmpty(d.Id) ? level.NewObjectId(LevelObjectKind.ShortcutDoor) : d.Id,
                    Kind = LevelObjectKind.ShortcutDoor, Cell = StandCell(d.X, d.Z), Yaw = d.Yaw,
                    Height = Math.Max(1, (int)Math.Ceiling(d.Height - 1e-3f))
                });
            }

            foreach (var d in config.KeyDoors)
            {
                level.PutObject(new LevelObject
                {
                    Id = string.IsNullOrEmpty(d.Id) ? level.NewObjectId(LevelObjectKind.KeyDoor) : d.Id,
                    Kind = LevelObjectKind.KeyDoor, Cell = StandCell(d.X, d.Z), Yaw = d.Yaw,
                    Height = Math.Max(1, (int)Math.Ceiling(d.Height - 1e-3f))
                });
            }

            foreach (var b in config.Bonfires)
            {
                level.PutObject(new LevelObject
                {
                    Id = string.IsNullOrEmpty(b.Id) ? level.NewObjectId(LevelObjectKind.Bonfire) : b.Id,
                    Kind = LevelObjectKind.Bonfire, Cell = StandCell(b.X, b.Z), Yaw = b.Yaw
                });
            }

            foreach (var c in config.Crates)
            {
                level.PutObject(new LevelObject
                {
                    Id = string.IsNullOrEmpty(c.Id) ? level.NewObjectId(LevelObjectKind.CrateStack) : c.Id,
                    Kind = LevelObjectKind.CrateStack, Cell = StandCell(c.X, c.Z), Yaw = c.Yaw, Height = Math.Max(1, c.Height)
                });
            }

            foreach (var c in config.Chests)
            {
                level.PutObject(new LevelObject
                {
                    Id = string.IsNullOrEmpty(c.Id) ? level.NewObjectId(LevelObjectKind.Chest) : c.Id,
                    Kind = LevelObjectKind.Chest, Cell = StandCell(c.X, c.Z), Yaw = c.Yaw, Item = c.Item, Count = Math.Max(1, c.Count)
                });
            }

            foreach (var b in config.Bridges)
            {
                level.PutObject(new LevelObject
                {
                    Id = string.IsNullOrEmpty(b.Id) ? level.NewObjectId(LevelObjectKind.DrawBridge) : b.Id,
                    Kind = LevelObjectKind.DrawBridge, Cell = StandCell(b.X, b.Z), Yaw = b.Yaw, Height = DrawBridgeShape.Height
                });
            }

            return level;
        }
    }
}
