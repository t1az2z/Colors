using System.Collections.Generic;
using ColorsAction.Configs;

namespace ColorsAction.Level
{
    /// <summary>
    /// К каким предметам и дверям карты привязана подсказка на полу. Когда все они сделаны (предметы подобраны,
    /// двери открыты), подсказка гаснет навсегда (<see cref="GameSession.CompletedHints"/>).
    /// <list type="bullet">
    /// <item>Задан <see cref="LevelHint.Target"/>: двери с таким id и ближайший к подсказке предмет с таким id.</item>
    /// <item>Не задан, действие "interact": все предметы и двери в радиусе <see cref="AutoRadius"/> клеток.</item>
    /// <item>Иначе подсказка ни к чему не привязана и показывается всегда.</item>
    /// </list>
    /// </summary>
    public static class HintTargets
    {
        public const float AutoRadius = 2.5f;

        /// <param name="items">Индексы в <see cref="LevelConfig.Items"/>.</param>
        /// <param name="doors">Индексы в <see cref="LevelConfig.Doors"/>.</param>
        public static void Resolve(LevelConfig level, LevelHint hint, List<int> items, List<int> doors, float radius = AutoRadius)
        {
            items.Clear();
            doors.Clear();
            if (level == null || hint == null)
                return;

            if (!string.IsNullOrEmpty(hint.Target))
            {
                for (int i = 0; i < level.Doors.Count; i++)
                {
                    if (level.Doors[i].Id == hint.Target)
                        doors.Add(i);
                }

                var best = -1;
                var bestDistance = float.MaxValue;
                for (int i = 0; i < level.Items.Count; i++)
                {
                    if (level.Items[i].Item != hint.Target)
                        continue;
                    var d = Distance2(hint, level.Items[i]);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = i;
                    }
                }

                if (best >= 0)
                    items.Add(best);
                return;
            }

            if (hint.Action != "interact")
                return;
            var r2 = radius * radius;
            for (int i = 0; i < level.Items.Count; i++)
            {
                if (Distance2(hint, level.Items[i]) <= r2)
                    items.Add(i);
            }

            for (int i = 0; i < level.Doors.Count; i++)
            {
                if (Distance2(hint, level.Doors[i]) <= r2)
                    doors.Add(i);
            }
        }

        private static float Distance2(LevelPoint a, LevelPoint b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return dx * dx + dz * dz;
        }
    }
}
