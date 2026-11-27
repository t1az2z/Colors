using System.IO;
using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Собирает префабы эффектов на Particle System (Resources/Fx): след меча, вспышку заряда, удар по земле.
    /// Префабы потом можно править руками в инспекторе — повторная сборка их перезапишет.
    /// Цвет оружия задаётся в игре через startColor (см. <see cref="ParticleFx"/>): в префабах цвета белые/светлые.
    /// </summary>
    public static class FxPrefabBuilder
    {
        private const string Root = "Assets/Game";
        private const string FxArt = Root + "/Art/Fx";
        private const string FxPrefabs = Root + "/Resources/Fx";

        [MenuItem("ColorsAction/Build FX Prefabs")]
        public static void Build()
        {
            EnsureFolder(Root + "/Art", "Fx");
            EnsureFolder(Root + "/Resources", "Fx");

            var dot = EnsureTexture("SoftDot", (u, v) =>
            {
                // Плотная середина и мягкий край: пятна сливаются в сплошную полосу.
                var d = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                return Mathf.Sqrt(d * d * (3f - 2f * d));
            });
            var trailDot = EnsureTexture("TrailDot", (u, v) =>
            {
                // Сплошной круг с узким сглаженным краем: перекрываясь, пятна дают полосу с чётким контуром.
                var r = Mathf.Sqrt(u * u + v * v);
                return Mathf.Clamp01((0.96f - r) / 0.1f);
            });
            var ring = EnsureTexture("Ring", (u, v) =>
            {
                var r = Mathf.Sqrt(u * u + v * v);
                // Кольцо с резким внешним краем и мягким хвостом внутрь.
                var outer = Mathf.Clamp01((0.98f - r) / 0.05f);
                var inner = Mathf.Clamp01((r - 0.45f) / 0.4f);
                return outer * Mathf.Sqrt(inner);
            });

            var soft = EnsureMaterial("FxSoft", dot, new Color(1.6f, 1.6f, 1.6f, 1f));
            var ringMat = EnsureMaterial("FxRing", ring, new Color(2.4f, 2.4f, 2.4f, 1f));
            var solid = EnsureMaterial("FxSolid", null, new Color(1.4f, 1.4f, 1.4f, 1f));
            var trail = EnsureMaterial("FxTrail", trailDot, new Color(1.6f, 1.6f, 1.6f, 1f));
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            Save(BuildSwordTrail(trail, solid, cube), "SwordTrail");
            Save(BuildChargeFlash(soft, solid, cube), "ChargeFlash");
            Save(BuildGroundSlam(soft, ringMat, solid, cube), "GroundSlam");
            Save(BuildHitFlash(soft, solid, cube), "HitFlash");
            AssetDatabase.SaveAssets();
            Debug.Log("ColorsAction: FX prefabs built in " + FxPrefabs);
        }

        // ------------------------------------------------------------------ след меча

        /// <summary>
        /// След: частицы испускает <see cref="WeaponTrail"/> вручную (Emit) вдоль клинка, модуль Emission выключен.
        /// Пятна с чётким краем (TrailDot) — полоса следа и яркая кромка по острию, мелкие кубики — искры с острия.
        /// </summary>
        private static GameObject BuildSwordTrail(Material trail, Material solid, Mesh cube)
        {
            var go = new GameObject("SwordTrail");
            var band = go.AddComponent<ParticleSystem>();
            Setup(band, trail, loop: true, unscaled: false, maxParticles: 2500);
            var main = band.main;
            main.startLifetime = 0.2f;
            main.startSize = 0.22f;
            main.startSpeed = 0f;
            Emission(band, 0f);
            // Пятна почти не сжимаются, след гаснет прозрачностью: контур не «оплывает» к концу жизни.
            SizeOverLifetime(band, Curve(1f, 0.75f));
            ColorOverLifetime(band, Fade(1f, 0.85f, 0f));

            var sparks = Child(go, "Sparks");
            Setup(sparks, solid, loop: true, unscaled: false, maxParticles: 200);
            main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.07f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
            main.gravityModifier = 1.2f;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Emission(sparks, 0f);
            SizeOverLifetime(sparks, Curve(1f, 0f));
            MeshRender(sparks, cube);
            return go;
        }

        // ------------------------------------------------------------------ вспышка заряда

        /// <summary>Вспышка на пике заряда: яркое ядро, лучи-штрихи во все стороны, кубики-искры. Время реальное.</summary>
        private static GameObject BuildChargeFlash(Material soft, Material solid, Mesh cube)
        {
            var go = new GameObject("ChargeFlash");
            var core = go.AddComponent<ParticleSystem>();
            Setup(core, soft, loop: false, unscaled: true, maxParticles: 4);
            var main = core.main;
            main.startLifetime = 0.18f;
            main.startSize = 1.7f;
            main.startSpeed = 0f;
            Burst(core, 1);
            SizeOverLifetime(core, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.2f)));
            ColorOverLifetime(core, Fade(1f, 1f, 0f));

            var rays = Child(go, "Rays");
            Setup(rays, soft, loop: false, unscaled: true, maxParticles: 32);
            main = rays.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 11f);
            Burst(rays, 14);
            Shape(rays, ParticleSystemShapeType.Sphere, 0.05f);
            Drag(rays, 6f);
            ColorOverLifetime(rays, Fade(1f, 0.9f, 0f));
            var r = rays.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.07f;
            r.lengthScale = 2f;

            var sparks = Child(go, "Sparks");
            Setup(sparks, solid, loop: false, unscaled: true, maxParticles: 32);
            main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.gravityModifier = 0.8f;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Burst(sparks, 10);
            Shape(sparks, ParticleSystemShapeType.Sphere, 0.1f);
            Drag(sparks, 3f);
            SizeOverLifetime(sparks, Curve(1f, 0f));
            MeshRender(sparks, cube);
            return go;
        }

        // ------------------------------------------------------------------ вспышка попадания

        /// <summary>
        /// Тёплая белая вспышка-звезда (парирование, удар по цепи, дуло дробовика, лечение): ядро, короткие лучи, искры.
        /// Цвет задан в префабе (в игре не перекрашивается — tint белый). Время реальное — видна и во время фриз-кадров.
        /// </summary>
        private static GameObject BuildHitFlash(Material soft, Material solid, Mesh cube)
        {
            var warm = new Color(1f, 0.8f, 0.38f, 1f);
            var go = new GameObject("HitFlash");
            var core = go.AddComponent<ParticleSystem>();
            Setup(core, soft, loop: false, unscaled: true, maxParticles: 4);
            var main = core.main;
            main.startLifetime = 0.2f;
            main.startSize = 1.1f;
            main.startSpeed = 0f;
            main.startColor = warm;
            Burst(core, 1);
            SizeOverLifetime(core, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0.1f)));
            ColorOverLifetime(core, Fade(1f, 1f, 0f));

            var rays = Child(go, "Rays");
            Setup(rays, soft, loop: false, unscaled: true, maxParticles: 16);
            main = rays.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startColor = warm;
            Burst(rays, 8);
            Shape(rays, ParticleSystemShapeType.Sphere, 0.05f);
            Drag(rays, 8f);
            ColorOverLifetime(rays, Fade(1f, 0.9f, 0f));
            var r = rays.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 2f;

            var sparks = Child(go, "Sparks");
            Setup(sparks, solid, loop: false, unscaled: true, maxParticles: 16);
            main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startColor = warm;
            main.gravityModifier = 0.8f;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Burst(sparks, 6);
            Shape(sparks, ParticleSystemShapeType.Sphere, 0.08f);
            Drag(sparks, 3f);
            SizeOverLifetime(sparks, Curve(1f, 0f));
            MeshRender(sparks, cube);
            return go;
        }

        // ------------------------------------------------------------------ удар по земле

        /// <summary>
        /// Удар оружием по земле, рассчитан на радиус 1 (в игре масштабируется под радиус удара).
        /// Кольцо волны лежит на земле, вспышка в точке удара, кубики-обломки разлетаются вверх и в стороны, пыль. Время реальное.
        /// </summary>
        private static GameObject BuildGroundSlam(Material soft, Material ringMat, Material solid, Mesh cube)
        {
            var go = new GameObject("GroundSlam");
            var ring = go.AddComponent<ParticleSystem>();
            Setup(ring, ringMat, loop: false, unscaled: true, maxParticles: 2);
            var main = ring.main;
            main.startLifetime = 0.32f;
            main.startSize = 2.2f; // диаметр при радиусе 1, с запасом на мягкий край
            main.startSpeed = 0f;
            Burst(ring, 1);
            SizeOverLifetime(ring, new AnimationCurve(new Keyframe(0f, 0.25f, 0f, 3f), new Keyframe(0.6f, 0.95f), new Keyframe(1f, 1f)));
            ColorOverLifetime(ring, Fade(1f, 0.8f, 0f));
            var rr = ring.GetComponent<ParticleSystemRenderer>();
            // Плашмя на земле: частица в плоскости XZ мира.
            rr.alignment = ParticleSystemRenderSpace.World;
            main.startRotation3D = true;
            main.startRotationX = Mathf.PI * 0.5f;
            ring.transform.localPosition = Vector3.up * 0.06f;

            var flash = Child(go, "Flash");
            Setup(flash, soft, loop: false, unscaled: true, maxParticles: 2);
            main = flash.main;
            main.startLifetime = 0.14f;
            main.startSize = 1.3f;
            main.startSpeed = 0f;
            Burst(flash, 1);
            SizeOverLifetime(flash, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.3f)));
            ColorOverLifetime(flash, Fade(1f, 1f, 0f));
            flash.transform.localPosition = Vector3.up * 0.25f;

            var debris = Child(go, "Debris");
            Setup(debris, solid, loop: false, unscaled: true, maxParticles: 64);
            main = debris.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.gravityModifier = 2.2f;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Burst(debris, 18);
            // Конус вверх: обломки летят веером вверх и в стороны.
            var shape = debris.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = 0.25f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            SizeOverLifetime(debris, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0f)));
            var rot = debris.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = rot.y = rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            MeshRender(debris, cube);

            var dust = Child(go, "Dust");
            Setup(dust, soft, loop: false, unscaled: true, maxParticles: 32);
            main = dust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startColor = new Color(1f, 1f, 1f, 0.55f);
            Burst(dust, 16);
            var ds = dust.shape;
            ds.enabled = true;
            ds.shapeType = ParticleSystemShapeType.Circle;
            ds.radius = 0.2f;
            ds.rotation = new Vector3(90f, 0f, 0f); // круг в плоскости земли
            ds.radiusThickness = 0f;
            Drag(dust, 5f);
            SizeOverLifetime(dust, Curve(0.6f, 1.3f));
            ColorOverLifetime(dust, Fade(1f, 0.6f, 0f));
            dust.transform.localPosition = Vector3.up * 0.15f;
            return go;
        }

        // ------------------------------------------------------------------ общие настройки

        private static void Setup(ParticleSystem ps, Material material, bool loop, bool unscaled, int maxParticles)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = loop ? 1f : 0.6f;
            main.loop = loop;
            main.playOnAwake = false;
            main.useUnscaledTime = unscaled;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = maxParticles;
            main.startColor = Color.white;
            main.stopAction = ParticleSystemStopAction.None;
            var shape = ps.shape;
            shape.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.YoungestInFront;
        }

        private static ParticleSystem Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go.AddComponent<ParticleSystem>();
        }

        private static void Emission(ParticleSystem ps, float rate)
        {
            var e = ps.emission;
            e.enabled = rate > 0f;
            e.rateOverTime = rate;
        }

        private static void Burst(ParticleSystem ps, int count)
        {
            var e = ps.emission;
            e.enabled = true;
            e.rateOverTime = 0f;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        private static void Shape(ParticleSystem ps, ParticleSystemShapeType type, float radius)
        {
            var s = ps.shape;
            s.enabled = true;
            s.shapeType = type;
            s.radius = radius;
        }

        private static void Drag(ParticleSystem ps, float drag)
        {
            var l = ps.limitVelocityOverLifetime;
            l.enabled = true;
            l.drag = drag;
            l.multiplyDragByParticleSize = false;
            l.multiplyDragByParticleVelocity = false;
        }

        private static void SizeOverLifetime(ParticleSystem ps, AnimationCurve curve)
        {
            var s = ps.sizeOverLifetime;
            s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static void ColorOverLifetime(ParticleSystem ps, Gradient gradient)
        {
            var c = ps.colorOverLifetime;
            c.enabled = true;
            c.color = gradient;
        }

        private static void MeshRender(ParticleSystem ps, Mesh mesh)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = mesh;
            r.alignment = ParticleSystemRenderSpace.World;
        }

        private static AnimationCurve Curve(float from, float to) => AnimationCurve.Linear(0f, from, 1f, to);

        /// <summary>Альфа: начало, середина, конец.</summary>
        private static Gradient Fade(float a0, float a1, float a2)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, 0.4f), new GradientAlphaKey(a2, 1f) });
            return g;
        }

        // ------------------------------------------------------------------ ассеты

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>Белая текстура с альфой по функции от координат (-1..1).</summary>
        private static Texture2D EnsureTexture(string name, System.Func<float, float, float> alpha)
        {
            var path = $"{FxArt}/{name}.png";
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var u = (x + 0.5f) / size * 2f - 1f;
                var v = (y + 0.5f) / size * 2f - 1f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha(u, v)));
            }

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material EnsureMaterial(string name, Texture2D texture, Color color)
        {
            var path = $"{FxArt}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("ColorsAction/Particle")) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = Shader.Find("ColorsAction/Particle");
            mat.renderQueue = 3000;
            mat.SetTexture("_MainTex", texture);
            mat.SetColor("_Color", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void Save(GameObject go, string name)
        {
            PrefabUtility.SaveAsPrefabAsset(go, $"{FxPrefabs}/{name}.prefab");
            Object.DestroyImmediate(go);
        }
    }
}
