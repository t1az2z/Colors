using System.Collections.Generic;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Усиленное свечение оружия в руке (заряд атаки): пока его поддерживают <see cref="Hold"/> каждый кадр,
    /// свечение тянется к заданному уровню, без поддержки — гаснет до обычного свечения оружия в руках.
    /// У стрелкового оружия (<see cref="For"/> с длиной дула) цветная часть не светится — раскаляется само дуло:
    /// раструб ствола (вершины модели в пределах длины дула от переднего края) перекрашивается от металла
    /// через красный и оранжевый до белого.
    /// </summary>
    public sealed class WeaponGlow : MonoBehaviour
    {
        /// <summary>Добавка к эмиссии при уровне 1.</summary>
        private const float Boost = 3f;

        /// <summary>Уровень, на котором дуло уже белое: заряд держит свечение до 0.6, полный заряд и вспышка — выше.</summary>
        private const float WhiteHotLevel = 0.6f;

        /// <summary>Накал дула: металл → красный → оранжевый → белый.</summary>
        private static readonly Color[] HeatColors =
        {
            new Color(0.36f, 0.37f, 0.42f),
            new Color(0.85f, 0.14f, 0.06f),
            new Color(1f, 0.55f, 0.18f),
            new Color(1f, 0.97f, 0.9f)
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static Material muzzleMaterial;

        private ItemVisual visual;
        private Color color;
        private float baseGlow;
        private float target;
        private float current;
        private float heldUntil;
        private bool dirty;
        private Renderer muzzle;
        private MaterialPropertyBlock block;

        /// <param name="muzzleLength">Длина дула от переднего края модели (ствол вдоль +Z), м: больше 0 —
        /// раскаляется дуло, а не цветная часть.</param>
        public static WeaponGlow For(GameObject weapon, Color color, float baseGlow, float muzzleLength = 0f)
        {
            if (weapon == null || !weapon.TryGetComponent<ItemVisual>(out var visual))
                return null;
            if (!weapon.TryGetComponent<WeaponGlow>(out var glow))
                glow = weapon.AddComponent<WeaponGlow>();
            glow.visual = visual;
            glow.color = color;
            glow.baseGlow = baseGlow;
            if (muzzleLength > 0f && glow.muzzle == null)
                glow.muzzle = CreateMuzzle(weapon, muzzleLength);
            return glow;
        }

        /// <summary>Держать свечение на уровне <paramref name="level"/> (0..1) в этом кадре.</summary>
        public void Hold(float level)
        {
            target = Mathf.Clamp01(level);
            heldUntil = Time.time + 0.05f;
        }

        /// <summary>Мгновенная вспышка до уровня 1, дальше гаснет сама.</summary>
        public void Flash()
        {
            current = 1f;
            dirty = true;
        }

        private void LateUpdate()
        {
            if (Time.time > heldUntil)
                target = 0f;
            var next = Mathf.MoveTowards(current, target, (target > current ? 6f : 4f) * Time.deltaTime);
            if (!dirty && Mathf.Approximately(next, current))
                return;
            current = next;
            dirty = false;
            if (muzzle != null)
                UpdateMuzzle();
            else if (visual != null)
                visual.SetColor(color, baseGlow + current * Boost);
        }

        private void UpdateMuzzle()
        {
            var on = current > 0.01f;
            if (muzzle.enabled != on)
                muzzle.enabled = on;
            if (!on)
                return;
            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, Heat(Mathf.Clamp01(current / WhiteHotLevel)));
            muzzle.SetPropertyBlock(block);
        }

        private static Color Heat(float t)
        {
            var x = t * (HeatColors.Length - 1);
            var i = Mathf.Min((int)x, HeatColors.Length - 2);
            return Color.Lerp(HeatColors[i], HeatColors[i + 1], x - i);
        }

        /// <summary>
        /// Копия раструба поверх модели: треугольники, все вершины которых не дальше <paramref name="length"/>
        /// от переднего края, чуть раздутые по нормалям, чтобы не мерцать с оригиналом. Неотображаемая модель
        /// (меш без Read/Write) — null, тогда светится цветная часть, как у остального оружия.
        /// </summary>
        private static Renderer CreateMuzzle(GameObject weapon, float length)
        {
            var filter = weapon.GetComponentInChildren<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable)
                return null;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var front = float.MinValue;
            foreach (var v in vertices)
                front = Mathf.Max(front, v.z);
            var from = front - length;

            var triangles = mesh.triangles;
            var heatVertices = new List<Vector3>();
            var heatTriangles = new List<int>();
            var map = new Dictionary<int, int>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                if (vertices[triangles[i]].z < from || vertices[triangles[i + 1]].z < from || vertices[triangles[i + 2]].z < from)
                    continue;
                for (int k = 0; k < 3; k++)
                {
                    var index = triangles[i + k];
                    if (!map.TryGetValue(index, out var n))
                    {
                        n = heatVertices.Count;
                        map[index] = n;
                        var push = index < normals.Length ? normals[index] * 0.004f : Vector3.zero;
                        heatVertices.Add(vertices[index] + push);
                    }

                    heatTriangles.Add(n);
                }
            }

            if (heatTriangles.Count == 0)
                return null;
            var heat = new Mesh { name = "MuzzleHeat" };
            heat.SetVertices(heatVertices);
            heat.SetTriangles(heatTriangles, 0);
            heat.RecalculateNormals();
            heat.RecalculateBounds();

            var go = new GameObject("MuzzleHeat");
            go.layer = filter.gameObject.layer;
            go.transform.SetParent(filter.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = heat;
            var renderer = go.AddComponent<MeshRenderer>();
            if (muzzleMaterial == null)
                muzzleMaterial = new Material(CurvedWorld.FindUnlit()) { name = "MuzzleHeat (runtime)" };
            renderer.sharedMaterial = muzzleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return renderer;
        }
    }
}
