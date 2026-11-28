using ColorsAction.Localization;
using System.Collections.Generic;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// То, с чем игрок взаимодействует на F / X: предмет на земле, дверь-срезка и т.п.
    /// <see cref="PlayerInteractor"/> выбирает ближайшее, HUD показывает подсказку.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>false — объект сейчас не участвует (предмет подобран, дверь уже открыта).</summary>
        bool Available { get; }

        /// <summary>Точка, до которой меряется расстояние (на уровне пола).</summary>
        Vector3 InteractPoint { get; }

        /// <summary>Подсказка (ключ локализации с аргументами): «взять: Меч», «открыть дверь»; кнопку добавляет HUD.</summary>
        LocText Prompt { get; }

        /// <summary>Можно ли воспользоваться из этой позиции игрока.</summary>
        bool CanInteract(Vector3 from);

        /// <summary>Текст без кнопки, когда воспользоваться нельзя («Не открывается с этой стороны»); пусто — ничего.</summary>
        LocText BlockedPrompt { get; }

        void Interact(Vector3 from);
    }

    /// <summary>
    /// Протяжённый объект (широкая дверь): расстояние до игрока меряется от ближайшей точки, а не от
    /// <see cref="IInteractable.InteractPoint"/>, чтобы открыть можно было, стоя у любого края.
    /// </summary>
    public interface IInteractableSpan
    {
        Vector3 NearestPoint(Vector3 from);
    }

    /// <summary>Реестр активных <see cref="IInteractable"/>: регистрируются в OnEnable, снимаются в OnDisable.</summary>
    public static class Interactables
    {
        private static readonly List<IInteractable> all = new List<IInteractable>();

        public static IReadOnlyList<IInteractable> All => all;

        public static void Register(IInteractable item)
        {
            if (!all.Contains(item))
                all.Add(item);
        }

        public static void Unregister(IInteractable item) => all.Remove(item);
    }
}
