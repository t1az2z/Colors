using System;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Configs/Water.json — стилизованная лоу-поли вода вокруг уровня, чтобы карта не висела в пустоте.
    /// Плоскость из треугольников лежит под всем уровнем и уходит далеко за край карты (изгиб мира уводит её за горизонт);
    /// волны — анимация вершин в шейдере ColorsAction/Water. Упавший в воду (в провал или яму) игрок тонет — погибает,
    /// как раньше при падении ниже дна. Высоты — <see cref="Level.WaterLayout"/>.
    /// </summary>
    [Preserve]
    public sealed class WaterConfig : IConfigPostLoad
    {
        public const string Key = "Water";

        /// <summary>Есть ли вода вообще.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Высота поверхности над дном уровня (низом самого нижнего блока), метры. Должна быть меньше 1: нижние блоки — суша.</summary>
        public float SurfaceAboveBottom { get; set; } = 0.35f;

        /// <summary>На сколько метров ниже поверхности игрок должен погрузиться, чтобы утонуть.</summary>
        public float DrownDepth { get; set; } = 0.9f;

        /// <summary>На сколько метров вода уходит за край карты в каждую сторону.</summary>
        public float Extent { get; set; } = 120f;

        /// <summary>Размер треугольника сетки, метры (крупнее — грубее грани и меньше вершин).</summary>
        public float CellSize { get; set; } = 1.6f;

        /// <summary>Случайный сдвиг вершин в плоскости, доля клетки (0 — ровная сетка, 0.4 — рваные грани).</summary>
        public float Jitter { get; set; } = 0.3f;

        /// <summary>Цвет у берега и на мелководье (hex).</summary>
        public string ShallowColor { get; set; } = "#A3E4EC";

        /// <summary>Цвет на глубине и вдали (hex).</summary>
        public string DeepColor { get; set; } = "#55AECB";

        /// <summary>Цвет пены у берега (hex).</summary>
        public string FoamColor { get; set; } = "#F4FBFF";

        /// <summary>Непрозрачность воды 0..1 (меньше 1 — у берега просвечивают блоки).</summary>
        public float Opacity { get; set; } = 0.9f;

        /// <summary>Глубина, на которой цвет полностью переходит в глубокий, метры.</summary>
        public float DepthFade { get; set; } = 4f;

        /// <summary>Ширина полосы пены у берега, метры (0 — без пены).</summary>
        public float FoamWidth { get; set; } = 0.35f;

        /// <summary>Высота волн (амплитуда), метры.</summary>
        public float WaveHeight { get; set; } = 0.2f;

        /// <summary>Длина волны, метры.</summary>
        public float WaveLength { get; set; } = 6f;

        /// <summary>Скорость волн, м/с.</summary>
        public float WaveSpeed { get; set; } = 1.1f;

        /// <summary>Направление основной волны, градусы (0 — вдоль +X).</summary>
        public float WaveDirection { get; set; } = 30f;

        /// <summary>Сила блика солнца на гранях 0..1.</summary>
        public float Specular { get; set; } = 0.25f;

        /// <summary>Насколько резко видны грани: 0 — ровная заливка, 1 — наклон граней для света усилен в 6 раз.</summary>
        public float Facets { get; set; } = 0.6f;

        /// <summary>Брызги, когда игрок падает в воду.</summary>
        public bool Splash { get; set; } = true;

        public void OnConfigLoaded()
        {
            SurfaceAboveBottom = Math.Max(-5f, Math.Min(0.95f, SurfaceAboveBottom));
            DrownDepth = Math.Max(0f, DrownDepth);
            Extent = Math.Max(0f, Extent);
            CellSize = Math.Max(0.25f, CellSize);
            Jitter = Math.Max(0f, Math.Min(0.45f, Jitter));
            Opacity = Math.Max(0f, Math.Min(1f, Opacity));
            DepthFade = Math.Max(0.01f, DepthFade);
            FoamWidth = Math.Max(0f, FoamWidth);
            WaveHeight = Math.Max(0f, WaveHeight);
            WaveLength = Math.Max(0.5f, WaveLength);
            Specular = Math.Max(0f, Math.Min(1f, Specular));
            Facets = Math.Max(0f, Math.Min(1f, Facets));
        }
    }
}
