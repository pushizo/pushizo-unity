#nullable enable

namespace PushizoSdk.Internal
{
    /// <summary>Derleme hedefine göre yerel köprüyü seçer (Editor ve diğer platformlarda <see cref="EditorNativeBridge"/>).</summary>
    internal static class PlatformFactory
    {
        public static INativeBridge Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidNativeBridge();
#elif UNITY_IOS && !UNITY_EDITOR
            return new IosNativeBridge();
#else
            return new EditorNativeBridge();
#endif
        }
    }
}
