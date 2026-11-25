using System;
using System.Collections.Generic;

namespace ColorsAction.Level.Navigation
{
    /// <summary>Шаг найденного пути: узел и то, как в него попали.</summary>
    public readonly struct PathStep
    {
        public readonly NavNode Node;
        public readonly NavMove Move;
        public readonly NavLink Link;

        public PathStep(NavNode node, NavMove move, NavLink link)
        {
            Node = node;
            Move = move;
            Link = link;
        }
    }

    /// <summary>A* по <see cref="NavGraph"/> с учётом профиля перемещения и состояния мира.</summary>
    public sealed class PathFinder
    {
        private readonly NavGraph graph;

        // Буферы переиспользуются между запросами.
        private float[] g = Array.Empty<float>();
        private int[] cameFrom = Array.Empty<int>();
        private NavLink[] cameBy = Array.Empty<NavLink>();
        private NavMove[] cameMove = Array.Empty<NavMove>();
        private int[] stamp = Array.Empty<int>();
        private bool[] closed = Array.Empty<bool>();
        private int query;
        private readonly MinHeap open = new MinHeap();

        /// <summary>Предел раскрытых узлов на запрос (защита от поиска по всему миру).</summary>
        public int MaxExpanded { get; set; } = 200000;

        public int LastExpanded { get; private set; }

        public PathFinder(NavGraph graph)
        {
            this.graph = graph;
        }

        public bool TryFind(NavNode start, NavNode goal, MovementProfile profile, INavContext context, List<PathStep> path)
        {
            path.Clear();
            LastExpanded = 0;
            if (start == null || goal == null)
                return false;
            Prepare();
            query++;

            Visit(start.Index, 0f, -1, null, NavMove.None);
            open.Clear();
            open.Push(start.Index, Heuristic(start, goal));

            while (open.Count > 0)
            {
                var current = open.Pop();
                if (closed[current])
                    continue;
                closed[current] = true;
                if (current == goal.Index)
                {
                    Reconstruct(goal.Index, path);
                    return true;
                }

                if (++LastExpanded > MaxExpanded)
                    return false;

                var node = graph.Nodes[current];
                foreach (var link in node.Links)
                {
                    var move = graph.Classify(link, profile, context);
                    if (move == NavMove.None)
                        continue;
                    var next = link.To;
                    var cost = g[current] + graph.Cost(link, move, profile);
                    if (stamp[next] == query && (closed[next] || cost >= g[next]))
                        continue;
                    Visit(next, cost, current, link, move);
                    open.Push(next, cost + Heuristic(graph.Nodes[next], goal));
                }
            }

            return false;
        }

        /// <summary>Все узлы, достижимые из старта (для наложения «куда пройдёт персонаж»).</summary>
        public HashSet<int> Reachable(NavNode start, MovementProfile profile, INavContext context)
        {
            var result = new HashSet<int>();
            if (start == null)
                return result;
            var stack = new Stack<int>();
            stack.Push(start.Index);
            result.Add(start.Index);
            while (stack.Count > 0)
            {
                var node = graph.Nodes[stack.Pop()];
                foreach (var link in node.Links)
                {
                    if (graph.Classify(link, profile, context) == NavMove.None)
                        continue;
                    if (result.Add(link.To))
                        stack.Push(link.To);
                }
            }

            return result;
        }

        private static float Heuristic(NavNode a, NavNode b)
        {
            var dx = a.Block.X - b.Block.X;
            var dz = a.Block.Z - b.Block.Z;
            // Особые связи дешевле расстояния, поэтому эвристика занижена вдвое — остаётся допустимой.
            return (float)Math.Sqrt(dx * dx + dz * dz) * 0.5f;
        }

        private void Visit(int index, float cost, int from, NavLink link, NavMove move)
        {
            if (stamp[index] != query)
            {
                stamp[index] = query;
                closed[index] = false;
            }

            g[index] = cost;
            cameFrom[index] = from;
            cameBy[index] = link;
            cameMove[index] = move;
        }

        private void Reconstruct(int goal, List<PathStep> path)
        {
            for (int i = goal; i >= 0; i = cameFrom[i])
                path.Add(new PathStep(graph.Nodes[i], cameMove[i], cameBy[i]));
            path.Reverse();
        }

        private void Prepare()
        {
            var n = graph.Nodes.Count;
            if (g.Length >= n)
                return;
            g = new float[n];
            cameFrom = new int[n];
            cameBy = new NavLink[n];
            cameMove = new NavMove[n];
            stamp = new int[n];
            closed = new bool[n];
            query = 0;
        }

        /// <summary>Двоичная куча (индекс узла, приоритет).</summary>
        private sealed class MinHeap
        {
            private readonly List<(int item, float priority)> items = new List<(int, float)>();

            public int Count => items.Count;
            public void Clear() => items.Clear();

            public void Push(int item, float priority)
            {
                items.Add((item, priority));
                var i = items.Count - 1;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (items[parent].priority <= items[i].priority)
                        break;
                    (items[parent], items[i]) = (items[i], items[parent]);
                    i = parent;
                }
            }

            public int Pop()
            {
                var top = items[0].item;
                var last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                var i = 0;
                while (true)
                {
                    var l = i * 2 + 1;
                    var r = l + 1;
                    var smallest = i;
                    if (l < items.Count && items[l].priority < items[smallest].priority)
                        smallest = l;
                    if (r < items.Count && items[r].priority < items[smallest].priority)
                        smallest = r;
                    if (smallest == i)
                        break;
                    (items[smallest], items[i]) = (items[i], items[smallest]);
                    i = smallest;
                }

                return top;
            }
        }
    }
}
