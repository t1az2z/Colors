using System.Collections.Generic;
using ColorsAction.Combat;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Цепь разводного моста (<see cref="DrawBridge"/>): натянута от кольца на опущенной створке до края поднятой.
    /// Цель для пуль (триггер-коллайдер вдоль цепи) и для фиксации (СКМ / R3, список <see cref="All"/>).
    /// Рвётся только оружием из Configs/Bridges.json (дробовик); удар другим оружием только раскачивает её.
    /// Объект стоит в середине цепи, локальная +Z — вдоль цепи.
    /// </summary>
    public sealed class BridgeChain : MonoBehaviour, IHittable, IWeaponGate
    {
        private static readonly List<BridgeChain> all = new List<BridgeChain>();

        private readonly List<Transform> links = new List<Transform>();
        private readonly List<Vector3> linkRest = new List<Vector3>();
        private DrawBridge owner;
        private BreakableState state;
        private BoxCollider hitBox;
        private float length;
        private float swingTimer;
        private float swingAmount;
        private Vector3 swingSide;

        /// <summary>Целые цепи на уровне (для фиксации).</summary>
        public static IReadOnlyList<BridgeChain> All => all;

        public bool Broken => state == null || state.Broken;

        /// <summary>Можно ли взять цепь в фиксацию: цела и это разрешено конфигом.</summary>
        public bool Lockable => !Broken && owner != null && owner.Config.LockOnChain;

        public string ColorId => owner != null ? owner.Config.Color : "red";

        public Vector3 HitPoint => transform.position;

        public bool ShowsDamageNumbers => false;

        public float Length => length;

        private void OnEnable() => all.Add(this);

        private void OnDisable() => all.Remove(this);

        /// <summary>Собрать цепь из звеньев между точками <paramref name="from"/> и <paramref name="to"/> (в мире).</summary>
        public void Build(DrawBridge bridge, Vector3 from, Vector3 to, Material material, int count)
        {
            owner = bridge;
            state = new BreakableState(bridge.Config.ChainHp, bridge.Config.BreakableBy);
            var axis = to - from;
            length = axis.magnitude;
            transform.SetPositionAndRotation((from + to) * 0.5f, Quaternion.LookRotation(axis / Mathf.Max(0.001f, length), Vector3.up));

            // Звенья: овал из четырёх брусков, каждое следующее повёрнуто на 90° вокруг оси цепи.
            var step = length / count;
            var linkLength = step * 1.45f;
            const float linkWidth = 0.13f;
            const float bar = 0.038f;
            for (int i = 0; i < count; i++)
            {
                var link = new GameObject($"Link{i}").transform;
                link.SetParent(transform, false);
                var rest = new Vector3(0f, 0f, -length * 0.5f + step * (i + 0.5f));
                link.SetLocalPositionAndRotation(rest, Quaternion.Euler(0f, 0f, i % 2 == 0 ? 0f : 90f));
                foreach (var s in Sides)
                {
                    Bar(link, material, new Vector3(s * (linkWidth - bar) * 0.5f, 0f, 0f), new Vector3(bar, bar, linkLength));
                    Bar(link, material, new Vector3(0f, 0f, s * (linkLength - bar) * 0.5f), new Vector3(linkWidth, bar, bar));
                }

                links.Add(link);
                linkRest.Add(rest);
            }

            // Попадание — по триггеру потолще цепи: пули дробовика и фиксация находят её легко, игрок сквозь неё проходит.
            hitBox = gameObject.AddComponent<BoxCollider>();
            hitBox.isTrigger = true;
            hitBox.size = new Vector3(0.32f, 0.32f, length);
        }

        private static readonly float[] Sides = { -1f, 1f };

        private static void Bar(Transform parent, Material material, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Bar";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // ------------------------------------------------------------------ удары

        public bool Accepts(string weaponId) => state == null || state.Accepts(weaponId);

        public void Deflect(Vector3 direction, Color weaponColor)
        {
            if (Broken)
                return;
            Swing(direction, owner.Config.ChainSwing * 0.6f);
            Debris.Burst(HitPoint, owner.ChainColor, 1, 0.04f, 1.5f, 0.5f);
        }

        public void ReceiveHit(HitInfo hit)
        {
            if (Broken)
                return;
            var result = state.Hit(hit.WeaponId, hit.Damage);
            if (result == BreakHitResult.Ignored)
            {
                Deflect(hit.Direction, hit.WeaponColor);
                return;
            }

            owner.Flash();
            if (result == BreakHitResult.Broken)
                Snap(hit.Direction);
            else
                Swing(hit.Direction, owner.Config.ChainSwing);
        }

        private void Swing(Vector3 direction, float degrees)
        {
            // Раскачка поперёк цепи в сторону удара: середина отходит сильнее концов.
            var side = Vector3.ProjectOnPlane(direction, transform.forward);
            swingSide = side.sqrMagnitude > 0.0001f ? transform.InverseTransformDirection(side.normalized) : Vector3.right;
            swingAmount = Mathf.Tan(degrees * Mathf.Deg2Rad) * length * 0.5f;
            swingTimer = owner.Config.ChainSwingTime;
        }

        /// <summary>Разрыв: звенья падают обломками (у места разрыва — разлетаются), мост начинает опускаться.</summary>
        private void Snap(Vector3 direction)
        {
            enabled = false;
            if (hitBox != null)
                Destroy(hitBox);
            var config = owner.Config;
            var flat = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.right;
            Debris.Burst(HitPoint, owner.ChainColor, 8, 0.05f, 4f, 0.9f);
            ParryFlash.Spawn(HitPoint, 0.35f);
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                // Близкие к середине (месту разрыва) звенья отлетают, дальние просто падают.
                var t = 1f - Mathf.Abs((i + 0.5f) / links.Count - 0.5f) * 2f;
                link.SetParent(owner.DebrisRoot, true);
                link.gameObject.layer = Debris.Layer;
                foreach (Transform b in link)
                    b.gameObject.layer = Debris.Layer;
                var box = link.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.13f, 0.13f, 0.2f);
                var rb = link.gameObject.AddComponent<Rigidbody>();
                rb.mass = 0.2f;
                var velocity = (flat * t + Random.insideUnitSphere * 0.4f + Vector3.up * (0.3f * t)) * config.ChainBreakForce * t;
                rb.AddForce(velocity, ForceMode.VelocityChange);
                rb.AddTorque(Random.onUnitSphere * Random.Range(3f, 9f) * t, ForceMode.VelocityChange);
                link.gameObject.AddComponent<FadeAndDie>().Life = config.DebrisLife * Random.Range(0.8f, 1.2f);
            }

            links.Clear();
            owner.ChainSnapped();
        }

        private void Update()
        {
            if (swingTimer <= 0f || links.Count == 0)
                return;
            swingTimer = Mathf.Max(0f, swingTimer - Time.deltaTime);
            var k = swingTimer / Mathf.Max(0.01f, owner.Config.ChainSwingTime);
            var wobble = Mathf.Sin(swingTimer * 38f) * swingAmount * k;
            for (int i = 0; i < links.Count; i++)
            {
                var bow = Mathf.Sin(Mathf.PI * (i + 0.5f) / links.Count);
                links[i].localPosition = linkRest[i] + swingSide * (wobble * bow);
            }
        }
    }
}
