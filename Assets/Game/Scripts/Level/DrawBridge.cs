using System.Collections.Generic;
using ColorsAction.Configs;
using ColorsAction.Level;
using GameCore.Config;
using GameCore.DI;
using Unity.Cinemachine;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Разводной мост 4×2×2 (<see cref="DrawBridgeShape"/>): две створки по 2 клетки из досок на продольных балках.
    /// Ближняя створка опущена; дальняя поднята на шарнире посередине моста (почти вертикально, с наклоном вперёд)
    /// и держится на красной цепи (<see cref="BridgeChain"/>), натянутой от кольца на ближней створке к её краю.
    /// По бокам шарниров — красные шестерни, остальной мост — в цвете мира. Цепь рвётся выстрелом из дробовика
    /// (Configs/Bridges.json): створка вздрагивает, падает под своим весом (шестерни крутятся), бьётся о землю
    /// с отскоком, пылью и тряской камеры — проход открыт навсегда (<see cref="GameSession.OpenedBridges"/>).
    /// Корень — в центре первой клетки на высоте опоры; длина — по локальной +X, ширина — по +Z.
    /// </summary>
    public sealed class DrawBridge : GameBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId_ = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // Геометрия (локально, метры). Настил чуть выше опоры, чтобы не мерцал с полом.
        private const float DeckTop = 0.04f;
        private const float PlankThickness = 0.09f;
        private const float ColliderThickness = 0.25f;
        private const float LeafLength = DrawBridgeShape.LeafLength;
        private const float Near = -0.5f;                       // ближний край моста
        private const float Hinge = Near + LeafLength;           // шарнир поднятой створки
        private const float WidthMin = -0.5f;
        private const float WidthMax = DrawBridgeShape.Width - 0.5f;
        private const float WidthMid = (WidthMin + WidthMax) * 0.5f;
        private const float GearThickness = 0.1f;

        private string id;
        private Material worldMaterial;

        private BridgeConfig config;
        private Palette palette;
        private GameSession session;

        private readonly List<Material> materials = new List<Material>();
        private readonly List<Transform> bigGears = new List<Transform>();
        private readonly List<Transform> smallGears = new List<Transform>();
        private Material chainMaterial;
        private Color chainColor;
        private Transform leaf;
        private Transform debrisRoot;
        private BridgeChain chain;
        private CinemachineImpulseSource impulse;

        // Падение створки: угол от горизонтали (градусы) и угловая скорость (градусы/с).
        private float angle;
        private float angularVelocity;
        private float fallDelay = -1f;
        private bool falling;
        private int bouncesLeft;
        private float flashTimer;
        private float trembleTimer;

        public string Id => id;
        public BridgeConfig Config => config;
        public Color ChainColor => chainColor;
        public Transform DebrisRoot => debrisRoot;
        public bool IsOpen => session != null && session.OpenedBridges.Contains(id);

        /// <summary>Угол дальней створки от горизонтали: 0 — опущена.</summary>
        public float LeafAngle => angle;

        public bool Falling => falling || fallDelay >= 0f;

        public BridgeChain Chain => chain;

        /// <summary>Вызвать до Inject: id моста и материал мира.</summary>
        public void Init(string bridgeId, Material material)
        {
            id = bridgeId;
            worldMaterial = material;
        }

        protected override void OnConstruct(IResolver resolver)
        {
            config = resolver.Resolve<ConfigService>().Load<BridgeConfig>(ConfigKeys.Bridges);
            palette = resolver.Resolve<Palette>();
            session = resolver.Resolve<GameSession>();
        }

        private void Start()
        {
            if (config == null)
                return;
            Build(IsOpen);
        }

        private void OnDestroy()
        {
            foreach (var m in materials)
                if (m != null)
                    Destroy(m);
        }

        // ------------------------------------------------------------------ модель

        private void Build(bool open)
        {
            var world = WorldColor();
            var deckColor = Shade(world, config.DeckShade);
            var plankA = CreateMaterial("BridgePlank", deckColor);
            var plankB = CreateMaterial("BridgePlankDark", Shade(deckColor, 0.93f));
            var beam = CreateMaterial("BridgeBeam", Shade(world, config.BeamShade));
            var mechanism = palette != null ? palette.Get(config.Color) : new Color(0.9f, 0.15f, 0.12f);
            chainColor = Shade(mechanism, config.GearShade);
            chainMaterial = CreateMaterial("BridgeChain", chainColor);
            var hub = CreateMaterial("BridgeGearHub", Shade(chainColor, 0.6f));

            debrisRoot = new GameObject("Debris").transform;
            debrisRoot.SetParent(transform, false);

            // Ближняя створка: лежит всегда, свой коллайдер настила.
            var nearLeaf = new GameObject("NearLeaf").transform;
            nearLeaf.SetParent(transform, false);
            nearLeaf.localPosition = new Vector3(Near, 0f, 0f);
            BuildLeaf(nearLeaf, plankA, plankB, beam, false);
            AddDeckCollider(nearLeaf.gameObject);
            // Кольцо, к которому прикована цепь: красная пластина на настиле ближе к началу моста.
            Part(nearLeaf, chainMaterial, new Vector3(0.9f, DeckTop + 0.025f, WidthMid), new Vector3(0.26f, 0.05f, 0.26f));
            Part(nearLeaf, hub, new Vector3(0.9f, DeckTop + 0.07f, WidthMid), new Vector3(0.08f, 0.06f, 0.08f));

            // Дальняя створка: поворачивается вокруг шарнира (ось — локальная Z), кинематическое тело для коллайдера.
            leaf = new GameObject("RaisedLeaf").transform;
            leaf.SetParent(transform, false);
            leaf.localPosition = new Vector3(Hinge, 0f, 0f);
            BuildLeaf(leaf, plankA, plankB, beam, true);
            AddDeckCollider(leaf.gameObject);
            var body = leaf.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;

            // Шестерни по бокам: большие у шарнира поднятой створки, поменьше у начала моста; оси — через всю ширину.
            foreach (var z in new[] { WidthMin - GearThickness, WidthMax + GearThickness })
            {
                bigGears.Add(BuildGear(new Vector3(Hinge, 0f, z), config.GearRadius, chainMaterial, hub));
                smallGears.Add(BuildGear(new Vector3(Near + config.SmallGearRadius * 0.55f, 0f, z), config.SmallGearRadius, chainMaterial, hub));
            }

            Axle(new Vector3(Hinge, 0f, WidthMid), beam);
            Axle(new Vector3(Near + config.SmallGearRadius * 0.55f, 0f, WidthMid), beam);

            angle = open ? 0f : config.RaisedAngle;
            ApplyAngle();
            if (open)
                return;

            // Цепь: от кольца на ближней створке к краю поднятой (на стороне настила, обращённой к игроку).
            var from = nearLeaf.TransformPoint(new Vector3(0.9f, DeckTop + 0.09f, WidthMid));
            var to = leaf.TransformPoint(new Vector3(LeafLength - 0.12f, DeckTop + 0.06f, WidthMid));
            var chainGo = new GameObject("Chain");
            chainGo.transform.SetParent(transform, true);
            chain = chainGo.AddComponent<BridgeChain>();
            chain.Build(this, from, to, chainMaterial, config.ChainLinks);

            impulse = gameObject.AddComponent<CinemachineImpulseSource>();
            impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            impulse.ImpulseDefinition.ImpulseDuration = 0.2f;
        }

        /// <summary>
        /// Створка от шарнира по +X на 2 м: доски поперёк хода с узкими щелями, бортики-балки по краям, поперечины снизу.
        /// У поднятой — красная оковка края, к которой крепится цепь.
        /// </summary>
        private void BuildLeaf(Transform parent, Material plankA, Material plankB, Material beam, bool raised)
        {
            const int planks = 8;
            const float gap = 0.016f;
            var plankLength = (LeafLength - (planks + 1) * gap) / planks;
            for (int i = 0; i < planks; i++)
            {
                var x = gap + plankLength * 0.5f + i * (plankLength + gap);
                Part(parent, i % 2 == 0 ? plankA : plankB, new Vector3(x, DeckTop - PlankThickness * 0.5f, WidthMid),
                    new Vector3(plankLength, PlankThickness, WidthMax - WidthMin - 0.16f));
            }

            // Бортики: невысокие балки вдоль краёв, чуть выше настила.
            foreach (var z in new[] { WidthMin + 0.07f, WidthMax - 0.07f })
                Part(parent, beam, new Vector3(LeafLength * 0.5f, DeckTop + 0.02f, z), new Vector3(LeafLength, 0.2f, 0.14f));
            // Поперечины под настилом.
            foreach (var x in new[] { 0.12f, LeafLength * 0.5f, LeafLength - 0.12f })
                Part(parent, beam, new Vector3(x, DeckTop - PlankThickness - 0.05f, WidthMid), new Vector3(0.14f, 0.1f, WidthMax - WidthMin));

            if (raised)
            {
                Part(parent, chainMaterial, new Vector3(LeafLength - 0.06f, DeckTop - 0.02f, WidthMid), new Vector3(0.12f, 0.16f, WidthMax - WidthMin - 0.2f));
                Part(parent, chainMaterial, new Vector3(LeafLength - 0.12f, DeckTop + 0.04f, WidthMid), new Vector3(0.1f, 0.06f, 0.1f));
            }
        }

        private static void AddDeckCollider(GameObject go)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(LeafLength * 0.5f, DeckTop - ColliderThickness * 0.5f, WidthMid);
            box.size = new Vector3(LeafLength, ColliderThickness, WidthMax - WidthMin);
        }

        /// <summary>Шестерня: обод из сегментов, зубья, три спицы и ступица; плоскость — XY, ось — локальная Z.</summary>
        private Transform BuildGear(Vector3 center, float radius, Material material, Material hubMaterial)
        {
            var gear = new GameObject("Gear").transform;
            gear.SetParent(transform, false);
            gear.localPosition = center;
            var teeth = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * radius / 0.21f));
            var rim = radius * 0.8f;
            var segment = 2f * Mathf.PI * rim / teeth * 1.08f;
            for (int i = 0; i < teeth; i++)
            {
                var a = 360f * i / teeth;
                var rotation = Quaternion.Euler(0f, 0f, a);
                Part(gear, material, rotation * new Vector3(rim, 0f, 0f), new Vector3(0.12f, segment, GearThickness), rotation);
                var toothRotation = Quaternion.Euler(0f, 0f, a + 180f / teeth);
                Part(gear, material, toothRotation * new Vector3(radius - 0.07f, 0f, 0f), new Vector3(0.14f, 0.1f, GearThickness), toothRotation);
            }

            for (int i = 0; i < 3; i++)
                Part(gear, material, Vector3.zero, new Vector3(rim * 2f, 0.08f, GearThickness * 0.8f), Quaternion.Euler(0f, 0f, 60f * i));
            Part(gear, hubMaterial, Vector3.zero, new Vector3(0.24f, 0.24f, GearThickness * 1.6f), Quaternion.Euler(0f, 0f, 45f));
            return gear;
        }

        private void Axle(Vector3 center, Material material) =>
            Part(transform, material, center, new Vector3(0.12f, 0.12f, WidthMax - WidthMin + 2f * GearThickness), Quaternion.Euler(0f, 0f, 45f));

        private static Transform Part(Transform parent, Material material, Vector3 position, Vector3 size, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Part";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation ?? Quaternion.identity);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        private Color WorldColor()
        {
            if (!string.IsNullOrEmpty(config.DeckTint))
                return Palette.Parse(config.DeckTint);
            if (worldMaterial != null && worldMaterial.HasProperty(BaseColorId))
                return worldMaterial.GetColor(BaseColorId);
            return new Color(0.86f, 0.86f, 0.88f);
        }

        private Material CreateMaterial(string name, Color color)
        {
            var source = worldMaterial != null ? worldMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var m = new Material(source) { name = name };
            // Без текстуры мира (рамка на каждом кубе): детали читаются чистым цветом.
            m.mainTexture = null;
            if (m.HasProperty("_BaseMap"))
                m.SetTexture("_BaseMap", null);
            SetColor(m, color, Color.black);
            materials.Add(m);
            return m;
        }

        private static void SetColor(Material m, Color color, Color emission)
        {
            if (m.HasProperty(BaseColorId))
                m.SetColor(BaseColorId, color);
            if (m.HasProperty(ColorId_))
                m.SetColor(ColorId_, color);
            if (m.HasProperty(EmissionColorId))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor(EmissionColorId, emission);
            }
        }

        private static Color Shade(Color c, float k) =>
            k >= 1f ? Color.Lerp(c, Color.white, Mathf.Clamp01(k - 1f)) : Color.Lerp(Color.black, c, Mathf.Clamp01(k));

        // ------------------------------------------------------------------ механика

        /// <summary>Попадание по цепи: короткая вспышка шестерён и цепи.</summary>
        public void Flash() => flashTimer = config.FlashTime;

        /// <summary>Цепь порвана: мост запоминается опущенным, створка вздрагивает и через паузу падает.</summary>
        public void ChainSnapped()
        {
            session?.OpenedBridges.Add(id);
            chain = null;
            fallDelay = config.FallDelay;
            trembleTimer = config.FallDelay;
            angularVelocity = 0f;
            bouncesLeft = config.Bounces;
        }

        private void ApplyAngle()
        {
            if (leaf != null)
                leaf.localRotation = Quaternion.Euler(0f, 0f, angle);
            // Шестерни у шарнира крутятся вместе со створкой (с передаточным числом), малые — навстречу.
            var spin = (config.RaisedAngle - angle) * config.GearRatio;
            foreach (var g in bigGears)
                g.localRotation = Quaternion.Euler(0f, 0f, -spin);
            var ratio = config.GearRadius / Mathf.Max(0.05f, config.SmallGearRadius);
            foreach (var g in smallGears)
                g.localRotation = Quaternion.Euler(0f, 0f, spin * ratio);
        }

        private void Update()
        {
            if (config == null || leaf == null)
                return;
            var dt = Time.deltaTime;
            if (flashTimer > 0f)
            {
                flashTimer = Mathf.Max(0f, flashTimer - dt);
                var f = flashTimer / Mathf.Max(0.01f, config.FlashTime);
                f *= f;
                SetColor(chainMaterial, Color.Lerp(chainColor, Color.white, f * 0.7f), Color.white * (f * 0.6f));
            }

            if (fallDelay >= 0f)
            {
                // Цепь лопнула: створка вздрагивает, качнувшись назад, и только потом валится вперёд.
                fallDelay -= dt;
                trembleTimer = Mathf.Max(0f, trembleTimer - dt);
                var k = trembleTimer / Mathf.Max(0.01f, config.FallDelay);
                leaf.localRotation = Quaternion.Euler(0f, 0f, angle + Mathf.Sin(k * Mathf.PI) * 2.5f + Mathf.Sin(Time.time * 60f) * 0.6f * k);
                if (fallDelay < 0f)
                    falling = true;
                return;
            }

            if (!falling)
                return;

            // Падение доски на шарнире: угловое ускорение 3g/(2L)·cos(угла), створка разгоняется к земле.
            var accel = config.FallGravity * 1.5f * Mathf.Abs(Physics.gravity.y) / LeafLength * Mathf.Cos(angle * Mathf.Deg2Rad);
            angularVelocity -= accel * Mathf.Rad2Deg * dt;
            angle += angularVelocity * dt;
            if (angle <= 0f)
            {
                angle = 0f;
                var speed = -angularVelocity;
                Land(speed, bouncesLeft == config.Bounces);
                if (bouncesLeft > 0 && speed > 40f)
                {
                    bouncesLeft--;
                    angularVelocity = speed * config.Bounce;
                }
                else
                {
                    angularVelocity = 0f;
                    falling = false;
                }
            }

            ApplyAngle();
        }

        /// <summary>Удар створки о землю: пыль вдоль края, тряска камеры (первый удар — полная, отскоки — слабее).</summary>
        private void Land(float speed, bool first)
        {
            var strength = first ? 1f : Mathf.Clamp01(speed / 400f);
            var dust = Mathf.RoundToInt(config.LandDust * (first ? 1f : 0.35f));
            if (dust > 0)
            {
                var color = Shade(WorldColor(), 0.85f);
                Debris.Burst(transform.TransformPoint(new Vector3(Hinge + LeafLength - 0.15f, 0.05f, WidthMin + 0.2f)), color, dust / 2, 0.06f, 2.5f, 0.9f);
                Debris.Burst(transform.TransformPoint(new Vector3(Hinge + LeafLength - 0.15f, 0.05f, WidthMax - 0.2f)), color, dust - dust / 2, 0.06f, 2.5f, 0.9f);
            }

            if (impulse != null && config.LandShake > 0f)
                impulse.GenerateImpulseWithVelocity(Vector3.down * (config.LandShake * strength));
        }
    }
}
