using System.Collections.Generic;
using System.IO;
using ColorsAction.Configs;
using GameCore.Config;
using GameCore.DI;
using GameCore.Newtonsoft;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Строит уровень из карты высот (<see cref="LevelConfig"/>): столбики кубов 1×1, объединённые в меши по чанкам 16×16
    /// с MeshCollider; ставит игрока на точку появления, раскладывает предметы, манекены и подсказки на полу.
    /// В редакторе показывает превью уровня (не сохраняется в сцену).
    /// </summary>
    [ExecuteAlways]
    public sealed class LevelBuilder : GameBehaviour
    {
        private const int ChunkSize = 16;

        [SerializeField] private Material worldMaterial;
        [SerializeField] private GameObject dummyPrefab;
        [SerializeField] private PlayerController player;
        [SerializeField] private Font hintFont;
        [SerializeField] private string editorPreviewLevel = "Levels/Test";

        private LevelConfig level;
        private GameBootstrap bootstrap;
        private GameInput input;
        private Transform root;
        private readonly List<HintMarker> hints = new List<HintMarker>();

        public LevelConfig Level => level;

        protected override void OnConstruct(IResolver resolver)
        {
            var configs = resolver.Resolve<ConfigService>();
            level = configs.Load<LevelConfig>(configs.Load<GameConfig>(ConfigKeys.Game).Level);
            bootstrap = resolver.Resolve<GameBootstrap>();
            input = resolver.Resolve<GameInput>();
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                BuildEditorPreview();
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                ClearRoot();
#endif
        }

        private void Start()
        {
            if (!Application.isPlaying || level == null)
                return;
            ClearRoot();
            BuildGeometry(level);
            SpawnEntities(level);
        }

        // ------------------------------------------------------------------ геометрия

        private void ClearRoot()
        {
            var existing = transform.Find("Generated");
            if (existing != null)
            {
                if (Application.isPlaying)
                    Destroy(existing.gameObject);
                else
                    DestroyImmediate(existing.gameObject);
            }

            root = null;
        }

        private void BuildGeometry(LevelConfig config)
        {
            root = new GameObject("Generated").transform;
            root.SetParent(transform, false);
            if (!Application.isPlaying)
                root.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;

            for (int cz = 0; cz < config.Length; cz += ChunkSize)
            for (int cx = 0; cx < config.Width; cx += ChunkSize)
            {
                var mesh = BuildChunk(config, cx, cz);
                if (mesh == null)
                    continue;
                var go = new GameObject($"Chunk_{cx / ChunkSize}_{cz / ChunkSize}");
                go.transform.SetParent(root, false);
                go.isStatic = true;
                go.hideFlags = root.gameObject.hideFlags;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = worldMaterial;
                r.shadowCastingMode = ShadowCastingMode.On;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
        }

        /// <summary>
        /// Верх столбика — четырёхугольник по высотам углов (<see cref="LevelConfig.TopCorners"/>): перепады до
        /// SlopeMaxStep становятся слопами. Боковые грани — только там, где сосед ниже, от его кромки до своей.
        /// UV в мировых метрах, текстура рисует тонкую рамку на каждом кубе 1×1.
        /// </summary>
        private static Mesh BuildChunk(LevelConfig config, int cx, int cz)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int z = cz; z < Mathf.Min(cz + ChunkSize, config.Length); z++)
            for (int x = cx; x < Mathf.Min(cx + ChunkSize, config.Width); x++)
            {
                if (config.HeightSteps(x, z) == LevelConfig.Hole)
                    continue;
                config.TopCorners(x, z, out var h00, out var h01, out var h11, out var h10);

                // Верх (возможно, наклонный).
                Top(verts, normals, uvs, tris,
                    new Vector3(x, h00, z), new Vector3(x, h01, z + 1), new Vector3(x + 1, h11, z + 1), new Vector3(x + 1, h10, z));

                // Бока: -X, +X, -Z, +Z. Кромка соседа берётся по его углам на общей стороне.
                Side(config, verts, normals, uvs, tris, x, z, -1, 0, h00, h01);
                Side(config, verts, normals, uvs, tris, x, z, 1, 0, h10, h11);
                Side(config, verts, normals, uvs, tris, x, z, 0, -1, h00, h10);
                Side(config, verts, normals, uvs, tris, x, z, 0, 1, h01, h11);
            }

            if (verts.Count == 0)
                return null;
            var mesh = new Mesh { name = $"LevelChunk_{cx}_{cz}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static void Top(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t,
            Vector3 p00, Vector3 p01, Vector3 p11, Vector3 p10)
        {
            // Диагональ выбираем так, чтобы излом шёл вдоль слопа, а не поперёк (меньше "седла").
            var splitMain = Mathf.Abs(p00.y - p11.y) <= Mathf.Abs(p01.y - p10.y);
            if (splitMain)
            {
                Tri(v, n, uv, t, p00, p01, p11);
                Tri(v, n, uv, t, p00, p11, p10);
            }
            else
            {
                Tri(v, n, uv, t, p00, p01, p10);
                Tri(v, n, uv, t, p01, p11, p10);
            }
        }

        private static void Tri(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c)
        {
            var i = v.Count;
            var normal = Vector3.Cross(b - a, c - a).normalized;
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(normal); n.Add(normal); n.Add(normal);
            uv.Add(new Vector2(a.x, a.z)); uv.Add(new Vector2(b.x, b.z)); uv.Add(new Vector2(c.x, c.z));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        /// <param name="ownA">Высота своего угла в начале стороны (меньшая координата вдоль стороны).</param>
        /// <param name="ownB">Высота своего угла в конце стороны.</param>
        private static void Side(LevelConfig config, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t,
            int x, int z, int dx, int dz, float ownA, float ownB)
        {
            var nx = x + dx;
            var nz = z + dz;
            float nbA, nbB;
            if (config.InBounds(nx, nz) && config.HeightSteps(nx, nz) != LevelConfig.Hole)
            {
                config.TopCorners(nx, nz, out var m00, out var m01, out var m11, out var m10);
                // Общая сторона у соседа — противоположная.
                if (dx == -1) { nbA = m10; nbB = m11; }
                else if (dx == 1) { nbA = m00; nbB = m01; }
                else if (dz == -1) { nbA = m01; nbB = m11; }
                else { nbA = m00; nbB = m10; }
            }
            else
            {
                nbA = nbB = -config.Depth;
            }

            var loA = Mathf.Min(nbA, ownA);
            var loB = Mathf.Min(nbB, ownB);
            if (ownA <= loA + 1e-4f && ownB <= loB + 1e-4f)
                return;

            // Точки стороны: A — меньшая координата вдоль стороны.
            Vector3 a, b;
            if (dx != 0)
            {
                var sx = dx < 0 ? x : x + 1;
                a = new Vector3(sx, 0, z);
                b = new Vector3(sx, 0, z + 1);
            }
            else
            {
                var sz = dz < 0 ? z : z + 1;
                a = new Vector3(x, 0, sz);
                b = new Vector3(x + 1, 0, sz);
            }

            var aLo = new Vector3(a.x, loA, a.z);
            var aHi = new Vector3(a.x, ownA, a.z);
            var bLo = new Vector3(b.x, loB, b.z);
            var bHi = new Vector3(b.x, ownB, b.z);
            var normal = new Vector3(dx, 0, dz);
            var along = dx != 0 ? z : x;

            // Обход по часовой, если смотреть снаружи (с нормали).
            var flip = (dx == -1) || (dz == 1);
            var i = v.Count;
            if (!flip)
            {
                v.Add(aLo); v.Add(aHi); v.Add(bHi); v.Add(bLo);
                uv.Add(new Vector2(along, loA)); uv.Add(new Vector2(along, ownA));
                uv.Add(new Vector2(along + 1, ownB)); uv.Add(new Vector2(along + 1, loB));
            }
            else
            {
                v.Add(bLo); v.Add(bHi); v.Add(aHi); v.Add(aLo);
                uv.Add(new Vector2(along + 1, loB)); uv.Add(new Vector2(along + 1, ownB));
                uv.Add(new Vector2(along, ownA)); uv.Add(new Vector2(along, loA));
            }

            for (int k = 0; k < 4; k++)
                n.Add(normal);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        // ------------------------------------------------------------------ объекты

        public Vector3 CellPosition(int x, int z) => new Vector3(x + 0.5f, level.SurfaceHeight(x, z, 0.5f, 0.5f), z + 0.5f);

        private void SpawnEntities(LevelConfig config)
        {
            if (player != null)
                player.Teleport(CellPosition(config.Spawn.X, config.Spawn.Z), config.Spawn.Yaw);

            foreach (var item in config.Items)
            {
                var go = new GameObject($"Pickup_{item.Item}");
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(CellPosition(item.X, item.Z), Quaternion.Euler(0, item.Yaw, 0));
                go.AddComponent<Pickup>().Init(item.Item);
                bootstrap.Inject(go);
            }

            if (dummyPrefab != null)
            {
                foreach (var d in config.Dummies)
                {
                    var go = Instantiate(dummyPrefab, CellPosition(d.X, d.Z), Quaternion.Euler(0, d.Yaw, 0), root);
                    go.name = "TrainingDummy";
                    bootstrap.Inject(go);
                }
            }

            foreach (var hint in config.Hints)
                hints.Add(HintMarker.Create(root, CellPosition(hint.X, hint.Z), hint.Action, input, hintFont));
        }

#if UNITY_EDITOR
        private void BuildEditorPreview()
        {
            ClearRoot();
            var path = Path.Combine(Application.streamingAssetsPath, "Configs");
            var service = new ConfigService(new FileConfigSource(path), new NewtonsoftConfigSerializer());
            if (!service.Exists(editorPreviewLevel))
                return;
            try
            {
                BuildGeometry(service.Load<LevelConfig>(editorPreviewLevel));
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        [ContextMenu("Rebuild Editor Preview")]
        private void RebuildPreview()
        {
            if (!Application.isPlaying)
                BuildEditorPreview();
        }
#endif
    }
}
