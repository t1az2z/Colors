using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>"!" над головой врага, заметившего игрока: выскакивает с отскоком и висит, пока идёт реакция.</summary>
    public sealed class AlertMarker : MonoBehaviour
    {
        private Text label;
        private RectTransform rect;
        private float time;
        private float duration;
        private Transform anchor;
        private float height;

        public static AlertMarker Create(Transform anchor, float height)
        {
            var go = new GameObject("AlertMarker", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            var marker = go.AddComponent<AlertMarker>();
            marker.rect = (RectTransform)go.transform;
            marker.rect.sizeDelta = new Vector2(100, 140);
            marker.anchor = anchor;
            marker.height = height;
            marker.label = UiFactory.Text(go.transform, "Text", "!", 120, TextAnchor.MiddleCenter, new Color(1f, 0.78f, 0.15f));
            marker.label.fontStyle = FontStyle.Bold;
            UiFactory.Stretch(marker.label.rectTransform);
            var outline = marker.label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.15f, 0.1f, 0.05f, 1f);
            outline.effectDistance = new Vector2(4, -4);
            go.SetActive(false);
            return marker;
        }

        public void Show(float seconds)
        {
            duration = seconds;
            time = 0f;
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        private void LateUpdate()
        {
            if (anchor == null)
            {
                Destroy(gameObject);
                return;
            }

            time += Time.deltaTime;
            if (time >= duration)
            {
                gameObject.SetActive(false);
                return;
            }

            // Выскакивает с перелётом, в конце быстро сжимается.
            var pop = time < 0.18f ? Mathf.Sin(time / 0.18f * Mathf.PI * 0.5f) * 1.25f : Mathf.Lerp(1.25f, 1f, Mathf.Clamp01((time - 0.18f) / 0.15f));
            var tail = Mathf.Clamp01((duration - time) / 0.15f);
            var bob = Mathf.Sin(time * 9f) * 0.04f;
            rect.position = anchor.position + Vector3.up * (height + bob);
            var cam = Camera.main;
            if (cam != null)
                rect.rotation = cam.transform.rotation;
            rect.localScale = Vector3.one * (0.006f * pop * tail);
        }
    }

    /// <summary>
    /// Здоровье, видимое по телу: целый → в трещинах (меньше 2/3) → с отколотыми углами (меньше 1/3).
    /// Трещины — текстура поверх цвета (рисуется кодом), сколы — вдавленные углы меша и разлетающиеся осколки.
    /// </summary>
    public sealed class CrackVisual : MonoBehaviour
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static Texture2D light;
        private static Texture2D heavy;

        private Renderer target;
        private CubeDeformer deformer;
        private Color chipColor;
        private int stage;
        private MaterialPropertyBlock block;

        public int Stage => stage;

        public void Init(Renderer renderer, CubeDeformer cubeDeformer, Color color)
        {
            target = renderer;
            deformer = cubeDeformer;
            chipColor = color;
            block = new MaterialPropertyBlock();
            EnsureUv();
        }

        /// <param name="healthFraction">Доля здоровья 0..1.</param>
        public void Refresh(float healthFraction)
        {
            var next = healthFraction > 0.66f ? 0 : healthFraction > 0.33f ? 1 : 2;
            if (next == stage || target == null)
                return;
            stage = next;
            target.GetPropertyBlock(block, 0);
            block.SetTexture(BaseMapId, stage == 0 ? Texture2D.whiteTexture : stage == 1 ? Light : Heavy);
            target.SetPropertyBlock(block, 0);

            if (stage == 2 && deformer != null)
            {
                // Откалываются два верхних угла и один нижний.
                var corners = new[] { new Vector3(0.5f, 1f, 0.5f), new Vector3(-0.5f, 1f, -0.5f), new Vector3(0.5f, 0f, -0.5f) };
                deformer.SetChips(corners, 0.34f);
                foreach (var c in corners)
                    Debris.Burst(deformer.transform.TransformPoint(c), chipColor, 3, 0.09f * deformer.transform.lossyScale.x / 0.8f);
            }
        }

        /// <summary>У меша из Blender нет развёртки: проецируем UV по доминирующей оси нормали (кубическая проекция).</summary>
        private void EnsureUv()
        {
            var mesh = deformer != null ? deformer.Mesh : null;
            if (mesh == null || (mesh.uv != null && mesh.uv.Length == mesh.vertexCount))
                return;
            var v = mesh.vertices;
            var n = mesh.normals;
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var a = new Vector3(Mathf.Abs(n[i].x), Mathf.Abs(n[i].y), Mathf.Abs(n[i].z));
                uv[i] = a.x >= a.y && a.x >= a.z ? new Vector2(v[i].z + 0.5f, v[i].y)
                    : a.y >= a.z ? new Vector2(v[i].x + 0.5f, v[i].z + 0.5f)
                    : new Vector2(v[i].x + 0.5f, v[i].y);
            }

            mesh.uv = uv;
        }

        private static Texture2D Light => light != null ? light : light = MakeCracks(7, 1234, 1.6f);
        private static Texture2D Heavy => heavy != null ? heavy : heavy = MakeCracks(16, 4321, 2.6f);

        /// <summary>Белая текстура с тёмными ветвящимися трещинами (умножается на цвет материала).</summary>
        private static Texture2D MakeCracks(int count, int seed, float width)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
                px[i] = Color.white;
            var rnd = new System.Random(seed);
            for (int c = 0; c < count; c++)
            {
                var p = new Vector2((float)rnd.NextDouble() * size, (float)rnd.NextDouble() * size);
                var angle = (float)rnd.NextDouble() * Mathf.PI * 2f;
                var length = 40 + rnd.Next(80);
                for (int s = 0; s < length; s++)
                {
                    angle += ((float)rnd.NextDouble() - 0.5f) * 0.9f;
                    p += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1.5f;
                    var w = width * (1f - s / (float)length * 0.7f);
                    Stamp(px, size, p, w);
                    if (rnd.NextDouble() < 0.03)
                        angle += (rnd.NextDouble() < 0.5 ? -1 : 1) * 0.9f; // ветвление-излом
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static void Stamp(Color[] px, int size, Vector2 p, float r)
        {
            var x0 = Mathf.FloorToInt(p.x - r - 1);
            var y0 = Mathf.FloorToInt(p.y - r - 1);
            for (int y = y0; y <= y0 + r * 2 + 2; y++)
            for (int x = x0; x <= x0 + r * 2 + 2; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), p);
                var a = Mathf.Clamp01(r - d + 0.5f);
                if (a <= 0f)
                    continue;
                var ix = ((x % size) + size) % size;
                var iy = ((y % size) + size) % size;
                var c = px[iy * size + ix];
                var v = Mathf.Min(c.r, Mathf.Lerp(1f, 0.18f, a));
                px[iy * size + ix] = new Color(v, v, v, 1f);
            }
        }
    }

    /// <summary>
    /// Осколки: маленькие кубики на физике, разлетаются и через секунду-две исчезают (или летят к игроку как опыт).
    /// Кубики берутся из пула и возвращаются в него — смерть врага не создаёт и не уничтожает объекты.
    /// </summary>
    public static class Debris
    {
        private static Material material;
        private static ComponentPool<DebrisChip> pool;
        private static MaterialPropertyBlock block;

        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Debris (runtime)", enableInstancing = true };
                    material.SetFloat("_Smoothness", 0.2f);
                }

                return material;
            }
        }

        /// <summary>Слой осколков: не мешают движению кубов (их коллизия этот слой не видит).</summary>
        public const int Layer = 2; // Ignore Raycast

        internal static ComponentPool<DebrisChip> Pool => pool ??= new ComponentPool<DebrisChip>("Debris", DebrisChip.Create);

        public static List<DebrisChip> Burst(Vector3 at, Color color, int count, float size, float force = 3f, float life = 1.6f)
        {
            var list = new List<DebrisChip>(count);
            block ??= new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            for (int i = 0; i < count; i++)
            {
                var chip = Pool.Get();
                chip.Launch(at + Random.insideUnitSphere * size, Random.rotation, Vector3.one * size * Random.Range(0.7f, 1.3f), block,
                    (Random.onUnitSphere + Vector3.up * 1.2f).normalized * force * Random.Range(0.6f, 1.2f),
                    Random.onUnitSphere * 10f, life * Random.Range(0.8f, 1.2f));
                list.Add(chip);
            }

            return list;
        }
    }

    /// <summary>
    /// Осколок из пула: кубик на физике. Обычно уменьшается к концу жизни и возвращается в пул; осколок-опыт
    /// (<see cref="FlyToAsExperience"/>) после разлёта летит к игроку с ускорением, долетев — начисляет опыт.
    /// </summary>
    public sealed class DebrisChip : MonoBehaviour
    {
        private Rigidbody body;
        private Collider box;
        private Renderer view;
        private Vector3 scale;
        private float life;
        private float time;

        // Опыт
        private Transform target;
        private int amount;
        private System.Action<int> collected;
        private float delay;
        private bool flying;
        private Vector3 velocity;

        public Rigidbody Body => body;

        internal static DebrisChip Create(Transform root)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Chip";
            go.layer = Debris.Layer;
            go.transform.SetParent(root, false);
            var chip = go.AddComponent<DebrisChip>();
            chip.box = go.GetComponent<Collider>();
            chip.view = go.GetComponent<Renderer>();
            chip.view.sharedMaterial = Debris.Material;
            chip.body = go.AddComponent<Rigidbody>();
            chip.body.mass = 0.05f;
            return chip;
        }

        internal void Launch(Vector3 position, Quaternion rotation, Vector3 size, MaterialPropertyBlock color, Vector3 velocityChange,
            Vector3 torque, float lifetime)
        {
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = size;
            scale = size;
            view.SetPropertyBlock(color);
            box.enabled = true;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            body.AddForce(velocityChange, ForceMode.VelocityChange);
            body.AddTorque(torque, ForceMode.VelocityChange);
            life = lifetime;
            time = 0f;
            target = null;
            collected = null;
            flying = false;
        }

        /// <summary>Сделать осколок опытом: через <paramref name="startDelay"/> секунд летит к цели и начисляет <paramref name="value"/>.</summary>
        public void FlyToAsExperience(Transform to, int value, float startDelay, System.Action<int> onCollected)
        {
            target = to;
            amount = value;
            delay = startDelay;
            collected = onCollected;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            time += dt;
            if (collected != null && target != null)
            {
                UpdateExperience(dt);
                return;
            }

            // Обычный осколок (или опыт без цели): уменьшается к концу жизни и уходит в пул.
            var k = Mathf.Clamp01((life - time) / 0.4f);
            transform.localScale = scale * k;
            if (time >= life)
                Debris.Pool.Release(this);
        }

        private void UpdateExperience(float dt)
        {
            if (time < delay)
                return;
            if (!flying)
            {
                // Разлёт кончился: физика выключается, дальше осколок ведёт себя сам.
                flying = true;
                velocity = body.linearVelocity;
                body.isKinematic = true;
                box.enabled = false;
            }

            var aim = target.position + Vector3.up * 0.5f;
            var to = aim - transform.position;
            var speed = Mathf.Lerp(4f, 22f, Mathf.Clamp01((time - delay) / 0.8f));
            velocity = Vector3.Lerp(velocity, to.normalized * speed, 1f - Mathf.Exp(-dt * 8f));
            transform.position += velocity * dt;
            transform.Rotate(Vector3.up, 720f * dt, Space.World);
            if (to.magnitude < 0.45f)
            {
                var done = collected;
                collected = null;
                done?.Invoke(amount);
                Debris.Pool.Release(this);
            }
        }
    }

    /// <summary>Уменьшается к концу жизни и исчезает.</summary>
    public sealed class FadeAndDie : MonoBehaviour
    {
        public float Life = 1.5f;
        private float time;
        private Vector3 scale;

        private void Start() => scale = transform.localScale;

        private void Update()
        {
            time += Time.deltaTime;
            var k = Mathf.Clamp01((Life - time) / 0.4f);
            transform.localScale = scale * k;
            if (time >= Life)
                Destroy(gameObject);
        }
    }
}
