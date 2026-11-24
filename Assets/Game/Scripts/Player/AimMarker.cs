using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Схема Б: маркер на земле там, куда смотрит и бьёт персонаж (курсор мыши или правый стик).
    /// Тёмный плоский диск, мягко догоняет точку прицела и слегка пульсирует.
    /// </summary>
    public sealed class AimMarker : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private PlayerController player;
        private Renderer disc;
        private MaterialPropertyBlock block;
        private float visible;
        private float phase;

        public static AimMarker Create(PlayerController player, Material material)
        {
            var go = new GameObject("AimMarker");
            var marker = go.AddComponent<AimMarker>();
            marker.player = player;

            var discGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            DestroyImmediate(discGo.GetComponent<Collider>());
            discGo.name = "Disc";
            discGo.transform.SetParent(go.transform, false);
            discGo.transform.localScale = new Vector3(0.6f, 0.006f, 0.6f);
            marker.disc = discGo.GetComponent<Renderer>();
            marker.disc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (material != null)
                marker.disc.sharedMaterial = material;
            marker.block = new MaterialPropertyBlock();
            go.SetActive(true);
            return marker;
        }

        private void LateUpdate()
        {
            if (player == null)
            {
                Destroy(gameObject);
                return;
            }

            var point = player.AimPoint;
            var dt = Time.unscaledDeltaTime;
            visible = Mathf.MoveTowards(visible, point.HasValue ? 1f : 0f, dt * 8f);
            if (point.HasValue)
            {
                var target = point.Value + Vector3.up * 0.02f;
                transform.position = visible < 0.05f
                    ? target
                    : Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-dt * 30f));
            }

            phase += dt;
            var scale = visible * (1f + Mathf.Sin(phase * 6f) * 0.06f);
            transform.localScale = new Vector3(scale, 1f, scale);
            disc.enabled = visible > 0.01f;

            disc.GetPropertyBlock(block);
            block.SetColor(BaseColorId, new Color(0.16f, 0.16f, 0.19f, 1f));
            disc.SetPropertyBlock(block);
        }
    }
}
