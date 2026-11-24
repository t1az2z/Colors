using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Кинематический контроллер для куба: коллизия — выровненный по осям бокс, а не капсула,
    /// поэтому высоты ступеней работают точно: подъём без прыжка до <see cref="StepHeight"/>,
    /// а уступ выше высоты прыжка нельзя "зацепить" скруглённым низом.
    /// Позиция объекта — центр нижней грани.
    /// </summary>
    public sealed class BoxMotor : MonoBehaviour
    {
        private const float Skin = 0.015f;
        private const float MinGroundNormalY = 0.7f;

        [SerializeField] private LayerMask collisionMask = ~0;

        private readonly Collider[] overlaps = new Collider[16];
        private Vector3 halfExtents = new Vector3(0.45f, 0.5f, 0.45f);
        private float colliderHeight = 1f;

        public float StepHeight { get; set; } = 0.3f;

        /// <summary>Вертикальная скорость (горизонталь задаётся смещением в <see cref="Move"/>).</summary>
        public float VerticalVelocity { get; set; }

        public bool Grounded { get; private set; }
        public bool WasGrounded { get; private set; }

        /// <summary>Вертикальная скорость в момент последнего приземления (для силы сжатия).</summary>
        public float LastLandingSpeed { get; private set; }

        public event System.Action Landed;

        public LayerMask CollisionMask
        {
            get => collisionMask;
            set => collisionMask = value;
        }

        public void Configure(float width, float height, float stepHeight)
        {
            colliderHeight = height;
            halfExtents = new Vector3(width * 0.5f, height * 0.5f, width * 0.5f);
            StepHeight = stepHeight;
            var box = GetProbe();
            box.size = halfExtents * 2f;
            box.transform.localPosition = Vector3.up * colliderHeight * 0.5f;
        }

        private Vector3 Center(Vector3 bottom) => bottom + Vector3.up * colliderHeight * 0.5f;
        private Vector3 CastHalf => halfExtents - Vector3.one * Skin;

        public void Teleport(Vector3 bottom)
        {
            transform.position = bottom;
            VerticalVelocity = 0;
            Grounded = false;
        }

        /// <summary>Сдвиг по горизонтали + вертикаль по <see cref="VerticalVelocity"/> (гравитацию считает вызывающий).</summary>
        public void Move(Vector3 horizontal, float dt, float? verticalDelta = null)
        {
            WasGrounded = Grounded;
            var pos = transform.position;
            pos = Depenetrate(pos);

            horizontal.y = 0;
            pos = MoveHorizontal(pos, horizontal);
            pos = MoveVertical(pos, verticalDelta ?? VerticalVelocity * dt);

            transform.position = pos;

            if (Grounded && !WasGrounded)
                Landed?.Invoke();
        }

        private Vector3 MoveHorizontal(Vector3 pos, Vector3 delta)
        {
            for (int iteration = 0; iteration < 3; iteration++)
            {
                var dist = delta.magnitude;
                if (dist < 1e-5f)
                    break;
                var dir = delta / dist;

                if (!Physics.BoxCast(Center(pos), CastHalf, dir, out var hit, Quaternion.identity, dist + Skin,
                        collisionMask, QueryTriggerInteraction.Ignore))
                {
                    pos += delta;
                    break;
                }

                var allowed = Mathf.Max(0f, hit.distance - Skin);
                pos += dir * allowed;
                var left = dist - allowed;

                if (Grounded && TryStepUp(ref pos, dir, left))
                    break;

                // Скольжение вдоль стены (только горизонтальная составляющая нормали).
                var n = hit.normal;
                n.y = 0;
                if (n.sqrMagnitude < 1e-4f)
                    break;
                n.Normalize();
                delta = Vector3.ProjectOnPlane(dir * left, n);
            }

            return pos;
        }

        private bool TryStepUp(ref Vector3 pos, Vector3 dir, float distance)
        {
            if (StepHeight <= 0)
                return false;
            var forward = Mathf.Max(distance, 0.05f);
            var center = Center(pos);
            // Вверх на высоту ступени.
            if (Physics.BoxCast(center, CastHalf, Vector3.up, out _, Quaternion.identity, StepHeight + Skin,
                    collisionMask, QueryTriggerInteraction.Ignore))
                return false;
            var raised = center + Vector3.up * StepHeight;
            // Вперёд на приподнятом уровне.
            if (Physics.BoxCast(raised, CastHalf, dir, out _, Quaternion.identity, forward + Skin,
                    collisionMask, QueryTriggerInteraction.Ignore))
                return false;
            var ahead = raised + dir * forward;
            // Вниз: должна найтись опора не ниже исходного пола.
            if (!Physics.BoxCast(ahead, CastHalf, Vector3.down, out var down, Quaternion.identity, StepHeight + Skin,
                    collisionMask, QueryTriggerInteraction.Ignore))
                return false;
            if (down.normal.y < MinGroundNormalY)
                return false;
            var drop = Mathf.Max(0f, down.distance - Skin);
            var newCenter = ahead + Vector3.down * drop;
            pos = newCenter - Vector3.up * colliderHeight * 0.5f;
            return true;
        }

        private Vector3 MoveVertical(Vector3 pos, float dy)
        {
            var center = Center(pos);
            if (dy > 0f)
            {
                Grounded = false;
                if (Physics.BoxCast(center, CastHalf, Vector3.up, out var up, Quaternion.identity, dy + Skin,
                        collisionMask, QueryTriggerInteraction.Ignore))
                {
                    dy = Mathf.Max(0f, up.distance - Skin);
                    VerticalVelocity = 0f;
                }

                return pos + Vector3.up * dy;
            }

            // Вниз: обычное падение, а на земле ещё и "прилипание" при спуске по ступеням.
            var probe = -dy;
            if (WasGrounded)
                probe = Mathf.Max(probe, StepHeight);
            if (Physics.BoxCast(center, CastHalf, Vector3.down, out var hit, Quaternion.identity, probe + Skin,
                    collisionMask, QueryTriggerInteraction.Ignore) && hit.normal.y >= MinGroundNormalY)
            {
                var landingSpeed = -VerticalVelocity;
                pos += Vector3.down * Mathf.Max(0f, hit.distance - Skin);
                if (!Grounded)
                    LastLandingSpeed = landingSpeed;
                Grounded = true;
                VerticalVelocity = 0f;
                return pos;
            }

            Grounded = false;
            return pos + Vector3.up * dy;
        }

        private Vector3 Depenetrate(Vector3 pos)
        {
            var center = Center(pos);
            var count = Physics.OverlapBoxNonAlloc(center, halfExtents - Vector3.one * (Skin * 0.5f), overlaps,
                Quaternion.identity, collisionMask, QueryTriggerInteraction.Ignore);
            if (count == 0)
                return pos;

            var box = GetProbe();
            for (int i = 0; i < count; i++)
            {
                var other = overlaps[i];
                if (other == box)
                    continue;
                if (Physics.ComputePenetration(box, center, Quaternion.identity, other, other.transform.position,
                        other.transform.rotation, out var direction, out var distance))
                {
                    center += direction * (distance + 0.001f);
                }
            }

            return center - Vector3.up * colliderHeight * 0.5f;
        }

        private BoxCollider probe;

        /// <summary>
        /// BoxCollider игрока: нужен ComputePenetration и чтобы враги могли попадать по игроку.
        /// Лежит на слое объекта (Player), который исключён из <see cref="collisionMask"/>.
        /// </summary>
        private BoxCollider GetProbe()
        {
            if (probe == null)
            {
                var go = new GameObject("MotorCollider");
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.up * colliderHeight * 0.5f;
                probe = go.AddComponent<BoxCollider>();
            }

            return probe;
        }

        private void Start() => GetProbe().size = halfExtents * 2f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Grounded ? Color.green : Color.yellow;
            Gizmos.DrawWireCube(Center(transform.position), halfExtents * 2f);
        }
    }
}
