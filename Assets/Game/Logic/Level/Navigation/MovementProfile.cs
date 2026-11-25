using System;
using System.Collections.Generic;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Level.Navigation
{
    /// <summary>
    /// Профиль перемещения персонажа (Configs/Movement.json): на какую высоту он шагает и прыгает, откуда спрыгивает
    /// и какими особыми связями пользуется. Карта пути одна на уровень, профиль фильтрует связи при поиске.
    /// </summary>
    [Preserve]
    public sealed class MovementProfile : IIdentified, IConfigPostLoad
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Перепад, который проходится шагом (ступенька, слоп).</summary>
        public float StepHeight { get; set; } = 0.3f;

        /// <summary>Наибольший подъём прыжком; 0 — не прыгает.</summary>
        public float JumpHeight { get; set; }

        /// <summary>Наибольшая высота спрыгивания.</summary>
        public float MaxDrop { get; set; } = 1f;

        /// <summary>Рост персонажа в метрах: столько свободного места нужно над клеткой.</summary>
        public float Height { get; set; } = 1f;

        /// <summary>Ходит ли по диагонали (без срезания углов).</summary>
        public bool Diagonal { get; set; } = true;

        /// <summary>Дополнительная цена прыжка и спрыгивания (чтобы путь предпочитал ровные дорожки).</summary>
        public float JumpCost { get; set; } = 1f;
        public float DropCost { get; set; } = 0.5f;

        /// <summary>Особые связи, доступные профилю (действия точек взаимодействия).</summary>
        public List<InteractionAction> Specials { get; set; } = new List<InteractionAction>();

        public bool CanJump => JumpHeight > StepHeight;

        public bool CanUse(InteractionAction action) => Specials != null && Specials.Contains(action);

        public void OnConfigLoaded()
        {
            Specials ??= new List<InteractionAction>();
            if (StepHeight < 0 || JumpHeight < 0 || MaxDrop < 0 || Height <= 0)
                throw new InvalidOperationException($"movement profile '{Id}': heights must be positive");
        }
    }

    /// <summary>Состояние мира для поиска пути: что есть у персонажа и какие барьеры открыты.</summary>
    public interface INavContext
    {
        bool HasItem(string itemId);
        bool IsOpen(string objectId);
    }

    /// <summary>Контекст из списков: удобен для тестов и наложения в редакторе.</summary>
    public sealed class NavContext : INavContext
    {
        public HashSet<string> Items { get; } = new HashSet<string>();
        public HashSet<string> Open { get; } = new HashSet<string>();

        /// <summary>Считать все предметы имеющимися, а барьеры открытыми (показать "всё, что вообще возможно").</summary>
        public bool Everything { get; set; }

        public bool HasItem(string itemId) => Everything || Items.Contains(itemId);
        public bool IsOpen(string objectId) => Everything || Open.Contains(objectId);
    }

    public static class NavContextExtensions
    {
        public static bool Satisfies(this INavContext context, NavCondition condition)
        {
            if (condition == null)
                return true;
            if (!string.IsNullOrEmpty(condition.RequiredItem) && !context.HasItem(condition.RequiredItem))
                return false;
            return string.IsNullOrEmpty(condition.RequiredOpen) || context.IsOpen(condition.RequiredOpen);
        }
    }
}
