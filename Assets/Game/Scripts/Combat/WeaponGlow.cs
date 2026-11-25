using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Усиленное свечение оружия в руке (заряд атаки): пока его поддерживают <see cref="Hold"/> каждый кадр,
    /// свечение тянется к заданному уровню, без поддержки — гаснет до обычного свечения оружия в руках.
    /// </summary>
    public sealed class WeaponGlow : MonoBehaviour
    {
        /// <summary>Добавка к эмиссии при уровне 1.</summary>
        private const float Boost = 3f;

        private ItemVisual visual;
        private Color color;
        private float baseGlow;
        private float target;
        private float current;
        private float heldUntil;
        private bool dirty;

        public static WeaponGlow For(GameObject weapon, Color color, float baseGlow)
        {
            if (weapon == null || !weapon.TryGetComponent<ItemVisual>(out var visual))
                return null;
            if (!weapon.TryGetComponent<WeaponGlow>(out var glow))
                glow = weapon.AddComponent<WeaponGlow>();
            glow.visual = visual;
            glow.color = color;
            glow.baseGlow = baseGlow;
            return glow;
        }

        /// <summary>Держать свечение на уровне <paramref name="level"/> (0..1) в этом кадре.</summary>
        public void Hold(float level)
        {
            target = Mathf.Clamp01(level);
            heldUntil = Time.time + 0.05f;
        }

        /// <summary>Мгновенная вспышка до уровня 1, дальше гаснет сама.</summary>
        public void Flash()
        {
            current = 1f;
            dirty = true;
        }

        private void LateUpdate()
        {
            if (Time.time > heldUntil)
                target = 0f;
            var next = Mathf.MoveTowards(current, target, (target > current ? 6f : 4f) * Time.deltaTime);
            if (!dirty && Mathf.Approximately(next, current))
                return;
            current = next;
            dirty = false;
            if (visual != null)
                visual.SetColor(color, baseGlow + current * Boost);
        }
    }
}
