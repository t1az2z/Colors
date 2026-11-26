using System.Collections.Generic;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Копии материалов со включённым _EMISSION: чтобы светить через MaterialPropertyBlock (вспышка игрока),
    /// материалу нужен этот keyword. Копия одна на исходный материал, общий ассет не меняется.
    /// </summary>
    public static class GlowMaterials
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Dictionary<Material, Material> Copies = new Dictionary<Material, Material>();

        /// <summary>Заменить у рендерера материалы без _EMISSION на светящиеся копии (с чёрной эмиссией — вид тот же).</summary>
        public static void EnsureEmission(Renderer renderer)
        {
            if (renderer == null)
                return;
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (source == null || !source.HasProperty(EmissionColorId) || source.IsKeywordEnabled("_EMISSION"))
                    continue;
                if (!Copies.TryGetValue(source, out var copy) || copy == null)
                {
                    copy = new Material(source) { name = source.name + " (Glow)" };
                    copy.EnableKeyword("_EMISSION");
                    copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    copy.SetColor(EmissionColorId, Color.black);
                    Copies[source] = copy;
                }

                materials[i] = copy;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }
}
