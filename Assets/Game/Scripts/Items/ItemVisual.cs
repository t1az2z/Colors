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

        public void SetColor(Color color, float emission = 0f)
        {
            block ??= new MaterialPropertyBlock();
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                foreach (var index in coloredMaterialIndices)
                {
                    if (index >= r.sharedMaterials.Length)
                        continue;
                    r.GetPropertyBlock(block, index);
                    block.SetColor(BaseColorId, color);
                    block.SetColor(EmissionColorId, color * emission);
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
