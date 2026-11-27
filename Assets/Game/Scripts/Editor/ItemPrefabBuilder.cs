using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Предметы по концепту Art/Concepts/Items.png: материалы, префабы в руках (Resources/Items: Sword, Shield, Shotgun,
    /// Club, Key, Flask), предметы на земле (Resources/Items/Pickups: меч со щитом, мушкет, ключ, флакон — лежат на
    /// плитах с травой, над ними луч и искры цвета предмета) и вид сундука (Resources/Props/Chest).
    /// Модели — Art/Models/*.fbx из Tools/Blender/make_models.py (слоты материалов описаны там).
    /// Меню ColorsAction → Items → Rebuild Concept Prefabs перезаписывает все эти префабы (GUID сохраняются).
    /// </summary>
    public static class ItemPrefabBuilder
    {
        private const string Models = "Assets/Game/Art/Models";
        private const string Materials = "Assets/Game/Art/Materials/Items";
        private const string FxArt = "Assets/Game/Art/Fx";
        private const string Items = "Assets/Game/Resources/Items";
        private const string Pickups = Items + "/Pickups";
        private const string Props = "Assets/Game/Resources/Props";

        [MenuItem("ColorsAction/Items/Rebuild Concept Prefabs")]
        public static void Build()
        {
            EnsureFolder(Materials);
            EnsureFolder(Props);
            EnsureFolder(Pickups);

            var colored = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Materials/WeaponColored.mat");
            if (colored == null)
            {
                Debug.LogError("ItemPrefabBuilder: WeaponColored.mat not found (run ColorsAction/Build Prototype Scene once)");
                return;
            }

            var gold = Lit("Gold", "#F0A63A", 0.35f);
            var grip = Lit("Grip", "#5A3A38", 0.2f);
            var white = Lit("Emblem", "#F4F1F4", 0.3f);
            var metal = Lit("Metal", "#A6A2BC", 0.5f, 0.35f);
            var metalDark = Lit("MetalDark", "#6F6D84", 0.5f, 0.35f);
            var dark = Lit("Dark", "#2E2A36", 0.2f);

            var sword = HeldPrefab("Sword", "Sword", new[] { colored, gold, grip, white }, true);
            var shield = HeldPrefab("Shield", "Shield", new[] { colored, metal, white }, true);
            var shotgun = HeldPrefab("Shotgun", "Shotgun", new[] { colored, metal, white, dark, metalDark }, true);
            HeldPrefab("Club", "Club", new[] { Lit("ClubHead", "#7E6068", 0.15f), Lit("ClubGrip", "#45302F", 0.15f),
                Lit("ClubWrap", "#E6D0AE", 0.2f) }, false);
            var key = HeldPrefab("Key", "Key", new[] { KeyMetal() }, false);
            var flask = HeldPrefab("Flask", "Flask", new[]
            {
                colored, Lit("FlaskGlass", "#CDEBDD", 0.8f), Lit("FlaskCollar", "#7FA0A3", 0.4f),
                Lit("FlaskCork", "#B5643A", 0.15f), Lit("FlaskRope", "#C28A52", 0.1f), Lit("FlaskTag", "#EBCFA5", 0.15f),
                Lit("FlaskCross", "#6FB43A", 0.2f), Lit("FlaskBubble", "#B8F07A", 0.6f)
            }, true);


            var baseMats = new[]
            {
                Lit("BaseStone", "#BBAFB7", 0.1f), Lit("BaseGrass", "#B7BE3A", 0.1f), Lit("BasePetal", "#FFFFFF", 0.2f),
                Lit("BaseFlower", "#F2C230", 0.2f), Lit("BaseLeafRed", "#E2452E", 0.2f), Lit("BaseLeafOrange", "#F29A2A", 0.2f)
            };
            var beamMat = BeamMaterial();
            var sparkleMat = SparkleMaterial();
            BuildChest(metal, white, dark);

            // Меч со щитом: меч стоит на навершии, наклонён в сторону и назад, щит прислонён рядом спереди.
            BuildPickup("Sword", baseMats, beamMat, sparkleMat, true, 0f, model =>
            {
                Place(sword, model, new Vector3(0.16f, 0.27f, -0.1f), new Vector3(-10f, 0f, -28f));
                Place(shield, model, new Vector3(-0.2f, 0.58f, 0.15f), new Vector3(-14f, 20f, 0f));
            });
            // Мушкет лежит на боку по диагонали, приклад на плитах, ствол чуть приподнят.
            BuildPickup("Shotgun", baseMats, beamMat, sparkleMat, true, 0f, model =>
                Place(shotgun, model, new Vector3(0f, 0.13f, 0.05f), new Vector3(0f, -55f, -90f)));
            // Ключ лежит плашмя.
            BuildPickup("Key", baseMats, beamMat, sparkleMat, false, 15f, model =>
                Place(key, model, new Vector3(0f, 0.1f, 0.05f), new Vector3(-90f, -35f, 0f)));
            // Флакон стоит на плитах.
            BuildPickup("Flask", baseMats, beamMat, sparkleMat, true, 0f, model =>
            {
                Place(flask, model, new Vector3(0f, 0.045f, 0f), new Vector3(0f, 25f, 0f));
                model.GetChild(0).localScale = Vector3.one * 1.4f;
            });

            AssetDatabase.SaveAssets();
            Debug.Log("ColorsAction: concept item prefabs rebuilt");
        }

        // ------------------------------------------------------------------ в руках

        /// <summary>Префаб модели (Resources/Items/&lt;name&gt;): меш из FBX, материалы по слотам; ItemVisual красит слот 0.</summary>
        private static GameObject HeldPrefab(string name, string meshName, Material[] materials, bool tinted)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = LoadMesh(name + ".fbx", meshName);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            if (tinted)
            {
                var visual = go.AddComponent<ItemVisual>();
                var so = new SerializedObject(visual);
                var list = so.FindProperty("renderers");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = r;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Items}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Металл ключа: материал KeyMetal (на него ссылаются старые префабы), цвет — серебро концепта.</summary>
        private static Material KeyMetal()
        {
            const string path = "Assets/Game/Art/Materials/KeyMetal.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
                return Lit("KeyMetal", "#A6A2BC", 0.5f, 0.35f);
            SetColor(mat, Hex("#B0ACC6"));
            mat.SetFloat("_Metallic", 0.35f);
            mat.SetFloat("_Smoothness", 0.5f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ сундук

        private static void BuildChest(Material metal, Material white, Material dark)
        {
            var mats = new[] { Lit("ChestWood", "#C8473A", 0.15f), metal, white, dark, Lit("ChestGap", "#9A3C30", 0.1f) };
            var root = new GameObject("Chest");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = LoadMesh("Chest.fbx", "ChestBody");
            body.AddComponent<MeshRenderer>().sharedMaterials = mats;
            var lid = new GameObject("Lid");
            lid.transform.SetParent(body.transform, false);
            // Шарнир — заднее верхнее ребро корпуса (размеры как в Chests.json и make_models.py CHEST_*).
            lid.transform.localPosition = new Vector3(0f, 0.42f, -0.31f);
            lid.AddComponent<MeshFilter>().sharedMesh = LoadMesh("Chest.fbx", "ChestLid");
            lid.AddComponent<MeshRenderer>().sharedMaterials = mats;
            ChestGlow(body.transform);
            PrefabUtility.SaveAsPrefabAsset(root, $"{Props}/Chest.prefab");
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Золотое свечение открытого сундука: мягкие пятна над проёмом (Glow, их пульс задаёт Chest) и поднимающиеся
        /// искорки (Glow/Motes), плюс тёплый свет. В префабе выключены — включает Chest при открытии.
        /// </summary>
        private static void ChestGlow(Transform body)
        {
            var soft = AssetDatabase.LoadAssetAtPath<Texture2D>(FxArt + "/SoftDot.png");
            var glowMat = ParticleMaterial("ChestGlow", soft);
            glowMat.SetColor("_Color", new Color(1.3f, 1.3f, 1.3f, 1f));
            var gold = new Color(1f, 0.76f, 0.23f, 0.28f);

            var go = new GameObject("Glow");
            go.transform.SetParent(body, false);
            go.transform.localPosition = new Vector3(0f, 0.47f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = 1.4f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.55f, 0.75f);
            main.startColor = gold;
            main.maxParticles = 8;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = go.GetComponent<ParticleSystem>().emission;
            emission.rateOverTime = 3f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.8f)));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(Fade());
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = glowMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = -10f;

            var motes = new GameObject("Motes");
            motes.transform.SetParent(go.transform, false);
            var mps = motes.AddComponent<ParticleSystem>();
            mps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var mm = mps.main;
            mm.loop = true;
            mm.prewarm = true;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
            mm.startSpeed = 0f;
            mm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            mm.startColor = new Color(1f, 0.85f, 0.45f, 1f);
            mm.maxParticles = 16;
            mm.simulationSpace = ParticleSystemSimulationSpace.Local;
            mm.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var me = mps.emission;
            me.rateOverTime = 7f;
            var mshape = mps.shape;
            mshape.shapeType = ParticleSystemShapeType.Box;
            mshape.scale = new Vector3(0.6f, 0.05f, 0.4f);
            var mv = mps.velocityOverLifetime;
            mv.enabled = true;
            mv.space = ParticleSystemSimulationSpace.Local;
            mv.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            mv.y = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            mv.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var ms = mps.sizeOverLifetime;
            ms.enabled = true;
            ms.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
            var mr = motes.GetComponent<ParticleSystemRenderer>();
            mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(FxArt + "/Sparkle.mat");
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);

            var light = new GameObject("GlowLight").AddComponent<Light>();
            light.transform.SetParent(body, false);
            light.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.76f, 0.23f);
            light.intensity = 0.5f;
            light.range = 1.5f;
            light.shadows = LightShadows.None;
            light.gameObject.SetActive(false);
        }

        private static Gradient Fade()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.35f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // ------------------------------------------------------------------ на земле

        private static void Place(GameObject prefab, Transform parent, Vector3 position, Vector3 euler)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
        }

        private static void BuildPickup(string name, Material[] baseMats, Material beamMat, Material sparkleMat,
            bool tint, float baseYaw, System.Action<Transform> fill)
        {
            var root = new GameObject(name + "Pickup");
            var visual = root.AddComponent<PickupVisual>();

            var ground = new GameObject("Base");
            ground.transform.SetParent(root.transform, false);
            ground.transform.localRotation = Quaternion.Euler(0f, baseYaw, 0f);
            ground.AddComponent<MeshFilter>().sharedMesh = LoadMesh("PickupBase.fbx", "PickupBase");
            ground.AddComponent<MeshRenderer>().sharedMaterials = baseMats;

            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            fill(model);

            var beam = new GameObject("Beam");
            beam.transform.SetParent(root.transform, false);
            beam.AddComponent<MeshFilter>().sharedMesh = BeamMesh();
            var beamRenderer = beam.AddComponent<MeshRenderer>();
            beamRenderer.sharedMaterial = beamMat;
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;

            var sparkles = Sparkles(root.transform, sparkleMat);

            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.93f, 0.8f);
            glow.range = 2.5f;
            glow.intensity = 0.7f;
            glow.shadows = LightShadows.None;

            var so = new SerializedObject(visual);
            so.FindProperty("model").objectReferenceValue = model;
            so.FindProperty("glow").objectReferenceValue = glow;
            so.FindProperty("tintGlow").boolValue = tint;
            so.FindProperty("beam").objectReferenceValue = beamRenderer;
            so.FindProperty("sparkles").objectReferenceValue = sparkles;
            so.FindProperty("bobHeight").floatValue = 0f;
            so.FindProperty("spinSpeed").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, $"{Pickups}/{name}.prefab");
            Object.DestroyImmediate(root);
        }

        private static ParticleSystem Sparkles(Transform parent, Material material)
        {
            var go = new GameObject("Sparkles");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 2f;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
            main.maxParticles = 12;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.rateOverTime = 4f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.9f, 0.9f, 0.9f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        // ------------------------------------------------------------------ луч и искры

        /// <summary>Три вертикальные плоскости крест-накрест (луч виден под любым углом камеры), UV.y — высота.</summary>
        private static Mesh BeamMesh()
        {
            const string path = FxArt + "/PickupBeam.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
                return mesh;
            const float width = 0.9f, height = 2.4f;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                var a = Mathf.PI * i / 3f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (width * 0.5f);
                var start = verts.Count;
                verts.Add(-d);
                verts.Add(d);
                verts.Add(d + Vector3.up * height);
                verts.Add(-d + Vector3.up * height);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 1f));
                tris.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            mesh = new Mesh { name = "PickupBeam" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(Enumerable.Repeat(Color.white, verts.Count).ToList());
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Material BeamMaterial()
        {
            // Мягкая полоса: ярче к середине и к низу, сверху тает.
            var tex = Texture("PickupBeam", 64, 128, (u, v) =>
            {
                var across = 1f - Mathf.Abs(u * 2f - 1f);
                return Mathf.Pow(across, 2.2f) * Mathf.Pow(1f - v, 1.6f) * Mathf.SmoothStep(0f, 1f, v / 0.08f);
            });
            return ParticleMaterial("PickupBeam", tex);
        }

        private static Material SparkleMaterial()
        {
            // Ромбик-искра, вытянутый по вертикали, с мягким ореолом.
            var tex = Texture("Sparkle", 64, 64, (u, v) =>
            {
                var x = Mathf.Abs(u * 2f - 1f);
                var y = Mathf.Abs(v * 2f - 1f);
                var diamond = Mathf.Clamp01((1f - (x / 0.55f + y)) * 8f);
                var halo = Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y)) * 0.25f;
                return Mathf.Max(diamond, halo);
            });
            return ParticleMaterial("Sparkle", tex);
        }

        private static Texture2D Texture(string name, int w, int h, System.Func<float, float, float> alpha)
        {
            var path = $"{FxArt}/{name}.png";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha((x + 0.5f) / w, (y + 0.5f) / h)));
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

        private static Material ParticleMaterial(string name, Texture2D texture)
        {
            var path = $"{FxArt}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("ColorsAction/Particle")) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.renderQueue = 3000;
            mat.SetTexture("_MainTex", texture);
            mat.SetColor("_Color", Color.white);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ утилиты

        private static Mesh LoadMesh(string file, string meshName)
        {
            var path = $"{Models}/{file}";
            var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
            var mesh = meshes.FirstOrDefault(m => m.name == meshName) ?? meshes.FirstOrDefault();
            if (mesh == null)
                Debug.LogError($"Mesh '{meshName}' not found in {path}");
            return mesh;
        }

        private static Material Lit(string name, string hex, float smoothness, float metallic = 0f)
        {
            var path = $"{Materials}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(CurvedWorld.FindLit());
                AssetDatabase.CreateAsset(mat, path);
            }

            SetColor(mat, Hex(hex));
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void SetColor(Material mat, Color color)
        {
            mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
