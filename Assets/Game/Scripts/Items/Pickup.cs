using ColorsAction.Inventory;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Предмет на земле. Лежит, слегка покачиваясь, подсвечен своим цветом.
    /// Игрок подбирает его на F / X (<see cref="PlayerInteractor"/>), предмет уходит в сетку инвентаря.
    /// </summary>
    public sealed class Pickup : GameBehaviour
    {
        private static readonly System.Collections.Generic.List<Pickup> all = new System.Collections.Generic.List<Pickup>();

        [SerializeField] private string itemId;

        private GameSession session;
        private ItemDatabase itemsDb;
        private Palette palette;
        private Transform visual;
        private float phase;
        private bool taken;
        private float takeTimer;

        public static System.Collections.Generic.IReadOnlyList<Pickup> All => all;
        public string ItemId => itemId;
        public string DisplayName => itemsDb != null && itemsDb.Exists(itemId) ? itemsDb.Get(itemId).Name : itemId;
        public bool Available => !taken;

        public void Init(string id) => itemId = id;

        protected override void OnConstruct(IResolver resolver)
        {
            session = resolver.Resolve<GameSession>();
            itemsDb = resolver.Resolve<ItemDatabase>();
            palette = resolver.Resolve<Palette>();
        }

        private void OnEnable() => all.Add(this);
        private void OnDisable() => all.Remove(this);

        private void Start()
        {
            var config = itemsDb.Get(itemId);
            var prefab = Resources.Load<GameObject>(config.Prefab);
            if (prefab == null)
            {
                Debug.LogError($"Pickup: no prefab for '{itemId}'", this);
                return;
            }

            visual = Instantiate(prefab, transform, false).transform;
            var color = palette.Get(config.Color);
            if (visual.TryGetComponent<ItemVisual>(out var iv))
            {
                iv.SetColor(color, 0.6f);
                iv.StripColliders();
            }

            // Лежит на боку над полом.
            visual.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.localPosition = Vector3.up * 0.12f;

            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.up * 0.6f;
            glow.type = LightType.Point;
            glow.color = color;
            glow.range = 3f;
            glow.intensity = 2.5f;
            glow.shadows = LightShadows.None;
            phase = Random.value * 10f;
        }

        private void Update()
        {
            if (visual == null)
                return;
            phase += Time.deltaTime;
            if (taken)
            {
                takeTimer += Time.deltaTime;
                var k = Mathf.Clamp01(takeTimer / 0.25f);
                transform.localScale = Vector3.one * (1f - k);
                visual.localPosition += Vector3.up * Time.deltaTime * 3f;
                if (k >= 1f)
                    Destroy(gameObject);
                return;
            }

            visual.localPosition = Vector3.up * (0.2f + Mathf.Sin(phase * 2f) * 0.05f);
            visual.localRotation = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, phase * 25f);
        }

        /// <summary>Подобрать. false — инвентарь полон.</summary>
        public bool TryTake()
        {
            if (taken || !session.PickUp(itemId))
                return false;
            taken = true;
            all.Remove(this);
            return true;
        }
    }
}
