using ColorsAction.Localization;
using ColorsAction.Level;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Дверь на ключ: плита поперёк прохода (локальная X — вдоль плиты), окованная полосами, с замком на обеих сторонах.
    /// Открывается с любой стороны на E / X, если в инвентаре есть ключ от неё (ключ выпадает из врага, у которого он
    /// задан в редакторе уровней). Ключ вставляется в замок со стороны игрока, поворачивается, засов дрожит и плита уходит
    /// в пол; ключ тратится, дверь открыта навсегда (<see cref="GameSession.OpenedKeyDoors"/>). Без ключа — «Заперто».
    /// </summary>
    public sealed class KeyDoor : GameBehaviour, IInteractable
    {
        private const float Thickness = 0.3f;
        private const float LockHeight = 1.05f;
        private const float TurnTime = 0.35f;
        private const float ShakeTime = 0.3f;
        private const float SlideTime = 0.9f;

        private static readonly Color SlabTint = new Color(0.6f, 0.6f, 0.64f);
        private static readonly Color IronTint = new Color(0.3f, 0.3f, 0.34f);
        private static readonly Color HoleTint = new Color(0.06f, 0.06f, 0.07f);

        private string id;
        private float height = 3f;
        private Material material;

        private GameSession session;
        private Transform slab;
        private Transform key;
        private Quaternion keyBase;
        private BoxCollider blocker;
        private float openTimer = -1f;

        public string Id => id;
        public bool IsOpen => session != null && session.OpenedKeyDoors.Contains(id);

        public bool Available => !IsOpen;
        public Vector3 InteractPoint => transform.position;
        public LocText Prompt => "prompt.keyDoor";
        public LocText BlockedPrompt => "prompt.keyDoor.locked";

        /// <summary>Вызвать до Inject: id, высота в метрах, материал мира.</summary>
        public void Init(string doorId, float doorHeight, Material worldMaterial)
        {
            id = doorId;
            height = Mathf.Max(1f, doorHeight);
            material = worldMaterial;
        }

        protected override void OnConstruct(IResolver resolver)
        {
            session = resolver.Resolve<GameSession>();
        }

        private void OnEnable() => Interactables.Register(this);
        private void OnDisable() => Interactables.Unregister(this);

        private void Start()
        {
            Build();
            if (IsOpen)
                SetOpenedPose();
        }

        private void Build()
        {
            slab = new GameObject("Slab").transform;
            slab.SetParent(transform, false);

            var body = Part("Body", slab, new Vector3(0f, height * 0.5f, 0f), new Vector3(0.98f, height, Thickness), SlabTint);
            blocker = body.gameObject.AddComponent<BoxCollider>();

            // Окованные полосы и замок — с обеих сторон: дверь открывается откуда угодно.
            foreach (var side in new[] { 1f, -1f })
            {
                var face = side * (Thickness * 0.5f + 0.02f);
                for (var y = 0.35f; y < height - 0.2f; y += 1.1f)
                    Part("Band", slab, new Vector3(0f, y, face), new Vector3(0.98f, 0.12f, 0.04f), IronTint);
                Part("LockPlate", slab, new Vector3(0f, LockHeight, face + side * 0.02f), new Vector3(0.3f, 0.36f, 0.05f), IronTint);
                Part("Keyhole", slab, new Vector3(0f, LockHeight + 0.05f, face + side * 0.05f), new Vector3(0.08f, 0.08f, 0.02f), HoleTint);
                Part("KeyholeSlot", slab, new Vector3(0f, LockHeight - 0.04f, face + side * 0.05f), new Vector3(0.04f, 0.12f, 0.02f), HoleTint);
            }
        }

        private Transform Part(string name, Transform parent, Vector3 position, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            if (material != null)
                r.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            r.SetPropertyBlock(block);
            return go.transform;
        }

        public bool CanInteract(Vector3 from) => Available && session != null && session.HasKey(id);

        public void Interact(Vector3 from)
        {
            if (!CanInteract(from) || !session.TryOpenKeyDoor(id))
                return;
            // Ключ входит в замок со стороны игрока.
            var local = transform.InverseTransformPoint(from);
            var side = local.z >= 0f ? 1f : -1f;
            key = new GameObject("Key").transform;
            key.SetParent(slab, false);
            key.localPosition = new Vector3(0f, LockHeight, side * (Thickness * 0.5f + 0.1f));
            keyBase = Quaternion.LookRotation(new Vector3(0f, 0f, -side));
            key.localRotation = keyBase;
            // Модель ключа (Resources/Items/Key, по концепту): стержнем в замок, кольцом наружу; без неё — из кубиков.
            var model = Resources.Load<GameObject>("Items/Key");
            if (model != null)
            {
                var view = Instantiate(model, key, false).transform;
                view.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                view.localPosition = new Vector3(0f, 0f, 0.02f);
                view.localScale = Vector3.one * 0.6f;
            }
            else
            {
                Part("Shaft", key, new Vector3(0f, 0f, 0.05f), new Vector3(0.04f, 0.04f, 0.2f), IronTint);
                Part("Bow", key, new Vector3(0f, 0f, -0.1f), new Vector3(0.16f, 0.16f, 0.04f), IronTint);
            }
            openTimer = 0f;
        }

        private void Update()
        {
            if (openTimer < 0f || slab == null)
                return;
            openTimer += Time.deltaTime;

            if (openTimer < TurnTime)
            {
                // Ключ поворачивается в замке на четверть оборота.
                var t = openTimer / TurnTime;
                if (key != null)
                    key.localRotation = keyBase * Quaternion.Euler(0f, 0f, 90f * t * t * (3f - 2f * t));
                return;
            }

            var shake = openTimer - TurnTime;
            if (shake < ShakeTime)
            {
                var s = 1f - shake / ShakeTime;
                slab.localPosition = new Vector3(Mathf.Sin(shake * 90f) * 0.03f * s, 0f, 0f);
                return;
            }

            var k = Mathf.Clamp01((shake - ShakeTime) / SlideTime);
            var eased = k * k * (3f - 2f * k);
            slab.localPosition = Vector3.down * (height + 0.05f) * eased;
            // Проход свободен, когда верх плиты ниже шага персонажа.
            if (blocker.enabled && height * (1f - eased) < 0.25f)
                blocker.enabled = false;
            if (k >= 1f)
                SetOpenedPose();
        }

        private void SetOpenedPose()
        {
            openTimer = -1f;
            if (blocker != null)
                blocker.enabled = false;
            if (slab != null)
                slab.gameObject.SetActive(false);
        }
    }
}
