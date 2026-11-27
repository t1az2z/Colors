using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Собирает сцену прототипа из кода: материалы, префабы предметов и манекена, игрок, камера, свет, уровень.
    /// Меню: ColorsAction → Build Prototype Scene. Повторный запуск пересобирает всё заново
    /// (ручные правки сцены Prototype.unity при этом теряются — правьте конфиги или этот скрипт).
    /// </summary>
    public static class PrototypeSceneBuilder
    {
        private const string Root = "Assets/Game";
        private const string Models = Root + "/Art/Models";
        private const string Materials = Root + "/Art/Materials";
        private const string Textures = Root + "/Art/Textures";
        private const string Prefabs = Root + "/Prefabs";
        private const string ItemsResources = Root + "/Resources/Items";
        private const string ScenePath = Root + "/Scenes/Prototype.unity";
        private const string PlayerLayer = "Player";

        [MenuItem("ColorsAction/Build Prototype Scene")]
        public static void Build()
        {
            EnsureFolders();
            var layer = EnsureLayer(PlayerLayer);
            EnsureLayer("Enemy");
            ConfigureRenderPipeline();

            // Материалы
            var grid = CreateGridTexture();
            var world = Lit("World", Hex("#F3F3F0"), 0.15f, grid);
            var body = Lit("PlayerBody", Hex("#A3A5AA"), 0.25f);
            var eyes = Lit("PlayerEyes", Hex("#18181C"), 0.6f);
            var colored = Lit("WeaponColored", Color.white, 0.45f, null, true);
            var handle = Lit("WeaponHandle", Hex("#2F2F35"), 0.3f);
            var rim = Lit("ShieldRim", Hex("#52505C"), 0.4f);
            var dummyRed = Lit("DummyBody", Hex("#E8413C"), 0.3f, null, true);
            var dummyBase = Lit("DummyBase", Hex("#5A5A60"), 0.2f);
            EnsureTrailMaterial();
            EnsurePlayerSilhouette();

            // Префабы
            CreateItemPrefab("Sword", new[] { colored, handle });
            CreateItemPrefab("Shield", new[] { colored, rim });
            CreateClubPrefab();
            // Интерфейс и предметы на земле: создаются, только если их ещё нет (ручные правки сохраняются).
            UiPrefabBuilder.Build(false);
            var dummyPrefab = CreateDummyPrefab(dummyRed, dummyBase);

            // Сцена
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#8C9098");
            RenderSettings.ambientEquatorColor = Hex("#74767C");
            RenderSettings.ambientGroundColor = Hex("#55565A");
            RenderSettings.skybox = null;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            sun.color = Hex("#FFF6EA");
            sun.intensity = 0.9f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;

            var bootstrapGo = new GameObject("GameBootstrap");
            bootstrapGo.AddComponent<GameInput>();
            var bootstrap = bootstrapGo.AddComponent<GameBootstrap>();
            var so = new SerializedObject(bootstrap);
            so.FindProperty("dontDestroyOnLoad").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            // HUD и экран инвентаря — префабы, их создаёт GameBootstrap при старте.
            Set(bootstrap, "hudPrefab", UiPrefabBuilder.HudPrefab);
            Set(bootstrap, "inventoryPrefab", UiPrefabBuilder.InventoryPrefab);

            var player = CreatePlayer(layer, body, eyes);

            var levelGo = new GameObject("Level");
            var level = levelGo.AddComponent<LevelBuilder>();
            Set(level, "worldMaterial", world);
            Set(level, "dummyPrefab", dummyPrefab);
            Set(level, "player", player.GetComponent<PlayerController>());

            CreateCamera(player.transform.Find("CameraTarget"));

            // Глобальный объём пост-обработки: мягкий тонмаппинг, без лишнего.
            CreateVolume();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("ColorsAction: prototype scene built at " + ScenePath);
        }

        // ------------------------------------------------------------------ игрок

        private static GameObject CreatePlayer(int layer, Material body, Material eyes)
        {
            var player = new GameObject("Player") { layer = layer };
            player.transform.position = new Vector3(10.5f, 0f, 9.5f);
            player.AddComponent<BoxMotor>();
            var controller = player.AddComponent<PlayerController>();
            var combat = player.AddComponent<PlayerCombat>();
            player.AddComponent<PlayerInteractor>();

            var cameraTarget = new GameObject("CameraTarget").transform;
            cameraTarget.SetParent(player.transform, false);
            cameraTarget.localPosition = new Vector3(0, 0.5f, 0);

            var bodyT = Child(player.transform, "Body", Vector3.zero, layer);
            var pivot = Child(bodyT, "RollPivot", new Vector3(0, 0.5f, 0), layer);
            var meshT = Child(pivot, "Mesh", new Vector3(0, -0.5f, 0), layer);
            meshT.gameObject.AddComponent<MeshFilter>().sharedMesh = LoadMesh("Player.fbx", "Player");
            var renderer = meshT.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { body, eyes };
            var deformer = meshT.gameObject.AddComponent<CubeDeformer>();
            var left = Child(meshT, "HandL", new Vector3(-0.64f, 0.4f, 0.1f), layer);
            var right = Child(meshT, "HandR", new Vector3(0.64f, 0.4f, 0.1f), layer);

            Set(controller, "body", bodyT);
            Set(controller, "rollPivot", pivot);
            Set(controller, "deformer", deformer);
            Set(combat, "leftSocket", left);
            Set(combat, "rightSocket", right);
            var impulse = player.AddComponent<CinemachineImpulseSource>();
            impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            impulse.ImpulseDefinition.ImpulseDuration = 0.12f;
            Set(combat, "impulse", impulse);
            return player;
        }

        private static Transform Child(Transform parent, string name, Vector3 localPosition, int layer)
        {
            var t = new GameObject(name) { layer = layer }.transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        // ------------------------------------------------------------------ камера

        private static void CreateCamera(Transform target)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("#D8D8D4");
            cam.fieldOfView = 25f;
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<CinemachineBrain>();
            camGo.transform.position = new Vector3(0, 15, -15);

            var vcamGo = new GameObject("IsoCamera");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Follow = target;
            var orbit = vcamGo.AddComponent<CinemachineOrbitalFollow>();
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.Radius = 26f;
            var composer = vcamGo.AddComponent<CinemachineRotationComposer>();
            composer.Damping = Vector2.zero;
            vcamGo.AddComponent<CinemachineImpulseListener>();
            var rig = vcamGo.AddComponent<CameraRig>();
            Set(rig, "virtualCamera", vcam);
            Set(rig, "followTarget", target);
        }

        private static void CreateVolume()
        {
            var go = new GameObject("PostProcess");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            var path = Root + "/Art/PrototypeVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                var tonemap = profile.Add<Tonemapping>(true);
                tonemap.mode.Override(TonemappingMode.Neutral);
                var bloom = profile.Add<Bloom>(true);
                bloom.intensity.Override(0.6f);
                bloom.threshold.Override(1.1f);
                var vignette = profile.Add<Vignette>(true);
                vignette.intensity.Override(0.18f);
                AssetDatabase.CreateAsset(profile, path);
            }

            volume.sharedProfile = profile;
        }

        // ------------------------------------------------------------------ префабы

        /// <summary>Префаб оружия в руках (Resources/Items). Уже существующий не пересоздаётся — его можно доводить руками.</summary>
        private static void CreateItemPrefab(string name, Material[] materials)
        {
            if (File.Exists($"{ItemsResources}/{name}.prefab"))
                return;
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = LoadMesh(name + ".fbx", name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            var visual = go.AddComponent<ItemVisual>();
            Set(visual, "renderers", new Renderer[] { r });
            PrefabUtility.SaveAsPrefabAsset(go, $"{ItemsResources}/{name}.prefab");
            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// Дубина врага (Resources/Items/Club), без ItemVisual — красить не нужно. Готовый префаб (по концепту его
        /// собирает ItemPrefabBuilder) не трогается.
        /// </summary>
        public static void CreateClubPrefab()
        {
            if (File.Exists($"{ItemsResources}/Club.prefab"))
                return;
            var head = Lit("ClubHead", Hex("#7E7F84"), 0.25f);
            var grip = Lit("ClubGrip", Hex("#3E3D42"), 0.3f);
            var go = new GameObject("Club");
            go.AddComponent<MeshFilter>().sharedMesh = LoadMesh("Club.fbx", "Club");
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { head, grip };
            PrefabUtility.SaveAsPrefabAsset(go, $"{ItemsResources}/Club.prefab");
            Object.DestroyImmediate(go);
        }

        private static GameObject CreateDummyPrefab(Material red, Material baseMat)
        {
            var root = new GameObject("TrainingDummy");
            var dummy = root.AddComponent<TrainingDummy>();

            var baseGo = new GameObject("Base");
            baseGo.transform.SetParent(root.transform, false);
            baseGo.AddComponent<MeshFilter>().sharedMesh = LoadMesh("Dummy.fbx", "DummyBase");
            baseGo.AddComponent<MeshRenderer>().sharedMaterial = baseMat;

            var pivot = new GameObject("TiltPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0, 0.06f, 0);
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(pivot, false);
            bodyGo.transform.localPosition = new Vector3(0, -0.06f, 0);
            bodyGo.AddComponent<MeshFilter>().sharedMesh = LoadMesh("Dummy.fbx", "DummyBody");
            var bodyRenderer = bodyGo.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = red;
            var capsule = bodyGo.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0, 0.75f, 0);
            capsule.height = 1.5f;
            capsule.radius = 0.38f;

            Set(dummy, "tiltPivot", pivot);
            Set(dummy, "bodyRenderer", bodyRenderer);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Prefabs}/TrainingDummy.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ ресурсы

        private static Mesh LoadMesh(string file, string meshName)
        {
            var path = $"{Models}/{file}";
            var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
            var mesh = meshes.FirstOrDefault(m => m.name == meshName) ?? meshes.FirstOrDefault();
            if (mesh == null)
                Debug.LogError($"Mesh '{meshName}' not found in {path}");
            return mesh;
        }

        private static Material Lit(string name, Color color, float smoothness, Texture texture = null, bool emission = false)
        {
            var path = $"{Materials}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(CurvedWorld.FindLit());
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            if (texture != null)
            {
                mat.SetTexture("_BaseMap", texture);
                mat.SetTextureScale("_BaseMap", Vector2.one);
            }

            if (emission)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Силуэт игрока за блоками: фича RenderObjects в рендерерах URP рисует слой Player материалом
        /// ColorsAction/Silhouette только там, где куб закрыт геометрией. Повторный вызов ничего не дублирует.
        /// </summary>
        [MenuItem("ColorsAction/Setup Player Silhouette")]
        public static void EnsurePlayerSilhouette()
        {
            const string matPath = Materials + "/PlayerSilhouette.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("ColorsAction/Silhouette")) { name = "PlayerSilhouette" };
                AssetDatabase.CreateAsset(mat, matPath);
            }

            var playerLayer = EnsureLayer(PlayerLayer);
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null)
                    continue;
                // Сначала метка видимых пикселей игрока, потом сам силуэт: иначе части игрока,
                // закрытые его же кубом (глаза сзади, оружие за спиной), просвечивают сквозь него.
                var wanted = new[] { "PlayerStencilMark", "PlayerSilhouette" };
                var present = data.rendererFeatures.Where(f => f != null && f.name.StartsWith("PlayerS")).Select(f => f.name);
                if (present.SequenceEqual(wanted))
                    continue;
                foreach (var name in present.ToArray())
                    RemoveRendererFeature(data, name);
                AddRenderObjects(data, wanted[0], RenderPassEvent.AfterRenderingOpaques, playerLayer, mat, 1);
                AddRenderObjects(data, wanted[1], RenderPassEvent.AfterRenderingTransparents, playerLayer, mat, 0);
            }

            AssetDatabase.SaveAssets();
        }

        private static void RemoveRendererFeature(UniversalRendererData data, string name)
        {
            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            for (int i = features.arraySize - 1; i >= 0; i--)
            {
                var feature = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (feature == null || feature.name != name)
                    continue;
                features.GetArrayElementAtIndex(i).objectReferenceValue = null;
                features.DeleteArrayElementAtIndex(i);
                map.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.RemoveObjectFromAsset(feature);
                Object.DestroyImmediate(feature, true);
                so.Update();
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }

        private static void AddRenderObjects(UniversalRendererData data, string name, RenderPassEvent evt, int layer, Material mat, int pass)
        {
            if (data.rendererFeatures.Exists(f => f != null && f.name == name))
                return;
            var feature = ScriptableObject.CreateInstance<RenderObjects>();
            feature.name = name;
            feature.settings.Event = evt;
            feature.settings.filterSettings.RenderQueueType = RenderQueueType.Opaque;
            feature.settings.filterSettings.LayerMask = 1 << layer;
            feature.settings.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.Material;
            feature.settings.overrideMaterial = mat;
            feature.settings.overrideMaterialPassIndex = pass;
            AssetDatabase.AddObjectToAsset(feature, data);

            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            Debug.Log("ColorsAction: " + name + " added to " + AssetDatabase.GetAssetPath(data));
        }

        /// <summary>Материал следа оружия (Resources/Materials/WeaponTrail), шейдер ColorsAction/WeaponTrail.</summary>
        public static Material EnsureTrailMaterial()
        {
            const string path = Root + "/Resources/Materials/WeaponTrail.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
            if (!AssetDatabase.IsValidFolder(Root + "/Resources/Materials"))
                AssetDatabase.CreateFolder(Root + "/Resources", "Materials");
            mat = new Material(Shader.Find("ColorsAction/WeaponTrail")) { name = "WeaponTrail", renderQueue = 3000 };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Белая плитка с тонкой серой рамкой: на каждом кубе 1×1 видна его граница.</summary>
        private static Texture2D CreateGridTexture()
        {
            var path = $"{Textures}/BlockGrid.png";
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                float v = edge < 2 ? 0.86f : edge < 4 ? 0.95f : 1f;
                tex.SetPixel(x, y, new Color(v, v, v, 1f));
            }

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void ConfigureRenderPipeline()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null)
                    continue;
                asset.shadowDistance = 90f;
                asset.shadowCascadeCount = 2;
                EditorUtility.SetDirty(asset);
            }
        }

        private static void EnsureFolders()
        {
            foreach (var folder in new[] { Materials, Textures, Prefabs, ItemsResources, Root + "/Scenes" })
            {
                if (AssetDatabase.IsValidFolder(folder))
                    continue;
                var parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }
        }

        public static int EnsureLayer(string name)
        {
            var existing = LayerMask.NameToLayer(name);
            if (existing >= 0)
                return existing;
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var p = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(p.stringValue))
                {
                    p.stringValue = name;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    return i;
                }
            }

            Debug.LogError("No free layer for " + name);
            return 0;
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError($"{target.GetType().Name}.{field} not found");
                return;
            }

            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string field, Renderer[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
    }
}
