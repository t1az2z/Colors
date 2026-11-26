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

        /// <summary>Подсказка взаимодействия.</summary>
        public string Prompt { get; set; } = "открыть сундук";

        /// <summary>Сжатие перед открытием, секунды (как у куба перед прыжком).</summary>
        public float SquashTime { get; set; } = 0.12f;

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
        }
    }
}
