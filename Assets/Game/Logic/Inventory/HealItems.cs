using ColorsAction.Combat;

namespace ColorsAction.Inventory
{
    /// <summary>Чем закончилось нажатие быстрого слота.</summary>
    public enum QuickUseResult
    {
        /// <summary>Слот пуст или предмет не используется с быстрого слота.</summary>
        Nothing,

        /// <summary>Лечилка без зарядов: ждёт костра.</summary>
        Empty,

        /// <summary>Здоровье и так полное (или игрок мёртв) — заряд не тратится.</summary>
        NotNeeded,

        /// <summary>Лечилка использована.</summary>
        Healed
    }

    /// <summary>
    /// Лечилки (<see cref="Configs.HealConfig"/>): использование с быстрого слота и восполнение зарядов у костра.
    /// </summary>
    public static class HealItems
    {
        /// <summary>Использовать лечилку: заряд -1, лечение на долю максимума. <paramref name="healed"/> — сколько восстановлено.</summary>
        public static QuickUseResult TryUse(Item item, Health health, out float healed)
        {
            healed = 0f;
            var heal = item?.Config.Heal;
            if (heal == null || health == null)
                return QuickUseResult.Nothing;
            if (item.Charges <= 0)
                return QuickUseResult.Empty;
            if (health.IsDead || health.Current.Value >= health.Max)
                return QuickUseResult.NotNeeded;

            var before = health.Current.Value;
            health.Heal(health.Max * heal.HealFraction);
            healed = health.Current.Value - before;
            item.Charges--;
            return QuickUseResult.Healed;
        }

        /// <summary>Восполнить заряды всех лечилок в инвентаре. true — хоть одна изменилась.</summary>
        public static bool Refill(PlayerInventory inventory)
        {
            if (inventory == null)
                return false;
            var changed = false;
            foreach (var slot in inventory.AllSlots())
            {
                var item = inventory.Get(slot);
                if (item == null || item.Config.Heal == null || item.Charges >= item.MaxCharges)
                    continue;
                item.Charges = item.MaxCharges;
                changed = true;
            }

            if (changed)
                inventory.NotifyItemChanged();
            return changed;
        }
    }
}
