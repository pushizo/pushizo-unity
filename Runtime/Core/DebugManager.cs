using System;
using HeroSignalSdk.Unity.Internal;

namespace HeroSignalSdk.Unity
{
    /// <summary>Tanılama (<c>HeroSignal.Debug</c>).</summary>
    public sealed class DebugManager
    {
        internal DebugManager()
        {
        }

        /// <summary>Geçerli günlük seviyesi (C# katmanı ve yerel SDK).</summary>
        public LogLevel LogLevel => SdkLog.Level;

        /// <summary>
        /// Günlük seviyesini ayarlar; Initialize'dan önce de çağrılabilir (yerel SDK'ya Initialize sırasında
        /// <see cref="HeroSignalOptions.LogLevel"/> ile, sonrasında bu çağrıyla iletilir). Token ve gizli değerler yazılmaz.
        /// </summary>
        public void SetLogLevel(LogLevel level)
        {
            if (!Enum.IsDefined(typeof(LogLevel), level)) throw new ArgumentOutOfRangeException(nameof(level), level, "Geçersiz günlük seviyesi.");
            SdkLog.Level = level;
            HeroSignal.PlatformOrNull()?.SetLogLevel(level);
        }
    }
}
