using ColorsAction.Configs;
using ColorsAction.Inventory;
using GameCore.Config;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Сундук в одной клетке (Configs/Chests.json, содержимое — из уровня). На E / X сжимается, как куб перед прыжком,
    /// подпрыгивает с покачиванием и открывает крышку; ресурс вылетает кусочками своего цвета и летит к игроку, как
    /// осколки врагов. Ресурс засчитывается, когда долетел последний кусочек (или через <see cref="ChestConfig.GrantTimeout"/>).
    /// Открытый сундук запоминается в <see cref="GameSession.OpenedChests"/> и после возрождения стоит открытым и пустым.
    /// Если ресурс не помещается в инвентарь, сундук не открывается («Инвентарь полон»).
    /// </summary>
    public sealed class Chest : GameBehaviour, IInteractable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private string id;
        private string itemId;
        private int count;
        private Material worldMaterial;

        private ChestConfig config;
        private GameSession session;
        private ItemDatabase items;
        private Palette palette;

        private Transform body;
        private Transform lid;
        private Material bodyMaterial;
        private Material trimMaterial;
        private Material bandMaterial;
        private Color resourceColor = Color.gray;

        private bool opened;
        private bool granted;
        private float openTime = -1f;
        private int piecesLeft;

        // Пружина деформации (сжатие/растяжение по вертикали), как у кубов.
        private float squash;
        private float squashVelocity;

        public string Id => id;

        public bool Available => config != null && !opened;
        public Vector3 InteractPoint => transform.position;
        public string Prompt => config?.Prompt ?? "открыть сундук";
        public string BlockedPrompt => string.Empty;
        public bool CanInteract(Vector3 from) => true;

        /// <summary>Вызвать до Inject: id, ресурс (id из Items.json) и количество, материал мира.</summary>
        public void Init(string chestId, string resourceId, int amount, Material material)
        {
            id = chestId;
            itemId = resourceId;
            count = Mathf.Max(1, amount);
            worldMaterial = material;
        }

        protected override void OnConstruct(IResolver resolver)
        {
            config = resolver.Resolve<ConfigService>().Load<ChestConfig>(ConfigKeys.Chests);
            session = resolver.Resolve<GameSession>();
            items = resolver.Resolve<ItemDatabase>();
            palette = resolver.Resolve<Palette>();
        }

        private void OnEnable() => Interactables.Register(this);
        private void OnDisable() => Interactables.Unregister(this);

        private void Start()
        {
            if (config == null)
                return;
            if (HasItem)
                resourceColor = palette != null ? palette.Get(items.Get(itemId).Color) : Color.gray;
            Build();
            if (session != null && session.OpenedChests.Contains(id))
            {
                // Открыт раньше (до смерти игрока): стоит открытым и пустым.
                opened = true;
                granted = true;
                lid.localRotation = Quaternion.Euler(-config.LidAngle, 0f, 0f);
            }
        }

        private void OnDestroy()
        {
            // Уровень перестраивается, пока кусочки летят: ресурс не должен пропасть.
            Grant();
            if (bodyMaterial != null)
                Destroy(bodyMaterial);
            if (trimMaterial != null)
                Destroy(trimMaterial);
            if (bandMaterial != null)
                Destroy(bandMaterial);
        }

        private bool HasItem => !string.IsNullOrEmpty(itemId) && items != null && items.Exists(itemId);

        public void Interact(Vector3 from)
        {
            if (opened || config == null)
                return;
            // Не влезает в инвентарь — сундук остаётся закрытым (GiveResources сам покажет «Инвентарь полон»).
            if (HasItem && !session.CanGiveResources(itemId, count))
            {
                session.GiveResources(itemId, count);
                squashVelocity -= 3f; // сундук «не поддаётся»
                return;
            }

            opened = true;
            granted = !HasItem;
            openTime = 0f;
            session.OpenedChests.Add(id);
            squashVelocity -= 6f;
        }

        // ------------------------------------------------------------------ открытие

        private void Update()
        {
            if (body == null)
                return;
            var dt = Time.deltaTime;

            if (openTime >= 0f)
            {
                var before = openTime;
                openTime += dt;
                var squashEnd = config.SquashTime;
                var openEnd = squashEnd + config.OpenTime;
                if (openTime < squashEnd)
                {
                    // Сжатие перед «прыжком».
                    squash = Mathf.Lerp(0f, -0.18f, openTime / Mathf.Max(0.01f, squashEnd));
                    squashVelocity = 0f;
                }
                else if (before < squashEnd)
                {
                    // Выпрыгнул: растяжение с покачиванием, вылет ресурса.
                    squashVelocity = 7f;
                    Burst();
                }

                var t = Mathf.Clamp01((openTime - squashEnd) / Mathf.Max(0.01f, config.OpenTime));
                var k = 1f - (1f - t) * (1f - t) * (1f - t);
                var overshoot = Mathf.Sin(t * Mathf.PI) * 12f * (1f - t);
                lid.localRotation = Quaternion.Euler(-(config.LidAngle * k + overshoot), 0f, 0f);

                if (openTime > openEnd && !granted && openTime > squashEnd + config.GrantTimeout)
                    Grant();
                if (openTime > openEnd && granted)
                    openTime = -1f;
            }

            // Пружина деформации: сжатие/растяжение по вертикали с сохранением объёма.
            if (openTime < 0f || openTime >= config.SquashTime)
            {
                squashVelocity += (-squash * 260f - squashVelocity * 14f) * dt;
                squash += squashVelocity * dt;
            }

            var y = 1f + squash;
            var xz = 1f / Mathf.Sqrt(Mathf.Max(0.3f, y));
            body.localScale = new Vector3(xz, y, xz);
        }

        /// <summary>Кусочки ресурса вылетают из сундука и после разлёта летят к игроку.</summary>
        private void Burst()
        {
            if (!HasItem)
                return;
            var player = FindAnyObjectByType<PlayerController>();
            var target = player != null ? player.transform : null;
            var n = Mathf.Clamp(count * config.PiecesPerResource, 1, config.MaxPieces);
            var at = transform.position + Vector3.up * (config.BodyHeight + 0.1f);
            var pieces = Debris.Burst(at, resourceColor, n, config.PieceSize, config.BurstForce, 3f);
            if (target == null)
            {
                Grant();
                return;
            }

            piecesLeft = pieces.Count;
            for (int i = 0; i < pieces.Count; i++)
                pieces[i].FlyToAsExperience(target, 0, config.FlyDelay + i * config.FlyStagger, _ => OnPieceArrived());
        }

        private void OnPieceArrived()
        {
            piecesLeft--;
            if (piecesLeft <= 0)
                Grant();
        }

        private void Grant()
        {
            if (granted || !opened)
                return;
            granted = true;
            if (!HasItem || session == null || session.GiveResources(itemId, count))
                return;

            // Пока кусочки летели, инвентарь заполнился (другой сундук, подобранный предмет): ресурс не пропадает —
            // сундук снова закрыт и его можно открыть позже («Инвентарь полон» уже показан).
            opened = false;
            granted = false;
            openTime = -1f;
            session.OpenedChests.Remove(id);
            if (lid != null)
                lid.localRotation = Quaternion.identity;
        }

        // ------------------------------------------------------------------ модель

        private void Build()
        {
            var tint = !string.IsNullOrEmpty(config.Tint) ? Palette.Parse(config.Tint) : new Color(0.6f, 0.62f, 0.66f);
            var trim = Color.Lerp(Color.black, tint, Mathf.Clamp01(config.TrimShade));
            bodyMaterial = CreateMaterial("ChestBody", tint);
            trimMaterial = CreateMaterial("ChestTrim", trim);
            bandMaterial = config.ShowResourceColor && HasItem ? CreateMaterial("ChestBand", resourceColor) : trimMaterial;

            var w = config.Width;
            var d = config.Depth;
            var h = config.BodyHeight;
            var lh = config.LidHeight;

            body = new GameObject("Body").transform;
            body.SetParent(transform, false);

            // Корпус и окантовка снизу/по углам.
            Piece(body, bodyMaterial, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
            Piece(body, trimMaterial, new Vector3(0f, 0.03f, 0f), new Vector3(w + 0.03f, 0.06f, d + 0.03f));
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Piece(body, trimMaterial, new Vector3(sx * (w * 0.5f - 0.02f), h * 0.5f, sz * (d * 0.5f - 0.02f)),
                    new Vector3(0.07f, h + 0.01f, 0.07f));

            // Крышка вращается вокруг заднего верхнего ребра (локальная −Z).
            lid = new GameObject("Lid").transform;
            lid.SetParent(body, false);
            lid.localPosition = new Vector3(0f, h, -d * 0.5f);
            if (lh > 0f)
            {
                Piece(lid, bodyMaterial, new Vector3(0f, lh * 0.5f, d * 0.5f), new Vector3(w + 0.02f, lh, d + 0.02f));
                // Полоса цвета ресурса поперёк крышки и замок спереди.
                Piece(lid, bandMaterial, new Vector3(0f, lh * 0.5f, d * 0.5f), new Vector3(0.16f, lh + 0.02f, d + 0.04f));
            }

            Piece(lid, trimMaterial, new Vector3(0f, -0.02f, d + 0.025f), new Vector3(0.12f, 0.14f, 0.05f));

            // Один ровный коллайдер на корпус: деформация не толкает игрока.
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, (h + lh) * 0.5f, 0f);
            box.size = new Vector3(w, h + lh, d);
        }

        private Transform Piece(Transform parent, Material material, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Part";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        private Material CreateMaterial(string name, Color color)
        {
            var source = worldMaterial != null ? worldMaterial : new Material(CurvedWorld.FindLit());
            var m = new Material(source) { name = name };
            m.mainTexture = null;
            if (m.HasProperty("_BaseMap"))
                m.SetTexture("_BaseMap", null);
            if (m.HasProperty(BaseColorId))
                m.SetColor(BaseColorId, color);
            if (m.HasProperty(ColorId))
                m.SetColor(ColorId, color);
            return m;
        }
    }
}
