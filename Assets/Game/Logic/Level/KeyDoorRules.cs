using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorsAction.Level
{
    /// <summary>
    /// Двери на ключ (<see cref="LevelObjectKind.KeyDoor"/>). Связь «ключ — дверь» задаётся у врага на карте:
    /// <see cref="LevelObject.Key"/> = id двери. Враг при смерти роняет ключ, ключ лежит, пока его не подберут, и уходит в
    /// инвентарь; один ключ открывает одну дверь с любой стороны и тратится. Здесь — проверки расстановки для редакторов.
    /// </summary>
    public static class KeyDoorRules
    {
        /// <summary>Id предмета-ключа в Items.json (kind Key). Какую дверь открывает экземпляр — <c>Item.KeyFor</c>.</summary>
        public const string KeyItemId = "key";

        /// <summary>Враги, роняющие ключ от двери <paramref name="doorId"/>.</summary>
        public static List<LevelObject> Holders(VoxelLevel level, string doorId)
        {
            if (level == null || string.IsNullOrEmpty(doorId))
                return new List<LevelObject>();
            return level.Objects.Where(o => o.Kind == LevelObjectKind.EnemySpawn && o.Key == doorId).ToList();
        }

        /// <summary>
        /// Предупреждения расстановки: дверь, ключа от которой нет ни у одного врага (её не открыть); ключ от одной двери
        /// у нескольких врагов; ключ от двери, которой нет; ключ у манекена (он не роняет ключ).
        /// </summary>
        public static List<string> Warnings(VoxelLevel level)
        {
            var result = new List<string>();
            if (level == null)
                return result;
            var doors = new HashSet<string>(level.Objects.Where(o => o.IsKeyDoor).Select(o => o.Id));
            foreach (var door in level.Objects.Where(o => o.IsKeyDoor))
            {
                var holders = Holders(level, door.Id).Where(VoxelLevelData.IsEnemy).ToList();
                if (holders.Count == 0)
                    result.Add($"Дверь {door.Id}: ни у одного врага нет ключа от неё — её не открыть.");
                else if (holders.Count > 1)
                    result.Add($"Дверь {door.Id}: ключ у нескольких врагов ({string.Join(", ", holders.Select(h => h.Id))}) — " +
                               "выпадет из каждого, нужен один.");
            }

            foreach (var o in level.Objects)
            {
                if (o.Kind != LevelObjectKind.EnemySpawn || string.IsNullOrEmpty(o.Key))
                    continue;
                if (!VoxelLevelData.IsEnemy(o))
                    result.Add($"{o.Id}: манекен не роняет ключ от {o.Key} — отдайте ключ врагу.");
                else if (!doors.Contains(o.Key))
                    result.Add($"Враг {o.Id}: ключ от двери {o.Key}, а такой двери на карте нет.");
            }

            return result;
        }

        /// <summary>
        /// Отдать ключ от двери <paramref name="doorId"/> врагу <paramref name="enemyId"/> (null — никому): у остальных
        /// врагов ключ от этой двери снимается. Возвращает пары (до, после) изменённых врагов — для одной отмены в редакторе.
        /// </summary>
        public static List<(LevelObject before, LevelObject after)> AssignHolder(VoxelLevel level, string doorId, string enemyId)
        {
            var changes = new List<(LevelObject, LevelObject)>();
            if (level == null || string.IsNullOrEmpty(doorId))
                return changes;
            foreach (var o in level.Objects)
            {
                if (o.Kind != LevelObjectKind.EnemySpawn)
                    continue;
                var wanted = o.Id == enemyId ? doorId : o.Key == doorId ? null : o.Key;
                if (string.Equals(wanted, o.Key, StringComparison.Ordinal))
                    continue;
                var after = o.Clone();
                after.Key = wanted;
                changes.Add((o.Clone(), after));
            }

            return changes;
        }
    }
}
