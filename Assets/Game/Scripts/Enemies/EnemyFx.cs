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

    /// <summary>Осколки: маленькие кубики на физике, разлетаются и через секунду-две исчезают.</summary>
    public static class Debris
    {
        private static Material material;

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

        public static List<Rigidbody> Burst(Vector3 at, Color color, int count, float size, float force = 3f, float life = 1.6f)
        {
            var list = new List<Rigidbody>();
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Chip";
                go.layer = Layer;
                go.transform.position = at + Random.insideUnitSphere * size;
                go.transform.rotation = Random.rotation;
                go.transform.localScale = Vector3.one * size * Random.Range(0.7f, 1.3f);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = Material;
                r.SetPropertyBlock(block);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.05f;
                rb.AddForce((Random.onUnitSphere + Vector3.up * 1.2f).normalized * force * Random.Range(0.6f, 1.2f), ForceMode.VelocityChange);
                rb.AddTorque(Random.onUnitSphere * 10f, ForceMode.VelocityChange);
                go.AddComponent<FadeAndDie>().Life = life * Random.Range(0.8f, 1.2f);
                list.Add(rb);
            }

            return list;
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

    /// <summary>
    /// Осколок-опыт: после разлёта по физике летит к игроку с ускорением; долетев, начисляет опыт и исчезает.
    /// </summary>
    public sealed class ExperienceShard : MonoBehaviour
    {
        public Transform Target;
        public int Amount;
        public System.Action<int> Collected;
        public float Delay = 0.8f;

        private float time;
        private Vector3 velocity;
        private Rigidbody body;

        private void Start() => body = GetComponent<Rigidbody>();

        private void Update()
        {
            time += Time.deltaTime;
            if (time < Delay || Target == null)
                return;
            if (body != null)
            {
                velocity = body.linearVelocity;
                Destroy(body);
                body = null;
                foreach (var c in GetComponents<Collider>())
                    Destroy(c);
            }

            var aim = Target.position + Vector3.up * 0.5f;
            var to = aim - transform.position;
            var speed = Mathf.Lerp(4f, 22f, Mathf.Clamp01((time - Delay) / 0.8f));
            velocity = Vector3.Lerp(velocity, to.normalized * speed, 1f - Mathf.Exp(-Time.deltaTime * 8f));
            transform.position += velocity * Time.deltaTime;
            transform.Rotate(Vector3.up, 720f * Time.deltaTime, Space.World);
            if (to.magnitude < 0.45f)
            {
                Collected?.Invoke(Amount);
                Destroy(gameObject);
            }
        }
    }
}
