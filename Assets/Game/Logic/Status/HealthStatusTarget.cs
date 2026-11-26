using ColorsAction.Combat;

namespace ColorsAction.Status
{
    /// <summary>Носитель эффектов поверх <see cref="Health"/>: урон эффектов (яд, горение) снимает здоровье напрямую.</summary>
    public sealed class HealthStatusTarget : IStatusTarget
    {
        private readonly Health health;

        public HealthStatusTarget(Health health)
        {
            this.health = health;
        }

        public void TakeStatusDamage(float amount, StatusEffect source) => health?.TakeDamage(amount);
    }
}
