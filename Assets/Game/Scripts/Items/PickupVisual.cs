using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Вид предмета, лежащего на земле: корень префаба Resources/Items/Pickups/&lt;Предмет&gt;.
    /// Модель (обычно вложенный префаб оружия из Resources/Items) покачивается и вращается, свет и модель
    /// красятся в цвет предмета. Всё, что не помечено здесь (постамент, частицы), остаётся как в префабе.
    /// </summary>
    public sealed class PickupVisual : MonoBehaviour
    {
        [Tooltip("Что качается и вращается. Его позиция и поворот в префабе — исходная поза.")]
        [SerializeField] private Transform model;
        [Tooltip("Подсветка; красится в цвет предмета.")]
        [SerializeField] private Light glow;
        [SerializeField] private bool tintGlow = true;
        [Tooltip("Свечение цветных материалов модели (как HeldWeaponGlow у оружия в руках).")]
        [SerializeField] private float emission = 0.6f;
        [SerializeField] private float bobHeight = 0.05f;
        [Tooltip("Частота покачивания, рад/с.")]
        [SerializeField] private float bobSpeed = 2f;
        [Tooltip("Скорость вращения, град/с, вокруг оси spinAxis модели.")]
        [SerializeField] private float spinSpeed = 25f;
        [SerializeField] private Vector3 spinAxis = Vector3.forward;
        [Tooltip("Скорость подъёма модели, когда предмет подобран, м/с.")]
        [SerializeField] private float takeRiseSpeed = 3f;

        private Vector3 basePosition;
        private Quaternion baseRotation;
        private float phase;

        public Transform Model => model;

        /// <summary>Настроить вид, созданный кодом (запасной вариант без префаба).</summary>
        public void Configure(Transform modelTransform, Light light)
        {
            model = modelTransform;
            glow = light;
        }

        /// <summary>Покрасить в цвет предмета и подготовить анимацию.</summary>
        public void Setup(Color color)
        {
            foreach (var iv in GetComponentsInChildren<ItemVisual>(true))
            {
                iv.SetColor(color, emission);
                iv.StripColliders();
            }

            if (glow != null && tintGlow)
                glow.color = color;
            if (model == null)
                model = transform;
            basePosition = model.localPosition;
            baseRotation = model.localRotation;
            phase = Random.value * 10f;
        }

        public void Tick(float dt)
        {
            if (model == null)
                return;
            phase += dt;
            model.localPosition = basePosition + Vector3.up * (Mathf.Sin(phase * bobSpeed) * bobHeight);
            model.localRotation = baseRotation * Quaternion.AngleAxis(phase * spinSpeed, spinAxis);
        }

        /// <summary>Анимация подбора: модель взлетает (масштаб корня уменьшает <see cref="Pickup"/>).</summary>
        public void TickTaken(float dt)
        {
            if (model != null)
                model.localPosition += Vector3.up * (dt * takeRiseSpeed);
        }
    }
}
