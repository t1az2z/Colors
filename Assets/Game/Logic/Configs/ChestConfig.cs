using System;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Configs/Chests.json — сундук с ресурсом: вид, открытие и полёт ресурса к игроку.
    /// Что лежит внутри (ресурс и количество), задаётся в уровне (<see cref="LevelChest"/>).
    /// </summary>
    [Preserve]
    public sealed class ChestConfig : IConfigPostLoad
    {
        /// <summary>Размер корпуса с крышкой, метры (сундук стоит в одной клетке 1×1).</summary>
        public float Width { get; set; } = 0.86f;

        public float Depth { get; set; } = 0.62f;

        /// <summary>Высота корпуса без крышки.</summary>
        public float BodyHeight { get; set; } = 0.42f;

        /// <summary>Высота крышки.</summary>
        public float LidHeight { get; set; } = 0.2f;

        /// <summary>Цвет корпуса в hex.</summary>
        public string Tint { get; set; } = "#9A9DA6";

        /// <summary>Яркость окантовки и замка относительно корпуса (меньше 1 — темнее).</summary>
        public float TrimShade { get; set; } = 0.55f;

        /// <summary>Полоса на крышке в цвете ресурса: видно, что внутри.</summary>
        public bool ShowResourceColor { get; set; } = true;

        /// <summary>Подсказка взаимодействия: ключ строки в Configs/Localization (или готовый текст).</summary>
        public string Prompt { get; set; } = "prompt.chest";

        /// <summary>Сжатие перед открытием, секунды (как у куба перед прыжком).</summary>
        public float SquashTime { get; set; } = 0.12f;

        /// <summary>Глубина сжатия перед открытием: доля высоты (0.2 — сундук ниже на 20%).</summary>
        public float SquashDepth { get; set; } = 0.22f;

        /// <summary>Панч после сжатия: скорость, с которой сундук вытягивается вверх (дальше пружина возвращает форму).</summary>
        public float Punch { get; set; } = 9f;

        /// <summary>Подскок при открытии: высота, метры, и время в воздухе, секунды. 0 — без подскока.</summary>
        public float HopHeight { get; set; } = 0.22f;

        public float HopTime { get; set; } = 0.3f;

        /// <summary>Пружинка при приземлении после подскока (скорость сжатия).</summary>
        public float LandSquash { get; set; } = 3.5f;

        /// <summary>Золотое свечение в открытом сундуке: цвет (hex), частота пульса, рад/с, и его размах (доля размера).</summary>
        public string GlowColor { get; set; } = "#FFC23A";

        public float GlowPulseSpeed { get; set; } = 3f;

        public float GlowPulse { get; set; } = 0.25f;

        /// <summary>Сколько открывается крышка, секунды.</summary>
        public float OpenTime { get; set; } = 0.35f;

        /// <summary>Угол открытой крышки, градусы.</summary>
        public float LidAngle { get; set; } = 105f;

        /// <summary>Сколько кусочков вылетает на одну штуку ресурса и максимум кусочков.</summary>
        public int PiecesPerResource { get; set; } = 2;

        public int MaxPieces { get; set; } = 14;

        /// <summary>Размер кусочка, метры.</summary>
        public float PieceSize { get; set; } = 0.14f;

        /// <summary>Сила разлёта из сундука вверх.</summary>
        public float BurstForce { get; set; } = 3.2f;

        /// <summary>Через сколько секунд после вылета кусочки летят к игроку (следующий — чуть позже).</summary>
        public float FlyDelay { get; set; } = 0.55f;

        public float FlyStagger { get; set; } = 0.05f;

        /// <summary>
        /// Ресурс засчитывается, когда долетел последний кусочек; если что-то помешало — не позже этого времени после
        /// открытия, секунды.
        /// </summary>
        public float GrantTimeout { get; set; } = 4f;

        public void OnConfigLoaded()
        {
            if (Width <= 0f || Depth <= 0f || BodyHeight <= 0f || LidHeight < 0f)
                throw new InvalidOperationException("chests: sizes must be > 0");
            if (PiecesPerResource < 1 || MaxPieces < 1)
                throw new InvalidOperationException("chests: piecesPerResource and maxPieces must be >= 1");
            Prompt ??= string.Empty;
            Tint ??= string.Empty;
            GlowColor ??= string.Empty;
            if (SquashDepth < 0f || SquashDepth > 0.6f)
                throw new InvalidOperationException("chests: squashDepth must be in 0..0.6");
            if (HopHeight < 0f || HopTime < 0f)
                throw new InvalidOperationException("chests: hopHeight and hopTime must be >= 0");
        }
    }
}
