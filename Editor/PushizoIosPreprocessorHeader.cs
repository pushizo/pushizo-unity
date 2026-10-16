using System.Text.RegularExpressions;

namespace PushizoSdk.Editor
{
    /// <summary>
    /// Unity'nin ürettiği Xcode projesindeki <c>Classes/Preprocessor.h</c> dönüşümü. Unity, projede uzak bildirim API'si
    /// kullanmadığını düşündüğünde <c>UNITY_USES_REMOTE_NOTIFICATIONS</c> değerini 0 yazar; bu durumda UnityAppController
    /// APNs token'ını almaz ve köprünün dinlediği <c>kUnityDidRegisterForRemoteNotificationsWithDeviceToken</c>
    /// bildirimini yayınlamaz. Dönüşüm saf metin işlemidir (platformdan bağımsız test edilebilir); iOS derleme sonrası
    /// adımı (<c>PushizoIosPostProcessor</c>) dosyaya uygular.
    /// </summary>
    public static class PushizoIosPreprocessorHeader
    {
        public const string RelativePath = "Classes/Preprocessor.h";
        public const string RemoteNotificationsDefine = "UNITY_USES_REMOTE_NOTIFICATIONS";

        private static readonly Regex DefinePattern = new Regex(
            @"^([ \t]*#[ \t]*define[ \t]+" + RemoteNotificationsDefine + @"[ \t]+)\d+",
            RegexOptions.Multiline);

        /// <summary>
        /// <c>UNITY_USES_REMOTE_NOTIFICATIONS</c> tanımını 1 yapar. Tanım yoksa (beklenmeyen Unity sürümü) dosya sonuna
        /// eklenir. Zaten 1 ise metin değişmeden döner (idempotent).
        /// </summary>
        public static string EnableRemoteNotifications(string header)
        {
            if (header == null) header = string.Empty;
            if (DefinePattern.IsMatch(header)) return DefinePattern.Replace(header, "${1}1", 1);
            var newline = header.Contains("\r\n") ? "\r\n" : "\n";
            var prefix = header.Length == 0 || header.EndsWith("\n") ? string.Empty : newline;
            return header + prefix + "#define " + RemoteNotificationsDefine + " 1" + newline;
        }
    }
}
