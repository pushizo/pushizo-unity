using System;

namespace HeroSignalSdk.Unity.Internal
{
    /// <summary>
    /// C# katmanının günlüğü; seviye <see cref="HeroSignal.Debug"/> ile yerel SDK'yla birlikte ayarlanır. Unity'de yazıcı
    /// <c>UnityEngine.Debug</c>'a bağlanır (<see cref="PlatformHooks"/>). Token ve gizli değerler yazılmaz.
    /// </summary>
    internal static class SdkLog
    {
        private static volatile LogLevel s_level = LogLevel.Warn;

        public static LogLevel Level
        {
            get => s_level;
            set => s_level = value;
        }

        /// <summary>Yazıcı (varsayılan <see cref="System.Diagnostics.Trace"/>).</summary>
        public static Action<LogLevel, string> Writer { get; set; } = (level, message) =>
            System.Diagnostics.Trace.WriteLine($"[HeroSignal] {level}: {message}");

        public static void Write(LogLevel level, string message, Exception error = null)
        {
            if (level == LogLevel.None || (int)level > (int)s_level) return;
            var text = error == null ? message : $"{message}: {error.GetType().Name}: {error.Message}";
            try
            {
                Writer(level, text);
            }
            catch (Exception)
            {
                // Günlük yazıcısı hatası SDK akışını bozmaz.
            }
        }
    }
}
