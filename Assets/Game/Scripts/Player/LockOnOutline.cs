using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Белый контур силуэта цели фиксации (<see cref="PlayerLockOn.Target"/>) — вместо прежнего ромба над головой.
    /// На каждый MeshRenderer цели (тело, оружие) вешаются два дочерних рендерера с той же сеткой: метка стенсила
    /// (Resources/Materials/LockOutlineMark, шейдер ColorsAction/OutlineMark) и раздутая оболочка
    /// (LockOutlineHull, ColorsAction/OutlineHull), видимая только снаружи силуэта и сквозь стены.
    /// Тело врага делит рабочую сетку с <see cref="CubeDeformer"/>, поэтому контур повторяет деформацию.
    /// Чтобы у кубов с острыми рёбрами контур не рвался на углах, в TEXCOORD3 пишется сглаженная нормаль
    /// (средняя по вершинам в одной точке). Создаётся бутстрапом (<see cref="Ensure"/>).
    /// </summary>
    public sealed class LockOnOutline : MonoBehaviour
    {
        private const string MarkResource = "Materials/LockOutlineMark";
        private const string HullResource = "Materials/LockOutlineHull";
        private const int SmoothNormalChannel = 3;
        private const float FadeTime = 0.12f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Копии общих сеток (оружие из префабов) со сглаженными нормалями.</summary>
        private static readonly Dictionary<Mesh, Mesh> smoothCopies = new Dictionary<Mesh, Mesh>();

        /// <summary>Рабочие сетки (деформер), в которые нормали уже записаны.</summary>
        private static readonly HashSet<Mesh> smoothedInPlace = new HashSet<Mesh>();

        private PlayerLockOn lockOn;
        private Material mark;
        private Material hull;
        private Color hullColor = Color.white;
        private Component current;
        private float fade;
        private readonly List<Part> parts = new List<Part>();

        private struct Part
        {
            public Renderer Source;
            public GameObject Mark;
            public GameObject Hull;
        }

        /// <summary>Один на сцену.</summary>
        public static LockOnOutline Ensure()
        {
            var existing = FindAnyObjectByType<LockOnOutline>();
            return existing != null ? existing : new GameObject("LockOnOutline").AddComponent<LockOnOutline>();
        }

        private void Awake()
        {
            var markAsset = Resources.Load<Material>(MarkResource);
            var hullAsset = Resources.Load<Material>(HullResource);
            if (markAsset == null || hullAsset == null)
            {
                Debug.LogError("LockOnOutline: нет материалов Resources/Materials/LockOutlineMark и LockOutlineHull");
                enabled = false;
                return;
            }

            mark = markAsset;
            hull = new Material(hullAsset);
            hullColor = hull.GetColor(ColorId);
        }

        private void OnDestroy()
        {
            Clear();
            if (hull != null)
                Destroy(hull);
        }

        private void LateUpdate()
        {
            if (lockOn == null)
                lockOn = FindAnyObjectByType<PlayerLockOn>();
            var target = lockOn != null ? lockOn.Target : null;
            if (target != current)
            {
                Clear();
                current = target;
                fade = 0f;
                if (current != null)
                    Build(current);
            }

            if (current == null)
                return;

            fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / FadeTime);
            var c = hullColor;
            c.a *= fade;
            hull.SetColor(ColorId, c);
            // Спрятанная часть (тело раскололось, оружие убрано) — без контура.
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var on = p.Source != null && p.Source.enabled && p.Source.gameObject.activeInHierarchy;
                if (p.Mark != null && p.Mark.activeSelf != on)
                    p.Mark.SetActive(on);
                if (p.Hull != null && p.Hull.activeSelf != on)
                    p.Hull.SetActive(on);
            }
        }

        private void Build(Component target)
        {
            foreach (var renderer in target.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly || renderer.name.StartsWith("LockOutline"))
                    continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;
                var mesh = SmoothMesh(filter.sharedMesh, renderer.GetComponent<CubeDeformer>());
                parts.Add(new Part
                {
                    Source = renderer,
                    Mark = Child(renderer.transform, "LockOutlineMark", mesh, mark),
                    Hull = Child(renderer.transform, "LockOutlineHull", mesh, hull),
                });
            }
        }

        private static GameObject Child(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = material;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        private void Clear()
        {
            foreach (var p in parts)
            {
                if (p.Mark != null)
                    Destroy(p.Mark);
                if (p.Hull != null)
                    Destroy(p.Hull);
            }

            parts.Clear();
        }

        /// <summary>
        /// Сетка со сглаженными нормалями в TEXCOORD3: рабочая сетка деформера — та же (пишем в неё, чтобы контур
        /// следовал деформации), общая сетка префаба — копия. Нечитаемая сетка остаётся как есть (обычные нормали).
        /// </summary>
        private static Mesh SmoothMesh(Mesh mesh, CubeDeformer deformer)
        {
            if (!mesh.isReadable)
                return mesh;
            if (deformer != null && deformer.Mesh == mesh)
            {
                if (smoothedInPlace.Add(mesh))
                    mesh.SetUVs(SmoothNormalChannel, SmoothNormals(mesh));
                return mesh;
            }

            if (smoothCopies.TryGetValue(mesh, out var copy) && copy != null)
                return copy;
            copy = Instantiate(mesh);
            copy.name = mesh.name + " (outline)";
            copy.SetUVs(SmoothNormalChannel, SmoothNormals(mesh));
            smoothCopies[mesh] = copy;
            return copy;
        }

        private static List<Vector3> SmoothNormals(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var sums = new Dictionary<Vector3Int, Vector3>();
            var keys = new Vector3Int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i] * 1000f;
                var key = new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));
                keys[i] = key;
                var n = i < normals.Length ? normals[i] : Vector3.zero;
                sums[key] = sums.TryGetValue(key, out var s) ? s + n : n;
            }

            var result = new List<Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                var n = sums[keys[i]];
                result.Add(n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.zero);
            }

            return result;
        }
    }
}
