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
        /// <summary>Цвет силуэта оружия за блоками (шейдер ColorsAction/Silhouette).</summary>
        private static readonly int SilhouetteTintId = Shader.PropertyToID("_SilhouetteTint");

        [SerializeField] private Renderer[] renderers;
        [SerializeField] private int[] coloredMaterialIndices = { 0 };

        private MaterialPropertyBlock block;

        public void SetColor(Color color, float emission = 0f)
        {
            block ??= new MaterialPropertyBlock();
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>();
            var tint = color;
            tint.a = 1f;
            foreach (var r in renderers)
            {
                for (int index = 0; index < r.sharedMaterials.Length; index++)
                {
                    r.GetPropertyBlock(block, index);
                    if (System.Array.IndexOf(coloredMaterialIndices, index) >= 0)
                    {
                        block.SetColor(BaseColorId, color);
                        block.SetColor(EmissionColorId, color * emission);
                    }

                    // Всё оружие целиком, включая рукоять, в силуэте окрашено цветом оружия.
                    block.SetColor(SilhouetteTintId, tint);
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
