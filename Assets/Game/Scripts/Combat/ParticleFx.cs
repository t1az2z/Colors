using System.Collections.Generic;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Разовый эффект на Particle System из префаба Resources/Fx/&lt;имя&gt; (собираются меню ColorsAction/Build FX Prefabs).
    /// Экземпляры берутся из пула; цвет оружия умножается на startColor каждой системы префаба (альфа — из префаба).
    /// Когда все системы отыграли, эффект возвращается в пул.
    /// </summary>
    public sealed class ParticleFx : MonoBehaviour
    {
        public const string ChargeFlash = "ChargeFlash";
        public const string GroundSlam = "GroundSlam";
        public const string SwordTrail = "SwordTrail";

        private static readonly Dictionary<string, ComponentPool<ParticleFx>> pools = new Dictionary<string, ComponentPool<ParticleFx>>();
        private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

        private ParticleSystem[] systems;
        private Color[] baseColors;
        private ComponentPool<ParticleFx> pool;

        /// <summary>Сыграть эффект в точке. <paramref name="scale"/> — масштаб всего эффекта.</summary>
        public static ParticleFx Play(string name, Vector3 position, Color tint, float scale = 1f) =>
            Play(name, position, Quaternion.identity, tint, scale);

        public static ParticleFx Play(string name, Vector3 position, Quaternion rotation, Color tint, float scale = 1f)
        {
            if (!pools.TryGetValue(name, out var pool))
            {
                pool = new ComponentPool<ParticleFx>("Fx_" + name, root => Create(name, root));
                pools[name] = pool;
            }

            var fx = pool.Get();
            if (fx == null)
                return null;
            fx.pool = pool;
            var t = fx.transform;
            t.SetPositionAndRotation(position, rotation);
            t.localScale = Vector3.one * scale;
            fx.Tint(tint);
            fx.systems[0].Play(true);
            return fx;
        }

        /// <summary>Создать экземпляр префаба (без пула): для постоянных эффектов вроде следа меча.</summary>
        public static GameObject Instantiate(string name)
        {
            var prefab = Prefab(name);
            return prefab != null ? Object.Instantiate(prefab) : null;
        }

        private static GameObject Prefab(string name)
        {
            if (!prefabs.TryGetValue(name, out var prefab) || prefab == null)
            {
                prefab = Resources.Load<GameObject>("Fx/" + name);
                if (prefab == null)
                    Debug.LogWarning($"No FX prefab 'Resources/Fx/{name}' (run ColorsAction/Build FX Prefabs)");
                prefabs[name] = prefab;
            }

            return prefab;
        }

        private static ParticleFx Create(string name, Transform root)
        {
            var prefab = Prefab(name);
            var go = prefab != null ? Object.Instantiate(prefab, root, false) : new GameObject(name);
            go.transform.SetParent(root, false);
            go.layer = Debris.Layer;
            var fx = go.AddComponent<ParticleFx>();
            fx.systems = go.GetComponentsInChildren<ParticleSystem>(true);
            if (fx.systems.Length == 0)
                fx.systems = new[] { go.AddComponent<ParticleSystem>() };
            fx.baseColors = new Color[fx.systems.Length];
            for (int i = 0; i < fx.systems.Length; i++)
            {
                fx.systems[i].gameObject.layer = Debris.Layer;
                fx.baseColors[i] = fx.systems[i].main.startColor.color;
            }

            return fx;
        }

        /// <summary>Перекрасить все системы: цвет префаба × цвет оружия.</summary>
        public void Tint(Color tint)
        {
            for (int i = 0; i < systems.Length; i++)
                TintSystem(systems[i], baseColors[i], tint);
        }

        public static void TintSystem(ParticleSystem ps, Color baseColor, Color tint)
        {
            var main = ps.main;
            main.startColor = new Color(baseColor.r * tint.r, baseColor.g * tint.g, baseColor.b * tint.b, baseColor.a);
        }

        private void LateUpdate()
        {
            if (systems[0].IsAlive(true))
                return;
            if (pool != null)
                pool.Release(this);
            else
                Destroy(gameObject);
        }
    }
}
