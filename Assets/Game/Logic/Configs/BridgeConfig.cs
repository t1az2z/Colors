using System;
using System.Collections.Generic;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Configs/Bridges.json — разводной мост 8×3 (створка 4 в высоту) (<see cref="ColorsAction.Level.DrawBridgeShape"/>): ближняя створка
    /// опущена, дальняя поднята и держится на двух цепях к раме. Цепи рвутся только оружием из <see cref="BreakableBy"/>
    /// (дробовик), створка падает и открывает проход. Шестерни и цепи — цвета <see cref="Color"/>, остальное — в цвете мира.
    /// </summary>
    [Preserve]
    public sealed class BridgeConfig : IConfigPostLoad
    {
        /// <summary>Прочность цепи. Один залп дробовика наносит больше — рвётся с первого попадания.</summary>
        public float ChainHp { get; set; } = 1f;

        /// <summary>Id оружия из Items.json, которым рвётся цепь. Пусто — любым.</summary>
        public List<string> BreakableBy { get; set; } = new List<string> { "shotgun" };

        /// <summary>Цвет шестерён и цепи (Colors.json).</summary>
        public string Color { get; set; } = "red";

        /// <summary>Цвет настила в hex. Пусто — цвет материала мира.</summary>
        public string DeckTint { get; set; } = "";

        /// <summary>Яркость досок настила относительно цвета мира (1 — как мир).</summary>
        public float DeckShade { get; set; } = 1f;

        /// <summary>Яркость балок и окантовки относительно цвета мира.</summary>
        public float BeamShade { get; set; } = 0.72f;

        /// <summary>Яркость каменных опор и тумб рам относительно цвета мира.</summary>
        public float StoneShade { get; set; } = 0.62f;

        /// <summary>Яркость шестерён и цепи относительно цвета <see cref="Color"/>.</summary>
        public float GearShade { get; set; } = 1f;

        /// <summary>Угол поднятой створки от горизонтали, градусы (больше 90 нельзя — створка должна падать вперёд).</summary>
        public float RaisedAngle { get; set; } = 72f;

        /// <summary>Радиус больших шестерён у шарнира поднятой створки, метры.</summary>
        public float GearRadius { get; set; } = 0.9f;

        /// <summary>Высота П-образных рам на концах моста (к дальней крепятся цепи), метры. Выше края поднятой створки.</summary>
        public float GantryHeight { get; set; } = 4.6f;

        /// <summary>Сколько оборотов шестерни на один оборот створки (шестерни крутятся, пока створка падает).</summary>
        public float GearRatio { get; set; } = 2.5f;

        /// <summary>Множитель силы тяжести при падении створки (1 — как настоящая доска длиной в створку).</summary>
        public float FallGravity { get; set; } = 1f;

        /// <summary>Отскок створки при ударе о землю (доля скорости) и сколько раз она подпрыгивает.</summary>
        public float Bounce { get; set; } = 0.22f;

        public int Bounces { get; set; } = 2;

        /// <summary>Пауза между разрывом цепи и началом падения, секунды (створка вздрагивает).</summary>
        public float FallDelay { get; set; } = 0.18f;

        /// <summary>Тряска камеры при ударе створки о землю.</summary>
        public float LandShake { get; set; } = 0.35f;

        /// <summary>Сколько обломков-пыли поднимается при ударе створки.</summary>
        public int LandDust { get; set; } = 14;

        /// <summary>Звеньев в цепи.</summary>
        public int ChainLinks { get; set; } = 15;

        /// <summary>Скорость разлёта звеньев при разрыве, м/с, и время до их исчезновения, с.</summary>
        public float ChainBreakForce { get; set; } = 3f;

        public float DebrisLife { get; set; } = 2.5f;

        /// <summary>Раскачка цепи от удара (градусы) и её затухание (с).</summary>
        public float ChainSwing { get; set; } = 6f;

        public float ChainSwingTime { get; set; } = 0.5f;

        /// <summary>Вспышка цепи при попадании, секунды.</summary>
        public float FlashTime { get; set; } = 0.12f;

        /// <summary>Можно ли взять цепь в фиксацию (СКМ / R3), чтобы прицелиться дробовиком.</summary>
        public bool LockOnChain { get; set; } = true;

        public void OnConfigLoaded()
        {
            BreakableBy ??= new List<string>();
            if (ChainHp <= 0f)
                throw new InvalidOperationException("bridges: chainHp must be > 0");
            RaisedAngle = Math.Max(10f, Math.Min(89f, RaisedAngle));
            ChainLinks = Math.Max(3, ChainLinks);
            GantryHeight = Math.Max(2.2f, GantryHeight);
            Bounces = Math.Max(0, Bounces);
        }
    }
}
