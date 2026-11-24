using System.Collections.Generic;
using GameCore.Events;

namespace GameCore.Unity
{
    /// <summary>
    /// Ищет обработчики шины, которые принадлежат уже уничтоженным MonoBehaviour
    /// (LogAllDestroyedButSubscribedHandlers из Sparta2). Вызывайте после смены сцены в редакторе или в тесте.
    /// </summary>
    public static class EventBusLeakCheck
    {
        public static List<string> FindDestroyedSubscribers(EventBus bus)
        {
            var leaks = new List<string>();
            foreach (var subscription in bus.GetSubscriptions())
            {
                var target = subscription.Value.Target as UnityEngine.Object;
                // Уничтоженный объект Unity не null для C#, но == null для Unity
                if (!ReferenceEquals(target, null) && target == null)
                {
                    leaks.Add($"{subscription.Value.Method.DeclaringType?.Name}.{subscription.Value.Method.Name} " +
                              $"is subscribed to {subscription.Key.Name} but its object is destroyed");
                }
            }

            return leaks;
        }

        public static int LogDestroyedSubscribers(EventBus bus, ILogger logger)
        {
            var leaks = FindDestroyedSubscribers(bus);
            foreach (var leak in leaks)
                logger.Error(nameof(EventBusLeakCheck), leak);
            return leaks.Count;
        }
    }
}
