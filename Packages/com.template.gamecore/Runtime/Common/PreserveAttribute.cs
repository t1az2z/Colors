using System;

namespace GameCore
{
    /// <summary>
    /// Защищает тип или член от вырезания линкером Unity (IL2CPP, managed stripping).
    /// Unity учитывает любой атрибут с именем PreserveAttribute, поэтому ядру не нужна ссылка на UnityEngine.
    /// Ставьте его на классы, которые создаются через рефлексию: сервисы контейнера и классы конфигов.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct |
                    AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Field |
                    AttributeTargets.Property | AttributeTargets.Interface | AttributeTargets.Enum,
        Inherited = false)]
    public sealed class PreserveAttribute : Attribute
    {
    }
}
