using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// След пули: яркая полоса от дула до точки попадания, сужается и тает за доли секунды.
    /// Время реальное — след виден и во время фриз-кадров.
    /// </summary>
    public sealed class ShotFx : MonoBehaviour
    {
        private const float Life = 0.09f;
        private const float Width = 0.07f;
        private static Material material;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private LineRenderer line;
        private MaterialPropertyBlock block;
        private Color color;
        private float time;

        private static Material Material
        {
            get
            {
                if (material == null)
                    material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "ShotTracer (runtime)" };
                return material;
            }
        }

        public static ShotFx Tracer(Vector3 from, Vector3 to, Color weaponColor)
        {
            var go = new GameObject("ShotTracer") { layer = Debris.Layer };
            var fx = go.AddComponent<ShotFx>();
            fx.line = go.AddComponent<LineRenderer>();
            fx.line.sharedMaterial = Material;
            fx.line.positionCount = 2;
            fx.line.SetPosition(0, from);
            fx.line.SetPosition(1, to);
            fx.line.useWorldSpace = true;
            fx.line.numCapVertices = 2;
            fx.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fx.line.receiveShadows = false;
            // HDR: светлее цвета оружия, чтобы полоса светилась и на белом мире.
            fx.color = Color.Lerp(weaponColor, Color.white, 0.35f) * 2.2f;
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
            var k = 1f - time / Life;
            line.startWidth = Width * k;
            line.endWidth = Width * 0.35f * k;
            line.startColor = line.endColor = color;
            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            line.SetPropertyBlock(block);
        }
    }
}
