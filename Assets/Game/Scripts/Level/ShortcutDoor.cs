using ColorsAction.Level;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Односторонняя дверь-срезка (Dark Souls): плита поперёк прохода. Лицевая сторона (поворот объекта, +Z локально)
    /// помечена засовом — только оттуда дверь открывается на F / X. С обратной стороны подсказка
    /// «Не открывается с этой стороны». Открытая дверь уходит в пол и больше не закрывается
    /// (id запоминается в <see cref="GameSession.OpenedShortcuts"/>). Правила — <see cref="ShortcutDoorRules"/>.
    /// </summary>
    public sealed class ShortcutDoor : GameBehaviour, IInteractable
    {
        private const float Thickness = 0.3f;
        private const float ShakeTime = 0.3f;
        private const float SlideTime = 0.9f;

        private string id;
        private float height = 3f;
        private Material material;

        private GameSession session;
        private ShortcutDoorState state;
        private Transform slab;
        private BoxCollider blocker;
        private float openTimer = -1f;

        public string Id => id;
        public bool IsOpen => state != null && state.IsOpen;

        public bool Available => state != null && !state.IsOpen;
        public Vector3 InteractPoint => transform.position;
        public string Prompt => "открыть дверь";
        public string BlockedPrompt => "Не открывается с этой стороны";

        /// <summary>Вызвать до Inject: id, высота в метрах, материал мира (будет затемнён).</summary>
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
            var p = transform.position;
            var open = session != null && session.OpenedShortcuts.Contains(id);
            state = new ShortcutDoorState(id, p.x, p.z, transform.eulerAngles.y, open);
            Build();
            if (open)
                SetOpenedPose();
        }

        private void Build()
        {
            slab = new GameObject("Slab").transform;
            slab.SetParent(transform, false);

            // Плита: в мировом материале, но темнее стен, чтобы проход читался.
            var tint = new Color(0.62f, 0.62f, 0.66f);
            var body = Part("Body", slab, new Vector3(0f, height * 0.5f, 0f), new Vector3(0.98f, height, Thickness), tint);

            // Засов на лицевой стороне (+Z): горизонтальный брус и две скобы.
            var latch = new Color(0.35f, 0.35f, 0.4f);
            var front = Thickness * 0.5f;
            Part("Bar", slab, new Vector3(0f, 1.1f, front + 0.06f), new Vector3(0.8f, 0.14f, 0.12f), latch);
            Part("BracketL", slab, new Vector3(-0.3f, 1.1f, front + 0.03f), new Vector3(0.1f, 0.3f, 0.06f), latch);
            Part("BracketR", slab, new Vector3(0.3f, 1.1f, front + 0.03f), new Vector3(0.1f, 0.3f, 0.06f), latch);

            blocker = body.gameObject.AddComponent<BoxCollider>();
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

        public bool CanInteract(Vector3 from) => state != null && state.CanOpenFrom(from.x, from.z);

        public void Interact(Vector3 from)
        {
            if (state == null || state.TryOpen(from.x, from.z) != DoorOpenResult.Opened)
                return;
            session?.OpenedShortcuts.Add(id);
            openTimer = 0f;
        }

        private void Update()
        {
            if (openTimer < 0f || slab == null)
                return;
            openTimer += Time.deltaTime;

            if (openTimer < ShakeTime)
            {
                // Засов отходит: короткая дрожь перед тем, как плита поедет вниз.
                var s = 1f - openTimer / ShakeTime;
                slab.localPosition = new Vector3(Mathf.Sin(openTimer * 90f) * 0.03f * s, 0f, 0f);
                return;
            }

            var k = Mathf.Clamp01((openTimer - ShakeTime) / SlideTime);
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
