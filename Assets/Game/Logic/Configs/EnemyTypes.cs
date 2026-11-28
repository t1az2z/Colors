using System;
using System.Collections.Generic;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Тип и цвет поставленного врага. Цвет у каждого врага свой (<see cref="LevelEnemy.Color"/>), у типа — только
    /// цвет по умолчанию (<see cref="EnemyConfig.Color"/>). Старые карты хранили цвет в id типа
    /// (<c>club_cube_orange</c>): такой id раскладывается на тип <c>club_cube</c> и цвет <c>orange</c>.
    /// </summary>
    public static class EnemyTypes
    {
        /// <summary>Цвета круга (Combat.json <c>colorWheel</c>), которыми мог кончаться старый id типа.</summary>
        public static readonly IReadOnlyList<string> LegacyColorSuffixes = new[] { "yellow", "orange", "red", "lilac", "blue", "green" };

        /// <summary>
        /// Тип врага в таблице и его цвет. Известный тип остаётся как есть. Неизвестный вида <c>тип_цвет</c>, где
        /// тип есть в таблице, — старый формат: тип без суффикса, цвет из суффикса (если у врага цвет не задан).
        /// </summary>
        /// <param name="known">Есть ли тип в Enemies.json.</param>
        /// <returns>false — тип не найден и старым форматом не читается.</returns>
        public static bool Resolve(string type, string color, Func<string, bool> known, out string resolvedType, out string resolvedColor)
        {
            resolvedType = type;
            resolvedColor = string.IsNullOrEmpty(color) ? null : color;
            if (string.IsNullOrEmpty(type))
                return false;
            if (known(type))
                return true;
            foreach (var suffix in LegacyColorSuffixes)
            {
                var tail = "_" + suffix;
                if (!type.EndsWith(tail, StringComparison.Ordinal) || type.Length == tail.Length)
                    continue;
                var baseType = type.Substring(0, type.Length - tail.Length);
                if (!known(baseType))
                    continue;
                resolvedType = baseType;
                resolvedColor ??= suffix;
                return true;
            }
            return false;
        }

        /// <summary>Цвет врага: свой из карты, иначе цвет типа.</summary>
        public static string ColorOf(EnemyConfig type, string color) =>
            !string.IsNullOrEmpty(color) ? color : type?.Color;
    }
}
