using System;
using UnityEngine;

namespace GameCore.Unity
{
    public sealed class UnityLogger : ILogger
    {
        public LogLevel MinLevel { get; set; } = LogLevel.Debug;

        public void Log(LogLevel level, string tag, string message)
        {
            if (level < MinLevel)
                return;

            var text = $"[{tag}] {message}";
            switch (level)
            {
                case LogLevel.Warning:
                    Debug.LogWarning(text);
                    break;
                case LogLevel.Error:
                    Debug.LogError(text);
                    break;
                default:
                    Debug.Log(text);
                    break;
            }
        }

        public void LogException(Exception exception, string tag) => Debug.LogException(exception);
    }
}
