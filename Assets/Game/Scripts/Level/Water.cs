using ColorsAction.Configs;
using ColorsAction.Level;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Лоу-поли вода вокруг уровня (Configs/Water.json): сетка треугольников со сдвинутыми вершинами под всей картой
    /// и далеко за её краем, шейдер ColorsAction/Water качает её волнами и изгибает вместе с миром. Коллайдера нет —
    /// упавший игрок тонет по <see cref="PlayerController.KillHeight"/> (её поднимает <see cref="LevelBuilder"/>
    /// до <see cref="WaterLayout.DrownHeight"/>). Здесь только брызги, когда игрок уходит под воду.
    /// </summary>
    public sealed class Water : MonoBehaviour
    {
        /// <summary>Материал с шейдером ColorsAction/Water (держит шейдер в сборке).</summary>
        public const string MaterialResource = "Water/Water";

        private static readonly int ShallowId = Shader.PropertyToID("_ShallowColor");
        private static readonly int DeepId = Shader.PropertyToID("_DeepColor");
        private static readonly int FoamId = Shader.PropertyToID("_FoamColor");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int DepthFadeId = Shader.PropertyToID("_DepthFade");
        private static readonly int FoamWidthId = Shader.PropertyToID("_FoamWidth");
        private static readonly int WaveHeightId = Shader.PropertyToID("_WaveHeight");
        private static readonly int WaveLengthId = Shader.PropertyToID("_WaveLength");
        private static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
        private static readonly int WaveDirectionId = Shader.PropertyToID("_WaveDirection");
        private static readonly int SpecularId = Shader.PropertyToID("_Specular");
        private static readonly int FacetsId = Shader.PropertyToID("_Facets");

        private WaterConfig config;
        private WaterLayout layout;
        private Transform player;
        private Material material;
        private Mesh mesh;
        private bool wasAbove = true;

        public WaterLayout Layout => layout;
        public Material Material => material;

        public void Init(WaterConfig config, WaterLayout layout, Transform player)
        {
            this.config = config;
            this.layout = layout;
            this.player = player;
            transform.position = new Vector3(0f, layout.Surface, 0f);

            mesh = BuildMesh(config, layout);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            material = CreateMaterial(config);
            r.sharedMaterial = material;
        }

        private static Material CreateMaterial(WaterConfig config)
        {
            var source = Resources.Load<Material>(MaterialResource);
            var m = source != null ? new Material(source) : new Material(Shader.Find("ColorsAction/Water"));
            m.name = "Water (runtime)";
            m.SetColor(ShallowId, Hex(config.ShallowColor, new Color(0.56f, 0.85f, 0.89f)));
            m.SetColor(DeepId, Hex(config.DeepColor, new Color(0.25f, 0.56f, 0.71f)));
            m.SetColor(FoamId, Hex(config.FoamColor, Color.white));
            m.SetFloat(OpacityId, config.Opacity);
            m.SetFloat(DepthFadeId, config.DepthFade);
            m.SetFloat(FoamWidthId, config.FoamWidth);
            m.SetFloat(WaveHeightId, config.WaveHeight);
            m.SetFloat(WaveLengthId, config.WaveLength);
            m.SetFloat(WaveSpeedId, config.WaveSpeed);
            m.SetFloat(WaveDirectionId, config.WaveDirection);
            m.SetFloat(SpecularId, config.Specular);
            m.SetFloat(FacetsId, config.Facets);
            return m;
        }

        private static Color Hex(string hex, Color fallback) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;

        /// <summary>
        /// Плоская сетка в локальных координатах (y = 0 — поверхность): вершины решётки сдвинуты на случайную долю клетки
        /// (детерминированно по индексу), диагональ квадрата чередуется — грани получаются разной формы, без «тетрадки».
        /// </summary>
        private static Mesh BuildMesh(WaterConfig config, WaterLayout layout)
        {
            var cell = config.CellSize;
            var nx = Mathf.Max(1, Mathf.CeilToInt((layout.MaxX - layout.MinX) / cell));
            var nz = Mathf.Max(1, Mathf.CeilToInt((layout.MaxZ - layout.MinZ) / cell));
            var vertices = new Vector3[(nx + 1) * (nz + 1)];
            var jitter = config.Jitter * cell;
            for (int z = 0, i = 0; z <= nz; z++)
            for (int x = 0; x <= nx; x++, i++)
            {
                var px = layout.MinX + x * cell;
                var pz = layout.MinZ + z * cell;
                // Край сетки не сдвигаем — прямоугольник ровный.
                if (x > 0 && x < nx && z > 0 && z < nz)
                {
                    px += (Hash(x, z, 0) - 0.5f) * 2f * jitter;
                    pz += (Hash(x, z, 1) - 0.5f) * 2f * jitter;
                }
                vertices[i] = new Vector3(px, 0f, pz);
            }

            var triangles = new int[nx * nz * 6];
            for (int z = 0, t = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                var a = z * (nx + 1) + x;
                var b = a + 1;
                var c = a + nx + 1;
                var d = c + 1;
                if (((x + z) & 1) == 0)
                {
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = d;
                    triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                }
                else
                {
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }

            var mesh = new Mesh { name = "Water", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            // Волны поднимают и опускают вершины в шейдере — границы с запасом по высоте.
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(0f, config.WaveHeight * 4f + 1f, 0f));
            mesh.bounds = bounds;
            return mesh;
        }

        private static float Hash(int x, int z, int salt)
        {
            unchecked
            {
                var h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private void Update()
        {
            if (player == null || config == null || !config.Splash)
                return;
            var p = player.position;
            var above = p.y > layout.Surface;
            if (wasAbove && !above && Inside(p))
                ParticleFx.Play(ParticleFx.GroundSlam, new Vector3(p.x, layout.Surface + 0.05f, p.z), material.GetColor(FoamId), 1.2f);
            wasAbove = above;
        }

        private bool Inside(Vector3 p) => p.x > layout.MinX && p.x < layout.MaxX && p.z > layout.MinZ && p.z < layout.MaxZ;

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
            if (material != null)
                Destroy(material);
        }
    }
}
