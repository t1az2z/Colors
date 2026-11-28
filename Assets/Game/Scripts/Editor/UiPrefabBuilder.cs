using System.Collections.Generic;
using System.IO;
using System.Linq;
using ColorsAction.Configs;
using GameCore.Config;
using GameCore.Newtonsoft;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Ассеты интерфейса и предметов на земле, которые раньше строились кодом во время игры:
    /// спрайты UI (Art/UI), иконки предметов (Art/UI/Icons + ItemIcons.asset), префабы HUD и инвентаря (Resources/UI),
    /// префабы предметов на земле (Resources/Items/Pickups). По умолчанию создаёт только отсутствующее —
    /// ручные правки PNG и префабов не теряются. Меню "… (overwrite)" пересоздаёт заново.
    /// </summary>
    public static class UiPrefabBuilder
    {
        private const string ArtUi = "Assets/Game/Art/UI";
        private const string IconsFolder = ArtUi + "/Icons";
        private const string IconsAsset = ArtUi + "/ItemIcons.asset";
        private const string UiResources = "Assets/Game/Resources/UI";
        private const string ItemsResources = "Assets/Game/Resources/Items";
        private const string PickupsFolder = ItemsResources + "/Pickups";
        private const int IconSize = 256;

        private static readonly Color Dark = new Color(0.16f, 0.16f, 0.18f);

        [MenuItem("ColorsAction/UI/Create Missing UI Assets")]
        public static void EnsureAll() => Build(false);

        [MenuItem("ColorsAction/UI/Rebuild UI Prefabs (overwrite)")]
        public static void RebuildPrefabs()
        {
            EnsureSprites(false);
            var icons = EnsureItemIcons(false);
            BuildUiPrefabs(icons, true);
        }

        /// <summary>Пересобрать только экран инвентаря (HUD и остальные префабы не трогаются).</summary>
        [MenuItem("ColorsAction/UI/Rebuild Inventory Screen (overwrite)")]
        public static void RebuildInventoryScreen()
        {
            var icons = EnsureItemIcons(false);
            Save(BuildSlot(), UiResources + "/InventorySlot.prefab");
            var slot = AssetDatabase.LoadAssetAtPath<SlotView>(UiResources + "/InventorySlot.prefab");
            Save(BuildInventory(icons, slot), UiResources + "/InventoryScreen.prefab");
            AssetDatabase.SaveAssets();
        }

        /// <summary>Предметы на земле и ключ теперь по концепту — их пересобирает <see cref="ItemPrefabBuilder"/>.</summary>
        [MenuItem("ColorsAction/UI/Rebuild Pickup Prefabs (overwrite)")]
        public static void RebuildPickups() => ItemPrefabBuilder.Build();

        /// <summary>Префабы ключа (модель и на земле) и его иконка, если их ещё нет.</summary>
        [MenuItem("ColorsAction/UI/Create Key Prefabs and Icon")]
        public static void CreateKeyAssets()
        {
            EnsureKeyPrefabs(false);
            EnsureItemIcons(false);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Пересобрать префабы ключа (после новой модели Art/Models/Key.fbx из Tools/Blender/make_models.py).</summary>
        [MenuItem("ColorsAction/UI/Rebuild Key Prefabs (overwrite)")]
        public static void RebuildKeyPrefabs() => ItemPrefabBuilder.Build();

        [MenuItem("ColorsAction/UI/Generate Item Icons")]
        public static void GenerateIcons() => EnsureItemIcons(false);

        public static void Build(bool overwrite)
        {
            EnsureSprites(overwrite);
            var icons = EnsureItemIcons(overwrite);
            BuildUiPrefabs(icons, overwrite);
            EnsurePickupPrefabs(overwrite);
            AssetDatabase.SaveAssets();
        }

        public static Hud HudPrefab => AssetDatabase.LoadAssetAtPath<Hud>(UiResources + "/Hud.prefab");
        public static InventoryView InventoryPrefab => AssetDatabase.LoadAssetAtPath<InventoryView>(UiResources + "/InventoryScreen.prefab");

        // ------------------------------------------------------------------ спрайты

        private static Sprite rounded;
        private static Sprite circle;
        private static Sprite ring;

        private static void EnsureSprites(bool overwrite)
        {
            EnsureFolder(ArtUi);
            rounded = WriteSprite(ArtUi + "/Rounded.png", UiSprites.Rounded.texture, new Vector4(16, 16, 16, 16), false, overwrite);
            circle = WriteSprite(ArtUi + "/Circle.png", UiSprites.Circle.texture, Vector4.zero, true, overwrite);
            ring = WriteSprite(ArtUi + "/Ring.png", UiSprites.Ring.texture, Vector4.zero, true, overwrite);
        }

        private static Sprite WriteSprite(string path, Texture2D texture, Vector4 border, bool mips, bool overwrite)
        {
            if (overwrite || !File.Exists(path))
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.spriteBorder = border;
                importer.mipmapEnabled = mips;
                importer.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ------------------------------------------------------------------ иконки предметов

        /// <summary>
        /// Иконки предметов из Items.json: PNG 256×256 в цвете предмета (как прежние иконки из прямоугольников).
        /// Существующие PNG не перезаписываются, ItemIcons.asset дополняется новыми предметами.
        /// </summary>
        private static ItemIcons EnsureItemIcons(bool overwrite)
        {
            EnsureFolder(IconsFolder);
            var configs = new ConfigService(new FileConfigSource(Path.Combine(Application.streamingAssetsPath, "Configs")),
                new NewtonsoftConfigSerializer());
            var items = configs.LoadTable<ItemConfig>(ConfigKeys.Items);
            var colors = configs.LoadTable<ColorConfig>(ConfigKeys.Colors);

            var asset = AssetDatabase.LoadAssetAtPath<ItemIcons>(IconsAsset);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<ItemIcons>();
                AssetDatabase.CreateAsset(asset, IconsAsset);
            }

            var so = new SerializedObject(asset);
            var list = so.FindProperty("icons");
            var known = new HashSet<string>();
            for (int i = 0; i < list.arraySize; i++)
                known.Add(list.GetArrayElementAtIndex(i).FindPropertyRelative("itemId").stringValue);

            foreach (var pair in items)
            {
                var config = pair.Value;
                if (string.IsNullOrEmpty(config.Prefab))
                    continue; // предметы без модели иконку не получают
                var color = colors.TryGet(config.Color ?? "", out var c) ? Palette.Parse(c.Hex) : Color.gray;
                var path = $"{IconsFolder}/{FileName(pair.Key)}.png";
                if (overwrite || !File.Exists(path))
                    WriteIcon(path, DrawIcon(config.Kind, color));
                var sprite = ImportIcon(path);
                if (known.Add(pair.Key))
                {
                    list.arraySize++;
                    var e = list.GetArrayElementAtIndex(list.arraySize - 1);
                    e.FindPropertyRelative("itemId").stringValue = pair.Key;
                    e.FindPropertyRelative("sprite").objectReferenceValue = sprite;
                }
            }

            // Запасная иконка: белая плитка, красится в цвет предмета.
            var fallbackPath = IconsFolder + "/Default.png";
            if (overwrite || !File.Exists(fallbackPath))
                WriteIcon(fallbackPath, DrawIcon(ItemKind.Consumable, Color.white));
            var fallback = so.FindProperty("fallback");
            if (fallback.objectReferenceValue == null)
                fallback.objectReferenceValue = ImportIcon(fallbackPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static string FileName(string id) => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id.Substring(1);

        private static void WriteIcon(string path, Texture2D tex)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
        }

        private static Sprite ImportIcon(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private struct Box
        {
            public Rect Rect;
            public Color Color;
            public Box(float xMin, float yMin, float xMax, float yMax, Color color)
            {
                Rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                Color = color;
            }
        }

        /// <summary>
        /// Иконка из прямоугольников в координатах 100×100 с центром в (0, 0), как прежняя SlotView.CreateIcon:
        /// щит — обод, лицевая сторона и умбон; меч (и прочее оружие) — клинок, гарда и рукоять под 45°; иначе плитка.
        /// </summary>
        private static Texture2D DrawIcon(ItemKind kind, Color color)
        {
            var boxes = new List<Box>();
            var rotate = 0f;
            switch (kind)
            {
                case ItemKind.Shield:
                    boxes.Add(new Box(-29, 2 - 35, 29, 2 + 35, Dark));
                    boxes.Add(new Box(-23, 2 - 29, 23, 2 + 29, color));
                    boxes.Add(new Box(-6, 0, 6, 12, Dark));
                    break;
                case ItemKind.Weapon:
                    var blade = 58f;
                    boxes.Add(new Box(-6, -8, 6, -8 + blade, color));
                    boxes.Add(new Box(-17, -14, 17, -6, Dark));
                    boxes.Add(new Box(-4, -30, 4, -12, Dark));
                    rotate = -45f;
                    break;
                case ItemKind.Key:
                    // Ключ: квадратное кольцо, стержень и две бородки; тёмный контур, дырка в кольце прозрачная.
                    boxes.Add(new Box(-16, 12, 16, 44, Dark));
                    boxes.Add(new Box(-5, -42, 5, 14, Dark));
                    boxes.Add(new Box(3, -42, 18, -30, Dark));
                    boxes.Add(new Box(3, -26, 14, -16, Dark));
                    boxes.Add(new Box(-11, 17, 11, 39, color));
                    boxes.Add(new Box(-2, -39, 2, 17, color));
                    boxes.Add(new Box(2, -39, 15, -33, color));
                    boxes.Add(new Box(2, -23, 11, -19, color));
                    boxes.Add(new Box(-6, 22, 6, 34, Dark));
                    boxes.Add(new Box(-3, 25, 3, 31, Color.clear));
                    rotate = -45f;
                    break;
                default:
                    boxes.Add(new Box(-25, -25, 25, 25, color));
                    break;
            }

            var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            var rad = -rotate * Mathf.Deg2Rad; // из пространства иконки в пространство фигуры
            var cos = Mathf.Cos(rad);
            var sin = Mathf.Sin(rad);
            const int ss = 4;
            var unit = 100f / IconSize;
            var pixels = new Color[IconSize * IconSize];
            for (int y = 0; y < IconSize; y++)
            for (int x = 0; x < IconSize; x++)
            {
                var acc = Color.clear;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                {
                    var px = (x + (sx + 0.5f) / ss) * unit - 50f;
                    var py = (y + (sy + 0.5f) / ss) * unit - 50f;
                    var lx = px * cos - py * sin;
                    var ly = px * sin + py * cos;
                    var c = Color.clear;
                    foreach (var b in boxes)
                        if (b.Rect.Contains(new Vector2(lx, ly)))
                            c = b.Color;
                    // Премультипликация, чтобы края не темнели при усреднении.
                    acc += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                }

                acc /= ss * ss;
                pixels[y * IconSize + x] = acc.a > 0f ? new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, acc.a) : Color.clear;
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ префабы UI

        private static void BuildUiPrefabs(ItemIcons icons, bool overwrite)
        {
            EnsureFolder(UiResources);
            var slotPath = UiResources + "/InventorySlot.prefab";
            if (overwrite || !File.Exists(slotPath))
                Save(BuildSlot(), slotPath);
            var numberPath = UiResources + "/DamageNumber.prefab";
            if (overwrite || !File.Exists(numberPath))
                Save(BuildDamageNumber(), numberPath);
            var hudPath = UiResources + "/Hud.prefab";
            if (overwrite || !File.Exists(hudPath))
                Save(BuildHud(icons, AssetDatabase.LoadAssetAtPath<Text>(numberPath)), hudPath);
            var invPath = UiResources + "/InventoryScreen.prefab";
            if (overwrite || !File.Exists(invPath))
                Save(BuildInventory(icons, AssetDatabase.LoadAssetAtPath<SlotView>(slotPath)), invPath);
        }

        private static GameObject CanvasRoot(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        private static RectTransform StretchRect(Transform parent, string name, Vector2 pivot)
        {
            var rect = UiFactory.Rect(parent, name, Vector2.zero, pivot, Vector2.zero, Vector2.zero);
            UiFactory.Stretch(rect);
            return rect;
        }

        private static Image WithSprite(Image image, Sprite sprite, bool sliced)
        {
            image.sprite = sprite;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 1f;
            image.raycastTarget = false;
            return image;
        }

        private static Image Bar(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var pivot = anchor.x <= 0f ? new Vector2(0, anchor.y >= 1f ? 1 : 0.5f) : new Vector2(0.5f, 0.5f);
            return WithSprite(UiFactory.Image(parent, name, color, anchor, pivot, pos, size), rounded, true);
        }

        private static Image IconImage(Transform parent, float size)
        {
            var icon = UiFactory.Image(parent, "Icon", Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.enabled = false;
            return icon;
        }

        private static GameObject BuildHud(ItemIcons icons, Text damageNumber)
        {
            var go = CanvasRoot("Hud", 10);
            var hud = go.AddComponent<Hud>();
            var root = StretchRect(go.transform, "HUD", Vector2.zero);
            var topLeft = new Vector2(0, 1);

            // Здоровье (слева вверху, по макету Tools/UI/ref_hud.png): рамка из атласа, заливка Filled поверх светлого
            // «хвоста» урона, цифры по центру, ромб-эмблема слева поверх рамки.
            // Смещения заливки внутри рамки — в пикселях исходного атласа (см. CROPS в make_ui_sprites.py), × масштаб.
            const float hpScale = 0.83f;
            var hpFrame = AtlasImage(root, "Health", "HealthFrame", topLeft, new Vector2(0, 1), new Vector2(92, -46), hpScale);
            // Заливка вписана во внутреннюю тёмную область рамки (замер по пикселям атласа: рамка x 20–575, y 15–71;
            // непрозрачная часть заливки x 6–564, y 5–54), по высоте растянута на всю подложку.
            var hpFillPos = new Vector2(14f, -9.3f) * hpScale;
            var hpFillSize = new Vector2(568f, 68.4f) * hpScale;
            var healthLag = AtlasImage(hpFrame.transform, "Lag", "HealthFillWhite", topLeft, new Vector2(0, 1), hpFillPos, hpScale);
            healthLag.rectTransform.sizeDelta = hpFillSize;
            healthLag.color = new Color(1f, 0.88f, 0.72f, 1f);
            var healthFill = AtlasImage(hpFrame.transform, "Fill", "HealthFill", topLeft, new Vector2(0, 1), hpFillPos, hpScale);
            healthFill.rectTransform.sizeDelta = hpFillSize;
            MakeFilled(healthLag);
            MakeFilled(healthFill);
            var healthText = UiFactory.Text(hpFrame.transform, "Value", "100 / 100", 30, TextAnchor.MiddleCenter, Color.white);
            UiFactory.Stretch(healthText.rectTransform);
            healthText.rectTransform.offsetMin = new Vector2(0, 3);
            healthText.fontStyle = FontStyle.Bold;
            UiFactory.Outline(healthText);
            foreach (var x in new[] { 52f, -52f })
            {
                var dot = AtlasImage(hpFrame.transform, x > 0 ? "DotL" : "DotR", "DotDiamond", new Vector2(x > 0 ? 0 : 1, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(x, 1), 1f);
                dot.color = new Color(1f, 1f, 1f, 0.9f);
            }

            AtlasImage(root, "Emblem", "Emblem", topLeft, new Vector2(0.5f, 0.5f), new Vector2(84, -82), 0.66f);

            // Выносливость — под здоровьем, тоньше, зелёная.
            const float stScale = 0.88f;
            var stFrame = AtlasImage(root, "Stamina", "StaminaFrame", topLeft, new Vector2(0, 1), new Vector2(112, -112), stScale);
            // Рамка x 19–414, y 16–38; заливка x 5–400, y 5–25.
            var stFillPos = new Vector2(14f, -10.5f) * stScale;
            var stFillSize = new Vector2(407f, 35f) * stScale;
            var staminaLag = AtlasImage(stFrame.transform, "Lag", "StaminaFillWhite", topLeft, new Vector2(0, 1), stFillPos, stScale);
            staminaLag.rectTransform.sizeDelta = stFillSize;
            staminaLag.color = new Color(1f, 0.8f, 0.45f, 1f);
            var staminaFill = AtlasImage(stFrame.transform, "Fill", "StaminaFill", topLeft, new Vector2(0, 1), stFillPos, stScale);
            staminaFill.rectTransform.sizeDelta = stFillSize;
            MakeFilled(staminaLag);
            MakeFilled(staminaFill);

            // Опыт — плашка справа внизу с горстью кубиков слева от неё; видна несколько секунд после получения опыта.
            var bottomRight = new Vector2(1, 0);
            var xpGroupRect = StretchRect(root, "ExperienceGroup", Vector2.zero);
            var xpGroup = xpGroupRect.gameObject.AddComponent<CanvasGroup>();
            xpGroup.alpha = 0f;
            xpGroup.interactable = false;
            xpGroup.blocksRaycasts = false;
            var xpPlate = AtlasImage(xpGroupRect, "Experience", "XpPlate", bottomRight, new Vector2(1, 0), new Vector2(-40, 56), 0.74f);
            var xpText = UiFactory.Text(xpPlate.transform, "Value", "Опыт  0", 27, TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Stretch(xpText.rectTransform);
            xpText.rectTransform.offsetMin = new Vector2(46, 2);
            xpText.rectTransform.offsetMax = new Vector2(-30, 0);
            UiFactory.Outline(xpText);
            AtlasImage(xpGroupRect, "Cubes", "Cubes", bottomRight, new Vector2(1, 0), new Vector2(-40 - xpPlate.rectTransform.sizeDelta.x + 70, 44), 0.56f);

            // Оружие в руках (справа вверху): круг-медальон из атласа, иконка внутри, клавиша колеса под ним.
            var primary = WeaponMedallion(root, new Vector2(-34, -26), 0.62f);
            // Предметы на крестовине (1–4 / D-pad): слева внизу крестом, видны, только когда есть предметы.
            var quickCross = UiFactory.Rect(root, "QuickItems", new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(150, 150), new Vector2(260, 260));
            var offsets = new[] { new Vector2(0, 86), new Vector2(86, 0), new Vector2(0, -86), new Vector2(-86, 0) };
            var quick = new HudItemSlot[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
                quick[i] = QuickSlot(quickCross, offsets[i], i);
            quickCross.gameObject.SetActive(false);

            var prompt = UiFactory.Text(root, "Prompt", "", 28, TextAnchor.MiddleCenter, UiFactory.TextColor);
            prompt.rectTransform.anchorMin = prompt.rectTransform.anchorMax = new Vector2(0.5f, 0);
            prompt.rectTransform.anchoredPosition = new Vector2(0, 150);
            UiFactory.Outline(prompt);

            var scheme = UiFactory.Text(root, "Scheme", "", 20, TextAnchor.LowerRight, UiFactory.MutedText);
            scheme.rectTransform.anchorMin = scheme.rectTransform.anchorMax = new Vector2(1, 0);
            scheme.rectTransform.pivot = new Vector2(1, 0);
            scheme.rectTransform.anchoredPosition = new Vector2(-40, 132);
            UiFactory.Outline(scheme);

            var toast = UiFactory.Text(root, "Toast", "", 30, TextAnchor.MiddleCenter, UiFactory.TextColor);
            toast.rectTransform.anchorMin = toast.rectTransform.anchorMax = new Vector2(0.5f, 1);
            toast.rectTransform.anchoredPosition = new Vector2(0, -90);
            UiFactory.Outline(toast);

            // Цифры урона: контейнер на весь экран с пивотом в левом нижнем углу.
            var numbers = StretchRect(root, "DamageNumbers", Vector2.zero);

            Set(hud, "icons", icons);
            Set(hud, "healthFill", healthFill);
            Set(hud, "healthLag", healthLag);
            Set(hud, "healthText", healthText);
            Set(hud, "staminaFill", staminaFill);
            Set(hud, "staminaLag", staminaLag);
            Set(hud, "xpText", xpText);
            Set(hud, "experienceGroup", xpGroup);
            Set(hud, "prompt", prompt);
            Set(hud, "toast", toast);
            Set(hud, "schemeLabel", scheme);
            Set(hud, "primaryWeapon", primary);
            Set(hud, "quickCross", quickCross.gameObject);
            SetArray(hud, "quick", quick);
            Set(hud, "numbersRoot", numbers);
            Set(hud, "damageNumberPrefab", damageNumber);
            return go;
        }

        // ------------------------------------------------------------------ новый интерфейс (атлас)

        private const string AtlasFolder = ArtUi + "/Atlas";
        private const string WheelArtFolder = ArtUi + "/Wheel";

        private static Sprite AtlasSprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{AtlasFolder}/{name}.png");
            if (sprite == null)
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{WheelArtFolder}/{name}.png");
            if (sprite == null)
                Debug.LogError($"UI sprite {name} not found (Tools/UI/make_ui_sprites.py)");
            return sprite;
        }

        /// <summary>Картинка из атласа в родном размере × scale (заострённые плашки — 9-slice, растягиваются по ширине).</summary>
        private static Image AtlasImage(Transform parent, string name, string sprite, Vector2 anchor, Vector2 pivot, Vector2 pos, float scale)
        {
            var s = AtlasSprite(name: sprite);
            var size = s != null ? s.rect.size * scale : Vector2.one * 32f;
            var image = UiFactory.Image(parent, name, Color.white, anchor, pivot, pos, size);
            image.sprite = s;
            image.type = s != null && s.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.01f, scale);
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Заливка полосы: Filled по горизонтали слева (Hud выставляет fillAmount).</summary>
        private static void MakeFilled(Image image)
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
        }

        /// <summary>Медальон оружия в руках: круглая рамка с ромбами (атлас), иконка, клавиша колеса под ним.</summary>
        private static HudItemSlot WeaponMedallion(Transform parent, Vector2 pos, float scale)
        {
            var bg = AtlasImage(parent, "Primary", "WeaponCircle", new Vector2(1, 1), new Vector2(1, 1), pos, scale);
            var size = bg.rectTransform.sizeDelta;
            var key = UiFactory.Text(bg.transform, "Key", "", 20, TextAnchor.UpperCenter, UiFactory.TextColor);
            key.rectTransform.anchorMin = key.rectTransform.anchorMax = new Vector2(0.5f, 0);
            key.rectTransform.pivot = new Vector2(0.5f, 1);
            key.rectTransform.anchoredPosition = new Vector2(0, -2);
            UiFactory.Outline(key);
            var icon = IconImage(bg.transform, size.x * 0.66f);
            var slot = bg.gameObject.AddComponent<HudItemSlot>();
            Set(slot, "background", bg);
            Set(slot, "icon", icon);
            Set(slot, "key", key);
            SetBool(slot, "hideWhenEmpty", true);
            SetColor(slot, "filledBackground", Color.white);
            bg.gameObject.SetActive(false);
            return slot;
        }

        /// <summary>Пересобрать только HUD (по атласу нового интерфейса); остальные префабы не трогаются.</summary>
        [MenuItem("ColorsAction/UI/Rebuild HUD (overwrite)")]
        public static void RebuildHud()
        {
            EnsureSprites(false);
            var icons = EnsureItemIcons(false);
            EnsureUiAtlas();
            Save(BuildHud(icons, AssetDatabase.LoadAssetAtPath<Text>(UiResources + "/DamageNumber.prefab")), UiResources + "/Hud.prefab");
            AssetDatabase.SaveAssets();
        }

        /// <summary>Resources/UI/UiAtlas — спрайты полос над врагами (их строит код во время игры).</summary>
        public static UiAtlas EnsureUiAtlas()
        {
            EnsureFolder(UiResources);
            var path = UiResources + "/UiAtlas.asset";
            var atlas = AssetDatabase.LoadAssetAtPath<UiAtlas>(path);
            if (atlas == null)
            {
                atlas = ScriptableObject.CreateInstance<UiAtlas>();
                AssetDatabase.CreateAsset(atlas, path);
            }

            atlas.EnemyHealthFrame = AtlasSprite("EnemyHealthFrame");
            atlas.EnemyHealthFill = AtlasSprite("EnemyHealthFill");
            atlas.EnemyPoiseFrame = AtlasSprite("EnemyPoiseFrame");
            atlas.EnemyPoiseFill = AtlasSprite("EnemyPoiseFill");
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            return atlas;
        }

        /// <summary>Кружок оружия: тёмный фон, кольцо цвета оружия, иконка, клавиша под ним.</summary>
        private static HudItemSlot WeaponCircle(Transform parent, Vector2 pos, float size, string name)
        {
            var bg = WithSprite(UiFactory.Image(parent, name, new Color(0, 0, 0, 0.5f), new Vector2(1, 1), new Vector2(1, 1), pos, new Vector2(size, size)), circle, false);
            var ringImage = WithSprite(UiFactory.Image(bg.transform, "Ring", Color.gray, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size)), ring, false);
            var key = UiFactory.Text(bg.transform, "Key", "", 18, TextAnchor.UpperCenter, UiFactory.TextColor);
            key.rectTransform.anchorMin = key.rectTransform.anchorMax = new Vector2(0.5f, 0);
            key.rectTransform.pivot = new Vector2(0.5f, 1);
            key.rectTransform.anchoredPosition = new Vector2(0, -4);
            UiFactory.Outline(key);
            var icon = IconImage(bg.transform, size * 0.78f);
            var slot = bg.gameObject.AddComponent<HudItemSlot>();
            Set(slot, "background", bg);
            Set(slot, "ring", ringImage);
            Set(slot, "icon", icon);
            Set(slot, "key", key);
            SetBool(slot, "hideWhenEmpty", true);
            bg.gameObject.SetActive(false);
            return slot;
        }

        /// <summary>Ячейка крестовины предметов: квадратная рамка из атласа, иконка, номер клавиши.</summary>
        private static HudItemSlot QuickSlot(Transform parent, Vector2 pos, int index)
        {
            var back = AtlasImage(parent, "Quick" + (index + 1), "Slot", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, 0.56f);
            back.type = Image.Type.Simple;
            var label = UiFactory.Text(back.transform, "Key", (index + 1).ToString(), 17, TextAnchor.LowerRight, UiFactory.TextColor);
            UiFactory.Stretch(label.rectTransform, 9);
            UiFactory.Outline(label);
            var icon = IconImage(back.transform, 56f);
            var slot = back.gameObject.AddComponent<HudItemSlot>();
            Set(slot, "background", back);
            Set(slot, "icon", icon);
            SetColor(slot, "filledBackground", Color.white);
            SetColor(slot, "emptyBackground", new Color(1f, 1f, 1f, 0.6f));
            return slot;
        }

        private static GameObject BuildDamageNumber()
        {
            var text = UiFactory.Text(null, "DamageNumber", "10", 34, TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Outline(text);
            return text.gameObject;
        }

        /// <summary>
        /// Клетка инвентаря (референс Tools/UI/ref_inventory.png): квадратная рамка из атласа, иконка тянется вместе
        /// с клеткой; подсветка «можно положить» — зелёные уголки, курсор геймпада — белые уголки.
        /// </summary>
        private static GameObject BuildSlot()
        {
            var bg = UiFactory.Image(null, "InventorySlot", Color.white, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(140, 140));
            bg.sprite = AtlasSprite("Slot");
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1.03f;
            var view = bg.gameObject.AddComponent<SlotView>();
            var highlight = Corners(bg.transform, "Highlight", new Color(0.45f, 1f, 0.5f, 1f));
            highlight.SetActive(false);
            var cursor = Corners(bg.transform, "Cursor", Color.white);
            cursor.SetActive(false);
            var caption = UiFactory.Text(bg.transform, "Caption", "", 20, TextAnchor.UpperCenter, UiFactory.MutedText);
            caption.rectTransform.anchorMin = caption.rectTransform.anchorMax = new Vector2(0.5f, 0);
            caption.rectTransform.pivot = new Vector2(0.5f, 1);
            caption.rectTransform.anchoredPosition = new Vector2(0, -4);
            var icon = IconImage(bg.transform, 85f);
            UiFactory.Stretch(icon.rectTransform, 22f);
            Set(view, "background", bg);
            Set(view, "highlight", highlight);
            Set(view, "cursor", cursor);
            Set(view, "icon", icon);
            Set(view, "caption", caption);
            SetColor(view, "normalColor", Color.white);
            SetColor(view, "heldColor", new Color(1f, 0.82f, 0.9f, 1f));
            return bg.gameObject;
        }

        /// <summary>Уголки выделения клетки (атлас), чуть шире клетки.</summary>
        private static GameObject Corners(Transform parent, string name, Color color)
        {
            var image = UiFactory.Image(parent, name, color, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UiFactory.Stretch(image.rectTransform, -10f);
            image.sprite = AtlasSprite("SlotCursor");
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.4f;
            image.fillCenter = false;
            image.raycastTarget = false;
            return image.gameObject;
        }

        /// <summary>
        /// Экран инвентаря по референсу Tools/UI/ref_inventory.png (спрайты второго атласа, Art/UI/Atlas): рамка-окно
        /// с ромбами, слева «Персонаж» — портрет-рамка с 3D-превью, плашка оружия под ним, крестовина 1–4; справа
        /// «Инвентарь» — сетка; внизу длинная плашка с подсказкой управления. Координаты — от левого верхнего угла окна.
        /// </summary>
        private static GameObject BuildInventory(ItemIcons icons, SlotView slotPrefab)
        {
            var go = CanvasRoot("InventoryScreen", 20);
            var view = go.AddComponent<InventoryView>();
            var dim = StretchRect(go.transform, "Dim", new Vector2(0.5f, 0.5f)).gameObject.AddComponent<Image>();
            dim.color = new Color(0.03f, 0.02f, 0.05f, 0.5f);

            var window = UiFactory.Image(go.transform, "Panel", Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1814, 970));
            window.sprite = AtlasSprite("Panel");
            window.type = Image.Type.Sliced;
            window.pixelsPerUnitMultiplier = 1.05f;
            var panel = window.rectTransform;
            AtlasImage(panel, "DiamondTop", "DiamondLarge", new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -6), 0.62f);
            AtlasImage(panel, "DiamondBottom", "DiamondLarge", new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 6), 0.62f);

            var topLeft = UiFactory.Rect(panel, "Content", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);

            TitlePlate(topLeft, "Персонаж", new Vector2(67, -43));
            TitlePlate(topLeft, "Инвентарь", new Vector2(923, -43));

            // Портрет: рамка, внутри 3D-превью персонажа (RenderTexture создаёт CharacterPreview в игре).
            var frame = AtlasImage(topLeft, "PortraitFrame", "PortraitFrame", new Vector2(0, 1), new Vector2(0, 1), new Vector2(67, -138), 1.15f);
            var previewPos = new Vector2(67 + 26, -138 - 30);
            var previewSize = frame.rectTransform.sizeDelta - new Vector2(52, 60);
            var previewRect = UiFactory.Rect(topLeft, "CharacterPreview", new Vector2(0, 1), new Vector2(0, 1), previewPos, previewSize);
            var preview = previewRect.gameObject.AddComponent<RawImage>();
            preview.color = Color.white;

            // Слоты создаются в игре из префаба InventorySlot, в контейнер Slots (тот же левый верхний угол окна).
            var slots = UiFactory.Rect(topLeft, "Slots", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);

            // Оружие в руках и его прокачка — на плашке под портретом.
            var captionPlate = AtlasImage(topLeft, "WeaponCaptionPlate", "CaptionPlate", new Vector2(0, 1), new Vector2(0, 1), new Vector2(48, -556), 1f);
            captionPlate.rectTransform.sizeDelta = new Vector2(450, 62);
            var weaponCaption = UiFactory.Text(captionPlate.transform, "WeaponCaption", "", 24, TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Stretch(weaponCaption.rectTransform);
            weaponCaption.rectTransform.offsetMin = new Vector2(40, 2);
            weaponCaption.rectTransform.offsetMax = new Vector2(-40, 0);

            var description = UiFactory.Text(topLeft, "Description", "", 24, TextAnchor.UpperLeft, UiFactory.TextColor);
            description.rectTransform.anchorMin = description.rectTransform.anchorMax = new Vector2(0, 1);
            description.rectTransform.pivot = new Vector2(0, 1);
            description.rectTransform.sizeDelta = new Vector2(820, 160);
            description.rectTransform.anchoredPosition = new Vector2(946, -640);

            // Подсказка управления — на длинной плашке внизу окна.
            var helpPlate = AtlasImage(topLeft, "HelpPlate", "LongPlate", new Vector2(0, 1), new Vector2(0, 1), new Vector2(56, -838), 1f);
            helpPlate.rectTransform.sizeDelta = new Vector2(1700, 74);
            var help = UiFactory.Text(helpPlate.transform, "Help", "", 22, TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Stretch(help.rectTransform);
            help.rectTransform.offsetMin = new Vector2(60, 2);
            help.rectTransform.offsetMax = new Vector2(-60, 0);

            // Иконка перетаскивания — поверх всего.
            var ghost = UiFactory.Image(go.transform, "DragGhost", Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100, 100));
            ghost.raycastTarget = false;
            ghost.preserveAspect = true;
            ghost.gameObject.SetActive(false);

            Set(view, "icons", icons);
            Set(view, "slotPrefab", slotPrefab);
            Set(view, "slotsRoot", slots);
            Set(view, "dragGhost", ghost);
            Set(view, "description", description);
            Set(view, "help", help);
            Set(view, "previewImage", preview);
            Set(view, "weaponCaption", weaponCaption);
            SetFloat(view, "slotSize", 140f);
            SetFloat(view, "gap", 18f);
            SetVector(view, "previewPosition", previewPos);
            SetVector(view, "previewSize", previewSize);
            SetVector(view, "crossCenter", new Vector2(700, -372));
            SetFloat(view, "crossSlotSize", 120f);
            SetFloat(view, "crossGap", 12f);
            SetVector(view, "gridOrigin", new Vector2(946, -149));
            return go;
        }

        private static void TitlePlate(Transform parent, string text, Vector2 pos)
        {
            var plate = AtlasImage(parent, "TitlePlate", "TitlePlate", new Vector2(0, 1), new Vector2(0, 1), pos, 0.83f);
            var t = UiFactory.Text(plate.transform, "Title", text, 34, TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(0, 3);
            t.fontStyle = FontStyle.Bold;
            UiFactory.Outline(t);
        }

        // ------------------------------------------------------------------ предметы на земле

        /// <summary>
        /// Префабы предметов на земле: Resources/Items/Pickups/&lt;Имя&gt; с вложенным префабом оружия из рук,
        /// лежащим на боку, и точечным светом цвета предмета. Здесь их удобно доводить руками (постамент, частицы…).
        /// </summary>
        public static void EnsurePickupPrefabs(bool overwrite)
        {
            EnsureFolder(PickupsFolder);
            var configs = new ConfigService(new FileConfigSource(Path.Combine(Application.streamingAssetsPath, "Configs")),
                new NewtonsoftConfigSerializer());
            EnsureKeyPrefabs(overwrite);
            foreach (var pair in configs.LoadTable<ItemConfig>(ConfigKeys.Items))
            {
                var held = pair.Value.Prefab;
                // Ключ строится отдельно (EnsureKeyPrefabs): висит стоя и крутится вокруг вертикали.
                if (string.IsNullOrEmpty(held) || pair.Value.IsKey)
                    continue;
                var heldAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Resources/{held}.prefab");
                if (heldAsset == null)
                    continue;
                var path = $"Assets/Game/Resources/{Pickup.PickupPath(held)}.prefab";
                if (!overwrite && File.Exists(path))
                    continue;
                EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));

                var root = new GameObject(Path.GetFileNameWithoutExtension(path) + "Pickup");
                var visual = root.AddComponent<PickupVisual>();
                var model = (GameObject)PrefabUtility.InstantiatePrefab(heldAsset);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                // Лежит на боку над полом.
                model.transform.localPosition = Vector3.up * 0.2f;
                model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                var glow = new GameObject("Glow").AddComponent<Light>();
                glow.transform.SetParent(root.transform, false);
                glow.transform.localPosition = Vector3.up * 0.6f;
                glow.type = LightType.Point;
                glow.range = 3f;
                glow.intensity = 2.5f;
                glow.shadows = LightShadows.None;

                Set(visual, "model", model.transform);
                Set(visual, "glow", glow);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Ключ от двери: модель Resources/Items/Key (меш Art/Models/Key.fbx из Blender — восьмигранное кольцо, муфта,
        /// стержень и бородка, бронзовый металл Art/Materials/KeyMetal; без FBX — запасной ключ из кубиков) и префаб
        /// на земле Resources/Items/Pickups/Key — ключ висит стоя над полом, покачивается и вращается вокруг вертикали,
        /// над ним тёплый свет. Существующие не перезаписываются без overwrite.
        /// </summary>
        public static void EnsureKeyPrefabs(bool overwrite)
        {
            EnsureFolder(PickupsFolder);
            var modelPath = ItemsResources + "/Key.prefab";
            var pickupPath = PickupsFolder + "/Key.prefab";
            var keyMesh = AssetDatabase.LoadAllAssetsAtPath(KeyModelPath).OfType<Mesh>().FirstOrDefault();
            if ((overwrite || !File.Exists(modelPath)) && keyMesh != null)
            {
                var key = new GameObject("Key");
                key.AddComponent<MeshFilter>().sharedMesh = keyMesh;
                key.AddComponent<MeshRenderer>().sharedMaterial = KeyMetal();
                Save(key, modelPath);
            }
            else if (overwrite || !File.Exists(modelPath))
            {
                var metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Materials/WeaponHandle.mat");
                var key = new GameObject("Key");
                void Bar(string name, float x0, float y0, float x1, float y1)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = name;
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.transform.SetParent(key.transform, false);
                    go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 0f);
                    go.transform.localScale = new Vector3(x1 - x0, y1 - y0, 0.07f);
                    if (metal != null)
                        go.GetComponent<MeshRenderer>().sharedMaterial = metal;
                }

                // Кольцо 0.3×0.3 из четырёх брусков, стержень вниз, две бородки справа.
                Bar("BowTop", -0.15f, 0.27f, 0.15f, 0.35f);
                Bar("BowBottom", -0.15f, 0.05f, 0.15f, 0.13f);
                Bar("BowLeft", -0.15f, 0.13f, -0.07f, 0.27f);
                Bar("BowRight", 0.07f, 0.13f, 0.15f, 0.27f);
                Bar("Shaft", -0.035f, -0.35f, 0.035f, 0.05f);
                Bar("ToothLow", 0.035f, -0.35f, 0.17f, -0.27f);
                Bar("ToothHigh", 0.035f, -0.22f, 0.13f, -0.16f);
                Save(key, modelPath);
            }

            if (!overwrite && File.Exists(pickupPath))
                return;
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var root = new GameObject("KeyPickup");
            var visual = root.AddComponent<PickupVisual>();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.up * 0.6f;

            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, 1f, 0.3f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.93f, 0.8f);
            glow.range = 3f;
            glow.intensity = 2.5f;
            glow.shadows = LightShadows.None;

            var so = new SerializedObject(visual);
            so.FindProperty("model").objectReferenceValue = model.transform;
            so.FindProperty("glow").objectReferenceValue = glow;
            so.FindProperty("tintGlow").boolValue = false;
            so.FindProperty("spinAxis").vector3Value = Vector3.up;
            so.FindProperty("spinSpeed").floatValue = 70f;
            so.FindProperty("bobHeight").floatValue = 0.08f;
            so.ApplyModifiedPropertiesWithoutUndo();
            Save(root, pickupPath);
        }

        private const string KeyModelPath = "Assets/Game/Art/Models/Key.fbx";
        private const string KeyMetalPath = "Assets/Game/Art/Materials/KeyMetal.mat";

        /// <summary>Бронзовый металл ключа (как у образца): копия материала рукояти оружия со своим цветом.</summary>
        private static Material KeyMetal()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(KeyMetalPath);
            if (mat != null)
                return mat;
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Materials/WeaponHandle.mat");
            mat = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var bronze = new Color(0.72f, 0.55f, 0.33f);
            mat.SetColor("_BaseColor", bronze);
            mat.SetColor("_Color", bronze);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", 0.55f);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0.45f);
            AssetDatabase.CreateAsset(mat, KeyMetalPath);
            return mat;
        }

        // ------------------------------------------------------------------ утилиты

        private static void Save(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            var parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
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

        private static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector(Object target, string field, Vector2 value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).vector2Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetColor(Object target, string field, Color value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).colorValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
