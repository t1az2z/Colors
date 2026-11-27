using System.Collections.Generic;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>Кто выпустил пулю: получает попадания и конец полёта (залп дробовика).</summary>
    public interface IPelletListener
    {
        /// <summary>Пуля коснулась цели и остановилась.</summary>
        void OnPelletHit(IHittable target, Vector3 point, Vector3 direction);

        /// <summary>Пуля исчезла: попала, упёрлась в стену или долетела до конца дальности.</summary>
        void OnPelletDone();
    }

    /// <summary>
    /// Пуля-снаряд: маленький светящийся шарик с коротким следом, летит по прямой с заданной скоростью,
    /// попадает в цель при касании (проверка сферой по пути за кадр), останавливается о стены и исчезает
    /// на конце дальности. Пули берутся из пула и возвращаются в него.
    /// </summary>
    public sealed class Pellet : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly RaycastHit[] hits = new RaycastHit[16];
        private static readonly HitDistanceComparer byDistance = new HitDistanceComparer();
        private static ComponentPool<Pellet> pool;
        private static Material material;

        private Renderer ball;
        private TrailRenderer trail;
        private MaterialPropertyBlock block;
        private IPelletListener listener;
        private Vector3 direction;
        private float speed;
        private float range;
        private float travelled;
        private float radius;
        private float life;
        private int mask;

        /// <summary>Пуль в полёте (для автотестов).</summary>
        public static int Active { get; private set; }

        private static ComponentPool<Pellet> Pool => pool ??= new ComponentPool<Pellet>("Pellets", Create);

        private static Material Material
        {
            get
            {
                if (material == null)
                    material = new Material(CurvedWorld.FindUnlit()) { name = "Pellet (runtime)" };
                return material;
            }
        }

        /// <summary>
        /// Выпустить пулю. Сначала проверяется отрезок от центра стрелка до дула (<paramref name="from"/> →
        /// <paramref name="muzzle"/>): враг вплотную или стена перед стволом ловят пулю сразу.
        /// </summary>
        /// <param name="color">Цвет свечения (HDR).</param>
        /// <param name="size">Диаметр шарика, метры.</param>
        /// <param name="hitRadius">Радиус проверки касания, метры.</param>
        public static void Launch(Vector3 from, Vector3 muzzle, Vector3 direction, float speed, float range, float size,
            float hitRadius, Color color, int mask, IPelletListener listener)
        {
            var pellet = Pool.Get();
            pellet.Init(muzzle, direction, speed, range, size, hitRadius, color, mask, listener);
            var toMuzzle = muzzle - from;
            var distance = toMuzzle.magnitude;
            if (distance > 0.001f && pellet.Cast(from, toMuzzle / distance, distance))
                return;
            // Дальность считается от центра стрелка, как раньше у луча.
            pellet.travelled = distance;
        }

        private static Pellet Create(Transform root)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Pellet";
            go.layer = Debris.Layer;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            var pellet = go.AddComponent<Pellet>();
            pellet.ball = go.GetComponent<Renderer>();
            pellet.ball.sharedMaterial = Material;
            pellet.ball.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pellet.ball.receiveShadows = false;
            pellet.trail = go.AddComponent<TrailRenderer>();
            pellet.trail.sharedMaterial = Material;
            pellet.trail.time = 0.05f;
            pellet.trail.minVertexDistance = 0.05f;
            pellet.trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            pellet.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pellet.trail.receiveShadows = false;
            pellet.trail.numCapVertices = 2;
            pellet.block = new MaterialPropertyBlock();
            return pellet;
        }

        private void Init(Vector3 position, Vector3 dir, float pelletSpeed, float maxRange, float size, float hitRadius,
            Color color, int layerMask, IPelletListener owner)
        {
            transform.position = position;
            transform.localScale = Vector3.one * size;
            direction = dir.normalized;
            speed = pelletSpeed;
            range = maxRange;
            travelled = 0f;
            radius = hitRadius;
            mask = layerMask;
            listener = owner;
            life = maxRange / Mathf.Max(0.1f, pelletSpeed) * 1.5f + 0.5f;
            block.SetColor(BaseColorId, color);
            ball.SetPropertyBlock(block);
            trail.SetPropertyBlock(block);
            // След ширины шарика; длину даёт время следа (у быстрой пули — длиннее).
            trail.widthMultiplier = size * 0.8f;
            trail.Clear();
            Active++;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f)
                return; // фриз-кадры: пуля висит
            life -= dt;
            var step = Mathf.Min(speed * dt, range - travelled);
            if (step > 0f && Cast(transform.position, direction, step))
                return;
            transform.position += direction * Mathf.Max(0f, step);
            travelled += Mathf.Max(0f, step);
            if (travelled >= range - 1e-4f || life <= 0f)
                Finish();
        }

        /// <summary>Сфера по отрезку: ближайшая цель — попадание, ближайшая стена — остановка. true — пуля закончилась.</summary>
        private bool Cast(Vector3 from, Vector3 dir, float distance)
        {
            var count = Physics.SphereCastNonAlloc(from, radius, dir, hits, distance, mask, QueryTriggerInteraction.Collide);
            if (count == 0)
                return false;
            System.Array.Sort(hits, 0, count, byDistance);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                // Пересечение в самом начале отрезка: у такой записи нет точки, берём начало.
                var point = hit.distance > 0f ? hit.point : from;
                var target = hit.collider.GetComponentInParent<IHittable>();
                if (target != null)
                {
                    if (target is EnemyController { Dead: true })
                        continue; // осколки ещё не разлетелись, тело уже не цель
                    transform.position = from + dir * hit.distance;
                    listener?.OnPelletHit(target, point, dir);
                    Finish();
                    return true;
                }

                // Подсказки и предметы на земле (триггеры) пули пропускают; твёрдое — стена.
                if (hit.collider.isTrigger)
                    continue;
                transform.position = from + dir * hit.distance;
                Finish();
                return true;
            }

            return false;
        }

        private void Finish()
        {
            if (!gameObject.activeSelf)
                return;
            Active--;
            var owner = listener;
            listener = null;
            Pool.Release(this);
            owner?.OnPelletDone();
        }

        private sealed class HitDistanceComparer : IComparer<RaycastHit>
        {
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
