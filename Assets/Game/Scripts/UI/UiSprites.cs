using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>Спрайты для HUD, нарисованные кодом: скруглённая плашка (9-slice) и круг со сглаживанием.</summary>
    public static class UiSprites
    {
        private static Sprite rounded;
        private static Sprite circle;
        private static Sprite ring;

        /// <summary>Плашка со скруглёнными углами радиусом 16 px, растягивается без искажения углов.</summary>
        public static Sprite Rounded => rounded != null ? rounded : rounded = MakeRounded(64, 16);

        public static Sprite Circle => circle != null ? circle : circle = MakeCircle(128, 0f);

        /// <summary>Кольцо (обводка круга).</summary>
        public static Sprite Ring => ring != null ? ring : ring = MakeCircle(128, 0.12f);

        public static Image Apply(Image image, Sprite sprite, bool sliced)
        {
            image.sprite = sprite;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 1f;
            return image;
        }

        private static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Расстояние до скруглённого прямоугольника (SDF), сглаживание в 1 px.
                var cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                var cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - radius;
                var a = Mathf.Clamp01(0.5f - d);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        private static Sprite MakeCircle(int size, float ringWidth)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                var outer = Mathf.Clamp01(r - d);
                var a = outer;
                if (ringWidth > 0f)
                    a *= Mathf.Clamp01(d - (r - size * ringWidth));
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
