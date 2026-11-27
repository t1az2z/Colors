using System;
using ColorsAction.Configs;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Искривление мира к горизонту. Картинку изгибают шейдеры (Art/Shaders/CurvedWorld.hlsl: ColorsAction/Lit, /Unlit,
    /// Particle, WeaponTrail, Silhouette); физика, навигация и логика остаются плоскими. Здесь — та же формула для
    /// кода, которому нужен экран: метки HUD над миром (<see cref="Bend"/>) и луч курсора (<see cref="Raycast"/>).
    /// Параметры — Camera.json → curvedWorld. Изгиб включается только для игровой камеры; превью персонажа и окно
    /// сцены рисуются плоскими.
    /// </summary>
    public sealed class CurvedWorld : IDisposable
    {
        private static readonly int CenterId = Shader.PropertyToID("_CurvedWorldCenter");
        private static readonly int ParamsId = Shader.PropertyToID("_CurvedWorldParams");

        /// <summary>Кривизна, 1/м (0 — мира не изгибается).</summary>
        public static float Curvature { get; set; }
        public static float FlatRadius { get; set; }
        /// <summary>Центр изгиба — игрок.</summary>
        public static Vector3 Center { get; private set; }
        public static bool Active => Curvature > 0f;

        /// <summary>URP Lit/Unlit с изгибом (те же свойства; генератор — Tools/Shaders/make_curved_urp_shaders.py).</summary>
        public const string LitShader = "ColorsAction/Lit";
        public const string UnlitShader = "ColorsAction/Unlit";

        public static Shader FindLit() => Shader.Find(LitShader) ?? Shader.Find("Universal Render Pipeline/Lit");
        public static Shader FindUnlit() => Shader.Find(UnlitShader) ?? Shader.Find("Universal Render Pipeline/Unlit");

        private readonly CurvedWorldConfig config;
        private readonly Transform center;
        private Camera camera;

        public CurvedWorld(CurvedWorldConfig config, Transform center)
        {
            this.config = config ?? new CurvedWorldConfig();
            this.center = center;
            Curvature = Mathf.Max(0f, this.config.Curvature);
            FlatRadius = Mathf.Max(0f, this.config.FlatRadius);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (camera != null)
                camera.ResetCullingMatrix();
            Curvature = 0f;
            SetGlobals(0f);
        }

        /// <summary>Каждый кадр до рендера (центр нужен и коду прицеливания в Update).</summary>
        public void Tick()
        {
            if (center != null)
                Center = center.position;
        }

        /// <summary>На сколько опущена точка мира на экране.</summary>
        public static float Drop(Vector3 world)
        {
            if (Curvature <= 0f)
                return 0f;
            var dx = world.x - Center.x;
            var dz = world.z - Center.z;
            var r = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - FlatRadius);
            return Curvature * r * r;
        }

        /// <summary>Где точка плоского мира видна на экране (для WorldToScreenPoint).</summary>
        public static Vector3 Bend(Vector3 world) => world - new Vector3(0f, Drop(world), 0f);

        /// <summary>Обратное к <see cref="Bend"/>: изгиб не сдвигает по горизонтали, поэтому подъём точный.</summary>
        public static Vector3 Unbend(Vector3 shown) => shown + new Vector3(0f, Drop(shown), 0f);

        /// <summary>
        /// Луч от камеры по изогнутой картинке: в плоском мире он превращается в кривую (каждая точка поднята
        /// на <see cref="Drop"/>), её и проверяем короткими отрезками.
        /// </summary>
        public static bool Raycast(Ray ray, float maxDistance, int mask, out RaycastHit hit)
        {
            if (!Active)
                return Physics.Raycast(ray, out hit, maxDistance, mask, QueryTriggerInteraction.Ignore);
            const float step = 1f;
            var prev = Unbend(ray.origin);
            for (var t = step; t <= maxDistance; t += step)
            {
                var next = Unbend(ray.GetPoint(t));
                var seg = next - prev;
                if (Physics.Raycast(prev, seg, out hit, seg.magnitude, mask, QueryTriggerInteraction.Ignore))
                    return true;
                prev = next;
            }
            hit = default;
            return false;
        }

        /// <summary>Пересечение изогнутого луча с горизонтальной плоскостью плоского мира на высоте <paramref name="height"/>.</summary>
        public static bool RaycastPlane(Ray ray, float height, float maxDistance, out Vector3 point)
        {
            point = default;
            if (!Active)
            {
                var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
                if (!plane.Raycast(ray, out var enter))
                    return false;
                point = ray.GetPoint(enter);
                return true;
            }

            float Above(float t) => Unbend(ray.GetPoint(t)).y - height;
            const float step = 1f;
            var a = 0f;
            var fa = Above(a);
            if (fa <= 0f)
                return false;
            for (var b = step; b <= maxDistance; b += step)
            {
                var fb = Above(b);
                if (fb <= 0f)
                {
                    // Корень между a и b — делим пополам.
                    for (var i = 0; i < 12; i++)
                    {
                        var m = (a + b) * 0.5f;
                        if (Above(m) > 0f) a = m; else b = m;
                    }
                    point = Unbend(ray.GetPoint(b));
                    point.y = height;
                    return true;
                }
                a = b;
            }
            return false;
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (camera == null)
                camera = Camera.main;
            var game = cam == camera && cam.cameraType == CameraType.Game;
            SetGlobals(game ? Curvature : 0f);
            if (game)
                UpdateCulling(cam);
        }

        private static void SetGlobals(float curvature)
        {
            Shader.SetGlobalVector(CenterId, Center);
            Shader.SetGlobalVector(ParamsId, new Vector4(curvature, FlatRadius, 0f, 0f));
        }

        /// <summary>
        /// Unity отсекает объекты по плоским границам, а изогнутые дальние куски опускаются в кадр сверху:
        /// пирамиду отсечения расширяем вверх (и немного в стороны).
        /// </summary>
        private void UpdateCulling(Camera cam)
        {
            var extra = Curvature > 0f ? Mathf.Clamp(config.CullingExtraAngle, 0f, 80f) : 0f;
            if (extra <= 0f || cam.orthographic)
            {
                cam.ResetCullingMatrix();
                return;
            }
            var near = cam.nearClipPlane;
            var half = cam.fieldOfView * 0.5f;
            var bottom = -near * Mathf.Tan(half * Mathf.Deg2Rad);
            var top = near * Mathf.Tan(Mathf.Min(half + extra, 85f) * Mathf.Deg2Rad);
            var side = near * Mathf.Tan(half * Mathf.Deg2Rad) * cam.aspect * 1.15f;
            var projection = Matrix4x4.Frustum(-side, side, bottom, top, near, cam.farClipPlane);
            cam.cullingMatrix = projection * cam.worldToCameraMatrix;
        }
    }
}
