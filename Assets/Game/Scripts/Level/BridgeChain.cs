using System.Collections.Generic;
using ColorsAction.Combat;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Цепи разводного моста (<see cref="DrawBridge"/>): две цепи от края поднятой створки к раме за шарниром — одна цель.
    /// Цель для пуль (триггер-коллайдер, накрывающий обе цепи и промежуток между ними) и для фиксации (СКМ / R3,
    /// список <see cref="All"/>); точка прицела — ближе к краю створки, чтобы залп шёл над ней. Рвётся только оружием
    /// из Configs/Bridges.json (дробовик), и сразу обе; удар другим оружием только раскачивает их.
    /// Объект стоит в середине между цепями, локальная +Z — вдоль цепей от створки к раме.
    /// </summary>
    public sealed class BridgeChain : MonoBehaviour, IHittable, IWeaponGate
    {
        private static readonly List<BridgeChain> all = new List<BridgeChain>();

        private readonly List<Link> links = new List<Link>();
        private DrawBridge owner;
        private BreakableState state;
        private BoxCollider hitBox;
        private float length;
        private float swingTimer;
        private float swingAmount;
        private Vector3 swingSide;

        private struct Link
        {
            public Transform Transform;
            public Vector3 Rest;

            /// <summary>Доля вдоль цепи 0..1 (для изгиба при раскачке).</summary>
            public float T;
        }

        /// <summary>Целые цепи на уровне (для фиксации).</summary>
        public static IReadOnlyList<BridgeChain> All => all;

        public bool Broken => state == null || state.Broken;

        /// <summary>Можно ли взять цепи в фиксацию: целы и это разрешено конфигом.</summary>
        public bool Lockable => !Broken && owner != null && owner.Config.LockOnChain;

        public string ColorId => owner != null ? owner.Config.Color : "red";

        /// <summary>Точка прицела: между цепями, на четверти длины от края створки.</summary>
        public Vector3 HitPoint => transform.TransformPoint(new Vector3(0f, 0f, -length * 0.25f));

        public bool ShowsDamageNumbers => false;

        public float Length => length;

        private void OnEnable() => all.Add(this);

        private void OnDisable() => all.Remove(this);

        /// <summary>Собрать цепи из звеньев между парами точек (в мире): from — у створки, to — у рамы.</summary>
        public void Build(DrawBridge bridge, IReadOnlyList<(Vector3 from, Vector3 to)> chains, Material material, int count)
        {
            owner = bridge;
            state = new BreakableState(bridge.Config.ChainHp, bridge.Config.BreakableBy);
            var from = Vector3.zero;
            var to = Vector3.zero;
            foreach (var c in chains)
            {
                from += c.from / chains.Count;
                to += c.to / chains.Count;
            }

            var axis = to - from;
            length = axis.magnitude;
            transform.SetPositionAndRotation((from + to) * 0.5f, Quaternion.LookRotation(axis / Mathf.Max(0.001f, length), Vector3.up));

            var spread = 0f;
            foreach (var c in chains)
            {
                BuildChain(c.from, c.to, material, count);
                var local = transform.InverseTransformPoint((c.from + c.to) * 0.5f);
                spread = Mathf.Max(spread, Mathf.Abs(local.x), Mathf.Abs(transform.InverseTransformPoint(c.from).x),
                    Mathf.Abs(transform.InverseTransformPoint(c.to).x));
            }

            // Попадание — по триггеру на обе цепи и промежуток: залп дробовика и фиксация находят их легко, игрок проходит сквозь.
            hitBox = gameObject.AddComponent<BoxCollider>();
            hitBox.isTrigger = true;
            hitBox.size = new Vector3(2f * spread + 0.35f, 0.4f, length);
        }

        /// <summary>Одна цепь: звенья-овалы из четырёх брусков, каждое следующее повёрнуто на 90° вокруг оси цепи.</summary>
        private void BuildChain(Vector3 from, Vector3 to, Material material, int count)
        {
            var root = new GameObject("Chain").transform;
            root.SetParent(transform, false);
            var axis = to - from;
            var chainLength = axis.magnitude;
            root.SetPositionAndRotation((from + to) * 0.5f, Quaternion.LookRotation(axis / Mathf.Max(0.001f, chainLength), Vector3.up));
            var step = chainLength / count;
            var linkLength = step * 1.45f;
            const float linkWidth = 0.13f;
            const float bar = 0.038f;
            for (int i = 0; i < count; i++)
            {
                var link = new GameObject($"Link{i}").transform;
                link.SetParent(root, false);
                var rest = new Vector3(0f, 0f, -chainLength * 0.5f + step * (i + 0.5f));
                link.SetLocalPositionAndRotation(rest, Quaternion.Euler(0f, 0f, i % 2 == 0 ? 0f : 90f));
                foreach (var s in Sides)
                {
                    Bar(link, material, new Vector3(s * (linkWidth - bar) * 0.5f, 0f, 0f), new Vector3(bar, bar, linkLength));
                    Bar(link, material, new Vector3(0f, 0f, s * (linkLength - bar) * 0.5f), new Vector3(linkWidth, bar, bar));
                }

                links.Add(new Link { Transform = link, Rest = rest, T = (i + 0.5f) / count });
            }
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
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
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
            // Раскачка поперёк цепей в сторону удара: середина отходит сильнее концов.
            var side = Vector3.ProjectOnPlane(direction, transform.forward);
            swingSide = side.sqrMagnitude > 0.0001f ? side.normalized : transform.right;
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
            var push = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
            Debris.Burst(HitPoint, owner.ChainColor, 10, 0.05f, 4f, 0.9f);
            ParryFlash.Spawn(HitPoint, 0.35f);
            foreach (var l in links)
            {
                var link = l.Transform;
                // Цепь рвётся ближе к створке: звенья у разрыва отлетают, дальние просто падают.
                var t = 1f - Mathf.Clamp01(Mathf.Abs(l.T - 0.25f) * 1.6f);
                link.SetParent(owner.DebrisRoot, true);
                link.gameObject.layer = Debris.Layer;
                foreach (Transform b in link)
                    b.gameObject.layer = Debris.Layer;
                var box = link.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.13f, 0.13f, 0.2f);
                var rb = link.gameObject.AddComponent<Rigidbody>();
                rb.mass = 0.2f;
                var velocity = (push * t + Random.insideUnitSphere * 0.4f + Vector3.up * (0.3f * t)) * config.ChainBreakForce * Mathf.Max(0.15f, t);
                rb.AddForce(velocity, ForceMode.VelocityChange);
                rb.AddTorque(Random.onUnitSphere * Random.Range(3f, 9f), ForceMode.VelocityChange);
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
            foreach (var l in links)
            {
                var parent = l.Transform.parent;
                var side = parent.InverseTransformDirection(swingSide);
                l.Transform.localPosition = l.Rest + side * (wobble * Mathf.Sin(Mathf.PI * l.T));
            }
        }
    }
}
