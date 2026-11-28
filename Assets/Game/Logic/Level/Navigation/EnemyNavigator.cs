using System;
using System.Collections.Generic;

namespace ColorsAction.Level.Navigation
{
    public enum NavSteerMode
    {
        /// <summary>Цель рядом (та же или соседняя клетка) или карты нет — идти к цели напрямую.</summary>
        Direct,

        /// <summary>Идти к точке пути <see cref="NavSteer.X"/>, <see cref="NavSteer.Z"/>.</summary>
        Path,

        /// <summary>Цель недостижима, ближе подойти нельзя — стоять (у края пропасти, перед закрытой дверью).</summary>
        Stay
    }

    /// <summary>Куда идти в этом кадре.</summary>
    public readonly struct NavSteer
    {
        public readonly NavSteerMode Mode;

        /// <summary>Точка, к которой идти (метры, центр клетки), для <see cref="NavSteerMode.Path"/>.</summary>
        public readonly float X;
        public readonly float Z;

        /// <summary>Следующий переход — прыжок вверх на <see cref="JumpRise"/> метров.</summary>
        public readonly bool Jump;
        public readonly float JumpRise;

        /// <summary>Сколько ещё идти по пути (метры по горизонтали); для <see cref="NavSteerMode.Direct"/> — 0.</summary>
        public readonly float Remaining;

        public NavSteer(NavSteerMode mode, float x = 0f, float z = 0f, bool jump = false, float jumpRise = 0f, float remaining = 0f)
        {
            Mode = mode;
            X = x;
            Z = z;
            Jump = jump;
            JumpRise = jumpRise;
            Remaining = remaining;
        }
    }

    /// <summary>
    /// Ведёт одного врага по карте пути (<see cref="EnemyReach"/>): A* до цели с учётом высоты (ступени, прыжки,
    /// спрыгивания), закрытых дверей, ящиков и моста; недостижимая цель — путь к ближайшему месту. Путь пересчитывается,
    /// когда цель перешла в другую клетку, мир изменился, враг сошёл с пути или прошло <see cref="RepathInterval"/>.
    /// Прямые участки пути схлопываются: враг идёт к дальней точке прямой, а не от клетки к клетке.
    /// </summary>
    public sealed class EnemyNavigator
    {
        /// <summary>Сколько прямых шагов пути можно пропустить за раз.</summary>
        private const int MaxCollapse = 6;

        private readonly EnemyReach reach;
        private readonly MovementProfile profile;
        private readonly List<PathStep> path = new List<PathStep>();
        private int next;
        private PathResult result = PathResult.None;
        private int goalIndex = -1;
        private int version = int.MinValue;
        private float sinceRepath;

        public EnemyNavigator(EnemyReach reach, MovementProfile profile)
        {
            this.reach = reach ?? throw new ArgumentNullException(nameof(reach));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        /// <summary>Как часто пересчитывать путь, даже если ничего не изменилось (цель могла сместиться в клетке).</summary>
        public float RepathInterval { get; set; } = 0.75f;

        /// <summary>Не чаще этого пересчитывать путь из-за того, что цель перешла в другую клетку.</summary>
        public float MinRepathInterval { get; set; } = 0.2f;

        /// <summary>Предел раскрытых узлов на один поиск.</summary>
        public int MaxExpanded { get; set; } = 6000;

        public IReadOnlyList<PathStep> Path => path;
        public PathResult Result => result;

        /// <summary>Сколько раз считался путь (для тестов).</summary>
        public int Repaths { get; private set; }

        /// <summary>Забыть путь (враг потерял игрока).</summary>
        public void Reset()
        {
            path.Clear();
            next = 0;
            result = PathResult.None;
            goalIndex = -1;
        }

        /// <summary>Куда идти из (x, y, z) к цели (gx, gy, gz), метры.</summary>
        public NavSteer Steer(float dt, float x, float y, float z, float gx, float gy, float gz)
        {
            sinceRepath += dt;
            var graph = reach.Graph;
            var start = graph.NodeAt(x, y + 0.1f, z);
            if (start == null)
                return new NavSteer(NavSteerMode.Direct);
            var goal = graph.NodeAt(gx, gy + 0.1f, gz);
            // Цель над пропастью (в прыжке) — ведём к последней известной клетке цели.
            if (goal == null && goalIndex >= 0)
                goal = graph.Nodes[goalIndex];
            if (goal == null || goal.Index == start.Index)
                return new NavSteer(NavSteerMode.Direct);

            var worldVersion = reach.WorldVersion;
            var onPath = Advance(start);
            var goalMoved = goal.Index != goalIndex && (sinceRepath >= MinRepathInterval || path.Count == 0);
            if (goalMoved || worldVersion != version || !onPath || sinceRepath >= RepathInterval)
            {
                goalIndex = goal.Index;
                version = worldVersion;
                sinceRepath = 0f;
                Repaths++;
                result = reach.FindPath(profile, start, goal, path, MaxExpanded);
                next = 1;
                if (result == PathResult.None)
                    path.Clear();
            }

            if (result == PathResult.None || next >= path.Count)
                return result == PathResult.Found ? new NavSteer(NavSteerMode.Direct) : new NavSteer(NavSteerMode.Stay);

            // Последний шаг пешком до клетки цели — дальше враг сам держит дистанцию до игрока.
            if (result == PathResult.Found && next == path.Count - 1 && path[next].Move == NavMove.Walk)
                return new NavSteer(NavSteerMode.Direct);

            var step = path[next];
            var target = next;
            if (step.Move == NavMove.Walk)
            {
                var dx = step.Node.Block.X - start.Block.X;
                var dz = step.Node.Block.Z - start.Block.Z;
                for (int i = next + 1; i < path.Count && i - next < MaxCollapse; i++)
                {
                    var s = path[i];
                    var prev = path[i - 1].Node;
                    if (s.Move != NavMove.Walk || s.Node.Block.X - prev.Block.X != dx || s.Node.Block.Z - prev.Block.Z != dz)
                        break;
                    target = i;
                }
            }

            var t = path[target].Node;
            var tx = t.Block.X + 0.5f;
            var tz = t.Block.Z + 0.5f;
            var remaining = Horizontal(x, z, tx, tz);
            for (int i = target + 1; i < path.Count; i++)
                remaining += path[i].Link?.Distance ?? 1f;
            var jump = step.Move == NavMove.Jump;
            return new NavSteer(NavSteerMode.Path, tx, tz, jump, jump ? step.Node.Surface - start.Surface : 0f, remaining);
        }

        /// <summary>
        /// Можно ли идти из (x, y, z) в направлении (dx, dz): точка на <paramref name="probe"/> метров впереди
        /// в той же клетке или в соседней, куда профиль проходит. Защищает от шагов с обрыва при отходе и обходе.
        /// </summary>
        public bool CanMove(float x, float y, float z, float dx, float dz, float probe = 0.55f)
        {
            var len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-4f)
                return true;
            var from = reach.Graph.NodeAt(x, y + 0.1f, z);
            if (from == null)
                return true;
            var px = (int)Math.Floor(x + dx / len * probe);
            var pz = (int)Math.Floor(z + dz / len * probe);
            return reach.CanStep(profile, from, px, pz);
        }

        /// <summary>Сдвинуть указатель пути на узел, где враг стоит сейчас. false — враг не на пути.</summary>
        private bool Advance(NavNode current)
        {
            if (path.Count == 0)
                return false;
            // Ищем текущий узел среди ближайших впереди (и одного позади — враг мог чуть откатиться).
            var from = Math.Max(0, next - 1);
            var to = Math.Min(path.Count - 1, next + MaxCollapse);
            for (int i = from; i <= to; i++)
            {
                if (path[i].Node.Index != current.Index)
                    continue;
                next = i + 1;
                return true;
            }

            return false;
        }

        private static float Horizontal(float ax, float az, float bx, float bz)
        {
            var dx = ax - bx;
            var dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
