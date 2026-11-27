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
    /// Разводной мост 8×3 (<see cref="DrawBridgeShape"/>), как на референсе: две створки по 4 клетки, каждая на шарнире
    /// у своего берега. Ближняя опущена и лежит на каменных опорах; дальняя поднята (с наклоном к игроку) и держится
    /// на двух красных цепях (<see cref="BridgeChain"/>), идущих от её края к П-образной раме за шарниром. У шарнира
    /// дальней створки по бокам — большие красные шестерни; рамы, опоры и настил — в цвете мира.
    /// Цепи рвутся выстрелом из дробовика (Configs/Bridges.json): створка вздрагивает, падает к ближней под своим весом
    /// (шестерни крутятся), бьётся о землю с отскоком, пылью и тряской камеры — проход открыт навсегда
    /// (<see cref="GameSession.OpenedBridges"/>).
    /// Корень — в центре первой клетки на высоте опоры; длина — по локальной +X, ширина — по +Z.
    /// </summary>
    public sealed class DrawBridge : GameBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId_ = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // Геометрия (локально, метры). Настил чуть выше опоры, чтобы не мерцал с полом.
        private const float DeckTop = 0.04f;
        private const float PlankThickness = 0.1f;
        private const float ColliderThickness = 0.25f;
        private const float LeafLength = DrawBridgeShape.LeafLength;
        private const float Near = -0.5f;                                   // ближний край моста, шарнир ближней створки
        private const float Far = DrawBridgeShape.Length - 0.5f;            // дальний край, шарнир дальней створки
        private const float WidthMin = -0.5f;
        private const float WidthMax = DrawBridgeShape.Width - 0.5f;
        private const float WidthMid = (WidthMin + WidthMax) * 0.5f;
        private const float HalfWidth = (WidthMax - WidthMin) * 0.5f;
        private const float GearThickness = 0.12f;
        private const float PostSize = 0.22f;

        private string id;
        private Material worldMaterial;

        private BridgeConfig config;
        private Palette palette;
        private GameSession session;

        private readonly List<Material> materials = new List<Material>();
        private readonly List<Transform> gears = new List<Transform>();
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
            var stone = CreateMaterial("BridgeStone", Shade(world, config.StoneShade));
            var mechanism = palette != null ? palette.Get(config.Color) : new Color(0.9f, 0.15f, 0.12f);
            chainColor = Shade(mechanism, config.GearShade);
            chainMaterial = CreateMaterial("BridgeChain", chainColor);
            var hub = CreateMaterial("BridgeGearHub", Shade(chainColor, 0.6f));

            debrisRoot = new GameObject("Debris").transform;
            debrisRoot.SetParent(transform, false);

            // Ближняя створка: лежит всегда, свой коллайдер настила; по бокам шарнира — каменные опоры.
            var nearLeaf = new GameObject("NearLeaf").transform;
            nearLeaf.SetParent(transform, false);
            nearLeaf.localPosition = new Vector3(Near, 0f, WidthMid);
            BuildLeaf(nearLeaf, plankA, plankB, beam);
            AddDeckCollider(nearLeaf.gameObject);
            foreach (var s in Sides)
                Bearing(new Vector3(Near + 0.22f, 0f, WidthMid + s * (HalfWidth + 0.16f)), stone);

            // Дальняя створка: шарнир у дальнего края, разворот на 180° — створка тянется к ближней и поднимается к игроку.
            leaf = new GameObject("RaisedLeaf").transform;
            leaf.SetParent(transform, false);
            leaf.localPosition = new Vector3(Far, 0f, WidthMid);
            BuildLeaf(leaf, plankA, plankB, beam);
            AddDeckCollider(leaf.gameObject);
            var body = leaf.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            // Красная оковка края, к которой крепятся цепи.
            Part(leaf, chainMaterial, new Vector3(LeafLength - 0.06f, DeckTop - 0.03f, 0f), new Vector3(0.12f, 0.18f, 2f * HalfWidth - 0.1f));

            // Большие шестерни по бокам шарнира дальней створки, ось — через всю ширину.
            foreach (var s in Sides)
                gears.Add(BuildGear(new Vector3(Far, 0f, WidthMid + s * (HalfWidth + GearThickness * 0.5f + 0.02f)), config.GearRadius, chainMaterial, hub));
            Part(transform, beam, new Vector3(Far, 0f, WidthMid), new Vector3(0.12f, 0.12f, 2f * HalfWidth + 2f * GearThickness + 0.1f),
                Quaternion.Euler(0f, 0f, 45f));

            // П-образные рамы на обоих концах: столбы на каменных тумбах по бокам, поперечина над проходом.
            var farGantry = Far + config.GearRadius + 0.3f;
            Gantry(Near - 0.45f, beam, stone);
            Gantry(farGantry, beam, stone);

            angle = open ? 0f : config.RaisedAngle;
            ApplyAngle();
            if (open)
                return;

            // Цепи: от углов края поднятой створки (сторона настила) к верхушкам столбов дальней рамы.
            var top = config.GantryHeight - 0.25f;
            var chains = new (Vector3 from, Vector3 to)[2];
            for (int i = 0; i < 2; i++)
            {
                var s = Sides[i];
                // Створка развёрнута на 180°: её локальная Z смотрит в обратную сторону.
                var z = -s * (HalfWidth - 0.18f);
                chains[i] = (leaf.TransformPoint(new Vector3(LeafLength - 0.1f, DeckTop + 0.06f, z)),
                    transform.TransformPoint(new Vector3(farGantry - PostSize * 0.6f, top, WidthMid + s * (HalfWidth + 0.2f))));
            }

            var chainGo = new GameObject("Chains");
            chainGo.transform.SetParent(transform, false);
            chain = chainGo.AddComponent<BridgeChain>();
            chain.Build(this, chains, chainMaterial, config.ChainLinks);

            impulse = gameObject.AddComponent<CinemachineImpulseSource>();
            impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            impulse.ImpulseDefinition.ImpulseDuration = 0.2f;
        }

        private static readonly float[] Sides = { -1f, 1f };

        /// <summary>
        /// Створка от шарнира по локальной +X на <see cref="LeafLength"/> м, ширина симметрично вокруг Z = 0: доски поперёк хода с узкими щелями,
        /// бортики-балки по краям, поперечины снизу.
        /// </summary>
        private static void BuildLeaf(Transform parent, Material plankA, Material plankB, Material beam)
        {
            // Четыре доски на метр, как было у створки в 2 м.
            const int planks = (int)(LeafLength * 4f);
            const float gap = 0.016f;
            var plankLength = (LeafLength - (planks + 1) * gap) / planks;
            for (int i = 0; i < planks; i++)
            {
                var x = gap + plankLength * 0.5f + i * (plankLength + gap);
                Part(parent, i % 2 == 0 ? plankA : plankB, new Vector3(x, DeckTop - PlankThickness * 0.5f, 0f),
                    new Vector3(plankLength, PlankThickness, 2f * HalfWidth - 0.16f));
            }

            // Бортики: невысокие балки вдоль краёв, чуть выше настила.
            foreach (var s in Sides)
                Part(parent, beam, new Vector3(LeafLength * 0.5f, DeckTop + 0.02f, s * (HalfWidth - 0.07f)), new Vector3(LeafLength, 0.2f, 0.14f));
            // Поперечины под настилом: по краям и через каждый метр.
            for (int i = 0; i <= (int)LeafLength; i++)
                Part(parent, beam, new Vector3(Mathf.Clamp(i, 0.12f, LeafLength - 0.12f), DeckTop - PlankThickness - 0.05f, 0f), new Vector3(0.14f, 0.1f, 2f * HalfWidth));
        }

        private static void AddDeckCollider(GameObject go)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(LeafLength * 0.5f, DeckTop - ColliderThickness * 0.5f, 0f);
            box.size = new Vector3(LeafLength, ColliderThickness, 2f * HalfWidth);
        }

        /// <summary>Каменная опора шарнира: тумба с закруглённой (срезанной) верхушкой, в которой лежит ось створки.</summary>
        private void Bearing(Vector3 at, Material stone)
        {
            Part(transform, stone, at + new Vector3(0f, -0.08f, 0f), new Vector3(0.5f, 0.16f, 0.26f));
            Part(transform, stone, at + new Vector3(0f, 0.08f, 0f), new Vector3(0.3f, 0.26f, 0.2f));
            Part(transform, stone, at + new Vector3(0f, 0.2f, 0f), new Vector3(0.21f, 0.21f, 0.2f), Quaternion.Euler(0f, 0f, 45f));
        }

        /// <summary>П-образная рама поперёк моста на позиции <paramref name="x"/>: тумбы, столбы с накладками, поперечина.</summary>
        private void Gantry(float x, Material wood, Material stone)
        {
            var height = config.GantryHeight;
            foreach (var s in Sides)
            {
                var z = WidthMid + s * (HalfWidth + 0.2f);
                Part(transform, stone, new Vector3(x, 0.3f, z), new Vector3(0.44f, 0.6f, 0.4f));
                Part(transform, wood, new Vector3(x, 0.6f + 0.05f, z), new Vector3(0.48f, 0.1f, 0.44f));
                Part(transform, wood, new Vector3(x, (0.6f + height) * 0.5f, z), new Vector3(PostSize, height - 0.6f, PostSize));
                // Накладка-«ухо» на верхушке, как на референсе.
                Part(transform, wood, new Vector3(x, height - 0.12f, z), new Vector3(PostSize + 0.12f, 0.36f, PostSize - 0.06f));
            }

            Part(transform, wood, new Vector3(x, height - 0.18f, WidthMid), new Vector3(0.16f, 0.16f, 2f * HalfWidth + 0.6f));
        }

        /// <summary>Шестерня: обод из сегментов, зубья, три спицы и ступица; плоскость — XY, ось — локальная Z.</summary>
        private Transform BuildGear(Vector3 center, float radius, Material material, Material hubMaterial)
        {
            var gear = new GameObject("Gear").transform;
            gear.SetParent(transform, false);
            gear.localPosition = center;
            var teeth = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * radius / 0.24f));
            var rim = radius * 0.78f;
            var segment = 2f * Mathf.PI * rim / teeth * 1.08f;
            for (int i = 0; i < teeth; i++)
            {
                var a = 360f * i / teeth;
                var rotation = Quaternion.Euler(0f, 0f, a);
                Part(gear, material, rotation * new Vector3(rim, 0f, 0f), new Vector3(0.16f, segment, GearThickness), rotation);
                var toothRotation = Quaternion.Euler(0f, 0f, a + 180f / teeth);
                Part(gear, material, toothRotation * new Vector3(radius - 0.08f, 0f, 0f), new Vector3(0.16f, 0.12f, GearThickness), toothRotation);
            }

            for (int i = 0; i < 3; i++)
                Part(gear, material, Vector3.zero, new Vector3(rim * 2f, 0.1f, GearThickness * 0.8f), Quaternion.Euler(0f, 0f, 60f * i));
            Part(gear, hubMaterial, Vector3.zero, new Vector3(0.26f, 0.26f, GearThickness * 1.6f), Quaternion.Euler(0f, 0f, 45f));
            return gear;
        }

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
            var source = worldMaterial != null ? worldMaterial : new Material(CurvedWorld.FindLit());
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

        /// <summary>Попадание по цепям: короткая вспышка шестерён и цепей.</summary>
        public void Flash() => flashTimer = config.FlashTime;

        /// <summary>Цепи порваны: мост запоминается опущенным, створка вздрагивает и через паузу падает.</summary>
        public void ChainSnapped()
        {
            session?.OpenedBridges.Add(id);
            chain = null;
            fallDelay = config.FallDelay;
            trembleTimer = config.FallDelay;
            angularVelocity = 0f;
            bouncesLeft = config.Bounces;
        }

        /// <summary>Поворот дальней створки: разворот к ближней (180° вокруг вертикали) и подъём на угол вокруг оси шарнира.</summary>
        private Quaternion LeafRotation(float degrees) => Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(0f, 0f, degrees);

        private void ApplyAngle()
        {
            if (leaf != null)
                leaf.localRotation = LeafRotation(angle);
            // Шестерни у шарнира крутятся вместе со створкой (с передаточным числом).
            var spin = (config.RaisedAngle - angle) * config.GearRatio;
            foreach (var g in gears)
                g.localRotation = Quaternion.Euler(0f, 0f, spin);
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
                // Цепи лопнули: створка вздрагивает, качнувшись назад, и только потом валится вперёд.
                fallDelay -= dt;
                trembleTimer = Mathf.Max(0f, trembleTimer - dt);
                var k = trembleTimer / Mathf.Max(0.01f, config.FallDelay);
                leaf.localRotation = LeafRotation(angle + Mathf.Sin(k * Mathf.PI) * 2.5f + Mathf.Sin(Time.time * 60f) * 0.6f * k);
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

        /// <summary>Удар створки о землю: пыль у края, где створки сходятся, тряска камеры (отскоки — слабее).</summary>
        private void Land(float speed, bool first)
        {
            var strength = first ? 1f : Mathf.Clamp01(speed / 400f);
            var dust = Mathf.RoundToInt(config.LandDust * (first ? 1f : 0.35f));
            if (dust > 0)
            {
                var color = Shade(WorldColor(), 0.85f);
                var edge = Far - LeafLength + 0.15f;
                Debris.Burst(transform.TransformPoint(new Vector3(edge, 0.05f, WidthMin + 0.2f)), color, dust / 2, 0.06f, 2.5f, 0.9f);
                Debris.Burst(transform.TransformPoint(new Vector3(edge, 0.05f, WidthMax - 0.2f)), color, dust - dust / 2, 0.06f, 2.5f, 0.9f);
            }

            if (impulse != null && config.LandShake > 0f)
                impulse.GenerateImpulseWithVelocity(Vector3.down * (config.LandShake * strength));
        }
    }
}
