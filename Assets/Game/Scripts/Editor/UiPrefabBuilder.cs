using System.Collections.Generic;
using System.IO;
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
            var slot = AssetDatabase.LoadAssetAtPath<SlotView>(UiResources + "/InventorySlot.prefab");
            Save(BuildInventory(icons, slot), UiResources + "/InventoryScreen.prefab");
            AssetDatabase.SaveAssets();
        }

        [MenuItem("ColorsAction/UI/Rebuild Pickup Prefabs (overwrite)")]
        public static void RebuildPickups() => EnsurePickupPrefabs(true);

        /// <summary>Префабы ключа (модель и на земле) и его иконка, если их ещё нет.</summary>
        [MenuItem("ColorsAction/UI/Create Key Prefabs and Icon")]
        public static void CreateKeyAssets()
        {
            EnsureKeyPrefabs(false);
            EnsureItemIcons(false);
            AssetDatabase.SaveAssets();
        }

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
            const float healthWidth = 420f;
            const float staminaWidth = 300f;
            var go = CanvasRoot("Hud", 10);
            var hud = go.AddComponent<Hud>();
            var root = StretchRect(go.transform, "HUD", Vector2.zero);

            // Здоровье (слева вверху): скруглённая полоса, "хвост" урона и цифры поверх.
            var topLeft = new Vector2(0, 1);
            var hpBack = Bar(root, "Health", topLeft, new Vector2(36, -34), new Vector2(healthWidth, 34), new Color(0, 0, 0, 0.55f));
            var healthLag = Bar(hpBack.transform, "Lag", new Vector2(0, 0.5f), new Vector2(3, 0), new Vector2(healthWidth - 6, 28), new Color(1f, 0.85f, 0.55f, 0.95f));
            var healthFill = Bar(hpBack.transform, "Fill", new Vector2(0, 0.5f), new Vector2(3, 0), new Vector2(healthWidth - 6, 28), new Color(0.88f, 0.27f, 0.25f, 1f));
            var healthText = UiFactory.Text(hpBack.transform, "Value", "100 / 100", 22, TextAnchor.MiddleCenter, Color.white);
            UiFactory.Stretch(healthText.rectTransform);
            UiFactory.Outline(healthText);

            // Выносливость — под здоровьем, тоньше.
            var stBack = Bar(root, "Stamina", topLeft, new Vector2(36, -76), new Vector2(staminaWidth, 16), new Color(0, 0, 0, 0.5f));
            var staminaLag = Bar(stBack.transform, "Lag", new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(staminaWidth - 4, 12), new Color(1f, 0.6f, 0.3f, 0.9f));
            var staminaFill = Bar(stBack.transform, "Fill", new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(staminaWidth - 4, 12), new Color(0.93f, 0.93f, 0.86f, 1f));

            var xpText = UiFactory.Text(root, "Experience", "Опыт  0", 20, TextAnchor.UpperLeft, UiFactory.TextColor);
            xpText.rectTransform.anchorMin = xpText.rectTransform.anchorMax = topLeft;
            xpText.rectTransform.pivot = new Vector2(0, 1);
            xpText.rectTransform.anchoredPosition = new Vector2(38, -100);
            UiFactory.Outline(xpText);

            // Оружие (справа вверху): правая рука — основное, крупнее; левая — дополнительное.
            var primary = WeaponCircle(root, new Vector2(-36, -32), 104f, "Primary");
            var secondary = WeaponCircle(root, new Vector2(-36 - 104 - 18, -44), 80f, "Secondary");

            // Предметы на крестовине (1–4 / D-pad): слева внизу крестом, видны, только когда есть предметы.
            var quickCross = UiFactory.Rect(root, "QuickItems", new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(130, 130), new Vector2(220, 220));
            var offsets = new[] { new Vector2(0, 70), new Vector2(70, 0), new Vector2(0, -70), new Vector2(-70, 0) };
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
            scheme.rectTransform.anchoredPosition = new Vector2(-40, 40);
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
            Set(hud, "prompt", prompt);
            Set(hud, "toast", toast);
            Set(hud, "schemeLabel", scheme);
            Set(hud, "primaryWeapon", primary);
            Set(hud, "secondaryWeapon", secondary);
            Set(hud, "quickCross", quickCross.gameObject);
            SetArray(hud, "quick", quick);
            Set(hud, "numbersRoot", numbers);
            Set(hud, "damageNumberPrefab", damageNumber);
            return go;
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

        /// <summary>Ячейка крестовины предметов: плашка, иконка, номер клавиши.</summary>
        private static HudItemSlot QuickSlot(Transform parent, Vector2 pos, int index)
        {
            var back = WithSprite(UiFactory.Image(parent, "Quick" + (index + 1), new Color(0, 0, 0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(64, 64)), rounded, true);
            var label = UiFactory.Text(back.transform, "Key", (index + 1).ToString(), 16, TextAnchor.LowerRight, UiFactory.MutedText);
            UiFactory.Stretch(label.rectTransform, 4);
            UiFactory.Outline(label);
            var icon = IconImage(back.transform, 52f);
            var slot = back.gameObject.AddComponent<HudItemSlot>();
            Set(slot, "background", back);
            Set(slot, "icon", icon);
            return slot;
        }

        private static GameObject BuildDamageNumber()
        {
            var text = UiFactory.Text(null, "DamageNumber", "10", 34, TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Outline(text);
            return text.gameObject;
        }

        private static GameObject BuildSlot()
        {
            var bg = UiFactory.Image(null, "InventorySlot", UiFactory.SlotColor, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(100, 100));
            var view = bg.gameObject.AddComponent<SlotView>();
            var highlight = UiFactory.Frame(bg.transform, "Highlight", UiFactory.Highlight, 4f);
            highlight.SetActive(false);
            var cursor = UiFactory.Frame(bg.transform, "Cursor", Color.white, 3f);
            cursor.SetActive(false);
            var caption = UiFactory.Text(bg.transform, "Caption", "", 18, TextAnchor.UpperCenter, UiFactory.MutedText);
            caption.rectTransform.anchorMin = caption.rectTransform.anchorMax = new Vector2(0.5f, 0);
            caption.rectTransform.pivot = new Vector2(0.5f, 1);
            caption.rectTransform.anchoredPosition = new Vector2(0, -6);
            var icon = IconImage(bg.transform, 85f);
            Set(view, "background", bg);
            Set(view, "highlight", highlight);
            Set(view, "cursor", cursor);
            Set(view, "icon", icon);
            Set(view, "caption", caption);
            return bg.gameObject;
        }

        private static GameObject BuildInventory(ItemIcons icons, SlotView slotPrefab)
        {
            var go = CanvasRoot("InventoryScreen", 20);
            var view = go.AddComponent<InventoryView>();
            var dim = StretchRect(go.transform, "Dim", new Vector2(0.5f, 0.5f)).gameObject.AddComponent<Image>();
            dim.color = new Color(0, 0, 0, 0.45f);

            var panel = UiFactory.Image(go.transform, "Panel", UiFactory.PanelColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 720)).rectTransform;
            var topLeft = UiFactory.Rect(panel, "Content", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);

            Title(topLeft, "Персонаж", new Vector2(40, -22));
            Title(topLeft, "Инвентарь", new Vector2(660, -22));

            // 3D-превью персонажа с оружием в руках (RenderTexture создаёт CharacterPreview в игре); справа — крестовина 1–4.
            var previewRect = UiFactory.Rect(topLeft, "CharacterPreview", new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -70), new Vector2(260, 300));
            var preview = previewRect.gameObject.AddComponent<RawImage>();
            preview.color = new Color(0.16f, 0.17f, 0.19f, 1f);

            // Слоты создаются в игре из префаба InventorySlot, в контейнер Slots (тот же левый верхний угол панели).
            var slots = UiFactory.Rect(topLeft, "Slots", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);

            // Оружие в руках и его прокачка — под превью.
            var weaponCaption = UiFactory.Text(topLeft, "WeaponCaption", "", 22, TextAnchor.UpperLeft, UiFactory.TextColor);
            weaponCaption.rectTransform.anchorMin = weaponCaption.rectTransform.anchorMax = new Vector2(0, 1);
            weaponCaption.rectTransform.pivot = new Vector2(0, 1);
            weaponCaption.rectTransform.sizeDelta = new Vector2(560, 32);
            weaponCaption.rectTransform.anchoredPosition = new Vector2(40, -382);

            var description = UiFactory.Text(topLeft, "Description", "", 24, TextAnchor.UpperLeft, UiFactory.TextColor);
            description.rectTransform.anchorMin = description.rectTransform.anchorMax = new Vector2(0, 1);
            description.rectTransform.pivot = new Vector2(0, 1);
            description.rectTransform.anchoredPosition = new Vector2(660, -640);

            var help = UiFactory.Text(topLeft, "Help", "", 20, TextAnchor.UpperLeft, UiFactory.MutedText);
            help.rectTransform.anchorMin = help.rectTransform.anchorMax = new Vector2(0, 1);
            help.rectTransform.pivot = new Vector2(0, 1);
            help.rectTransform.anchoredPosition = new Vector2(40, -640);

            // Иконка перетаскивания — поверх всего.
            var ghost = UiFactory.Image(go.transform, "DragGhost", Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90, 90));
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
            return go;
        }

        private static void Title(Transform parent, string text, Vector2 pos)
        {
            var t = UiFactory.Text(parent, "Title", text, 30, TextAnchor.UpperLeft, UiFactory.TextColor);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0, 1);
            t.rectTransform.pivot = new Vector2(0, 1);
            t.rectTransform.anchoredPosition = pos;
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
        /// Ключ от двери: модель Resources/Items/Key (кубики: квадратное кольцо, стержень, бородки; тёмный металл —
        /// ключ читается на белом полу) и префаб на земле Resources/Items/Pickups/Key — ключ висит стоя над полом,
        /// покачивается и вращается вокруг вертикали, над ним тёплый свет. Существующие не перезаписываются без overwrite.
        /// </summary>
        public static void EnsureKeyPrefabs(bool overwrite)
        {
            EnsureFolder(PickupsFolder);
            var modelPath = ItemsResources + "/Key.prefab";
            var pickupPath = PickupsFolder + "/Key.prefab";
            if (overwrite || !File.Exists(modelPath))
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
