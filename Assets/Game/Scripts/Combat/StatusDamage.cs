using ColorsAction.Combat;
using ColorsAction.Status;

namespace ColorsAction.Unity
{
    /// <summary>Цель, у которой есть статус-эффекты (враг): уязвимость цели усиливает удар по ней.</summary>
    public interface IStatusHolder
    {
        StatusEffects Status { get; }
    }

    /// <summary>
    /// Урон с учётом статус-эффектов: усиление атакующего (<see cref="StatusEffects.DamageDealtMultiplier"/>) ×
    /// уязвимость цели (<see cref="StatusEffects.DamageTakenMultiplier"/>). Вызывается там, где игрок считает урон,
    /// до <see cref="IHittable.ReceiveHit"/> и цифры урона, чтобы цифра совпадала с уроном.
    /// </summary>
    public static class StatusDamage
    {
        public static float Multiplier(StatusEffects attacker, IHittable target)
        {
            var multiplier = attacker?.DamageDealtMultiplier ?? 1f;
            if (target is IStatusHolder holder && holder.Status != null)
                multiplier *= holder.Status.DamageTakenMultiplier;
            return multiplier;
        }

        public static HitResult Scale(StatusEffects attacker, IHittable target, HitResult result)
        {
            var multiplier = Multiplier(attacker, target);
            return multiplier == 1f ? result : result.WithDamage(result.Damage * multiplier);
        }
    }
}
