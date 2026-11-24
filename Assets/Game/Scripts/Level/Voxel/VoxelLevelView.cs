using System;
using System.Collections.Generic;
using ColorsAction.Level;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColorsAction.Unity.Voxel
{
    /// <summary>Как показывать этажи выше активного.</summary>
    public enum UpperFloorsMode
    {
        Visible,
        Ghost,
        Hidden
    }

    /// <summary>
    /// Показывает <see cref="VoxelLevel"/>: куски 16×16 клеток отдельно для каждого этажа, перестраиваются только
    /// изменённые куски. В редакторе активный этаж обычный, этажи выше полупрозрачные или скрыты, ниже — затемнены.
    /// </summary>
    [ExecuteAlways]
    public sealed class VoxelLevelView : MonoBehaviour
    {
        public const int ChunkSize = 16;

        [SerializeField] private Material baseMaterial;
        [SerializeField] private bool colliders = true;

        private VoxelLevel level;
        private Func<string, Color> palette;
        private readonly Dictionary<(int cx, int cz, int floor), GameObject> chunks = new Dictionary<(int, int, int), GameObject>();
        private readonly Dictionary<(int cx, int cz, int floor), List<string>> chunkColors = new Dictionary<(int, int, int), List<string>>();
        private readonly HashSet<(int cx, int cz, int floor)> dirty = new HashSet<(int, int, int)>();
        private readonly Dictionary<(string color, int mode), Material> materials = new Dictionary<(string, int), Material>();
        private bool fullRebuild;

        private int activeFloor = -1;
        private UpperFloorsMode upperMode = UpperFloorsMode.Visible;
        private bool darkenBelow;

        public VoxelLevel Level => level;
        public bool HasPendingChanges => fullRebuild || dirty.Count > 0;

        public Material BaseMaterial
        {
            get => baseMaterial;
            set => baseMaterial = value;
        }

        public bool Colliders
        {
            get => colliders;
            set => colliders = value;
        }

        /// <summary>Привязывает уровень; <paramref name="colorOf"/> переводит id цвета палитры в цвет.</summary>
        public void Bind(VoxelLevel newLevel, Func<string, Color> colorOf)
        {
            if (level != null)
            {
                level.BlocksChanged -= OnBlocksChanged;
                level.MarkupChanged -= OnMarkupChanged;
            }

            level = newLevel;
            palette = colorOf;
            ClearMaterials();
            if (level != null)
            {
                level.BlocksChanged += OnBlocksChanged;
                level.MarkupChanged += OnMarkupChanged;
            }

            fullRebuild = true;
            Flush();
        }

        /// <summary>Режим этажей: активный обычный, выше — по режиму, ниже — затемнены (если включено). -1 — все обычные.</summary>
        public void SetFloorDisplay(int active, UpperFloorsMode upper, bool darken)
        {
            if (active == activeFloor && upper == upperMode && darken == darkenBelow)
                return;
            activeFloor = active;
            upperMode = upper;
            darkenBelow = darken;
            foreach (var kv in chunks)
                ApplyMaterials(kv.Key, kv.Value);
        }

        /// <summary>Перестраивает изменённые куски.</summary>
        public void Flush()
        {
            if (level == null)
            {
                ClearChunks();
                return;
            }

            if (fullRebuild)
            {
                fullRebuild = false;
                dirty.Clear();
                ClearChunks();
                var all = new Dictionary<(int, int, int), List<Int3>>();
                foreach (var cell in level.Blocks.Keys)
                {
                    var key = ChunkOf(cell);
                    if (!all.TryGetValue(key, out var list))
                        all[key] = list = new List<Int3>();
                    list.Add(cell);
                }

                foreach (var kv in all)
                    BuildChunk(kv.Key, kv.Value);
                return;
            }

            if (dirty.Count == 0)
                return;
            var keys = new List<(int, int, int)>(dirty);
            dirty.Clear();
            foreach (var key in keys)
                BuildChunk(key);
        }

        private void LateUpdate()
        {
            if (HasPendingChanges)
                Flush();
        }

        private void OnDestroy()
        {
            if (level != null)
            {
                level.BlocksChanged -= OnBlocksChanged;
                level.MarkupChanged -= OnMarkupChanged;
            }

            ClearChunks();
            ClearMaterials();
        }

        // ------------------------------------------------------------------ куски

        public (int cx, int cz, int floor) ChunkOf(Int3 cell) =>
            (FloorDiv(cell.X, ChunkSize), FloorDiv(cell.Z, ChunkSize), level.FloorIndexOf(cell.Y));

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

        private void OnBlocksChanged(IReadOnlyCollection<Int3> cells)
        {
            if (cells.Count > 4096)
            {
                fullRebuild = true;
                return;
            }

            foreach (var c in cells)
            {
                dirty.Add(ChunkOf(c));
                // Соседи могли открыть или закрыть свои грани.
                dirty.Add(ChunkOf(new Int3(c.X + 1, c.Y, c.Z)));
                dirty.Add(ChunkOf(new Int3(c.X - 1, c.Y, c.Z)));
                dirty.Add(ChunkOf(new Int3(c.X, c.Y, c.Z + 1)));
                dirty.Add(ChunkOf(new Int3(c.X, c.Y, c.Z - 1)));
                dirty.Add(ChunkOf(c.Above));
                dirty.Add(ChunkOf(c.Below));
            }
        }

        private int floorsSignature;

        private void OnMarkupChanged()
        {
            // Этажи поменялись — куски по этажам нужно разложить заново.
            var sig = 17;
            foreach (var f in level.Floors)
                sig = sig * 31 + f.BaseY;
            if (sig != floorsSignature)
            {
                floorsSignature = sig;
                fullRebuild = true;
            }
        }

        private void BuildChunk((int cx, int cz, int floor) key, List<Int3> known = null)
        {
            if (chunks.TryGetValue(key, out var old))
            {
                DestroyMesh(old);
                DestroySafe(old);
                chunks.Remove(key);
                chunkColors.Remove(key);
            }

            var (from, to) = level.FloorRange(key.floor);
            var cells = known ?? new List<Int3>();
            int x0 = key.cx * ChunkSize, z0 = key.cz * ChunkSize;
            foreach (var cell in known != null ? (IEnumerable<Int3>)Array.Empty<Int3>() : level.Blocks.Keys)
            {
                if (cell.X >= x0 && cell.X < x0 + ChunkSize && cell.Z >= z0 && cell.Z < z0 + ChunkSize && cell.Y >= from && cell.Y < to)
                    cells.Add(cell);
            }

            if (cells.Count == 0)
                return;
            var result = VoxelMesher.Build(level, cells, c => c.Y >= from && c.Y < to, $"Voxel_{key.cx}_{key.cz}_f{key.floor}");
            if (result == null)
                return;

            var go = new GameObject($"Chunk_{key.cx}_{key.cz}_f{key.floor}");
            go.hideFlags = gameObject.hideFlags;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = result.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.On;
            if (colliders)
                go.AddComponent<MeshCollider>().sharedMesh = result.Mesh;
            chunks[key] = go;
            chunkColors[key] = result.Colors;
            ApplyMaterials(key, go);
        }

        private void ApplyMaterials((int cx, int cz, int floor) key, GameObject go)
        {
            var r = go.GetComponent<MeshRenderer>();
            var mode = 0;
            if (activeFloor >= 0 && key.floor > activeFloor)
            {
                if (upperMode == UpperFloorsMode.Hidden)
                {
                    r.enabled = false;
                    return;
                }

                if (upperMode == UpperFloorsMode.Ghost)
                    mode = 2;
            }
            else if (activeFloor >= 0 && key.floor < activeFloor && darkenBelow)
                mode = 1;

            r.enabled = true;
            r.shadowCastingMode = mode == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
            var colors = chunkColors[key];
            var mats = new Material[colors.Count];
            for (int i = 0; i < colors.Count; i++)
                mats[i] = MaterialFor(colors[i], mode);
            r.sharedMaterials = mats;
        }

        /// <summary>Материал цвета: 0 — обычный, 1 — затемнённый, 2 — полупрозрачный.</summary>
        private Material MaterialFor(string color, int mode)
        {
            if (materials.TryGetValue((color, mode), out var m) && m != null)
                return m;
            var source = baseMaterial != null ? baseMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m = new Material(source) { name = $"Voxel_{color ?? "world"}_{mode}", hideFlags = HideFlags.DontSave };
            var baseColor = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white;
            var c = color == null || palette == null ? baseColor : palette(color);
            if (mode == 1)
                c = Color.Lerp(c, Color.black, 0.55f);
            if (mode == 2)
            {
                c.a = 0.18f;
                MakeTransparent(m);
            }

            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", c);
            m.color = c;
            materials[(color, mode)] = m;
            return m;
        }

        private static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        private void ClearChunks()
        {
            foreach (var go in chunks.Values)
            {
                DestroyMesh(go);
                DestroySafe(go);
            }

            chunks.Clear();
            chunkColors.Clear();
            // Остатки после перезагрузки домена.
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroySafe(transform.GetChild(i).gameObject);
        }

        private void ClearMaterials()
        {
            foreach (var m in materials.Values)
                DestroySafe(m);
            materials.Clear();
        }

        private static void DestroyMesh(GameObject go)
        {
            if (go == null)
                return;
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                DestroySafe(mf.sharedMesh);
        }

        private static void DestroySafe(UnityEngine.Object o)
        {
            if (o == null)
                return;
            if (Application.isPlaying)
                Destroy(o);
            else
                DestroyImmediate(o);
        }
    }
}
