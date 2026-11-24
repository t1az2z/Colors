using System.Collections.Generic;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// След за клинком: лента между рукоятью и остриём, которая живёт <see cref="Lifetime"/> секунд и тает.
    /// Между кадрами точки досэмплируются по дуге вокруг центра персонажа, поэтому быстрый взмах даёт гладкий полукруг.
    /// Время игровое: во время фриз-кадров след замирает вместе с ударом.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class WeaponTrail : MonoBehaviour
    {
        private const string MaterialPath = "Materials/WeaponTrail";
        private const int MaxSamples = 96;

        private struct Sample
        {
            public Vector3 Base;
            public Vector3 Tip;
            public float Time;
        }

        private readonly List<Sample> samples = new List<Sample>();
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> tris = new List<int>();

        private Transform blade;
        private Transform pivot;
        private Vector3 baseLocal;
        private Vector3 tipLocal;
        private Color color;
        private Mesh mesh;
        private GameObject view;
        private bool hasLast;
        private Vector3 lastBase;
        private Vector3 lastTip;

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
            mesh = new Mesh { name = "WeaponTrail" };
            mesh.MarkDynamic();
            view = new GameObject("WeaponTrail");
            view.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = view.AddComponent<MeshRenderer>();
            r.sharedMaterial = Resources.Load<Material>(MaterialPath);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private void OnDestroy()
        {
            if (view != null)
                Destroy(view);
            if (mesh != null)
                Destroy(mesh);
        }

        private void LateUpdate()
        {
            var now = Time.time;
            if (Emitting && blade != null)
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
                    AddArc(lastBase, lastTip, b, t, now);
                else
                    Add(b, t, now);
                lastBase = b;
                lastTip = t;
                hasLast = true;
            }
            else
            {
                hasLast = false;
            }

            while (samples.Count > 0 && now - samples[0].Time > Lifetime)
                samples.RemoveAt(0);
            while (samples.Count > MaxSamples)
                samples.RemoveAt(0);
            Rebuild(now);
        }

        /// <summary>Досэмплировать путь от прошлого кадра до текущего по дуге вокруг центра персонажа.</summary>
        private void AddArc(Vector3 b0, Vector3 t0, Vector3 b1, Vector3 t1, float now)
        {
            var center = pivot != null ? pivot.position : (b0 + b1) * 0.5f;
            var angle = Vector3.Angle(t0 - center, t1 - center);
            var steps = Mathf.Clamp(Mathf.CeilToInt(angle / 6f), 1, 12);
            var prevTime = samples.Count > 0 ? samples[samples.Count - 1].Time : now;
            for (int i = 1; i <= steps; i++)
            {
                var k = i / (float)steps;
                Add(SlerpAround(center, b0, b1, k), SlerpAround(center, t0, t1, k), Mathf.Lerp(prevTime, now, k));
            }
        }

        private static Vector3 SlerpAround(Vector3 center, Vector3 a, Vector3 b, float k)
        {
            var da = a - center;
            var db = b - center;
            var dir = Vector3.Slerp(da.normalized, db.normalized, k);
            return center + dir * Mathf.Lerp(da.magnitude, db.magnitude, k);
        }

        private void Add(Vector3 b, Vector3 t, float time) => samples.Add(new Sample { Base = b, Tip = t, Time = time });

        private void Rebuild(float now)
        {
            mesh.Clear();
            if (samples.Count < 2)
                return;
            verts.Clear();
            colors.Clear();
            uvs.Clear();
            tris.Clear();
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                var life = 1f - Mathf.Clamp01((now - s.Time) / Lifetime);
                var c = color;
                c.a *= Mathf.Sqrt(life);
                verts.Add(s.Base);
                verts.Add(s.Tip);
                colors.Add(c);
                colors.Add(c);
                var u = i / (float)(samples.Count - 1);
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, 1f));
                if (i == 0)
                    continue;
                var v = i * 2;
                tris.Add(v - 2); tris.Add(v - 1); tris.Add(v + 1);
                tris.Add(v - 2); tris.Add(v + 1); tris.Add(v);
            }

            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
