using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// След за клинком на Particle System (префаб Resources/Fx/SwordTrail): вдоль клинка между рукоятью и остриём
    /// испускаются мягкие частицы, которые живут доли секунды и тают; с острия срываются кубики-искры.
    /// Между кадрами точки досэмплируются по дуге вокруг центра персонажа, поэтому быстрый взмах даёт гладкий полукруг.
    /// Время игровое: во время фриз-кадров след замирает вместе с ударом.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class WeaponTrail : MonoBehaviour
    {
        /// <summary>Частиц поперёк клинка на один шаг дуги.</summary>
        private const int PointsAlongBlade = 6;
        /// <summary>Доля шагов дуги, на которых с острия срывается искра.</summary>
        private const float SparkChance = 0.18f;

        private Transform blade;
        private Transform pivot;
        private Vector3 baseLocal;
        private Vector3 tipLocal;
        private Color color;
        private GameObject view;
        private ParticleSystem band;
        private ParticleSystem sparks;
        private bool hasLast;
        private Vector3 lastBase;
        private Vector3 lastTip;

        /// <summary>Время жизни частиц следа, секунды.</summary>
        public float Lifetime { get; set; } = 0.2f;

        /// <summary>След не дальше этого расстояния от центра куба (дальность эффекта), метры. 0 — без ограничения.</summary>
        public float MaxRadius { get; set; }
        public bool Emitting { get; set; }

        /// <summary>Добавить след к модели оружия. Клинок — вдоль локальной +Y модели.</summary>
        public static WeaponTrail Attach(GameObject weapon, Transform pivot, Color color)
        {
            var filter = weapon.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return null;
            var bounds = filter.sharedMesh.bounds;
            var trail = weapon.AddComponent<WeaponTrail>();
            trail.blade = filter.transform;
            trail.pivot = pivot;
            trail.tipLocal = new Vector3(0f, bounds.max.y, 0f);
            trail.baseLocal = new Vector3(0f, bounds.max.y * 0.22f, 0f);
            trail.color = color;
            trail.color.a = 0.95f;
            return trail;
        }

        private void Awake()
        {
            view = ParticleFx.Instantiate(ParticleFx.SwordTrail);
            if (view == null)
                return;
            view.name = "WeaponTrail";
            view.layer = Debris.Layer;
            band = view.GetComponent<ParticleSystem>();
            var child = view.transform.Find("Sparks");
            sparks = child != null ? child.GetComponent<ParticleSystem>() : null;
            if (band != null)
                band.Play(true);
        }

        private void OnDestroy()
        {
            if (view != null)
                Destroy(view);
        }

        private void LateUpdate()
        {
            if (Emitting && blade != null && band != null)
            {
                var b = blade.TransformPoint(baseLocal);
                var t = blade.TransformPoint(tipLocal);
                if (MaxRadius > 0f && pivot != null)
                {
                    // Обрезаем след по дальности эффекта: остриё дальше радиуса подтягивается к нему.
                    var c = pivot.position;
                    var reach = Vector3.Distance(c, t);
                    if (reach > MaxRadius)
                    {
                        var along = (t - b).magnitude;
                        var cut = Mathf.Clamp01((reach - MaxRadius) / Mathf.Max(0.001f, along));
                        t = Vector3.Lerp(t, b, cut);
                    }
                }

                if (hasLast)
                    EmitArc(lastBase, lastTip, b, t);
                lastBase = b;
                lastTip = t;
                hasLast = true;
            }
            else
            {
                hasLast = false;
            }
        }

        /// <summary>Испустить частицы по дуге от прошлого кадра до текущего вокруг центра персонажа.</summary>
        private void EmitArc(Vector3 b0, Vector3 t0, Vector3 b1, Vector3 t1)
        {
            var center = pivot != null ? pivot.position : (b0 + b1) * 0.5f;
            var angle = Vector3.Angle(t0 - center, t1 - center);
            // Почти неподвижный клинок (фиксация позы) не оставляет следа.
            if (angle < 0.5f && (t1 - t0).sqrMagnitude < 0.0004f)
                return;
            var steps = Mathf.Clamp(Mathf.CeilToInt(angle / 4f), 1, 16);
            var emit = new ParticleSystem.EmitParams { startColor = color, startLifetime = Lifetime, applyShapeToPosition = false };
            for (int i = 1; i <= steps; i++)
            {
                var k = i / (float)steps;
                var b = SlerpAround(center, b0, b1, k);
                var t = SlerpAround(center, t0, t1, k);
                var length = Vector3.Distance(b, t);
                for (int j = 0; j < PointsAlongBlade; j++)
                {
                    // От рукояти к острию: у острия частицы крупнее и плотнее, к рукояти след растворяется.
                    var along = (j + 0.5f) / PointsAlongBlade;
                    emit.position = Vector3.Lerp(b, t, along);
                    emit.startSize = length / PointsAlongBlade * (2.2f + 1.6f * along);
                    var c = color;
                    c.a *= Mathf.SmoothStep(0.15f, 1f, along);
                    emit.startColor = c;
                    band.Emit(emit, 1);
                }

                if (sparks != null && Random.value < SparkChance)
                {
                    var spark = new ParticleSystem.EmitParams
                    {
                        position = t,
                        velocity = (t - center).normalized * Random.Range(0.5f, 1.5f) + Random.insideUnitSphere * 0.8f,
                        startColor = Color.Lerp(color, Color.white, 0.4f),
                        applyShapeToPosition = false
                    };
                    sparks.Emit(spark, 1);
                }
            }
        }

        private static Vector3 SlerpAround(Vector3 center, Vector3 a, Vector3 b, float k)
        {
            var da = a - center;
            var db = b - center;
            var dir = Vector3.Slerp(da.normalized, db.normalized, k);
            return center + dir * Mathf.Lerp(da.magnitude, db.magnitude, k);
        }
    }
}
