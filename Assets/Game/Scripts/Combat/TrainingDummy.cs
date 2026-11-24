using ColorsAction.Configs;
using GameCore.Config;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Тренировочный манекен: бесконечное здоровье, при ударе отклоняется от удара на пружине и мигает красным.
    /// Параметры — Configs/Dummy.json.
    /// </summary>
    public sealed class TrainingDummy : GameBehaviour, IHittable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Transform tiltPivot;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private float height = 1.5f;

        private DummyConfig config;
        private Palette palette;
        private MaterialPropertyBlock block;
        private Color baseColor;
        private Color flashColor;

        // Наклон: угол-пружина по двум осям (x — вокруг локального X, y — вокруг Z).
        private Vector2 tilt;
        private Vector2 tiltVelocity;
        private float flashTimer;
        private float totalDamage;

        public string ColorId => config != null ? config.Color : "red";
        public Vector3 HitPoint => transform.position + Vector3.up * height * 0.75f;
        public float TotalDamage => totalDamage;

        protected override void OnConstruct(IResolver resolver)
        {
            config = resolver.Resolve<ConfigService>().Load<DummyConfig>(ConfigKeys.Dummy);
            palette = resolver.Resolve<Palette>();
        }

        private void Start()
        {
            block = new MaterialPropertyBlock();
            baseColor = palette.Get(config.Color);
            flashColor = Palette.Parse(config.FlashColor);
            ApplyColor(0f);
        }

        public void ReceiveHit(HitInfo hit)
        {
            totalDamage += hit.Damage;
            // Отклоняемся от удара: ось вращения перпендикулярна направлению удара.
            var dir = hit.Direction;
            dir.y = 0;
            if (dir.sqrMagnitude < 0.0001f)
                dir = -transform.forward;
            dir.Normalize();
            var push = config.TiltPerHit * Mathf.Max(0.5f, hit.Knockback);
            // Наклон верха в сторону dir: поворот вокруг X на +dir.z, вокруг Z на -dir.x.
            tiltVelocity += new Vector2(dir.z, -dir.x) * push * 12f;
            flashTimer = config.FlashDuration;
        }

        private void Update()
        {
            if (config == null)
                return;
            var dt = Time.deltaTime;
            tiltVelocity += (-tilt * config.Stiffness - tiltVelocity * config.Damping) * dt;
            tilt += tiltVelocity * dt;
            tilt = Vector2.ClampMagnitude(tilt, config.MaxTilt);
            if (tiltPivot != null)
                tiltPivot.rotation = Quaternion.Euler(tilt.x, 0f, tilt.y) * transform.rotation;

            if (flashTimer > 0f)
            {
                flashTimer -= dt;
                ApplyColor(Mathf.Clamp01(flashTimer / config.FlashDuration));
            }
        }

        private void ApplyColor(float flash)
        {
            if (bodyRenderer == null)
                return;
            // Вспышка: цвет к ярко-красному и свечение, затухает за FlashDuration.
            var pulse = flash * flash;
            bodyRenderer.GetPropertyBlock(block, 0);
            block.SetColor(BaseColorId, Color.Lerp(baseColor, flashColor, pulse));
            block.SetColor(EmissionColorId, flashColor * (pulse * config.FlashIntensity));
            bodyRenderer.SetPropertyBlock(block, 0);
        }
    }
}
