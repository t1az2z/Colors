using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Префаб колеса оружия Resources/UI/WeaponWheel по макету Tools/UI/ref_weapon_wheel.png: тёмный круг в рамке
    /// с ромбами сверху и снизу, 6 секторов (белые спрайты, цвет ставит <see cref="WeaponWheel"/>), белая обводка
    /// выбранного, ромб на рамке у выбранного, середина с названием оружия. Спрайты — Art/UI/Wheel
    /// (Tools/UI/make_ui_sprites.py), пивот сектора — центр колеса, поэтому сектор i просто повёрнут на −60°·i.
    /// </summary>
    public static class WeaponWheelPrefabBuilder
    {
        private const string Folder = "Assets/Game/Art/UI/Wheel";
        private const string PrefabPath = "Assets/Game/Resources/UI/WeaponWheel.prefab";
        private const int SectorCount = 6;

        [MenuItem("ColorsAction/UI/Rebuild Weapon Wheel (overwrite)")]
        public static void Build()
        {
            var go = new GameObject("WeaponWheel", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var component = go.AddComponent<WeaponWheel>();

            var root = UiFactory.Rect(go.transform, "Root", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UiFactory.Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var dim = UiFactory.Image(root, "Dim", new Color(0.06f, 0.04f, 0.08f, 0.35f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = false;

            var size = Vector2.one * (WeaponWheel.Radius * 2f + 8f);
            var wheel = UiFactory.Rect(root, "Wheel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            SpriteImage(wheel, "Back", "WheelBack", Vector2.zero);

            var views = new WeaponWheel.SectorView[SectorCount];
            var step = 360f / SectorCount;
            for (int i = 0; i < SectorCount; i++)
            {
                var fill = SpriteImage(wheel, "Sector" + i, "WheelSector", Vector2.zero);
                fill.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -i * step);
                views[i] = new WeaponWheel.SectorView { Rect = fill.rectTransform, Fill = fill };
            }

            // Обводки поверх всех секторов: заходят на зазор и край соседа.
            for (int i = 0; i < SectorCount; i++)
            {
                var outline = SpriteImage(wheel, "Outline" + i, "WheelSectorOutline", Vector2.zero);
                outline.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -i * step);
                outline.enabled = false;
                views[i].Outline = outline;
            }

            for (int i = 0; i < SectorCount; i++)
            {
                var angle = i * step * Mathf.Deg2Rad;
                var mid = (WeaponWheel.SectorInner + WeaponWheel.SectorOuter) * 0.5f + 6f;
                var pos = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * mid;
                var icon = UiFactory.Image(wheel, "Icon" + i, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, Vector2.one * 110f);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                var label = UiFactory.Text(wheel, "Label" + i, "?", 54, TextAnchor.MiddleCenter, new Color(0.96f, 0.88f, 0.86f, 0.75f));
                var lr = label.rectTransform;
                lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);
                lr.pivot = new Vector2(0.5f, 0.5f);
                lr.anchoredPosition = pos;
                lr.sizeDelta = new Vector2(150f, 70f);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                views[i].Icon = icon;
                views[i].Label = label;
            }

            // Ромбы на рамке сверху и снизу; маленький — у выбранного сектора.
            SpriteImage(wheel, "DiamondTop", "WheelDiamond", new Vector2(0f, WeaponWheel.MarkerRadius));
            SpriteImage(wheel, "DiamondBottom", "WheelDiamond", new Vector2(0f, -WeaponWheel.MarkerRadius));
            var marker = SpriteImage(wheel, "Marker", "WheelDiamondSmall", new Vector2(-WeaponWheel.MarkerRadius, 0f));
            marker.gameObject.SetActive(false);

            var hub = SpriteImage(wheel, "Hub", "WheelHubFill", Vector2.zero);
            SpriteImage(wheel, "HubFrame", "WheelHubFrame", Vector2.zero);
            var title = UiFactory.Text(wheel, "Title", "Меч и щит", 36, TextAnchor.MiddleCenter, Color.white);
            Place(title.rectTransform, new Vector2(0f, 12f), new Vector2(270f, 56f));
            title.fontStyle = FontStyle.Bold;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 20;
            title.resizeTextMaxSize = 36;
            var subtitle = UiFactory.Text(wheel, "Subtitle", "в руках", 24, TextAnchor.MiddleCenter, Color.white);
            Place(subtitle.rectTransform, new Vector2(0f, -34f), new Vector2(260f, 36f));

            Set(component, "wheel", wheel);
            Set(component, "group", group);
            Set(component, "hub", hub);
            Set(component, "title", title);
            Set(component, "subtitle", subtitle);
            Set(component, "marker", marker.rectTransform);
            var so = new SerializedObject(component);
            var list = so.FindProperty("sectorViews");
            list.arraySize = SectorCount;
            for (int i = 0; i < SectorCount; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Rect").objectReferenceValue = views[i].Rect;
                e.FindPropertyRelative("Fill").objectReferenceValue = views[i].Fill;
                e.FindPropertyRelative("Outline").objectReferenceValue = views[i].Outline;
                e.FindPropertyRelative("Icon").objectReferenceValue = views[i].Icon;
                e.FindPropertyRelative("Label").objectReferenceValue = views[i].Label;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log("WeaponWheel prefab rebuilt: " + PrefabPath);
        }

        /// <summary>Спрайт в родном размере; пивот прямоугольника — пивот спрайта (у сектора — центр колеса).</summary>
        private static Image SpriteImage(Transform parent, string name, string sprite, Vector2 pos)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/{sprite}.png");
            if (s == null)
                Debug.LogError($"WeaponWheel sprite {sprite} not found (Tools/UI/make_ui_sprites.py)");
            var size = s != null ? s.rect.size : Vector2.one * 64f;
            var pivot = s != null ? s.pivot / s.rect.size : new Vector2(0.5f, 0.5f);
            var image = UiFactory.Image(parent, name, Color.white, new Vector2(0.5f, 0.5f), pivot, pos, size);
            image.sprite = s;
            image.raycastTarget = false;
            return image;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
