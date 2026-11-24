using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Деформация куба без скелета (GDD, раздел 6): сжатие-растяжение, изгиб и наклон вершин меша.
    /// Параметры ведут пружины (spring-damper), поэтому вобблинг после прыжка и приземления получается сам.
    /// <para>Меш: начало координат внизу по центру, высота <see cref="height"/>, нужна сетка по высоте (Read/Write включён).
    /// Деформация считается на CPU: у куба ~1000 вершин, это дёшево и не требует своего шейдера.</para>
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public sealed class CubeDeformer : MonoBehaviour
    {
        [SerializeField] private float height = 1f;
        [SerializeField] private float stiffness = 220f;
        [SerializeField] private float damping = 9f;

        private Mesh mesh;
        private Vector3[] rest;
        private Vector3[] work;

        private Spring squash;
        private Spring bendX, bendZ;
        private Spring leanX, leanZ;

        /// <summary>Целевое сжатие: &lt;0 — сплющен, &gt;0 — вытянут.</summary>
        public float SquashTarget { get; set; }

        /// <summary>Целевой изгиб (смещение верха по X/Z локально, в метрах; квадратичный по высоте).</summary>
        public Vector2 BendTarget { get; set; }

        /// <summary>Целевой наклон (сдвиг верха, линейный по высоте).</summary>
        public Vector2 LeanTarget { get; set; }

        public float Squash => squash.Value;
        public float Height => height;

        public void Configure(float springStiffness, float springDamping)
        {
            stiffness = springStiffness;
            damping = springDamping;
        }

        private void Awake()
        {
            var filter = GetComponent<MeshFilter>();
            var source = filter.sharedMesh;
            if (source == null || !source.isReadable)
            {
                Debug.LogError($"{name}: CubeDeformer needs a readable mesh (Read/Write enabled)", this);
                enabled = false;
                return;
            }

            mesh = Instantiate(source);
            mesh.name = source.name + " (deformed)";
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            rest = source.vertices;
            work = new Vector3[rest.Length];
        }

        /// <summary>Мгновенно задать сжатие (кадры подготовки к прыжку).</summary>
        public void SetSquash(float value, float velocity = 0f)
        {
            squash.Value = value;
            squash.Velocity = velocity;
        }

        /// <summary>Толчок пружин: скорость сжатия и изгиба. Отсюда вобблинг.</summary>
        public void Impulse(float squashVelocity, Vector2 bendVelocity = default)
        {
            squash.Velocity += squashVelocity;
            bendX.Velocity += bendVelocity.x;
            bendZ.Velocity += bendVelocity.y;
        }

        public void ResetShape()
        {
            squash = default;
            bendX = bendZ = leanX = leanZ = default;
            SquashTarget = 0;
            BendTarget = LeanTarget = Vector2.zero;
        }

        private void LateUpdate()
        {
            if (mesh == null)
                return;

            var dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            // Два подшага: жёсткие пружины стабильны и при низком FPS.
            for (int i = 0; i < 2; i++)
            {
                var h = dt * 0.5f;
                squash.Step(SquashTarget, stiffness, damping, h);
                bendX.Step(BendTarget.x, stiffness * 0.8f, damping, h);
                bendZ.Step(BendTarget.y, stiffness * 0.8f, damping, h);
                leanX.Step(LeanTarget.x, stiffness * 0.6f, damping, h);
                leanZ.Step(LeanTarget.y, stiffness * 0.6f, damping, h);
            }

            squash.Value = Mathf.Clamp(squash.Value, -0.6f, 0.8f);

            for (int i = 0; i < rest.Length; i++)
                work[i] = DeformPoint(rest[i]);
            mesh.vertices = work;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>Та же деформация для произвольной точки в локальных координатах меша (сокеты рук).</summary>
        public Vector3 DeformPoint(Vector3 p)
        {
            var sy = 1f + squash.Value;
            var sxz = 1f / Mathf.Sqrt(Mathf.Max(0.2f, sy));
            var t = Mathf.Clamp01(p.y / height);
            var t2 = t * t;
            return new Vector3(
                p.x * sxz + bendX.Value * t2 + leanX.Value * t,
                p.y * sy,
                p.z * sxz + bendZ.Value * t2 + leanZ.Value * t);
        }

        private struct Spring
        {
            public float Value;
            public float Velocity;

            public void Step(float target, float k, float c, float dt)
            {
                Velocity += ((target - Value) * k - Velocity * c) * dt;
                Value += Velocity * dt;
            }
        }
    }
}
