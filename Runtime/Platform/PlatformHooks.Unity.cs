using System.Threading;
using UnityEngine;

namespace HeroSignalSdk.Unity.Internal
{
    /// <summary>Unity platform seçimi ve günlük yazıcısı (çekirdekteki kısmi metotların uygulaması).</summary>
    internal static partial class PlatformHooks
    {
        static partial void CreatePlatformBridge(ref IHeroSignalBridge bridge)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge = new AndroidBridge();
#elif UNITY_IOS && !UNITY_EDITOR
            bridge = new IosBridge();
#else
            bridge = new UnsupportedBridge(Application.isEditor ? "Unity Editör" : Application.platform.ToString());
#endif
        }

        static partial void ConfigurePlatformLogging()
        {
            SdkLog.Writer = (level, message) =>
            {
                switch (level)
                {
                    case LogLevel.Error: UnityEngine.Debug.LogError("[HeroSignal] " + message); break;
                    case LogLevel.Warn: UnityEngine.Debug.LogWarning("[HeroSignal] " + message); break;
                    default: UnityEngine.Debug.Log("[HeroSignal] " + message); break;
                }
            };
        }
    }

    /// <summary>
    /// Oyun açılışında ana iş parçacığı bağlamını (<c>UnitySynchronizationContext</c>) yakalar ve Editör'de alan yeniden
    /// yüklemesi kapalıyken (Enter Play Mode Options) önceki oturumun durumunu sıfırlar.
    /// </summary>
    internal static class HeroSignalBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            HeroSignal.ResetForPlayMode();
            HeroSignal.CaptureMainThread(SynchronizationContext.Current);
        }
    }
}
