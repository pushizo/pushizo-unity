#nullable enable
using System;

namespace PushizoSdk.Internal
{
    /// <summary>C# katmanının günlüğü (Unity konsolu); token/gizli değer yazılmaz.</summary>
    internal static class SdkLog
    {
        private static volatile LogLevel s_level = LogLevel.Warn;

        public static LogLevel Level
        {
            get => s_level;
            set => s_level = value;
        }

        /// <summary>Yazıcı; varsayılan Unity konsolu (testlerde değiştirilebilir).</summary>
        public static Action<LogLevel, string> Writer { get; set; } = DefaultWriter;

        public static void Write(LogLevel level, string message, Exception? error = null)
        {
            if (level == LogLevel.None || (int)level > (int)s_level) return;
            var text = error is null ? message : $"{message}: {error.GetType().Name}: {error.Message}";
            try
            {
                Writer(level, text);
            }
            catch (Exception)
            {
                // Günlük yazıcısı hatası SDK akışını bozmaz.
            }
        }

        private static void DefaultWriter(LogLevel level, string message)
        {
            var text = "[Pushizo] " + message;
            switch (level)
            {
                case LogLevel.Error: UnityEngine.Debug.LogError(text); break;
                case LogLevel.Warn: UnityEngine.Debug.LogWarning(text); break;
                default: UnityEngine.Debug.Log(text); break;
            }
        }
    }
}
