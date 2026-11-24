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
        /// Верх столбика — всегда; боковые грани — только там, где сосед ниже (от его высоты до своей).
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
                var h = config.HeightAt(x, z);
                var bottom = -config.Depth;

                // Верх.
                Quad(verts, normals, uvs, tris,
                    new Vector3(x, h, z), new Vector3(x, h, z + 1), new Vector3(x + 1, h, z + 1), new Vector3(x + 1, h, z),
                    Vector3.up, new Vector2(x, z), new Vector2(1, 1));

                // Бока: -X, +X, -Z, +Z.
                Side(config, verts, normals, uvs, tris, x, z, h, bottom, -1, 0);
                Side(config, verts, normals, uvs, tris, x, z, h, bottom, 1, 0);
                Side(config, verts, normals, uvs, tris, x, z, h, bottom, 0, -1);
                Side(config, verts, normals, uvs, tris, x, z, h, bottom, 0, 1);
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

        private static void Side(LevelConfig config, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t,
            int x, int z, float h, float bottom, int dx, int dz)
        {
            var nx = x + dx;
            var nz = z + dz;
            float neighbour = config.InBounds(nx, nz) ? config.HeightAt(nx, nz) : bottom;
            if (neighbour >= h)
                return;
            var normal = new Vector3(dx, 0, dz);
            float lo = neighbour, hi = h;
            // Отрезок грани на стороне соседа.
            Vector3 a, b;
            if (dx == -1) { a = new Vector3(x, 0, z + 1); b = new Vector3(x, 0, z); }
            else if (dx == 1) { a = new Vector3(x + 1, 0, z); b = new Vector3(x + 1, 0, z + 1); }
            else if (dz == -1) { a = new Vector3(x, 0, z); b = new Vector3(x + 1, 0, z); }
            else { a = new Vector3(x + 1, 0, z + 1); b = new Vector3(x, 0, z + 1); }

            var u0 = dx != 0 ? a.z : a.x;
            Quad(v, n, uv, t,
                new Vector3(a.x, lo, a.z), new Vector3(a.x, hi, a.z), new Vector3(b.x, hi, b.z), new Vector3(b.x, lo, b.z),
                normal, new Vector2(dx != 0 ? z : x, lo), new Vector2(1, hi - lo));
            _ = u0;
        }

        private static void Quad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t,
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Vector2 uvOrigin, Vector2 uvSize)
        {
            var i = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            for (int k = 0; k < 4; k++)
                n.Add(normal);
            uv.Add(uvOrigin);
            uv.Add(uvOrigin + new Vector2(0, uvSize.y));
            uv.Add(uvOrigin + uvSize);
            uv.Add(uvOrigin + new Vector2(uvSize.x, 0));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        // ------------------------------------------------------------------ объекты

        public Vector3 CellPosition(int x, int z) => new Vector3(x + 0.5f, level.HeightAt(x, z), z + 0.5f);

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
