using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorsAction.Level
{
    /// <summary>
    /// Воксельный уровень: блоки по клеткам, этажи, объекты и точки взаимодействия.
    /// Модель без движка: её редактирует редактор уровней, по ней строятся меши и карта пути.
    /// </summary>
    public sealed class VoxelLevel
    {
        private readonly Dictionary<Int3, Block> blocks = new Dictionary<Int3, Block>();
        private readonly List<Floor> floors = new List<Floor>();
        private readonly List<LevelObject> objects = new List<LevelObject>();
        private readonly List<InteractionPoint> points = new List<InteractionPoint>();

        public string Name { get; set; } = "Новый уровень";

        /// <summary>Растёт при каждом изменении — по нему вид и карта пути понимают, что пора пересобраться.</summary>
        public int Version { get; private set; }

        /// <summary>Клетки, в которых поменялись блоки (для частичной пересборки мешей).</summary>
        public event Action<IReadOnlyCollection<Int3>> BlocksChanged;

        /// <summary>Поменялись объекты, точки или этажи.</summary>
        public event Action MarkupChanged;

        public IReadOnlyDictionary<Int3, Block> Blocks => blocks;
        public IReadOnlyList<Floor> Floors => floors;
        public IReadOnlyList<LevelObject> Objects => objects;
        public IReadOnlyList<InteractionPoint> Points => points;
        public int BlockCount => blocks.Count;

        public VoxelLevel()
        {
            floors.Add(new Floor { Id = "f0", Name = "Этаж 0", BaseY = 0 });
        }

        // ------------------------------------------------------------------ блоки

        public bool TryGet(Int3 cell, out Block block) => blocks.TryGetValue(cell, out block);
        public bool Has(Int3 cell) => blocks.ContainsKey(cell);

        /// <summary>Ставит блок (null — убирает). Возвращает прежнее значение.</summary>
        public Block? Set(Int3 cell, Block? block)
        {
            var had = blocks.TryGetValue(cell, out var old);
            if (block.HasValue)
            {
                if (had && old.Equals(block.Value))
                    return old;
                blocks[cell] = block.Value;
            }
            else
            {
                if (!had)
                    return null;
                blocks.Remove(cell);
            }

            Version++;
            BlocksChanged?.Invoke(new[] { cell });
            return had ? old : (Block?)null;
        }

        /// <summary>Пакетная запись без события на каждую клетку.</summary>
        public void SetMany(IEnumerable<KeyValuePair<Int3, Block?>> changes)
        {
            var changed = new List<Int3>();
            foreach (var kv in changes)
            {
                var had = blocks.TryGetValue(kv.Key, out var old);
                if (kv.Value.HasValue)
                {
                    if (had && old.Equals(kv.Value.Value))
                        continue;
                    blocks[kv.Key] = kv.Value.Value;
                }
                else
                {
                    if (!had)
                        continue;
                    blocks.Remove(kv.Key);
                }

                changed.Add(kv.Key);
            }

            if (changed.Count == 0)
                return;
            Version++;
            BlocksChanged?.Invoke(changed);
        }

        /// <summary>Блок, который держит персонажа (не декор).</summary>
        public bool IsSolid(Int3 cell) => blocks.TryGetValue(cell, out var b) && BlockShapes.IsSolid(b.Shape);

        /// <summary>Клетка закрыта целиком (полный куб).</summary>
        public bool IsFull(Int3 cell) => blocks.TryGetValue(cell, out var b) && BlockShapes.FillsCell(b.Shape);

        public (Int3 min, Int3 max) Bounds()
        {
            if (blocks.Count == 0)
                return (Int3.Zero, Int3.Zero);
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            foreach (var c in blocks.Keys)
            {
                if (c.X < minX) minX = c.X;
                if (c.Y < minY) minY = c.Y;
                if (c.Z < minZ) minZ = c.Z;
                if (c.X > maxX) maxX = c.X;
                if (c.Y > maxY) maxY = c.Y;
                if (c.Z > maxZ) maxZ = c.Z;
            }

            return (new Int3(minX, minY, minZ), new Int3(maxX, maxY, maxZ));
        }

        // ------------------------------------------------------------------ этажи

        /// <summary>Этаж, которому принадлежит абсолютная высота y (последний этаж с BaseY ≤ y).</summary>
        public int FloorIndexOf(int y)
        {
            var index = 0;
            for (int i = 0; i < floors.Count; i++)
            {
                if (floors[i].BaseY <= y)
                    index = i;
            }

            return index;
        }

        /// <summary>Абсолютный диапазон клеток этажа: [BaseY, следующий BaseY).</summary>
        public (int from, int to) FloorRange(int index)
        {
            var from = index == 0 ? int.MinValue : floors[index].BaseY;
            var to = index + 1 < floors.Count ? floors[index + 1].BaseY : int.MaxValue;
            return (from, to);
        }

        public Floor AddFloor(string name, int baseY)
        {
            var floor = new Floor { Id = NewId("f", floors.Select(f => f.Id)), Name = name, BaseY = baseY };
            InsertFloor(floor);
            return floor;
        }

        public void InsertFloor(Floor floor)
        {
            floors.Add(floor);
            SortFloors();
            Touch();
        }

        public void RemoveFloor(string id)
        {
            if (floors.Count <= 1)
                throw new InvalidOperationException("level needs at least one floor");
            floors.RemoveAll(f => f.Id == id);
            Touch();
        }

        public void ReplaceFloor(Floor floor)
        {
            var i = floors.FindIndex(f => f.Id == floor.Id);
            if (i < 0)
                throw new KeyNotFoundException(floor.Id);
            floors[i] = floor;
            SortFloors();
            Touch();
        }

        private void SortFloors() => floors.Sort((a, b) => a.BaseY.CompareTo(b.BaseY));

        // ------------------------------------------------------------------ объекты и точки

        public LevelObject FindObject(string id) => objects.Find(o => o.Id == id);
        public InteractionPoint FindPoint(string id) => points.Find(p => p.Id == id);

        public string NewObjectId(LevelObjectKind kind) => NewId(kind.ToString().ToLowerInvariant() + "_", objects.Select(o => o.Id));
        public string NewPointId(InteractionAction action) => NewId(action.ToString().ToLowerInvariant() + "_", points.Select(p => p.Id));

        /// <summary>Вставляет или заменяет объект с тем же Id.</summary>
        public void PutObject(LevelObject obj)
        {
            if (string.IsNullOrEmpty(obj.Id))
                throw new ArgumentException("object id is required");
            var i = objects.FindIndex(o => o.Id == obj.Id);
            if (i >= 0)
                objects[i] = obj;
            else
                objects.Add(obj);
            Touch();
        }

        public bool RemoveObject(string id)
        {
            var removed = objects.RemoveAll(o => o.Id == id) > 0;
            if (removed)
                Touch();
            return removed;
        }

        public void PutPoint(InteractionPoint point)
        {
            if (string.IsNullOrEmpty(point.Id))
                throw new ArgumentException("point id is required");
            var i = points.FindIndex(p => p.Id == point.Id);
            if (i >= 0)
                points[i] = point;
            else
                points.Add(point);
            Touch();
        }

        public bool RemovePoint(string id)
        {
            var removed = points.RemoveAll(p => p.Id == id) > 0;
            if (removed)
                Touch();
            return removed;
        }

        /// <summary>Барьер, закрывающий клетку, или null.</summary>
        public LevelObject BarrierAt(Int3 cell)
        {
            foreach (var o in objects)
            {
                if (o.Covers(cell))
                    return o;
            }

            return null;
        }

        private void Touch()
        {
            Version++;
            MarkupChanged?.Invoke();
        }

        private static string NewId(string prefix, IEnumerable<string> existing)
        {
            var set = new HashSet<string>(existing);
            for (int i = 1; ; i++)
            {
                var id = prefix + i;
                if (!set.Contains(id))
                    return id;
            }
        }

        /// <summary>Полностью заменяет содержимое (загрузка). Историю правок сбрасывает вызывающий.</summary>
        public void Reset(string name, IEnumerable<KeyValuePair<Int3, Block>> newBlocks, IEnumerable<Floor> newFloors,
            IEnumerable<LevelObject> newObjects, IEnumerable<InteractionPoint> newPoints)
        {
            Name = name;
            blocks.Clear();
            foreach (var kv in newBlocks)
                blocks[kv.Key] = kv.Value;
            floors.Clear();
            floors.AddRange(newFloors);
            if (floors.Count == 0)
                floors.Add(new Floor { Id = "f0", Name = "Этаж 0", BaseY = 0 });
            SortFloors();
            objects.Clear();
            objects.AddRange(newObjects);
            points.Clear();
            points.AddRange(newPoints);
            Version++;
            BlocksChanged?.Invoke(blocks.Keys.ToList());
            MarkupChanged?.Invoke();
        }
    }
}
