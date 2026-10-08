using UnityEditor;
using UnityEngine;

namespace HeroSignalSdk.Unity.Editor
{
    /// <summary>
    /// Derleme ayarları (Assets → Create → HeroSignal → Settings). Varlık yoksa varsayılanlar kullanılır.
    /// Çalışma zamanı yapılandırması (App ID, API adresi) <c>HeroSignal.Initialize</c> ile verilir; buradaki App ID ve
    /// API adresi yalnızca iOS Notification Service Extension'ın Info.plist'ine yazılır.
    /// </summary>
    [CreateAssetMenu(fileName = "HeroSignalSettings", menuName = "HeroSignal/Settings")]
    public sealed class HeroSignalSettings : ScriptableObject
    {
        [Tooltip("HeroSignal App ID (app_…); iOS NSE için.")]
        public string appId = string.Empty;

        [Tooltip("HeroSignal API adresi; iOS NSE için.")]
        public string apiUrl = "https://api.herosignal.dev";

        [Header("iOS")]
        [Tooltip("Zengin medya, rozet artışı ve teslimat onayı için Notification Service Extension hedefi eklensin.")]
        public bool iosAddNotificationServiceExtension = true;

        [Tooltip("aps-environment: development (geliştirme/TestFlight öncesi) veya production.")]
        public string iosApnsEnvironment = "development";

        [Tooltip("Uygulama ve NSE arasında rozet sayacı paylaşımı için App Group (ör. group.com.example.game). Boşsa eklenmez.")]
        public string iosAppGroup = string.Empty;

        [Header("Android")]
        [Tooltip("google-services.json'dan Firebase string kaynakları üretilsin (Firebase Unity SDK kullanılmıyorsa gerekli).")]
        public bool androidGenerateGoogleServicesResources = true;

        [Tooltip("google-services.json yolu (proje köküne göre).")]
        public string googleServicesJsonPath = "Assets/google-services.json";

        /// <summary>Projedeki ilk ayar varlığı; yoksa varsayılan değerlerle geçici örnek.</summary>
        public static HeroSignalSettings LoadOrDefault()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(HeroSignalSettings)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<HeroSignalSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) return asset;
            }
            return CreateInstance<HeroSignalSettings>();
        }
    }
}
