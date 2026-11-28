using System;
using System.Collections.Generic;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Configs/Crates.json — стопка ломаемых ящиков (цветной барьер «синие ящики», GDD раздел 8).
    /// Здоровье общее на всю стопку; удары оружием не из <see cref="BreakableBy"/> ящики не ломают.
    /// </summary>
    [Preserve]
    public sealed class CrateConfig : IConfigPostLoad
    {
        /// <summary>Здоровье стопки. Синий меч по синим ящикам бьёт ×0.4 (свой цвет на круге): 2 / 2 / 2.8 за первые
        /// три удара комбо — 6 HP ломаются ровно третьим базовым ударом.</summary>
        public float Hp { get; set; } = 6f;

        /// <summary>Id оружия из Items.json, которым ящики ломаются. Пусто — любым.</summary>
        public List<string> BreakableBy { get; set; } = new List<string> { "sword" };

        /// <summary>Цвет ящиков (Colors.json): красит доски и даёт бонус урона оружию того же цвета.</summary>
        public string Color { get; set; } = "blue";

        /// <summary>Цвет досок в hex (например "#B07A45" — дерево). Пусто — цвет <see cref="Color"/> из палитры.</summary>
        public string Tint { get; set; } = "";

        /// <summary>Яркость досок относительно цвета (1 — как в палитре, больше — светлее).</summary>
        public float PlankShade { get; set; } = 1.15f;

        /// <summary>Яркость рамы и распорок относительно цвета.</summary>
        public float FrameShade { get; set; } = 0.7f;

        /// <summary>Поворот верхнего ящика вокруг вертикали, градусы (каждый следующий — в другую сторону).</summary>
        public float TopTwist { get; set; } = 8f;

        /// <summary>Сдвиг верхнего ящика вбок, метры.</summary>
        public float TopOffset { get; set; } = 0.05f;

        /// <summary>Показывать ли цифры урона по ящикам.</summary>
        public bool ShowDamageNumbers { get; set; }

        /// <summary>Дрожь от удара: амплитуда (м) и время (с). Неподходящее оружие трясёт слабее.</summary>
        public float ShakeAmount { get; set; } = 0.06f;

        public float ShakeTime { get; set; } = 0.25f;

        /// <summary>Множитель дрожи от удара неподходящим оружием (ящик не поддаётся).</summary>
        public float DeflectShake { get; set; } = 0.35f;

        /// <summary>Вспышка цвета при попадании, секунды.</summary>
        public float FlashTime { get; set; } = 0.12f;

        /// <summary>Сколько щепок отлетает от удара.</summary>
        public int ChipsPerHit { get; set; } = 4;

        /// <summary>Скорость разлёта досок при разрушении (м/с) и время, через которое они исчезают (с).</summary>
        public float BreakForce { get; set; } = 4.5f;

        public float DebrisLife { get; set; } = 2.5f;

        public void OnConfigLoaded()
        {
            BreakableBy ??= new List<string>();
            if (Hp <= 0f)
                throw new InvalidOperationException("crates: hp must be > 0");
        }
    }
}
