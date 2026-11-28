using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Вид предмета, лежащего на земле: корень префаба Resources/Items/Pickups/&lt;Предмет&gt;.
    /// Модель (обычно вложенный префаб оружия из Resources/Items) покачивается и вращается, свет и модель
    /// красятся в цвет предмета. Префабы по концепту (ItemPrefabBuilder) лежат неподвижно на плитах, а внимание
    /// привлекают луч света (<see cref="beam"/>, пульсирует) и искры (<see cref="sparkles"/>) цвета предмета.
    /// Всё, что не помечено здесь (плиты, трава), остаётся как в префабе.
    /// </summary>
    public sealed class PickupVisual : MonoBehaviour
    {
        [Tooltip("Что качается и вращается. Его позиция и поворот в префабе — исходная поза.")]
        [SerializeField] private Transform model;
        [Tooltip("Подсветка; красится в цвет предмета.")]
        [SerializeField] private Light glow;
        [SerializeField] private bool tintGlow = true;
        [Tooltip("Луч света над предметом (материал на шейдере ColorsAction/Particle, цвет — _Color).")]
        [SerializeField] private Renderer beam;
        [Tooltip("Искры вокруг предмета; цвет частиц — смесь белого и цвета предмета.")]
        [SerializeField] private ParticleSystem sparkles;
        [Tooltip("Цвет луча и искр, если предмет без цвета (ключ): tintGlow выключен.")]
        [SerializeField] private Color ownColor = new Color(1f, 0.9f, 0.65f);
        [SerializeField] private float beamAlpha = 0.45f;
        [Tooltip("Пульсация луча: доля яркости и частота, рад/с.")]
        [SerializeField] private float beamPulse = 0.25f;
        [SerializeField] private float beamPulseSpeed = 2.2f;
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

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock beamBlock;
        private Color beamColor;
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
            var accent = tintGlow ? color : ownColor;
            beamColor = Color.Lerp(accent, Color.white, 0.25f);
            if (sparkles != null)
            {
                var main = sparkles.main;
                main.startColor = Color.Lerp(accent, Color.white, 0.55f);
            }

            ApplyBeam(1f);
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
            if (beam != null && beamPulse > 0f)
                ApplyBeam(1f - beamPulse * (0.5f + 0.5f * Mathf.Sin(phase * beamPulseSpeed)));
            // Только вверх от исходной позы: предмет на плите не уходит в неё.
            model.localPosition = basePosition + Vector3.up * ((0.5f + 0.5f * Mathf.Sin(phase * bobSpeed)) * bobHeight);
            model.localRotation = baseRotation * Quaternion.AngleAxis(phase * spinSpeed, spinAxis);
        }

        private void ApplyBeam(float k)
        {
            if (beam == null)
                return;
            beamBlock ??= new MaterialPropertyBlock();
            var c = beamColor;
            c.a = beamAlpha * k;
            beam.GetPropertyBlock(beamBlock);
            beamBlock.SetColor(ColorId, c);
            beam.SetPropertyBlock(beamBlock);
        }

        /// <summary>Анимация подбора: модель взлетает (масштаб корня уменьшает <see cref="Pickup"/>).</summary>
        public void TickTaken(float dt)
        {
            if (model != null)
                model.localPosition += Vector3.up * (dt * takeRiseSpeed);
        }
    }
}
