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
        private Mesh source;
        private Vector3[] rest;
        private Vector3[] dents;
        private Vector3[] work;

        private Spring squash;
        private Spring bendX, bendZ;
        private Spring leanX, leanZ;
        private Spring twist;
        private Spring pitch;
        private Spring dirSquash;

        /// <summary>Целевое сжатие: &lt;0 — сплющен, &gt;0 — вытянут.</summary>
        public float SquashTarget { get; set; }

        /// <summary>Целевой изгиб (смещение верха по X/Z локально, в метрах; квадратичный по высоте).</summary>
        public Vector2 BendTarget { get; set; }

        /// <summary>Целевой наклон (сдвиг верха, линейный по высоте).</summary>
        public Vector2 LeanTarget { get; set; }

        /// <summary>Скручивание вокруг вертикали, градусы у верха (низ не крутится). Плюс — верх поворачивает вправо.</summary>
        public float TwistTarget { get; set; }

        /// <summary>
        /// Поклон, градусы у верха: куб изгибается вперёд-вниз вокруг нижнего ребра (минус — назад).
        /// Угол растёт к верху квадратично, поэтому верх загибается над передней гранью, а низ стоит.
        /// </summary>
        public float PitchTarget { get; set; }

        /// <summary>
        /// Сжатие вдоль горизонтального направления <see cref="DirSquashAxis"/> (координаты меша X/Z):
        /// &lt;0 — куб сплющен в эту сторону (и чуть вытянут вверх), &gt;0 — вытянут.
        /// </summary>
        public float DirSquashTarget { get; set; }
        public Vector2 DirSquashAxis { get; set; } = new Vector2(0f, 1f);

        // ---- Анимации движения и покоя (Configs/Animations.json): складываются с тем, что пишут удары, прыжки и перекаты.

        /// <summary>
        /// Прямая добавка к сжатию, без пружины (цикл шагов, дыхание): амплитуда точная при любой частоте.
        /// Объём сохраняется так же, как у <see cref="SquashTarget"/>.
        /// </summary>
        public float DirectSquash { get; set; }

        /// <summary>Прямая добавка к изгибу (X/Z меша, квадратично по высоте), без пружины: покачивание шагов.</summary>
        public Vector2 DirectBend { get; set; }

        /// <summary>
        /// Добавка к цели скручивания, градусы (осмотр по сторонам, слежение за оружием). Идёт через ту же пружину,
        /// что и <see cref="TwistTarget"/> — удары её не затирают.
        /// </summary>
        public float TwistOffset { get; set; }

        /// <summary>Добавка к цели поклона, градусы (через пружину поклона).</summary>
        public float PitchOffset { get; set; }

        /// <summary>Сдвиг глаз по передней грани (X — вправо, Y — вверх), доля куба. Без пружины.</summary>
        public Vector2 EyeOffset { get; set; }

        /// <summary>Закрытость глаз: 0 — открыты, 1 — закрыты (сплющены по высоте до <see cref="EyeClosedScale"/>).</summary>
        public float EyeBlink { get; set; }

        /// <summary>Высота закрытого глаза, доля открытого.</summary>
        public float EyeClosedScale { get; set; } = 0.12f;

        /// <summary>У меша есть глаза (второй сабмеш): моргание и взгляд работают.</summary>
        public bool HasEyes => eyeIndices != null;

        /// <summary>Пружины идут по реальному времени: сцена подбора предмета, пока мир стоит (time scale = 0).</summary>
        public bool UnscaledTime { get; set; }

        public float Squash => squash.Value;

        /// <summary>
        /// Ширина и глубина куба относительно меша (меш 1×1, высота не меняется): точки меша и сокеты рук
        /// сжимаются по X/Z до деформации. Позы рук и сколы задаются для куба 1×1, сжатие их подгоняет.
        /// </summary>
        public float Width { get; set; } = 1f;

        /// <summary>Исходный меш (до копирования для деформации) — для врагов того же вида.</summary>
        public Mesh SourceMesh => source;

        /// <summary>Рабочая копия меша (её же рисует MeshFilter).</summary>
        public Mesh Mesh => mesh;

        /// <summary>
        /// Сколы: вершины у указанных углов (координаты меша) вдавливаются к центру, угол как будто отколот.
        /// null — без сколов.
        /// </summary>
        public void SetChips(Vector3[] corners, float radius)
        {
            if (rest == null)
                return;
            if (corners == null || corners.Length == 0)
            {
                dents = null;
                return;
            }

            dents = new Vector3[rest.Length];
            var center = new Vector3(0f, height * 0.5f, 0f);
            for (int i = 0; i < rest.Length; i++)
            {
                foreach (var c in corners)
                {
                    var d = Vector3.Distance(rest[i], c);
                    if (d >= radius)
                        continue;
                    // Срез плоскостью: чем ближе к углу, тем глубже вдавлено к центру.
                    var depth = (radius - d) * 0.85f;
                    dents[i] += (center - rest[i]).normalized * depth;
                }
            }
        }
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

            this.source = source;
            mesh = Instantiate(source);
            mesh.name = source.name + " (deformed)";
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            rest = source.vertices;
            work = new Vector3[rest.Length];
            FindEyes(source);
        }

        // ---- Глаза: вершины второго сабмеша (тёмные пластины на передней грани), центр своего глаза по высоте.

        private int[] eyeIndices;
        private float[] eyeCenterY;

        /// <summary>
        /// Глаза — сабмеш 1 (материал Eyes, см. Tools/Blender/make_models.py). Каждый глаз — вершины по одну сторону
        /// от середины по X; моргание сплющивает глаз к его собственному центру.
        /// </summary>
        private void FindEyes(Mesh source)
        {
            eyeIndices = null;
            eyeCenterY = null;
            if (source.subMeshCount < 2)
                return;
            var set = new System.Collections.Generic.HashSet<int>(source.GetTriangles(1));
            if (set.Count == 0)
                return;
            eyeIndices = new int[set.Count];
            set.CopyTo(eyeIndices);
            float leftMin = float.MaxValue, leftMax = float.MinValue, rightMin = float.MaxValue, rightMax = float.MinValue;
            foreach (var i in eyeIndices)
            {
                var y = rest[i].y;
                if (rest[i].x < 0f)
                {
                    leftMin = Mathf.Min(leftMin, y);
                    leftMax = Mathf.Max(leftMax, y);
                }
                else
                {
                    rightMin = Mathf.Min(rightMin, y);
                    rightMax = Mathf.Max(rightMax, y);
                }
            }

            eyeCenterY = new float[eyeIndices.Length];
            for (int k = 0; k < eyeIndices.Length; k++)
                eyeCenterY[k] = rest[eyeIndices[k]].x < 0f ? (leftMin + leftMax) * 0.5f : (rightMin + rightMax) * 0.5f;
        }

        /// <summary>Глаза в покое: сдвиг взгляда и моргание (до деформации тела, поэтому глаза гнутся вместе с ним).</summary>
        private void ApplyEyes()
        {
            if (eyeIndices == null)
                return;
            var blink = Mathf.Clamp01(EyeBlink);
            var scale = 1f - blink * (1f - Mathf.Clamp01(EyeClosedScale));
            var offset = EyeOffset;
            for (int k = 0; k < eyeIndices.Length; k++)
            {
                var i = eyeIndices[k];
                var p = rest[i];
                if (dents != null)
                    p += dents[i];
                var cy = eyeCenterY[k];
                p.y = cy + (p.y - cy) * scale + offset.y;
                p.x += offset.x;
                work[i] = DeformPoint(p);
            }
        }

        /// <summary>Мгновенно задать сжатие (кадры подготовки к прыжку).</summary>
        public void SetSquash(float value, float velocity = 0f)
        {
            squash.Value = value;
            squash.Velocity = velocity;
        }

        /// <summary>Толчок пружин: скорость сжатия и изгиба. Отсюда вобблинг.</summary>
        /// <summary>Толчок сжатия вдоль направления (например, в сторону переката).</summary>
        public void ImpulseDirectional(Vector2 axis, float velocity)
        {
            if (axis.sqrMagnitude > 1e-4f)
                DirSquashAxis = axis.normalized;
            dirSquash.Velocity += velocity;
        }

        public void Impulse(float squashVelocity, Vector2 bendVelocity = default)
        {
            squash.Velocity += squashVelocity;
            bendX.Velocity += bendVelocity.x;
            bendZ.Velocity += bendVelocity.y;
        }

        public void ResetShape()
        {
            squash = default;
            bendX = bendZ = leanX = leanZ = twist = pitch = dirSquash = default;
            TwistTarget = PitchTarget = DirSquashTarget = 0f;
            SquashTarget = 0;
            BendTarget = LeanTarget = Vector2.zero;
            DirectSquash = TwistOffset = PitchOffset = EyeBlink = 0f;
            DirectBend = EyeOffset = Vector2.zero;
        }

        private void LateUpdate()
        {
            if (mesh == null)
                return;

            var dt = Mathf.Min(UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime, 1f / 30f);
            // Два подшага: жёсткие пружины стабильны и при низком FPS.
            for (int i = 0; i < 2; i++)
            {
                var h = dt * 0.5f;
                squash.Step(SquashTarget, stiffness, damping, h);
                bendX.Step(BendTarget.x, stiffness * 0.8f, damping, h);
                bendZ.Step(BendTarget.y, stiffness * 0.8f, damping, h);
                leanX.Step(LeanTarget.x, stiffness * 0.6f, damping, h);
                leanZ.Step(LeanTarget.y, stiffness * 0.6f, damping, h);
                twist.Step(TwistTarget + TwistOffset, stiffness * 0.9f, damping * 0.8f, h);
                pitch.Step(PitchTarget + PitchOffset, stiffness * 0.9f, damping, h);
                dirSquash.Step(DirSquashTarget, stiffness, damping, h);
            }

            squash.Value = Mathf.Clamp(squash.Value, -0.6f, 0.8f);
            dirSquash.Value = Mathf.Clamp(dirSquash.Value, -0.5f, 0.5f);

            // После перекомпиляции во время игры Unity восстанавливает null-массив сколов как пустой.
            if (dents != null && dents.Length != rest.Length)
                dents = null;
            for (int i = 0; i < rest.Length; i++)
                work[i] = DeformPoint(dents != null ? rest[i] + dents[i] : rest[i]);
            ApplyEyes();
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
            var x = p.x * sxz * Width;
            var z = p.z * sxz * Width;
            var y = p.y * sy;
            // Сжатие вдоль направления: проекция на ось масштабируется, объём уходит вверх.
            var ds = dirSquash.Value;
            if (Mathf.Abs(ds) > 1e-4f)
            {
                var axis = DirSquashAxis;
                var along = x * axis.x + z * axis.y;
                x += axis.x * along * ds;
                z += axis.y * along * ds;
                y *= 1f - ds * 0.5f;
            }

            // Прямое сжатие (шаги, дыхание) поверх пружины: тот же закон с сохранением объёма (CubeShape) —
            // сплющенный шире, вытянутый уже.
            var direct = Mathf.Clamp(DirectSquash, -0.4f, 0.4f);
            if (Mathf.Abs(direct) > 1e-5f)
            {
                var wide = ColorsAction.Animation.CubeShape.WidthScale(direct);
                x *= wide;
                z *= wide;
                y *= ColorsAction.Animation.CubeShape.HeightScale(direct);
            }

            // Скручивание: угол растёт от низа к верху (плавно), как у закручиваемого бруска.
            var angle = twist.Value * Mathf.Deg2Rad * t * (2f - t);
            if (Mathf.Abs(angle) > 1e-4f)
            {
                var cos = Mathf.Cos(angle);
                var sin = Mathf.Sin(angle);
                var rx = x * cos + z * sin;
                var rz = -x * sin + z * cos;
                x = rx;
                z = rz;
            }

            // Поклон: поворот вокруг горизонтальной оси у основания, угол ~ высота².
            var pitchAngle = pitch.Value * Mathf.Deg2Rad * t2;
            if (Mathf.Abs(pitchAngle) > 1e-4f)
            {
                var cos = Mathf.Cos(pitchAngle);
                var sin = Mathf.Sin(pitchAngle);
                var ry = y * cos - z * sin;
                var rz = y * sin + z * cos;
                y = ry;
                z = rz;
            }

            return new Vector3(
                x + (bendX.Value + DirectBend.x) * t2 + leanX.Value * t,
                y,
                z + (bendZ.Value + DirectBend.y) * t2 + leanZ.Value * t);
        }

        /// <summary>Поворот, который деформация даёт в точке (для сокетов рук: оружие крутится вместе с кубом).</summary>
        public Quaternion DeformRotation(Vector3 p)
        {
            var t = Mathf.Clamp01(p.y / height);
            return Quaternion.Euler(pitch.Value * t * t, 0f, 0f) * Quaternion.Euler(0f, twist.Value * t * (2f - t), 0f);
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
