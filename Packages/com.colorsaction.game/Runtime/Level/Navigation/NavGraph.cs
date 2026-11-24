using System;
using System.Collections.Generic;

namespace ColorsAction.Level.Navigation
{
    public enum NavLinkKind
    {
        /// <summary>Геометрическая связь между соседними клетками: шаг, прыжок или спрыгивание — решает профиль по перепаду.</summary>
        Move,

        /// <summary>Особая связь из точки взаимодействия.</summary>
        Special
    }

    /// <summary>Как профиль проходит связь.</summary>
    public enum NavMove
    {
        None,
        Walk,
        Jump,
        Drop,
        Special
    }

    /// <summary>Узел — верх блока, над которым есть место. <see cref="Surface"/> — высота пола в центре клетки.</summary>
    public sealed class NavNode
    {
        public int Index;

        /// <summary>Клетка блока-опоры.</summary>
        public Int3 Block;

        /// <summary>Клетка, в которой стоит персонаж (по ней ставятся объекты и точки).</summary>
        public Int3 Stand;

        public float Surface;

        /// <summary>Свободная высота над полом (ограничена <see cref="NavGraph.MaxClearance"/>).</summary>
        public float Clearance;

        /// <summary>Крутой слоп: пешком не проходится.</summary>
        public bool Steep;

        /// <summary>Барьер, стоящий в клетке, — узел закрыт, пока барьер не открыт.</summary>
        public string Barrier;

        public readonly List<NavLink> Links = new List<NavLink>();

        public float Ceiling => Surface + Clearance;
    }

    public sealed class NavLink
    {
        public int From;
        public int To;
        public NavLinkKind Kind;

        /// <summary>Перепад высоты (по краям клеток): плюс — вверх.</summary>
        public float Rise;

        /// <summary>Горизонтальная длина связи.</summary>
        public float Distance;

        /// <summary>Свободная высота на всём переходе.</summary>
        public float Clearance;

        public bool Diagonal;

        // Особые связи
        public InteractionAction Action;
        public string PointId;
        public NavCondition Condition;
    }

    /// <summary>Параметры сборки карты: пределы, шире любых профилей.</summary>
    public sealed class NavBuildSettings
    {
        public float MaxRise { get; set; } = 2f;
        public float MaxDrop { get; set; } = 8f;

        /// <summary>Перепад, при котором диагональный шаг ещё возможен (обе боковые клетки должны быть такими же).</summary>
        public float DiagonalStep { get; set; } = 0.3f;

        /// <summary>Узлы с меньшей свободной высотой не создаются.</summary>
        public float MinClearance { get; set; } = 0.5f;
    }

    /// <summary>
    /// Карта пути по вокселям. Строится из <see cref="VoxelLevel"/> целиком (на сохранение или по кнопке «Пересобрать»);
    /// связь доступна персонажу, если подходит под его <see cref="MovementProfile"/>, условие выполнено и цель не закрыта барьером.
    /// </summary>
    public sealed class NavGraph
    {
        public const float MaxClearance = 8f;

        private static readonly (int dx, int dz)[] Orthogonal = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        private static readonly (int dx, int dz)[] Diagonals = { (1, 1), (1, -1), (-1, 1), (-1, -1) };

        private readonly List<NavNode> nodes = new List<NavNode>();
        private readonly Dictionary<(int x, int z), List<int>> columns = new Dictionary<(int x, int z), List<int>>();
        private readonly Dictionary<Int3, int> byBlock = new Dictionary<Int3, int>();

        public IReadOnlyList<NavNode> Nodes => nodes;
        public int LinkCount { get; private set; }

        /// <summary>Версия уровня, из которой собрана карта.</summary>
        public int LevelVersion { get; private set; }

        /// <summary>Точки, связь которых не удалось построить (нет узла под точкой или нет цели).</summary>
        public List<string> Warnings { get; } = new List<string>();

        public static NavGraph Build(VoxelLevel level, NavBuildSettings settings = null)
        {
            var graph = new NavGraph();
            graph.BuildInternal(level, settings ?? new NavBuildSettings());
            return graph;
        }

        public NavNode NodeOnBlock(Int3 block) => byBlock.TryGetValue(block, out var i) ? nodes[i] : null;

        public IReadOnlyList<int> Column(int x, int z) =>
            columns.TryGetValue((x, z), out var list) ? (IReadOnlyList<int>)list : Array.Empty<int>();

        /// <summary>Узел, на котором стоит персонаж в клетке <paramref name="stand"/>: верх опоры в этой клетке или чуть ниже.</summary>
        public NavNode NodeAtStand(Int3 stand)
        {
            NavNode best = null;
            foreach (var i in Column(stand.X, stand.Z))
            {
                var n = nodes[i];
                if (n.Stand.Y == stand.Y)
                    return n;
                // Персонаж в воздухе над узлом (до 1 клетки): берём ближайший ниже.
                if (n.Surface <= stand.Y + 0.01f && n.Surface >= stand.Y - 1.01f)
                    best = n;
            }

            return best;
        }

        /// <summary>Ближайший узел под мировой точкой (x, y, z) в метрах.</summary>
        public NavNode NodeAt(float x, float y, float z)
        {
            var cx = (int)Math.Floor(x);
            var cz = (int)Math.Floor(z);
            NavNode best = null;
            foreach (var i in Column(cx, cz))
            {
                var n = nodes[i];
                if (n.Surface <= y + 0.3f && (best == null || n.Surface > best.Surface))
                    best = n;
            }

            return best;
        }

        // ------------------------------------------------------------------ сборка

        private void BuildInternal(VoxelLevel level, NavBuildSettings s)
        {
            LevelVersion = level.Version;

            // Опоры по столбцам, снизу вверх.
            var solidByColumn = new Dictionary<(int, int), List<int>>();
            foreach (var kv in level.Blocks)
            {
                if (!BlockShapes.IsSolid(kv.Value.Shape))
                    continue;
                var key = (kv.Key.X, kv.Key.Z);
                if (!solidByColumn.TryGetValue(key, out var ys))
                    solidByColumn[key] = ys = new List<int>();
                ys.Add(kv.Key.Y);
            }

            foreach (var kv in solidByColumn)
            {
                var ys = kv.Value;
                ys.Sort();
                for (int i = 0; i < ys.Count; i++)
                {
                    var cell = new Int3(kv.Key.Item1, ys[i], kv.Key.Item2);
                    var block = level.Blocks[cell];
                    var surface = cell.Y + BlockShapes.CenterTop(block.Shape);
                    var ceiling = i + 1 < ys.Count ? ys[i + 1] : surface + MaxClearance;
                    var clearance = Math.Min(MaxClearance, ceiling - surface);
                    if (clearance < s.MinClearance)
                        continue;
                    var node = new NavNode
                    {
                        Index = nodes.Count,
                        Block = cell,
                        Stand = new Int3(cell.X, (int)Math.Floor(surface + 1e-3), cell.Z),
                        Surface = surface,
                        Clearance = clearance,
                        Steep = block.Shape == BlockShape.SteepSlope
                    };
                    node.Barrier = level.BarrierAt(node.Stand)?.Id;
                    nodes.Add(node);
                    byBlock[cell] = node.Index;
                    if (!columns.TryGetValue(kv.Key, out var col))
                        columns[kv.Key] = col = new List<int>();
                    col.Add(node.Index);
                }
            }

            foreach (var node in nodes)
            {
                var a = level.Blocks[node.Block];
                if (node.Steep)
                    continue;
                foreach (var (dx, dz) in Orthogonal)
                {
                    foreach (var j in Column(node.Block.X + dx, node.Block.Z + dz))
                    {
                        var other = nodes[j];
                        if (other.Steep)
                            continue;
                        var b = level.Blocks[other.Block];
                        var edgeA = node.Block.Y + BlockShapes.EdgeTop(a.Shape, a.Rotation, dx, dz);
                        var edgeB = other.Block.Y + BlockShapes.EdgeTop(b.Shape, b.Rotation, -dx, -dz);
                        var rise = edgeB - edgeA;
                        if (rise > s.MaxRise || -rise > s.MaxDrop)
                            continue;
                        var top = Math.Max(Math.Max(edgeA, edgeB), Math.Max(node.Surface, other.Surface));
                        var clearance = Math.Min(node.Ceiling, other.Ceiling) - top;
                        if (clearance < s.MinClearance)
                            continue;
                        AddLink(node, new NavLink
                        {
                            From = node.Index, To = other.Index, Kind = NavLinkKind.Move, Rise = rise, Distance = 1f,
                            Clearance = clearance
                        });
                    }
                }
            }

            // Диагонали: только по ровному, обе боковые клетки проходимы шагом (углы не срезаются).
            foreach (var node in nodes)
            {
                if (node.Steep)
                    continue;
                foreach (var (dx, dz) in Diagonals)
                {
                    var side1 = WalkNeighbour(node, dx, 0, s.DiagonalStep);
                    var side2 = WalkNeighbour(node, 0, dz, s.DiagonalStep);
                    if (side1 == null || side2 == null)
                        continue;
                    var target1 = WalkNeighbour(nodes[side1.To], 0, dz, s.DiagonalStep);
                    var target2 = WalkNeighbour(nodes[side2.To], dx, 0, s.DiagonalStep);
                    if (target1 == null || target2 == null || target1.To != target2.To)
                        continue;
                    var other = nodes[target1.To];
                    var clearance = Math.Min(Math.Min(side1.Clearance, side2.Clearance),
                        Math.Min(target1.Clearance, target2.Clearance));
                    AddLink(node, new NavLink
                    {
                        From = node.Index, To = other.Index, Kind = NavLinkKind.Move, Rise = other.Surface - node.Surface,
                        Distance = 1.41421356f, Clearance = clearance, Diagonal = true
                    });
                }
            }

            // Особые связи из точек взаимодействия.
            foreach (var point in level.Points)
            {
                if (!InteractionPoint.CreatesLink(point.Action) || string.IsNullOrEmpty(point.TargetPoint))
                    continue;
                var target = level.FindPoint(point.TargetPoint);
                if (target == null)
                {
                    Warnings.Add($"{point.Id}: цель {point.TargetPoint} не найдена");
                    continue;
                }

                var from = NodeAtStand(point.Cell);
                var to = NodeAtStand(target.Cell);
                if (from == null || to == null)
                {
                    Warnings.Add($"{point.Id}: под точкой {(from == null ? point.Id : target.Id)} нет опоры");
                    continue;
                }

                AddSpecial(from, to, point);
                if (point.TwoWay)
                    AddSpecial(to, from, point);
            }
        }

        private NavLink WalkNeighbour(NavNode node, int dx, int dz, float maxStep)
        {
            foreach (var link in node.Links)
            {
                if (link.Kind != NavLinkKind.Move || link.Diagonal)
                    continue;
                var to = nodes[link.To];
                if (to.Block.X - node.Block.X == dx && to.Block.Z - node.Block.Z == dz && Math.Abs(link.Rise) <= maxStep)
                    return link;
            }

            return null;
        }

        private void AddSpecial(NavNode from, NavNode to, InteractionPoint point)
        {
            var dx = to.Block.X - from.Block.X;
            var dz = to.Block.Z - from.Block.Z;
            AddLink(from, new NavLink
            {
                From = from.Index, To = to.Index, Kind = NavLinkKind.Special, Rise = to.Surface - from.Surface,
                Distance = (float)Math.Sqrt(dx * dx + dz * dz), Clearance = MaxClearance, Action = point.Action,
                PointId = point.Id, Condition = point.Condition
            });
        }

        private void AddLink(NavNode node, NavLink link)
        {
            node.Links.Add(link);
            LinkCount++;
        }

        // ------------------------------------------------------------------ фильтр по профилю

        /// <summary>Может ли персонаж с профилем стоять на узле.</summary>
        public static bool CanStand(NavNode node, MovementProfile profile, INavContext context)
        {
            if (node.Steep || node.Clearance < profile.Height)
                return false;
            return node.Barrier == null || (context != null && context.IsOpen(node.Barrier));
        }

        /// <summary>Как профиль проходит связь (<see cref="NavMove.None"/> — никак).</summary>
        public NavMove Classify(NavLink link, MovementProfile profile, INavContext context)
        {
            var to = nodes[link.To];
            if (link.Kind == NavLinkKind.Special)
            {
                if (!profile.CanUse(link.Action))
                    return NavMove.None;
                if (context != null && !context.Satisfies(link.Condition))
                    return NavMove.None;
                if (to.Barrier != null && (context == null || !context.IsOpen(to.Barrier)))
                    return NavMove.None;
                return NavMove.Special;
            }

            if (!CanStand(to, profile, context) || link.Clearance < profile.Height)
                return NavMove.None;
            if (link.Diagonal && !profile.Diagonal)
                return NavMove.None;
            if (Math.Abs(link.Rise) <= profile.StepHeight + 1e-4f)
                return NavMove.Walk;
            if (link.Diagonal)
                return NavMove.None;
            if (link.Rise > 0)
                return profile.CanJump && link.Rise <= profile.JumpHeight + 1e-4f ? NavMove.Jump : NavMove.None;
            return -link.Rise <= profile.MaxDrop + 1e-4f ? NavMove.Drop : NavMove.None;
        }

        public float Cost(NavLink link, NavMove move, MovementProfile profile)
        {
            switch (move)
            {
                case NavMove.Walk: return link.Distance + Math.Abs(link.Rise) * 0.5f;
                case NavMove.Jump: return link.Distance + profile.JumpCost + link.Rise;
                case NavMove.Drop: return link.Distance + profile.DropCost;
                case NavMove.Special: return 1f + link.Distance * 0.5f;
                default: return float.PositiveInfinity;
            }
        }
    }
}
