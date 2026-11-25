using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Вспышка успешного парирования: яркая звезда из лучей-брусков, повёрнутая к камере. Лучи выстреливают
    /// за пару кадров и тают. Время реальное — вспышка видна и во время фриз-кадров (time scale = 0).
    /// Вспышки (их много: каждый выстрел) берутся из пула.
    /// </summary>
    public sealed class ParryFlash : MonoBehaviour
    {
        private const int Rays = 8;
        private const float Life = 0.28f;
        private static Material material;
        private static ComponentPool<ParryFlash> pool;

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
            pool ??= new ComponentPool<ParryFlash>("ParryFlash", Create);
            var flash = pool.Get();
            flash.transform.position = position;
            flash.size = size;
            flash.time = 0f;
            // Лучи разной длины, чуть случайный поворот — вспышка не выглядит штампом.
            for (int i = 0; i < Rays; i++)
                flash.rays[i].localRotation = Quaternion.Euler(0f, 0f, i * 360f / Rays + Random.Range(-8f, 8f));
            flash.Apply();
            return flash;
        }

        private static ParryFlash Create(Transform root)
        {
            var go = new GameObject("ParryFlash");
            go.transform.SetParent(root, false);
            var flash = go.AddComponent<ParryFlash>();
            for (int i = 0; i < Rays; i++)
                flash.rays[i] = Part(go.transform, "Ray");
            flash.core = Part(go.transform, "Core");
            flash.core.localRotation = Quaternion.Euler(0f, 0f, 45f);
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
                pool.Release(this);
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
