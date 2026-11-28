using System;

namespace ColorsAction.Animation
{
    /// <summary>
    /// Сжатие-вытягивание куба с сохранением объёма: высота × (1 + squash), ширина и глубина × 1/√(1 + squash).
    /// Сплющенный куб расширяется в стороны, вытянутый — сужается; произведение масштабов всегда 1.
    /// Та же формула — в <c>CubeDeformer.DeformPoint</c>.
    /// </summary>
    public static class CubeShape
    {
        /// <summary>Сильнее не сплющиваем: при squash → -1 куб стал бы бесконечно широким.</summary>
        public const float MinHeightScale = 0.2f;

        /// <summary>Масштаб по высоте для сжатия <paramref name="squash"/> (&lt;0 — сплющен, &gt;0 — вытянут).</summary>
        public static float HeightScale(float squash) => Math.Max(MinHeightScale, 1f + squash);

        /// <summary>Масштаб по ширине и глубине — объём сохраняется.</summary>
        public static float WidthScale(float squash) => 1f / (float)Math.Sqrt(HeightScale(squash));

        /// <summary>Объём куба относительно исходного (для проверок): 1 при любом сжатии.</summary>
        public static float Volume(float squash)
        {
            var w = WidthScale(squash);
            return HeightScale(squash) * w * w;
        }
    }
}
