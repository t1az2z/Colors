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

        /// <summary>Id оружия из Items.json, которым нанесён удар.</summary>
        public string WeaponId;

        /// <summary>Сколько удар заполняет шкалу выносливости врага (<see cref="ColorsAction.Combat.Poise.HitAmount"/>).</summary>
        public float Poise;
    }

    /// <summary>То, что можно ударить: манекен, враги, разрушаемые объекты.</summary>
    public interface IHittable
    {
        /// <summary>Id цвета цели (Configs/Colors.json) для бонуса урона.</summary>
        string ColorId { get; }

        Vector3 HitPoint { get; }

        void ReceiveHit(HitInfo hit);
    }

    /// <summary>Цель, которую берёт не любое оружие (ящики ломаются только мечом).</summary>
    public interface IWeaponGate
    {
        bool Accepts(string weaponId);

        /// <summary>Удар неподходящим оружием: урона нет, цель только отзывается (дрожь, щепки).</summary>
        void Deflect(Vector3 direction, Color weaponColor);

        /// <summary>Всплывают ли над целью цифры урона.</summary>
        bool ShowsDamageNumbers { get; }
    }

    /// <summary>Проверки <see cref="IWeaponGate"/> в местах, где игрок наносит урон.</summary>
    public static class HitGate
    {
        /// <summary>true — удар отбит (цель отозвалась сама): урон, цифры и фриз-кадры не нужны.</summary>
        public static bool Deflects(IHittable target, string weaponId, Vector3 direction, Color weaponColor)
        {
            if (!(target is IWeaponGate gate) || gate.Accepts(weaponId))
                return false;
            gate.Deflect(direction, weaponColor);
            return true;
        }

        public static bool ShowsDamage(IHittable target) => !(target is IWeaponGate gate) || gate.ShowsDamageNumbers;
    }
}
