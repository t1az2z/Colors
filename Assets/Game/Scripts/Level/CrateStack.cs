using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Level;
using GameCore.Config;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Стопка ломаемых ящиков (цветной барьер «синие ящики», GDD раздел 8): ящики 2×1×1 из досок один на другом,
    /// верхние чуть повёрнуты. Закрывает проход 2 клетки в ширину и <see cref="LevelCrate.Height"/> в высоту — выше
    /// прыжка. Ломается только оружием из Configs/Crates.json (меч): от ударов отлетают доски, при нуле здоровья
    /// стопка разваливается на доски, проход открыт навсегда (<see cref="GameSession.BrokenCrates"/>).
    /// Удар другим оружием отбивается (<see cref="IWeaponGate"/>): ящик вздрагивает, урона нет.
    /// Корень стоит в центре первой клетки, ящики тянутся по локальной +X (<see cref="CrateStackShape"/>).
    /// </summary>
    public sealed class CrateStack : GameBehaviour, IHittable, IWeaponGate
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId_ = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // Размеры ящика (локально, от центра): чуть меньше клеток, чтобы между ящиками читался шов.
        private const float HalfX = 0.94f;
        private const float HalfY = 0.48f;
        private const float HalfZ = 0.44f;
        private const float Slat = 0.05f; // толщина доски
        private const float FrameWidth = 0.12f;

        private string id;
        private int crates = CrateStackShape.DefaultHeight;
        private Material worldMaterial;

        private CrateConfig config;
        private Palette palette;
        private GameSession session;
        private BreakableState state;

        private Transform body;
        private readonly List<Transform> colliders = new List<Transform>();
        private readonly List<Transform> pieces = new List<Transform>();
        private readonly HashSet<Transform> posts = new HashSet<Transform>();
        private Material plankMaterial;
        private Material frameMaterial;
        private Color plankColor;
        private Color frameColor;
        private float shakeTimer;
        private float shakeAmount;
        private Vector3 shakeDirection;
        private float flashTimer;

        public string Id => id;
        public bool IsBroken => state != null && state.Broken;

        public string ColorId => config != null ? config.Color : "blue";

        public Vector3 HitPoint => transform.TransformPoint(new Vector3(0.5f, Mathf.Min(crates, 2) * 0.5f, 0f));

        public bool ShowsDamageNumbers => config != null && config.ShowDamageNumbers;

        /// <summary>Вызвать до Inject: id стопки, сколько ящиков в высоту, материал мира.</summary>
        public void Init(string stackId, int height, Material material)
        {
            id = stackId;
            crates = Mathf.Max(1, height);
            worldMaterial = material;
        }

        protected override void OnConstruct(IResolver resolver)
        {
            config = resolver.Resolve<ConfigService>().Load<CrateConfig>(ConfigKeys.Crates);
            palette = resolver.Resolve<Palette>();
            session = resolver.Resolve<GameSession>();
        }

        private void Start()
        {
            if (config == null)
                return;
            // Разбитые раньше (до смерти игрока) не возвращаются.
            if (session != null && session.BrokenCrates.Contains(id))
            {
                Destroy(gameObject);
                return;
            }

            state = new BreakableState(config.Hp, config.BreakableBy);
            Build();
        }

        private void OnDestroy()
        {
            if (plankMaterial != null)
                Destroy(plankMaterial);
            if (frameMaterial != null)
                Destroy(frameMaterial);
        }

        // ------------------------------------------------------------------ модель

        private void Build()
        {
            var color = !string.IsNullOrEmpty(config.Tint) ? Palette.Parse(config.Tint)
                : palette != null ? palette.Get(config.Color) : new Color(0.18f, 0.42f, 1f);
            plankColor = Shade(color, config.PlankShade);
            frameColor = Shade(color, config.FrameShade);
            plankMaterial = CreateMaterial("CratePlank", plankColor);
            frameMaterial = CreateMaterial("CrateFrame", frameColor);

            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            var collidersRoot = new GameObject("Colliders").transform;
            collidersRoot.SetParent(transform, false);

            for (int k = 0; k < crates; k++)
            {
                // Нижний ящик ровно, каждый следующий повёрнут и сдвинут в другую сторону — стопка «живая».
                var side = k % 2 == 1 ? 1f : -1f;
                var twist = k == 0 ? 0f : config.TopTwist * side;
                var shift = k == 0 ? 0f : config.TopOffset * side;
                var position = new Vector3(0.5f, k + 0.5f, shift);
                var rotation = Quaternion.Euler(0f, twist, 0f);

                var crate = new GameObject($"Crate{k}").transform;
                crate.SetParent(body, false);
                crate.SetLocalPositionAndRotation(position, rotation);
                BuildCrate(crate);

                // Коллайдер отдельно от модели: дрожь от ударов не толкает игрока.
                var hull = new GameObject($"Crate{k}").transform;
                hull.SetParent(collidersRoot, false);
                hull.SetLocalPositionAndRotation(position, rotation);
                hull.gameObject.AddComponent<BoxCollider>().size = new Vector3(2f, 1f, 1f);
                colliders.Add(hull);
            }
        }

        /// <summary>
        /// Ящик-решётка из досок, как на концепте: четыре угловые стойки, на каждой стороне три доски с просветами,
        /// дно из трёх досок, верх открыт.
        /// </summary>
        private void BuildCrate(Transform crate)
        {
            const float gap = 0.09f; // просвет между досками
            var slatH = (2f * HalfY - 2f * gap) / 3f;
            var postX = HalfX - FrameWidth * 0.5f;
            var postZ = HalfZ - FrameWidth * 0.5f;

            // Угловые стойки (тёмные): на них держатся доски.
            foreach (var sx in Sides)
            foreach (var sz in Sides)
                posts.Add(Piece(crate, frameMaterial, new Vector3(sx * postX, 0f, sz * postZ), new Vector3(FrameWidth, 2f * HalfY, FrameWidth)));

            for (int i = 0; i < 3; i++)
            {
                var y = -HalfY + slatH * 0.5f + i * (slatH + gap);
                foreach (var s in Sides)
                {
                    // Длинные стороны (±Z): доски снаружи стоек, на всю длину.
                    Piece(crate, plankMaterial, new Vector3(0f, y, s * (HalfZ + Slat * 0.5f)), new Vector3(2f * HalfX + 0.04f, slatH, Slat));
                    // Торцы (±X): между досками длинных сторон.
                    Piece(crate, plankMaterial, new Vector3(s * (HalfX + Slat * 0.5f), y, 0f), new Vector3(Slat, slatH, 2f * HalfZ));
                }
            }

            // Дно: три доски вдоль X с просветами — видно сверху через открытый верх.
            var bottomW = (2f * HalfZ - 2f * gap) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var z = -HalfZ + bottomW * 0.5f + i * (bottomW + gap);
                Piece(crate, plankMaterial, new Vector3(0f, -HalfY + Slat * 0.5f, z), new Vector3(2f * HalfX - 2f * FrameWidth, Slat, bottomW));
            }
        }

        private static readonly float[] Sides = { -1f, 1f };

        private Transform Piece(Transform parent, Material material, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Plank";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            pieces.Add(go.transform);
            return go.transform;
        }

        private Material CreateMaterial(string name, Color color)
        {
            var source = worldMaterial != null ? worldMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var m = new Material(source) { name = name };
            // Без текстуры мира (рамка на каждом кубе): доски читаются чистым цветом и рамой.
            m.mainTexture = null;
            if (m.HasProperty("_BaseMap"))
                m.SetTexture("_BaseMap", null);
            SetColor(m, color, Color.black);
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

        // ------------------------------------------------------------------ удары

        public bool Accepts(string weaponId) => state == null || state.Accepts(weaponId);

        public void Deflect(Vector3 direction, Color weaponColor)
        {
            if (state == null || state.Broken)
                return;
            Shake(direction, config.ShakeAmount * config.DeflectShake);
            Debris.Burst(HitPoint - Flat(direction) * 0.5f, plankColor, 1, 0.05f, 1.5f, 0.6f);
        }

        public void ReceiveHit(HitInfo hit)
        {
            if (state == null)
                return;
            var result = state.Hit(hit.WeaponId, hit.Damage);
            if (result == BreakHitResult.Ignored)
            {
                Deflect(hit.Direction, hit.WeaponColor);
                return;
            }

            var direction = Flat(hit.Direction);
            Debris.Burst(SurfacePoint(direction), plankColor, config.ChipsPerHit, 0.07f, 3f, 1.2f);
            if (result == BreakHitResult.Broken)
            {
                Break(direction);
                return;
            }

            Shake(direction, config.ShakeAmount * Mathf.Max(1f, hit.Knockback));
            flashTimer = config.FlashTime;
            // Чем меньше здоровья, тем больше досок уже отлетело: стопка на глазах разваливается.
            KnockOffPlanks(direction, Mathf.RoundToInt(pieces.Count * 0.12f * hit.Damage / state.MaxHp * 3f));
        }

        /// <summary>Точка на стороне стопки, обращённой к удару (откуда летят щепки).</summary>
        private Vector3 SurfacePoint(Vector3 direction) => HitPoint - direction * 0.45f;

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
        }

        private void Shake(Vector3 direction, float amount)
        {
            shakeDirection = direction;
            shakeAmount = amount;
            shakeTimer = config.ShakeTime;
        }

        /// <summary>Отбивает несколько досок со стороны удара: они улетают обломками.</summary>
        private void KnockOffPlanks(Vector3 direction, int count)
        {
            if (count <= 0)
                return;
            // Оставляем минимум досок, чтобы до разрушения стопка оставалась стопкой.
            count = Mathf.Min(count, pieces.Count - pieces.Count * 2 / 3);
            var from = SurfacePoint(direction) + Vector3.up * Random.Range(-0.4f, 0.4f);
            for (int n = 0; n < count; n++)
            {
                var best = -1;
                var bestDistance = float.MaxValue;
                for (int i = 0; i < pieces.Count; i++)
                {
                    // Стойки держат ящик до конца: отлетают только доски.
                    if (posts.Contains(pieces[i]))
                        continue;
                    var d = (pieces[i].position - from).sqrMagnitude + Random.Range(0f, 0.15f);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = i;
                    }
                }

                if (best < 0)
                    return;
                var piece = pieces[best];
                pieces.RemoveAt(best);
                Launch(piece, direction, config.BreakForce * 0.6f);
            }
        }

        /// <summary>Разрушение: все доски разлетаются от удара, коллайдеры убираются, проход открыт навсегда.</summary>
        private void Break(Vector3 direction)
        {
            session?.BrokenCrates.Add(id);
            foreach (var c in colliders)
                if (c != null)
                    Destroy(c.gameObject);
            colliders.Clear();
            foreach (var piece in pieces)
                Launch(piece, direction, config.BreakForce);
            pieces.Clear();
            for (int k = 0; k < crates; k++)
                Debris.Burst(transform.TransformPoint(new Vector3(0.5f, k + 0.5f, 0f)), plankColor, 6, 0.08f, 4f, 1.4f);
            Destroy(gameObject, 0.05f);
        }

        /// <summary>Доска становится обломком: своя физика, разлёт от удара и вверх, исчезает через DebrisLife.</summary>
        private void Launch(Transform piece, Vector3 direction, float force)
        {
            piece.SetParent(transform.parent, true);
            piece.gameObject.layer = Debris.Layer;
            piece.gameObject.AddComponent<BoxCollider>();
            var rb = piece.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            var spread = Random.insideUnitSphere * 0.6f;
            var velocity = (direction + Vector3.up * Random.Range(0.4f, 1.1f) + spread) * force * Random.Range(0.6f, 1.1f);
            rb.AddForce(velocity, ForceMode.VelocityChange);
            rb.AddTorque(Random.onUnitSphere * Random.Range(4f, 12f), ForceMode.VelocityChange);
            piece.gameObject.AddComponent<FadeAndDie>().Life = config.DebrisLife * Random.Range(0.8f, 1.2f);
        }

        private void Update()
        {
            if (body == null)
                return;
            var dt = Time.deltaTime;
            if (shakeTimer > 0f)
            {
                shakeTimer = Mathf.Max(0f, shakeTimer - dt);
                var k = shakeTimer / Mathf.Max(0.01f, config.ShakeTime);
                // Затухающая дрожь вдоль удара плюс немного вбок.
                var wobble = Mathf.Sin(shakeTimer * 70f) * shakeAmount * k;
                var side = Vector3.Cross(Vector3.up, shakeDirection) * (Mathf.Sin(shakeTimer * 53f) * shakeAmount * 0.4f * k);
                body.position = transform.position + shakeDirection * wobble + side;
            }
            else if (body.localPosition != Vector3.zero)
            {
                body.localPosition = Vector3.zero;
            }

            if (flashTimer > 0f)
            {
                flashTimer = Mathf.Max(0f, flashTimer - dt);
                var f = flashTimer / Mathf.Max(0.01f, config.FlashTime);
                f *= f;
                SetColor(plankMaterial, Color.Lerp(plankColor, Color.white, f * 0.7f), Color.white * (f * 0.6f));
                SetColor(frameMaterial, Color.Lerp(frameColor, Color.white, f * 0.5f), Color.white * (f * 0.4f));
            }
        }
    }
}
