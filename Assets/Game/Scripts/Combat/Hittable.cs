using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>Данные попадания по цели.</summary>
    public struct HitInfo
    {
        public float Damage;
        public bool ColorBonus;
        public Color WeaponColor;
        public Vector3 Direction;
        public Vector3 Point;
        public float Knockback;
    }

    /// <summary>То, что можно ударить: манекен, враги, разрушаемые объекты.</summary>
    public interface IHittable
    {
        /// <summary>Id цвета цели (Configs/Colors.json) для бонуса урона.</summary>
        string ColorId { get; }

        Vector3 HitPoint { get; }

        void ReceiveHit(HitInfo hit);
    }
}
