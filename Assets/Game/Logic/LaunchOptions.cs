using System;
using System.Collections.Generic;
using ColorsAction.Configs;
using GameCore;

namespace ColorsAction
{
    /// <summary>Стартовое оружие: id из Items.json и уровень апгрейда 0..3.</summary>
    [Preserve]
    public sealed class StartWeapon
    {
        public string Item { get; set; }
        public int Upgrade { get; set; }
    }

    /// <summary>
    /// Параметры запуска игры поверх конфигов: какой уровень грузить, с каким оружием и опытом начинать.
    /// В редакторе их задаёт окно «Параметры запуска» (ColorsAction/Параметры запуска), в сборке — пустые.
    /// </summary>
    [Preserve]
    public sealed class LaunchOptions
    {
        /// <summary>Выключено — игра запускается как обычно, по Game.json.</summary>
        public bool Enabled { get; set; }

        /// <summary>Ключ уровня: "Levels/Test" (карта высот) или "VoxelLevels/Имя" (из веб-редактора). Пусто — из Game.json.</summary>
        public string Level { get; set; }

        public List<StartWeapon> Weapons { get; set; } = new List<StartWeapon>();

        /// <summary>Опыт на старте. Меньше нуля — не трогать.</summary>
        public int Experience { get; set; } = -1;

        public static LaunchOptions None => new LaunchOptions();

        /// <summary>Какой уровень грузить.</summary>
        public string LevelKey(GameConfig game) => Enabled && !string.IsNullOrEmpty(Level) ? Level : game.Level;

        /// <summary>Выдаёт стартовое оружие (на колесо; первое — в руки), ставит апгрейды и опыт.</summary>
        public void Apply(GameSession session)
        {
            if (!Enabled || session == null)
                return;
            foreach (var w in Weapons ?? new List<StartWeapon>())
            {
                if (w == null || string.IsNullOrEmpty(w.Item))
                    continue;
                // Предмета нет в Items.json (например, старые "fists" в сохранённых параметрах) — пропускаем целиком.
                if (!session.GiveItem(w.Item, equip: true))
                    continue;
                session.Upgrades.Set(w.Item, Math.Max(0, Math.Min(Progression.WeaponUpgrades.MaxTier, w.Upgrade)));
            }

            if (Experience >= 0)
                session.Experience.Value = Experience;
        }
    }

    /// <summary>Виды уровней по ключу конфига.</summary>
    public static class LevelKeys
    {
        public const string VoxelFolder = "VoxelLevels";
        public const string HeightmapFolder = "Levels";

        /// <summary>Воксельный уровень (формат веб-редактора) — ключ в папке VoxelLevels.</summary>
        public static bool IsVoxel(string key) =>
            !string.IsNullOrEmpty(key) && key.Replace('\\', '/').StartsWith(VoxelFolder + "/", StringComparison.OrdinalIgnoreCase);
    }
}
