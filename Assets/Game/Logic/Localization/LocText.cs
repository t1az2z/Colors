namespace ColorsAction.Localization
{
    /// <summary>
    /// Текст по ключу локализации с аргументами: так игровые объекты отдают подписи, не зная языка
    /// (переводит <see cref="Strings.Get(LocText)"/>). Ключа нет в таблице — показывается сам ключ (удобно для литералов из конфигов).
    /// </summary>
    public readonly struct LocText
    {
        public readonly string Key;
        public readonly object[] Args;

        public LocText(string key, params object[] args)
        {
            Key = key;
            Args = args;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Key);

        public static readonly LocText None = default;

        public static implicit operator LocText(string key) => new LocText(key);

        public override string ToString() => Key ?? string.Empty;
    }
}
