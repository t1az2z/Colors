using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorsAction.Level.Navigation
{
    /// <summary>
    /// Может ли враг дойти до игрока по воксельному уровню: карта пути (<see cref="NavGraph"/>) с профилем перемещения
    /// врага и текущим состоянием мира. Закрытая дверь-срезка, закрытая дверь на ключ, неразбитые ящики и поднятая створка
    /// моста — стены; открытые — проход. Под настилом моста (в уровне там пропасть) добавляются опоры, чтобы опущенный мост соединял берега.
    /// Достижимые узлы считаются заливкой от узла врага и кэшируются до изменения мира.
    /// </summary>
    public sealed class EnemyReach
    {
        private readonly NavGraph graph;
        private readonly PathFinder finder;
        private readonly Context context;
        private readonly Func<int> worldVersion;
        private readonly Dictionary<(int node, string profile), HashSet<int>> cache = new Dictionary<(int, string), HashSet<int>>();
        private int cachedVersion = int.MinValue;

        /// <param name="isOpen">Открыт ли объект (дверь открыта, ящики разбиты, мост опущен).</param>
        /// <param name="worldVersion">Меняется, когда что-то открылось: сбрасывает кэш.</param>
        public EnemyReach(VoxelLevel level, Func<string, bool> isOpen, Func<int> worldVersion)
        {
            if (level == null)
                throw new ArgumentNullException(nameof(level));
            graph = NavGraph.Build(WithBridgeDecks(level, out var farDecks));
            // Опоры под дальней створкой есть, только пока мост опущен: поднятая створка стоит у дальнего края,
            // а под клетками 4–6 пропасть. Узел на такой опоре закрыт, как клетка барьера, пока мост не открыт.
            foreach (var node in graph.Nodes)
            {
                if (node.Barrier == null && farDecks.TryGetValue(node.Block, out var bridgeId))
                    node.Barrier = bridgeId;
            }

            finder = new PathFinder(graph);
            // Цветные барьеры игра пока не строит — для врага их нет.
            var alwaysOpen = new HashSet<string>(level.Objects.Where(o => o.IsBarrier).Select(o => o.Id));
            context = new Context(id => alwaysOpen.Contains(id) || (isOpen != null && isOpen(id)));
            this.worldVersion = worldVersion ?? (() => 0);
        }

        public NavGraph Graph => graph;

        /// <summary>Дойдёт ли враг с профилем <paramref name="profile"/> из точки <paramref name="from"/> до <paramref name="to"/> (метры).</summary>
        public bool CanReach(MovementProfile profile, float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
        {
            if (profile == null)
                return true;
            var start = graph.NodeAt(fromX, fromY, fromZ);
            // Враг не над картой (ошибка расстановки) — не мешаем ему, как раньше.
            if (start == null)
                return true;
            var goal = graph.NodeAt(toX, toY, toZ);
            if (goal == null)
                return false;
            if (start.Index == goal.Index)
                return true;
            var version = worldVersion();
            if (version != cachedVersion)
            {
                cache.Clear();
                cachedVersion = version;
            }

            var key = (start.Index, profile.Id ?? string.Empty);
            if (!cache.TryGetValue(key, out var reachable))
                cache[key] = reachable = finder.Reachable(start, profile, context);
            return reachable.Contains(goal.Index);
        }

        /// <summary>
        /// Копия уровня с опорами под всеми клетками разводных мостов (настил на высоте клетки моста).
        /// <paramref name="farDecks"/> — добавленные опоры под дальней створкой (клетки длины от
        /// <see cref="DrawBridgeShape.LeafLength"/> до <see cref="DrawBridgeShape.RaisedColumn"/> − 1) → id моста.
        /// </summary>
        private static VoxelLevel WithBridgeDecks(VoxelLevel level, out Dictionary<Int3, string> farDecks)
        {
            farDecks = new Dictionary<Int3, string>();
            var copy = new VoxelLevel();
            copy.Reset(level.Name, level.Blocks, level.Floors.Select(f => f.Clone()), level.Objects.Select(o => o.Clone()),
                level.Points.Select(p => p.Clone()));
            var decks = new List<KeyValuePair<Int3, Block?>>();
            foreach (var o in level.Objects)
            {
                if (!o.IsBridge)
                    continue;
                for (int i = 0; i < DrawBridgeShape.Length; i++)
                for (int j = 0; j < DrawBridgeShape.Width; j++)
                {
                    var c = DrawBridgeShape.Cell(o.Cell, o.Yaw, i, j);
                    var below = new Int3(c.X, c.Y - 1, c.Z);
                    if (copy.Blocks.ContainsKey(below) || decks.Any(d => d.Key.Equals(below)))
                        continue;
                    decks.Add(new KeyValuePair<Int3, Block?>(below, Block.Cube()));
                    if (i >= DrawBridgeShape.LeafLength && i < DrawBridgeShape.RaisedColumn)
                        farDecks[below] = o.Id;
                }
            }

            if (decks.Count > 0)
                copy.SetMany(decks);
            return copy;
        }

        private sealed class Context : INavContext
        {
            private readonly Func<string, bool> isOpen;

            public Context(Func<string, bool> isOpen) => this.isOpen = isOpen;

            // Особыми связями (предметы) враги не пользуются.
            public bool HasItem(string itemId) => false;
            public bool IsOpen(string objectId) => isOpen(objectId);
        }
    }
}
