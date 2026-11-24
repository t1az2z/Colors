using System;

namespace GameCore
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Логгер ядра. Движок подставляет свою реализацию: Debug.Log в Unity, GD.Print в Godot.
    /// </summary>
    public interface ILogger
    {
        void Log(LogLevel level, string tag, string message);
        void LogException(Exception exception, string tag);
    }

    public static class LoggerExtensions
    {
        public static void Debug(this ILogger logger, string tag, string message) => logger.Log(LogLevel.Debug, tag, message);
        public static void Info(this ILogger logger, string tag, string message) => logger.Log(LogLevel.Info, tag, message);
        public static void Warning(this ILogger logger, string tag, string message) => logger.Log(LogLevel.Warning, tag, message);
        public static void Error(this ILogger logger, string tag, string message) => logger.Log(LogLevel.Error, tag, message);
    }

    public sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new NullLogger();

        public void Log(LogLevel level, string tag, string message)
        {
        }

        public void LogException(Exception exception, string tag)
        {
        }
    }

    /// <summary>
    /// Пишет в System.Console. Подходит для тестов и серверных сборок.
    /// </summary>
    public sealed class ConsoleLogger : ILogger
    {
        public LogLevel MinLevel { get; set; } = LogLevel.Debug;

        public void Log(LogLevel level, string tag, string message)
        {
            if (level < MinLevel)
                return;
            Console.WriteLine($"[{level}] [{tag}] {message}");
        }

        public void LogException(Exception exception, string tag)
        {
            Console.WriteLine($"[Exception] [{tag}] {exception}");
        }
    }
}
