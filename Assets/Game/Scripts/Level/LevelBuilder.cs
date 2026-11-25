using System.Collections.Generic;
using System.IO;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Level;
using GameCore.Config;
using GameCore.DI;
using GameCore.Events;
using GameCore.Newtonsoft;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Строит уровень из карты высот (<see cref="LevelConfig"/>): столбики кубов 1×1, объединённые в меши по чанкам 16×16
    /// с MeshCollider; ставит игрока на точку появления, раскладывает предметы, манекены, подсказки на полу, двери-срезки
    /// и костры, обносит карту невидимыми стенами. Отдых у костра (и смерть игрока) возрождает врагов.
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
        private GameConfig game;
        private GameBootstrap bootstrap;
        private GameInput input;
        private GameSession session;
        private CombatDirector director;
        private Transform root;
        private readonly List<HintMarker> hints = new List<HintMarker>();
        private readonly List<EnemyController> enemies = new List<EnemyController>();
        private readonly List<System.IDisposable> subscriptions = new List<System.IDisposable>();

        public LevelConfig Level => level;

        protected override void OnConstruct(IResolver resolver)
        {
            var configs = resolver.Resolve<ConfigService>();
            game = configs.Load<GameConfig>(ConfigKeys.Game);
            level = configs.Load<LevelConfig>(game.Level);
            bootstrap = resolver.Resolve<GameBootstrap>();
            input = resolver.Resolve<GameInput>();
            session = resolver.Resolve<GameSession>();
            director = resolver.Resolve<CombatDirector>();
            var events = resolver.Resolve<IEventBus>();
            // Отдых у костра (и смерть, если так настроено в Game.json) возрождает всех врагов уровня.
            subscriptions.Add(events.Subscribe<BonfireRested>(RespawnEnemies));
            subscriptions.Add(events.Subscribe<PlayerRespawned>(() =>
            {
                if (game.RespawnEnemiesOnDeath)
                    RespawnEnemies();
            }));
        }

        private void OnDestroy()
        {
            foreach (var s in subscriptions)
                s.Dispose();
            subscriptions.Clear();
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
            {
                var spawn = CellPosition(config.Spawn.X, config.Spawn.Z);
                player.Teleport(spawn, config.Spawn.Yaw);
                player.SetSpawn(spawn, config.Spawn.Yaw);
            }

            var pickups = new Pickup[config.Items.Count];
            for (int i = 0; i < config.Items.Count; i++)
            {
                var item = config.Items[i];
                var go = new GameObject($"Pickup_{item.Item}");
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(CellPosition(item.X, item.Z), Quaternion.Euler(0, item.Yaw, 0));
                pickups[i] = go.AddComponent<Pickup>();
                pickups[i].Init(item.Item);
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

            SpawnEnemies(config);

            // Двери-срезки: открываются только с лицевой стороны (yaw), дальше открыты навсегда.
            var doors = new ShortcutDoor[config.Doors.Count];
            for (int i = 0; i < config.Doors.Count; i++)
            {
                var door = config.Doors[i];
                var go = new GameObject($"ShortcutDoor_{door.Id}");
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(CellPosition(door.X, door.Z), Quaternion.Euler(0, door.Yaw, 0));
                doors[i] = go.AddComponent<ShortcutDoor>();
                doors[i].Init(door.Id, door.Height, worldMaterial);
                bootstrap.Inject(go);
            }

            SpawnHints(config, pickups, doors);

            // Костры: отдых, возрождение врагов, точка возрождения, апгрейды оружия.
            foreach (var b in config.Bonfires)
            {
                var go = new GameObject($"Bonfire_{b.Id}");
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(CellPosition(b.X, b.Z), Quaternion.Euler(0, b.Yaw, 0));
                go.AddComponent<Bonfire>().Init(b.Id, RestPoint(config, b), b.Yaw, worldMaterial);
                bootstrap.Inject(go);
            }

            BuildEdgeWalls(config);
        }

        /// <summary>Враги: тело — тот же скруглённый куб, что у игрока (исходный меш и материалы берём у него).</summary>
        private void SpawnEnemies(LevelConfig config)
        {
            enemies.Clear();
            if (player == null || config.Enemies.Count == 0)
                return;
            var playerMesh = player.Deformer;
            var mesh = playerMesh != null && playerMesh.SourceMesh != null
                ? playerMesh.SourceMesh
                : playerMesh != null ? playerMesh.GetComponent<MeshFilter>().sharedMesh : null;
            var materials = playerMesh != null ? playerMesh.GetComponent<Renderer>().sharedMaterials : null;
            if (mesh == null)
                return;
            foreach (var e in config.Enemies)
                enemies.Add(EnemyController.Spawn(e.Type, CellPosition(e.X, e.Z), e.Yaw, root, bootstrap, mesh, materials));
        }

        /// <summary>
        /// Все враги уровня заново на своих местах и в исходном состоянии (живые убираются, погибшие возвращаются).
        /// Очередь атак <see cref="CombatDirector"/> и фиксация игрока сбрасываются, новые враги не помнят игрока.
        /// </summary>
        public void RespawnEnemies()
        {
            if (!Application.isPlaying || level == null || root == null)
                return;
            if (player != null && player.LockOn != null)
                player.LockOn.Release();
            foreach (var e in enemies)
            {
                if (e == null)
                    continue;
                // Сразу выключаем: Destroy сработает в конце кадра, а старый враг не должен успеть встать в очередь атак.
                e.gameObject.SetActive(false);
                Destroy(e.gameObject);
            }

            enemies.Clear();
            director?.Clear();
            SpawnEnemies(level);
        }

        /// <summary>
        /// Подсказки на полу. Привязанные к предметам и дверям (<see cref="HintTargets"/>) гаснут навсегда, когда всё
        /// подобрано / открыто; выполненные (<see cref="GameSession.CompletedHints"/>) не создаются вовсе.
        /// </summary>
        private void SpawnHints(LevelConfig config, Pickup[] pickups, ShortcutDoor[] doors)
        {
            var itemIndices = new List<int>();
            var doorIndices = new List<int>();
            foreach (var hint in config.Hints)
            {
                if (session != null && session.CompletedHints.Contains(hint.Id))
                    continue;
                var marker = HintMarker.Create(root, CellPosition(hint.X, hint.Z), hint.Action, input, hintFont);
                hints.Add(marker);
                HintTargets.Resolve(config, hint, itemIndices, doorIndices);
                if (itemIndices.Count == 0 && doorIndices.Count == 0)
                    continue;

                var boundPickups = new List<Pickup>();
                foreach (var i in itemIndices)
                    boundPickups.Add(pickups[i]);
                var boundDoors = new List<ShortcutDoor>();
                foreach (var i in doorIndices)
                    boundDoors.Add(doors[i]);
                var id = hint.Id;
                marker.BindTargets(() =>
                {
                    // Подобранный предмет уничтожается (== null), дверь остаётся открытой.
                    foreach (var p in boundPickups)
                        if (p != null && p.Available)
                            return false;
                    foreach (var d in boundDoors)
                        if (d != null && !d.IsOpen)
                            return false;
                    return true;
                }, () => session?.CompletedHints.Add(id), boundPickups.Count == 0 ? "открыть" : null);
            }
        }

        /// <summary>
        /// Где игрок появляется после смерти у костра: соседняя клетка в сторону его поворота (yaw), если на неё можно
        /// встать; иначе — рядом с костром в ту же сторону.
        /// </summary>
        private Vector3 RestPoint(LevelConfig config, LevelBonfire b)
        {
            var yaw = b.Yaw * Mathf.Deg2Rad;
            var dx = Mathf.RoundToInt(Mathf.Sin(yaw));
            var dz = Mathf.RoundToInt(Mathf.Cos(yaw));
            int x = b.X + dx, z = b.Z + dz;
            var own = CellPosition(b.X, b.Z);
            if ((dx != 0 || dz != 0) && config.InBounds(x, z) && config.HeightSteps(x, z) != LevelConfig.Hole)
            {
                var p = CellPosition(x, z);
                if (Mathf.Abs(p.y - own.y) <= 0.3f)
                    return p;
            }

            return own + new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * 1.1f;
        }

        /// <summary>Невидимые стены по внешнему краю карты (<see cref="LevelEdgeWalls"/>): с уровня не упасть.</summary>
        private void BuildEdgeWalls(LevelConfig config)
        {
            var walls = new GameObject("EdgeWalls").transform;
            walls.SetParent(root, false);
            var i = 0;
            foreach (var w in LevelEdgeWalls.Build(config))
            {
                var go = new GameObject($"Wall{i++}");
                go.transform.SetParent(walls, false);
                go.isStatic = true;
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(w.CenterX, w.CenterY, w.CenterZ);
                box.size = new Vector3(w.SizeX, w.SizeY, w.SizeZ);
            }
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
