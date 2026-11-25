using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Ударная волна удара в прыжке: кольцо на земле расходится до радиуса поражения и тает.
    /// Показывает, кого задело. Время реальное — кольцо видно и во время фриз-кадров.
    /// </summary>
    public sealed class ShockRingFx : MonoBehaviour
    {
        private const float Life = 0.32f;
        private const int Segments = 48;
        private static Material material;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private LineRenderer line;
        private MaterialPropertyBlock block;
        private Color color;
        private float radius;
        private float time;

        private static Material Material
        {
            get
            {
                if (material == null)
                    material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "ShockRing (runtime)" };
                return material;
            }
        }

        public static ShockRingFx Spawn(Vector3 position, float radius, Color weaponColor)
        {
            var go = new GameObject("ShockRing") { layer = Debris.Layer };
            go.transform.position = position + Vector3.up * 0.06f;
            var fx = go.AddComponent<ShockRingFx>();
            fx.radius = radius;
            fx.line = go.AddComponent<LineRenderer>();
            fx.line.sharedMaterial = Material;
            fx.line.useWorldSpace = false;
            fx.line.loop = true;
            fx.line.positionCount = Segments;
            fx.line.alignment = LineAlignment.TransformZ;
            fx.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fx.line.receiveShadows = false;
            // Кольцо лежит на земле: плоскость линии — горизонталь.
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            // HDR: светлее цвета оружия, чтобы кольцо светилось и на белом мире.
            fx.color = Color.Lerp(weaponColor, Color.white, 0.4f) * 2f;
            fx.color.a = 1f;
            fx.Apply();
            return fx;
        }

        private void LateUpdate()
        {
            time += Time.unscaledDeltaTime;
            if (time >= Life)
            {
                Destroy(gameObject);
                return;
            }

            Apply();
        }

        private void Apply()
        {
            var t = time / Life;
            // Быстро расходится до радиуса и истончается.
            var r = radius * (0.25f + 0.75f * (1f - (1f - t) * (1f - t)));
            for (int i = 0; i < Segments; i++)
            {
                var a = i * Mathf.PI * 2f / Segments;
                // Локальная плоскость XY после поворота на 90° по X — горизонталь мира.
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }

            line.widthMultiplier = 0.22f * (1f - t);
            var c = color;
            c.a = 1f - t;
            line.startColor = line.endColor = c;
            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, c);
            line.SetPropertyBlock(block);
        }
    }
}
