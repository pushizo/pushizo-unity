namespace HeroSignalSdk.Unity.Internal
{
    /// <summary>
    /// Platform seçimi. Unity katmanı (<c>Runtime/Platform/PlatformHooks.Unity.cs</c>) kısmi metotları uygular:
    /// Android/iOS köprüsü, <c>UnityEngine.Debug</c> günlüğü. Çekirdek tek başına (doğrulama derlemesi) derlendiğinde
    /// uygulama yoktur ve <see cref="UnsupportedBridge"/> kullanılır.
    /// </summary>
    internal static partial class PlatformHooks
    {
        public static IHeroSignalBridge CreateBridge()
        {
            IHeroSignalBridge bridge = null;
            CreatePlatformBridge(ref bridge);
            return bridge ?? new UnsupportedBridge(".NET");
        }

        /// <summary>Unity katmanı günlük yazıcısını bağlar (yoksa varsayılan Trace yazıcısı kalır).</summary>
        public static void ConfigureLogging() => ConfigurePlatformLogging();

        static partial void CreatePlatformBridge(ref IHeroSignalBridge bridge);

        static partial void ConfigurePlatformLogging();
    }
}
