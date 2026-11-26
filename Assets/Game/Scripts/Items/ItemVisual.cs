using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Модель предмета (меч, щит). Цвет берётся из палитры по id цвета предмета и красит
    /// "цветные" материалы (по индексам), остальные (рукоять, обод) не трогает.
    /// </summary>
    public sealed class ItemVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Renderer[] renderers;
        [SerializeField] private int[] coloredMaterialIndices = { 0 };

        private MaterialPropertyBlock block;
        private Color color = Color.white;
        private float emission;
        private float blink;
        private float blinkGlow;
        private bool colored;

        public void SetColor(Color color, float emission = 0f)
        {
            this.color = color;
            this.emission = emission;
            colored = true;
            Apply();
        }

        /// <summary>
        /// Белая вспышка поверх цвета (неуязвимость игрока): все материалы тянутся к белому на <paramref name="amount"/>
        /// и светятся белым с яркостью amount × <paramref name="glow"/>. 0 — обычный вид.
        /// </summary>
        public void SetBlink(float amount, float glow)
        {
            if (Mathf.Approximately(amount, blink) && Mathf.Approximately(glow, blinkGlow))
                return;
            if (amount > 0f && blink <= 0f)
                foreach (var r in Renderers())
                    GlowMaterials.EnsureEmission(r);
            blink = amount;
            blinkGlow = glow;
            Apply();
        }

        private Renderer[] Renderers()
        {
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>();
            return renderers;
        }

        private void Apply()
        {
            block ??= new MaterialPropertyBlock();
            var flash = Color.white * (blink * blinkGlow);
            foreach (var r in Renderers())
            {
                if (r == null)
                    continue;
                var materials = r.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    var tinted = colored && System.Array.IndexOf(coloredMaterialIndices, index) >= 0;
                    if (!tinted && blink <= 0f && !r.HasPropertyBlock())
                        continue;
                    var material = materials[index];
                    var baseColor = tinted ? color
                        : material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
                    var glow = tinted ? color * emission : Color.black;
                    r.GetPropertyBlock(block, index);
                    block.SetColor(BaseColorId, Color.Lerp(baseColor, Color.white, blink));
                    block.SetColor(EmissionColorId, glow + flash);
                    r.SetPropertyBlock(block, index);
                }
            }
        }

        /// <summary>Для предмета в руках колайдеры не нужны.</summary>
        public void StripColliders()
        {
            foreach (var c in GetComponentsInChildren<Collider>())
                Destroy(c);
        }
    }
}
