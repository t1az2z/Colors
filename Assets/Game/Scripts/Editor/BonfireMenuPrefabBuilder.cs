using ColorsAction.Progression;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Префаб меню костра Resources/UI/BonfireMenu по макету Tools/UI/ref_bonfire.png: окно в рамке с ромбами,
    /// заголовок «Костёр», опыт на плашке справа, строки оружия (иконка + 3 карточки апгрейдов из атласа: обычная,
    /// выбранная в золотой рамке, закрытая), панель описания и длинная плашка с подсказкой управления.
    /// Спрайты — Art/UI/Atlas (Tools/UI/make_ui_sprites.py). Координаты — от левого верхнего угла окна, экран 1920×1080.
    /// </summary>
    public static class BonfireMenuPrefabBuilder
    {
        private const string Folder = "Assets/Game/Art/UI/Atlas";
        private const string PrefabPath = "Assets/Game/Resources/UI/BonfireMenu.prefab";

        private const float CardWidth = 445f;
        private const float CardHeight = 175f;
        private const float CardStep = 470f;
        private const float CardsLeft = 300f;
        private const float RowHeight = 205f;

        private static readonly Color TitleColor = new Color(1f, 0.72f, 0.38f, 1f);
        private static readonly Color SoftText = new Color(0.9f, 0.84f, 0.88f, 1f);

        [MenuItem("ColorsAction/UI/Rebuild Bonfire Menu (overwrite)")]
        public static void Build()
        {
            var go = new GameObject("BonfireMenu", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            var view = go.AddComponent<BonfireView>();

            var dim = UiFactory.Image(go.transform, "Dim", new Color(0.05f, 0.03f, 0.06f, 0.5f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true; // мышь не кликает сквозь меню

            var window = Sliced(go.transform, "Panel", "Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1814, 970), 1.05f);
            Picture(window.transform, "DiamondTop", "DiamondLarge", new Vector2(0.5f, 1), new Vector2(0, -6), 0.62f);
            Picture(window.transform, "DiamondBottom", "DiamondLarge", new Vector2(0.5f, 0), new Vector2(0, 6), 0.62f);
            var content = UiFactory.Rect(window.transform, "Content", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);

            var title = UiFactory.Text(content, "Title", "Костёр", 66, TextAnchor.UpperLeft, TitleColor);
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, new Vector2(96, -24), new Vector2(700, 86));
            UiFactory.Outline(title);
            var subtitle = UiFactory.Text(content, "Subtitle", "Здоровье восстановлено, враги вернулись. Улучшения оружия:", 30, TextAnchor.UpperLeft, SoftText);
            Place(subtitle.rectTransform, new Vector2(96, -104), new Vector2(1400, 44));

            // Опыт — плашка справа вверху.
            var xpPlate = Picture(content, "ExperiencePlate", "TitlePlate", new Vector2(0, 1), new Vector2(1580, -66), 0.75f);
            var experience = UiFactory.Text(xpPlate.transform, "Experience", "Опыт  0", 32, TextAnchor.MiddleCenter, UiFactory.TextColor);
            experience.fontStyle = FontStyle.Bold;
            UiFactory.Stretch(experience.rectTransform);
            experience.rectTransform.offsetMin = new Vector2(0, 3);
            UiFactory.Outline(experience);

            var rows = UiFactory.Rect(content, "Rows", new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -168), new Vector2(1700, 400));
            var rowTemplate = BuildRow(rows);

            // Описание — панель в рамке, сообщение (изучено / не хватает) — в её правом нижнем углу.
            var descPanel = Sliced(content, "DescriptionPanel", "Panel", new Vector2(0, 1), new Vector2(0, 1), new Vector2(56, -566), new Vector2(1700, 236), 1.7f);
            Picture(descPanel.transform, "Diamond", "DiamondSmall", new Vector2(0.5f, 1), new Vector2(0, 0), 0.62f);
            var description = UiFactory.Text(descPanel.transform, "Description", "", 30, TextAnchor.UpperLeft, UiFactory.TextColor);
            UiFactory.Stretch(description.rectTransform);
            description.rectTransform.offsetMin = new Vector2(62, 24);
            description.rectTransform.offsetMax = new Vector2(-62, -30);
            var message = UiFactory.Text(descPanel.transform, "Message", "", 28, TextAnchor.LowerRight, TitleColor);
            UiFactory.Stretch(message.rectTransform);
            message.rectTransform.offsetMin = new Vector2(62, 26);
            message.rectTransform.offsetMax = new Vector2(-62, -30);

            var helpPlate = Sliced(content, "HelpPlate", "LongPlate", new Vector2(0, 1), new Vector2(0, 1), new Vector2(56, -838), new Vector2(1700, 76), 1f);
            var help = UiFactory.Text(helpPlate.transform, "Help", "", 24, TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Stretch(help.rectTransform);
            help.rectTransform.offsetMin = new Vector2(60, 2);
            help.rectTransform.offsetMax = new Vector2(-60, 0);

            var so = new SerializedObject(view);
            so.FindProperty("rowsRoot").objectReferenceValue = rows;
            so.FindProperty("rowTemplate").objectReferenceValue = rowTemplate;
            so.FindProperty("experience").objectReferenceValue = experience;
            so.FindProperty("description").objectReferenceValue = description;
            so.FindProperty("message").objectReferenceValue = message;
            so.FindProperty("help").objectReferenceValue = help;
            so.FindProperty("rowHeight").floatValue = RowHeight;
            so.FindProperty("ownedColor").colorValue = new Color(0.78f, 1f, 0.8f, 1f);
            so.FindProperty("availableColor").colorValue = Color.white;
            so.FindProperty("expensiveColor").colorValue = new Color(0.8f, 0.78f, 0.84f, 1f);
            so.FindProperty("lockedColor").colorValue = Color.white;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log("Bonfire menu prefab rebuilt: " + PrefabPath);
        }

        /// <summary>Строка оружия: иконка с разделителем под ней и три карточки апгрейдов.</summary>
        private static GameObject BuildRow(RectTransform parent)
        {
            var row = UiFactory.Rect(parent, "RowTemplate", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(1700, CardHeight));
            var icon = UiFactory.Image(row, "Icon", Color.white, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(130, -4), new Vector2(140, 140));
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Picture(row, "Divider", "DividerShort", new Vector2(0, 1), new Vector2(130, -156), 0.95f);

            var sprites = new[] { Load("BonfireCard"), Load("BonfireCardSelected"), Load("BonfireCardLocked") };
            for (int t = 1; t <= WeaponUpgrades.MaxTier; t++)
            {
                var bg = UiFactory.Image(row, "Tier" + t, Color.white, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(CardsLeft + (t - 1) * CardStep, 0), new Vector2(CardWidth, CardHeight));
                bg.sprite = sprites[0];
                bg.type = Image.Type.Sliced;
                bg.raycastTarget = true;
                Picture(bg.transform, "DiamondTop", "DiamondSmall", new Vector2(0.5f, 1), new Vector2(0, 0), 0.5f);
                Picture(bg.transform, "DiamondBottom", "DiamondSmall", new Vector2(0.5f, 0), new Vector2(0, 0), 0.5f);

                var label = UiFactory.Text(bg.transform, "Label", "", 30, TextAnchor.UpperLeft, UiFactory.TextColor);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.fontStyle = FontStyle.Bold;
                UiFactory.Stretch(label.rectTransform);
                label.rectTransform.offsetMin = new Vector2(34, 70);
                label.rectTransform.offsetMax = new Vector2(-28, -28);

                var plate = Sliced(bg.transform, "StatePlate", "CaptionPlate", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-20, 22), new Vector2(390, 48), 1.25f);
                var state = UiFactory.Text(plate.transform, "State", "", 22, TextAnchor.MiddleCenter, SoftText);
                UiFactory.Stretch(state.rectTransform);
                state.rectTransform.offsetMin = new Vector2(26, 2);
                state.rectTransform.offsetMax = new Vector2(-26, 0);

                var cell = bg.gameObject.AddComponent<BonfireCell>();
                cell.Setup(t, bg, label, state, null);
                var so = new SerializedObject(cell);
                so.FindProperty("cardSprite").objectReferenceValue = sprites[0];
                so.FindProperty("selectedSprite").objectReferenceValue = sprites[1];
                so.FindProperty("lockedSprite").objectReferenceValue = sprites[2];
                so.FindProperty("statePlate").objectReferenceValue = plate.gameObject;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        private static Sprite Load(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/{name}.png");
            if (sprite == null)
                Debug.LogError($"Bonfire sprite {name} not found (Tools/UI/make_ui_sprites.py)");
            return sprite;
        }

        /// <summary>Картинка в родном размере × scale, пивот по центру.</summary>
        private static Image Picture(Transform parent, string name, string sprite, Vector2 anchor, Vector2 pos, float scale)
        {
            var s = Load(sprite);
            var size = s != null ? s.rect.size * scale : Vector2.one * 32f;
            var image = UiFactory.Image(parent, name, Color.white, anchor, new Vector2(0.5f, 0.5f), pos, size);
            image.sprite = s;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>9-slice плашка заданного размера; multiplier — во сколько раз уменьшить бордеры.</summary>
        private static Image Sliced(Transform parent, string name, string sprite, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float multiplier)
        {
            var image = UiFactory.Image(parent, name, Color.white, anchor, pivot, pos, size);
            image.sprite = Load(sprite);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = multiplier;
            image.raycastTarget = false;
            return image;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
