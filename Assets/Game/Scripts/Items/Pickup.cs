using ColorsAction.Configs;
using ColorsAction.Inventory;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Предмет на земле. Лежит, слегка покачиваясь, подсвечен своим цветом.
    /// Вид — префаб Resources/Items/Pickups/&lt;имя префаба оружия&gt; (<see cref="PickupVisual"/>), без него — модель из рук.
    /// Игрок подбирает его на F / X (<see cref="PlayerInteractor"/>), предмет уходит в сетку инвентаря.
    /// </summary>
    public sealed class Pickup : GameBehaviour, IInteractable
    {
        private static readonly System.Collections.Generic.List<Pickup> all = new System.Collections.Generic.List<Pickup>();

        [SerializeField] private string itemId;

        private GameSession session;
        private ItemDatabase itemsDb;
        private Palette palette;
        private PickupVisual visual;
        private bool taken;
        private float takeTimer;

        public static System.Collections.Generic.IReadOnlyList<Pickup> All => all;
        public string ItemId => itemId;
        public string DisplayName => itemsDb != null && itemsDb.Exists(itemId) ? itemsDb.Get(itemId).Name : itemId;
        public bool Available => !taken;
        public Vector3 InteractPoint => transform.position;
        public string Prompt => "взять: " + DisplayName;
        public string BlockedPrompt => string.Empty;
        public bool CanInteract(Vector3 from) => !taken;
        public void Interact(Vector3 from) => TryTake();

        public void Init(string id) => itemId = id;

        protected override void OnConstruct(IResolver resolver)
        {
            session = resolver.Resolve<GameSession>();
            itemsDb = resolver.Resolve<ItemDatabase>();
            palette = resolver.Resolve<Palette>();
        }

        private void OnEnable()
        {
            all.Add(this);
            Interactables.Register(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
            Interactables.Unregister(this);
        }

        private void Start()
        {
            var config = itemsDb.Get(itemId);
            var color = palette.Get(config.Color);

            // Свой префаб "на земле" (Resources/Items/Pickups/<имя префаба оружия>), иначе — модель из рук со светом.
            var pickupPrefab = string.IsNullOrEmpty(config.Prefab) ? null : Resources.Load<PickupVisual>(PickupPath(config.Prefab));
            if (pickupPrefab != null)
            {
                visual = Instantiate(pickupPrefab, transform, false);
            }
            else
            {
                visual = CreateFallbackVisual(config.Prefab);
                if (visual == null)
                {
                    Debug.LogError($"Pickup: no prefab for '{itemId}'", this);
                    return;
                }
            }

            visual.Setup(color);
        }

        /// <summary>Путь префаба предмета на земле: "Items/Sword" → "Items/Pickups/Sword".</summary>
        public static string PickupPath(string heldPrefab)
        {
            var slash = heldPrefab.LastIndexOf('/');
            var name = slash >= 0 ? heldPrefab.Substring(slash + 1) : heldPrefab;
            var folder = slash >= 0 ? heldPrefab.Substring(0, slash + 1) : string.Empty;
            return folder + "Pickups/" + name;
        }

        /// <summary>
        /// Запасной вид без префаба: модель из рук лежит на боку над полом, над ней точечный свет.
        /// У предмета без модели (лечилка) — склянка из примитивов: кубик-колба с горлышком, стоит вертикально.
        /// </summary>
        private PickupVisual CreateFallbackVisual(string heldPrefab)
        {
            var prefab = string.IsNullOrEmpty(heldPrefab) ? null : Resources.Load<GameObject>(heldPrefab);
            var consumable = itemsDb.Get(itemId).Kind == ItemKind.Consumable;
            if (prefab == null && !consumable)
                return null;
            var root = new GameObject("Visual");
            root.transform.SetParent(transform, false);
            Transform model;
            if (prefab != null)
            {
                model = Instantiate(prefab, root.transform, false).transform;
                model.localRotation = Quaternion.Euler(90f, 0f, 0f);
                model.localPosition = Vector3.up * 0.2f;
            }
            else
            {
                model = BuildFlask(root.transform);
            }

            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = Vector3.up * 0.6f;
            glow.type = LightType.Point;
            glow.range = 3f;
            glow.intensity = 2.5f;
            glow.shadows = LightShadows.None;

            var v = root.AddComponent<PickupVisual>();
            v.Configure(model, glow);
            return v;
        }

        /// <summary>
        /// Склянка из примитивов (пока нет модели): кубическая колба, горлышко и пробка. Ось вращения подбора —
        /// локальная Z модели, поэтому модель повёрнута так, что Z смотрит вверх.
        /// </summary>
        private static Transform BuildFlask(Transform parent)
        {
            var model = new GameObject("Flask").transform;
            model.SetParent(parent, false);
            model.localPosition = Vector3.up * 0.15f;
            model.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            void Part(PrimitiveType type, string name, Vector3 position, Vector3 scale)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(model, false);
                go.transform.localPosition = position;
                go.transform.localScale = scale;
            }

            Part(PrimitiveType.Cube, "Body", new Vector3(0f, 0f, 0.15f), new Vector3(0.3f, 0.3f, 0.3f));
            Part(PrimitiveType.Cube, "Neck", new Vector3(0f, 0f, 0.36f), new Vector3(0.12f, 0.12f, 0.14f));
            Part(PrimitiveType.Cube, "Cork", new Vector3(0f, 0f, 0.46f), new Vector3(0.15f, 0.15f, 0.06f));
            // Цвет предмета: ItemVisual красит все рендереры склянки.
            model.gameObject.AddComponent<ItemVisual>();
            return model;
        }

        private void Update()
        {
            if (visual == null)
                return;
            if (taken)
            {
                takeTimer += Time.deltaTime;
                var k = Mathf.Clamp01(takeTimer / 0.25f);
                transform.localScale = Vector3.one * (1f - k);
                visual.TickTaken(Time.deltaTime);
                if (k >= 1f)
                    Destroy(gameObject);
                return;
            }

            visual.Tick(Time.deltaTime);
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
