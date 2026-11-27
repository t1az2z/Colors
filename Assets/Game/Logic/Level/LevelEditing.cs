using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorsAction.Level
{
    /// <summary>Обратимая правка уровня.</summary>
    public interface ILevelCommand
    {
        string Name { get; }
        void Do(VoxelLevel level);
        void Undo(VoxelLevel level);

        /// <summary>Пустая правка (ничего не изменила) в историю не попадает.</summary>
        bool IsEmpty { get; }
    }

    /// <summary>Правка блоков: для каждой клетки помнит значение до и после.</summary>
    public sealed class BlockEditCommand : ILevelCommand
    {
        private readonly Dictionary<Int3, (Block? before, Block? after)> cells = new Dictionary<Int3, (Block?, Block?)>();

        public string Name { get; }
        public IReadOnlyDictionary<Int3, (Block? before, Block? after)> Cells => cells;

        public BlockEditCommand(string name)
        {
            Name = name;
        }

        public bool IsEmpty
        {
            get
            {
                foreach (var kv in cells)
                {
                    if (!Equal(kv.Value.before, kv.Value.after))
                        return false;
                }

                return true;
            }
        }

        /// <summary>Записывает изменение клетки и сразу применяет его к уровню.</summary>
        public void Record(VoxelLevel level, Int3 cell, Block? after)
        {
            Block? current = level.TryGet(cell, out var b) ? b : (Block?)null;
            if (cells.TryGetValue(cell, out var entry))
                cells[cell] = (entry.before, after);
            else
            {
                if (Equal(current, after))
                    return;
                cells[cell] = (current, after);
            }

            level.Set(cell, after);
        }

        public void Do(VoxelLevel level) => Apply(level, false);
        public void Undo(VoxelLevel level) => Apply(level, true);

        private void Apply(VoxelLevel level, bool undo)
        {
            var list = new List<KeyValuePair<Int3, Block?>>(cells.Count);
            foreach (var kv in cells)
                list.Add(new KeyValuePair<Int3, Block?>(kv.Key, undo ? kv.Value.before : kv.Value.after));
            level.SetMany(list);
        }

        private static bool Equal(Block? a, Block? b) =>
            a.HasValue == b.HasValue && (!a.HasValue || a.Value.Equals(b.Value));
    }

    /// <summary>Правка разметки (объекты, точки, этажи): снимки до и после.</summary>
    public sealed class MarkupCommand : ILevelCommand
    {
        private readonly Action<VoxelLevel> doAction;
        private readonly Action<VoxelLevel> undoAction;

        public string Name { get; }
        public bool IsEmpty => false;

        public MarkupCommand(string name, Action<VoxelLevel> doAction, Action<VoxelLevel> undoAction)
        {
            Name = name;
            this.doAction = doAction;
            this.undoAction = undoAction;
        }

        public void Do(VoxelLevel level) => doAction(level);
        public void Undo(VoxelLevel level) => undoAction(level);

        /// <summary>Поставить или заменить объект (before == null — объект новый).</summary>
        public static MarkupCommand PutObject(LevelObject before, LevelObject after)
        {
            var b = before?.Clone();
            var a = after.Clone();
            return new MarkupCommand(before == null ? "Поставить объект" : "Изменить объект",
                l => l.PutObject(a.Clone()),
                l =>
                {
                    if (b == null)
                        l.RemoveObject(a.Id);
                    else
                        l.PutObject(b.Clone());
                });
        }

        /// <summary>Заменить несколько объектов одной отменой (ключ двери переходит от врага к врагу).</summary>
        public static MarkupCommand PutObjects(string name, IEnumerable<(LevelObject before, LevelObject after)> changes)
        {
            var list = changes.Select(c => (b: c.before.Clone(), a: c.after.Clone())).ToList();
            return new MarkupCommand(name,
                l =>
                {
                    foreach (var c in list)
                        l.PutObject(c.a.Clone());
                },
                l =>
                {
                    foreach (var c in list)
                        l.PutObject(c.b.Clone());
                });
        }

        public static MarkupCommand RemoveObject(LevelObject obj)
        {
            var o = obj.Clone();
            return new MarkupCommand("Удалить объект", l => l.RemoveObject(o.Id), l => l.PutObject(o.Clone()));
        }

        public static MarkupCommand PutPoint(InteractionPoint before, InteractionPoint after)
        {
            var b = before?.Clone();
            var a = after.Clone();
            return new MarkupCommand(before == null ? "Поставить точку" : "Изменить точку",
                l => l.PutPoint(a.Clone()),
                l =>
                {
                    if (b == null)
                        l.RemovePoint(a.Id);
                    else
                        l.PutPoint(b.Clone());
                });
        }

        public static MarkupCommand RemovePoint(InteractionPoint point)
        {
            var p = point.Clone();
            return new MarkupCommand("Удалить точку", l => l.RemovePoint(p.Id), l => l.PutPoint(p.Clone()));
        }

        public static MarkupCommand AddFloor(Floor floor)
        {
            var f = floor.Clone();
            return new MarkupCommand("Добавить этаж", l => l.InsertFloor(f.Clone()), l => l.RemoveFloor(f.Id));
        }

        public static MarkupCommand RemoveFloor(Floor floor)
        {
            var f = floor.Clone();
            return new MarkupCommand("Удалить этаж", l => l.RemoveFloor(f.Id), l => l.InsertFloor(f.Clone()));
        }

        public static MarkupCommand ChangeFloor(Floor before, Floor after)
        {
            var b = before.Clone();
            var a = after.Clone();
            return new MarkupCommand("Изменить этаж", l => l.ReplaceFloor(a.Clone()), l => l.ReplaceFloor(b.Clone()));
        }
    }

    /// <summary>
    /// История правок с отменой и повтором. Позиция <see cref="Position"/> — число применённых правок;
    /// редактор Unity связывает её со своим Undo (Ctrl+Z / Ctrl+Y).
    /// </summary>
    public sealed class LevelHistory
    {
        private readonly VoxelLevel level;
        private readonly List<ILevelCommand> commands = new List<ILevelCommand>();
        private BlockEditCommand stroke;

        public int Limit { get; set; } = 500;
        public int Position { get; private set; }
        public int Count => commands.Count;
        public bool CanUndo => Position > 0;
        public bool CanRedo => Position < commands.Count;
        public bool InStroke => stroke != null;

        /// <summary>Сколько правок выброшено из начала истории из-за <see cref="Limit"/> (для синхронизации номеров).</summary>
        public int Trimmed { get; private set; }

        public event Action Changed;

        public LevelHistory(VoxelLevel level)
        {
            this.level = level;
        }

        public string UndoName => CanUndo ? commands[Position - 1].Name : null;
        public string RedoName => CanRedo ? commands[Position].Name : null;

        /// <summary>Уже выполненную правку кладёт в историю (отбрасывая отменённый хвост).</summary>
        public bool Push(ILevelCommand command)
        {
            if (command.IsEmpty)
                return false;
            if (Position < commands.Count)
                commands.RemoveRange(Position, commands.Count - Position);
            commands.Add(command);
            Position++;
            if (commands.Count > Limit)
            {
                commands.RemoveAt(0);
                Position--;
                Trimmed++;
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>Выполняет правку и кладёт её в историю.</summary>
        public bool Execute(ILevelCommand command)
        {
            command.Do(level);
            return Push(command);
        }

        /// <summary>Начинает мазок: все клетки до <see cref="EndStroke"/> — одна правка.</summary>
        public void BeginStroke(string name)
        {
            EndStroke();
            stroke = new BlockEditCommand(name);
        }

        public void StrokeSet(Int3 cell, Block? block)
        {
            if (stroke == null)
                throw new InvalidOperationException("BeginStroke first");
            stroke.Record(level, cell, block);
        }

        /// <summary>Заканчивает мазок; true — если мазок что-то изменил и попал в историю.</summary>
        public bool EndStroke()
        {
            if (stroke == null)
                return false;
            var s = stroke;
            stroke = null;
            return Push(s);
        }

        /// <summary>Отменяет незаконченный мазок без записи в историю.</summary>
        public void CancelStroke()
        {
            if (stroke == null)
                return;
            stroke.Undo(level);
            stroke = null;
        }

        public bool Undo()
        {
            EndStroke();
            if (!CanUndo)
                return false;
            Position--;
            commands[Position].Undo(level);
            Changed?.Invoke();
            return true;
        }

        public bool Redo()
        {
            EndStroke();
            if (!CanRedo)
                return false;
            commands[Position].Do(level);
            Position++;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Номер состояния, не меняющийся при обрезке истории: Trimmed + Position.</summary>
        public int Revision => Trimmed + Position;

        /// <summary>Отматывает историю к номеру состояния (для связки с Undo Unity).</summary>
        public void SeekRevision(int revision)
        {
            EndStroke();
            while (Revision > revision && Undo())
            {
            }

            while (Revision < revision && Redo())
            {
            }
        }

        public void Clear()
        {
            stroke = null;
            commands.Clear();
            Position = 0;
            Trimmed = 0;
            Changed?.Invoke();
        }
    }

    /// <summary>Режим кисти блоков.</summary>
    public enum BrushMode
    {
        /// <summary>Блок только на уровне H.</summary>
        Single,

        /// <summary>Столбец от пола этажа до H.</summary>
        Column
    }

    /// <summary>Операции кисти поверх истории: рисование, стирание и покраска квадратом размера Size.</summary>
    public sealed class LevelBrush
    {
        private readonly VoxelLevel level;
        private readonly LevelHistory history;

        public int Size { get; set; } = 1;
        public BrushMode Mode { get; set; } = BrushMode.Single;

        public LevelBrush(VoxelLevel level, LevelHistory history)
        {
            this.level = level;
            this.history = history;
        }

        /// <summary>Клетки квадрата кисти с центром в (x, z).</summary>
        public IEnumerable<(int x, int z)> Footprint(int x, int z)
        {
            var size = Math.Max(1, Size);
            var start = -(size - 1) / 2;
            for (int dz = 0; dz < size; dz++)
            for (int dx = 0; dx < size; dx++)
                yield return (x + start + dx, z + start + dz);
        }

        /// <summary>Ставит блок на абсолютной высоте y (в режиме столбца — от floorBase до y).</summary>
        public void Paint(int x, int y, int z, Block block, int floorBase)
        {
            EnsureStroke("Рисование");
            foreach (var (cx, cz) in Footprint(x, z))
            {
                if (Mode == BrushMode.Column)
                {
                    for (int cy = Math.Min(floorBase, y); cy < y; cy++)
                    {
                        // Под верхним блоком столбец заполняется полными кубами того же цвета.
                        history.StrokeSet(new Int3(cx, cy, cz), Block.Cube(block.Color));
                    }
                }

                history.StrokeSet(new Int3(cx, y, cz), block);
            }
        }

        /// <summary>Убирает блоки только на высоте y; ниже и выше остаются.</summary>
        public void Erase(int x, int y, int z)
        {
            EnsureStroke("Стирание");
            foreach (var (cx, cz) in Footprint(x, z))
                history.StrokeSet(new Int3(cx, y, cz), null);
        }

        /// <summary>Меняет цвет стоящих блоков на высоте y, форму не трогает.</summary>
        public void Recolor(int x, int y, int z, string color)
        {
            EnsureStroke("Покраска");
            foreach (var (cx, cz) in Footprint(x, z))
            {
                var cell = new Int3(cx, y, cz);
                if (level.TryGet(cell, out var b))
                    history.StrokeSet(cell, b.WithColor(color));
            }
        }

        /// <summary>Покраска одного блока, в который попал луч (без учёта H).</summary>
        public void RecolorCell(Int3 cell, string color)
        {
            EnsureStroke("Покраска");
            if (level.TryGet(cell, out var b))
                history.StrokeSet(cell, b.WithColor(color));
        }

        public bool End() => history.EndStroke();

        private void EnsureStroke(string name)
        {
            if (!history.InStroke)
                history.BeginStroke(name);
        }
    }
}
