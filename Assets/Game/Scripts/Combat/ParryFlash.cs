using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Вспышка успешного парирования: яркая звезда из лучей-брусков, повёрнутая к камере. Лучи выстреливают
    /// за пару кадров и тают. Время реальное — вспышка видна и во время фриз-кадров (time scale = 0).
    /// </summary>
    public sealed class ParryFlash : MonoBehaviour
    {
        private const int Rays = 8;
        private const float Life = 0.28f;
        private static Material material;

        private readonly Transform[] rays = new Transform[Rays];
        private Transform core;
        private float time;
        private float size;

        private static Material Material
        {
            get
            {
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "ParryFlash (runtime)" };
                    // HDR-цвет выше порога bloom: вспышка светится даже на белом мире.
                    material.SetColor("_BaseColor", new Color(2.6f, 2.35f, 1.5f, 1f));
                }

                return material;
            }
        }

        public static ParryFlash Spawn(Vector3 position, float size = 1f)
        {
            var go = new GameObject("ParryFlash");
            go.transform.position = position;
            var flash = go.AddComponent<ParryFlash>();
            flash.size = size;
            for (int i = 0; i < Rays; i++)
            {
                var ray = Part(go.transform, "Ray");
                // Лучи разной длины, чуть случайный поворот — вспышка не выглядит штампом.
                ray.localRotation = Quaternion.Euler(0f, 0f, i * 360f / Rays + Random.Range(-8f, 8f));
                flash.rays[i] = ray;
            }

            flash.core = Part(go.transform, "Core");
            flash.core.localRotation = Quaternion.Euler(0f, 0f, 45f);
            flash.Apply();
            return flash;
        }

        private static Transform Part(Transform parent, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.layer = Debris.Layer;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private void LateUpdate()
        {
            time += Time.unscaledDeltaTime;
            if (time >= Life)
            {
                Destroy(gameObject);
                return;
            }

            Apply();
        }

        private void Apply()
        {
            var cam = Camera.main;
            if (cam != null)
                transform.rotation = cam.transform.rotation;
            var t = time / Life;
            // Выстрел за ~2 кадра, затем лучи тонеют и укорачиваются.
            var grow = Mathf.Clamp01(t / 0.12f);
            var fade = 1f - Mathf.Clamp01((t - 0.12f) / 0.88f);
            for (int i = 0; i < Rays; i++)
            {
                var length = size * (i % 2 == 0 ? 0.9f : 0.55f) * (0.5f + 0.5f * grow);
                var width = size * 0.07f * fade;
                var ray = rays[i];
                var dir = ray.localRotation * Vector3.up;
                ray.localPosition = dir * (length * 0.5f + size * 0.12f * grow);
                ray.localScale = new Vector3(width, length * fade, width);
            }

            core.localScale = Vector3.one * size * 0.3f * (1f - t) * (0.6f + 0.4f * grow);
        }
    }
}
