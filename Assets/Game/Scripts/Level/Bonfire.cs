using GameCore.DI;
using GameCore.Events;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Костёр (Dark Souls): кольцо серых камней, поленья и огонёк из светящихся кубиков. На F / X игрок садится
    /// отдохнуть (<see cref="BonfireRestRequested"/>): игра переходит в меню костра (апгрейды оружия), здоровье и
    /// выносливость восстанавливаются, враги уровня возрождаются, костёр становится точкой возрождения после смерти.
    /// </summary>
    public sealed class Bonfire : GameBehaviour, IInteractable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static readonly Color StoneColor = new Color(0.58f, 0.59f, 0.62f);
        private static readonly Color LogColor = new Color(0.33f, 0.32f, 0.34f);
        private static readonly Color[] FireColors =
        {
            new Color(1f, 0.42f, 0.08f), new Color(1f, 0.62f, 0.12f), new Color(1f, 0.85f, 0.3f), new Color(0.95f, 0.25f, 0.06f)
        };

        private string id;
        private Vector3 restPoint;
        private float restYaw;
        private Material worldMaterial;

        private IEventBus events;
        private System.IDisposable restedSubscription;
        private Transform[] flames;
        private Vector3[] flameBase;
        private float[] flamePhase;
        private Light glow;
        private Material fireMaterial;
        private float flare;

        public string Id => id;

        public bool Available => events != null;
        public Vector3 InteractPoint => transform.position;
        public string Prompt => "отдохнуть у костра";
        public string BlockedPrompt => string.Empty;
        public bool CanInteract(Vector3 from) => true;

        public void Interact(Vector3 from) => events?.Publish(new BonfireRestRequested(id));

        /// <summary>Вызвать до Inject: id, место, где игрок появляется после смерти, материал мира.</summary>
        public void Init(string bonfireId, Vector3 respawnPoint, float respawnYaw, Material material)
        {
            id = bonfireId;
            restPoint = respawnPoint;
            restYaw = respawnYaw;
            worldMaterial = material;
        }

        protected override void OnConstruct(IResolver resolver)
        {
            events = resolver.Resolve<IEventBus>();
            restedSubscription = events.Subscribe<BonfireRested>(OnRested);
        }

        private void OnEnable() => Interactables.Register(this);
        private void OnDisable() => Interactables.Unregister(this);

        private void OnDestroy()
        {
            restedSubscription?.Dispose();
            if (fireMaterial != null)
                Destroy(fireMaterial);
        }

        private void Start() => Build();

        private void OnRested(BonfireRested e)
        {
            if (e.BonfireId != id)
                return;
            // Отдых здесь: после смерти игрок появится у этого костра.
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null)
                player.SetSpawn(restPoint, restYaw);
            flare = 1f;
        }

        // ------------------------------------------------------------------ вид

        private void Build()
        {
            // Кольцо камней.
            const int stones = 8;
            for (int i = 0; i < stones; i++)
            {
                var a = i * Mathf.PI * 2f / stones;
                var s = 0.17f + (i % 3) * 0.03f;
                var p = Part($"Stone{i}", new Vector3(Mathf.Sin(a) * 0.42f, s * 0.5f, Mathf.Cos(a) * 0.42f),
                    new Vector3(s * 1.2f, s, s), StoneColor, worldMaterial);
                p.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg + (i % 2) * 15f, 0f);
            }

            // Поленья крест-накрест.
            for (int i = 0; i < 3; i++)
            {
                var log = Part($"Log{i}", new Vector3(0f, 0.07f + i * 0.04f, 0f), new Vector3(0.09f, 0.09f, 0.62f), LogColor, worldMaterial);
                log.localRotation = Quaternion.Euler(-8f + i * 8f, i * 60f + 15f, 0f);
            }

            // Огонь: кубики, светящиеся своим цветом.
            fireMaterial = FireMaterial();
            const int count = 7;
            flames = new Transform[count];
            flameBase = new Vector3[count];
            flamePhase = new float[count];
            for (int i = 0; i < count; i++)
            {
                var a = i * 2.4f;
                var r = i == 0 ? 0f : 0.1f + (i % 2) * 0.05f;
                var size = i == 0 ? 0.22f : 0.12f + (i % 3) * 0.025f;
                var pos = new Vector3(Mathf.Sin(a) * r, 0.2f + (i == 0 ? 0.05f : 0f) + (i % 3) * 0.06f, Mathf.Cos(a) * r);
                var color = FireColors[i % FireColors.Length];
                var flame = Part($"Flame{i}", pos, Vector3.one * size, color, fireMaterial, color * 2.2f);
                flame.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                flames[i] = flame;
                flameBase[i] = pos;
                flamePhase[i] = Random.value * 10f;
            }

            glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.up * 0.7f;
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.6f, 0.25f);
            glow.range = 5f;
            glow.intensity = 3f;
            glow.shadows = LightShadows.None;

            // Чтобы не заходить в огонь: низкий блок в размер кольца камней.
            var blocker = gameObject.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0f, 0.3f, 0f);
            blocker.size = new Vector3(0.9f, 0.6f, 0.9f);
        }

        private Transform Part(string name, Vector3 position, Vector3 size, Color color, Material material, Color? emission = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            if (material != null)
                r.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            block.SetColor("_Color", color);
            if (emission.HasValue)
                block.SetColor(EmissionColorId, emission.Value);
            r.SetPropertyBlock(block);
            return go.transform;
        }

        private Material FireMaterial()
        {
            var source = worldMaterial != null ? worldMaterial : new Material(CurvedWorld.FindLit());
            var m = new Material(source) { name = "BonfireFire" };
            m.mainTexture = null;
            if (m.HasProperty("_BaseMap"))
                m.SetTexture("_BaseMap", null);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        private void Update()
        {
            if (flames == null)
                return;
            var t = Time.time;
            flare = Mathf.MoveTowards(flare, 0f, Time.deltaTime * 0.8f);
            var boost = 1f + flare * 0.8f;
            for (int i = 0; i < flames.Length; i++)
            {
                // Кубики пляшут: подпрыгивают, крутятся и пульсируют вразнобой.
                var ph = flamePhase[i] + t * (3f + i * 0.4f);
                flames[i].localPosition = flameBase[i] * boost + Vector3.up * (Mathf.Sin(ph) * 0.04f + flare * 0.15f);
                flames[i].localRotation = Quaternion.Euler(Mathf.Sin(ph * 0.7f) * 25f, t * 90f + i * 40f, Mathf.Cos(ph) * 25f);
                var baseSize = i == 0 ? 0.22f : 0.12f + (i % 3) * 0.025f;
                flames[i].localScale = Vector3.one * baseSize * (0.85f + Mathf.Sin(ph * 1.3f) * 0.15f) * boost;
            }

            if (glow != null)
                glow.intensity = (2.6f + Mathf.PerlinNoise(t * 4f, 0.3f) * 1.2f) * boost;
        }
    }
}
