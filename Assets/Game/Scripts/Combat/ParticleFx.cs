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
        /// <summary>Брызги при падении в воду: кубики-капли разлетаются в стороны. Цвет — цвет брызг воды (tint).</summary>
        public const string WaterSplash = "WaterSplash";
        /// <summary>Тёплая белая вспышка: парирование, удар по цепи, дуло дробовика. Цвет — из префаба.</summary>
        public const string HitFlash = "HitFlash";
        /// <summary>
        /// Лечение: зелёные спиральные ленты вокруг куба, искры-квадратики и плюсы, поднимающиеся вверх.
        /// Частицы в локальном пространстве — эффект идёт за игроком (<see cref="Follow"/>). Цвет — из префаба.
        /// </summary>
        public const string HealBurst = "HealBurst";

        /// <summary>
        /// Подбор нового предмета (<see cref="PickupSequence"/>): как <see cref="HealBurst"/>, но без плюсиков, белый —
        /// красится в цвет предмета (tint), и по реальному времени (мир в сцене стоит). Идёт за игроком (<see cref="Follow"/>).
        /// </summary>
        public const string PickupBurst = "PickupBurst";

        private static readonly Dictionary<string, ComponentPool<ParticleFx>> pools = new Dictionary<string, ComponentPool<ParticleFx>>();
        private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

        private ParticleSystem[] systems;
        private Color[] baseColors;
        private ComponentPool<ParticleFx> pool;
        private Transform follow;

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
            fx.follow = null;
            var t = fx.transform;
            t.SetPositionAndRotation(position, rotation);
            t.localScale = Vector3.one * scale;
            fx.Tint(tint);
            fx.systems[0].Play(true);
            return fx;
        }

        /// <summary>Вспышка <see cref="HitFlash"/> размера <paramref name="size"/> (как у прежней ParryFlash).</summary>
        public static ParticleFx Flash(Vector3 position, float size = 1f) => Play(HitFlash, position, Color.white, size);

        /// <summary>Держать эффект в точке <paramref name="target"/>, пока он играет (для систем в локальном пространстве).</summary>
        public ParticleFx Follow(Transform target)
        {
            follow = target;
            return this;
        }

        /// <summary>Играет ли сейчас хоть один эффект с этим именем (для автопрогонов).</summary>
        public static bool AnyPlaying(string name)
        {
            if (!pools.TryGetValue(name, out var pool))
                return false;
            foreach (Transform child in pool.Root)
                if (child.gameObject.activeSelf)
                    return true;
            return false;
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
            if (follow != null)
                transform.position = follow.position;
            if (systems[0].IsAlive(true))
                return;
            follow = null;
            if (pool != null)
                pool.Release(this);
            else
                Destroy(gameObject);
        }
    }
}
