using System;
using System.Collections.Generic;
using ColorsAction.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Иконки предметов для инвентаря и HUD: id предмета (как в Items.json) → спрайт из Assets/Game/Art/UI/Icons.
    /// Иконка нарисована в цвете предмета и не перекрашивается. Для предмета без иконки — плитка <see cref="fallback"/>,
    /// окрашенная в цвет предмета. Новые PNG создаёт меню ColorsAction → UI → Generate Item Icons (существующие не трогает).
    /// </summary>
    [CreateAssetMenu(menuName = "ColorsAction/Item Icons", fileName = "ItemIcons")]
    public sealed class ItemIcons : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string itemId;
            public Sprite sprite;
        }

        [SerializeField] private Entry[] icons = Array.Empty<Entry>();
        [SerializeField] private Sprite fallback;

        private Dictionary<string, Sprite> map;

        public IReadOnlyList<Entry> Icons => icons;

        public Sprite Find(string itemId)
        {
            if (map == null)
            {
                map = new Dictionary<string, Sprite>();
                foreach (var e in icons)
                    if (!string.IsNullOrEmpty(e.itemId) && e.sprite != null)
                        map[e.itemId] = e.sprite;
            }

            return itemId != null && map.TryGetValue(itemId, out var s) ? s : null;
        }

        /// <summary>Показать иконку предмета в <paramref name="image"/>; null — спрятать.</summary>
        public void Apply(Image image, Item item, Color color, float alpha = 1f)
        {
            if (image == null)
                return;
            image.enabled = item != null;
            if (item == null)
                return;
            var sprite = Find(item.Config.Id);
            image.sprite = sprite != null ? sprite : fallback;
            image.preserveAspect = true;
            var c = sprite != null ? Color.white : color;
            c.a *= alpha;
            image.color = c;
        }

        private void OnValidate() => map = null;
    }
}
