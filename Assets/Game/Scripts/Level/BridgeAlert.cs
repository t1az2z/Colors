using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Восклицательный знак над поднятым разводным мостом (<see cref="DrawBridge"/>): «здесь поможет дробовик».
    /// Собран из кубиков, как весь мир: столбик и точка цвета цепей, висит в проёме дальней рамы
    /// за поднятой створкой, виден издалека с обеих сторон моста. Покачивается вверх-вниз, медленно поворачивается и «дышит» сжатием-растяжением, как кубы
    /// персонажей. Появляется с пружинкой, исчезает сжатием (<see cref="Shown"/>).
    /// </summary>
    public sealed class BridgeAlert : MonoBehaviour
    {
        private const float Size = 0.34f;        // толщина столбика и точки, м
        private const float BarHeight = 0.9f;
        private const float Gap = 0.16f;
        /// <summary>Полная высота знака (точка, зазор, столбик), м.</summary>
        public const float Height = Size + Gap + BarHeight;

        private const float BobHeight = 0.14f;
        private const float BobSpeed = 2.2f;
        private const float SpinSpeed = 40f;     // градусов в секунду

        private Transform body;
        private Material material;
        private Vector3 basePosition;
        private float scale;        // текущий масштаб 0..1 с пружиной
        private float scaleVelocity;
        private float phase;

        /// <summary>Показывать ли знак (меняется плавно).</summary>
        public bool Shown { get; set; }

        /// <summary>Знак заметен (для автотестов).</summary>
        public bool Visible => body != null && body.gameObject.activeSelf && scale > 0.5f;

        /// <param name="partMaterial">Материал кубиков (им владеет мост).</param>
        public void Build(Material partMaterial)
        {
            basePosition = transform.localPosition;
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            material = partMaterial;

            // Низ точки — в нуле, столбик над ней.
            Cube(new Vector3(0f, Size * 0.5f, 0f), new Vector3(Size, Size, Size));
            Cube(new Vector3(0f, Size + Gap + BarHeight * 0.5f, 0f), new Vector3(Size, BarHeight, Size));
            body.localScale = Vector3.zero;
            body.gameObject.SetActive(false);
            phase = Random.value * 10f;
        }

        private void Cube(Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Part";
            Destroy(go.GetComponent<Collider>());
            go.layer = Debris.Layer; // пули и фиксация сквозь знак
            go.transform.SetParent(body, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private void Update()
        {
            if (body == null)
                return;
            var dt = Time.deltaTime;
            var target = Shown ? 1f : 0f;
            if (Shown)
            {
                // Пружина с перелётом — знак «выскакивает».
                scaleVelocity += ((target - scale) * 180f - scaleVelocity * 14f) * dt;
                scale += scaleVelocity * dt;
            }
            else
            {
                scaleVelocity = 0f;
                scale = Mathf.MoveTowards(scale, 0f, dt * 5f);
            }

            var active = scale > 0.001f || Shown;
            if (body.gameObject.activeSelf != active)
                body.gameObject.SetActive(active);
            if (!active)
                return;

            phase += dt;
            var bob = Mathf.Sin(phase * BobSpeed);
            transform.localPosition = basePosition + Vector3.up * (bob * BobHeight);
            body.localRotation = Quaternion.Euler(0f, phase * SpinSpeed, 0f);
            // Растягивается вверх в верхней точке и сплющивается в нижней, объём примерно сохраняется.
            var stretch = 1f + bob * 0.08f;
            var squash = 1f / Mathf.Sqrt(stretch);
            var s = Mathf.Max(0f, scale);
            body.localScale = new Vector3(squash * s, stretch * s, squash * s);
        }
    }
}
