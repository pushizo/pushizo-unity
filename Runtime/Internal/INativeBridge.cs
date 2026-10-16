#nullable enable
using System;

namespace PushizoSdk.Internal
{
    /// <summary>
    /// Yerel köprü sonucu: başarıda <paramref name="json"/> (işleme göre null olabilir), hatada
    /// <paramref name="errorCode"/> + <paramref name="errorMessage"/>. Herhangi bir iş parçacığından çağrılabilir.
    /// </summary>
    internal delegate void NativeResult(string? json, string? errorCode, string? errorMessage);

    /// <summary>
    /// Yerel köprü sözleşmesi: Android'de <c>com.pushizo.unity.PushizoUnityBridge</c> (Java,
    /// Runtime/Plugins/Android), iOS'ta <c>hs_*</c> C fonksiyonları (Swift, Runtime/Plugins/iOS), Editor'de
    /// <see cref="EditorNativeBridge"/>. Karmaşık değerler JSON metni olarak taşınır (biçimler <see cref="BridgeJson"/>).
    /// </summary>
    internal interface INativeBridge
    {
        bool IsNative { get; }

        /// <summary>Yerel SDK'yı başlatır; olaylar <c>(tür, json)</c> olarak herhangi bir iş parçacığından gelir.</summary>
        void Start(string appId, string optionsJson, Action<string, string> onEvent);

        void SetConsentGiven(bool given);
        /// <summary>Kanıt app'in kimlik doğrulama kipine uymuyorsa hata mesajı döner (giriş yapılmaz); başarıda null.</summary>
        string? Login(string externalId, string? identityHash, string? jwt);

        /// <summary>Kimlik JWT'sini yeniler; giriş yapılmamışsa hata mesajı döner, başarıda null.</summary>
        string? UpdateUserJwt(string jwt);
        void Logout();
        void SetLogLevel(int level);
        string? GetExternalId();
        string? GetUserId();
        void AddTags(string tagsJson);
        void RemoveTags(string keysJson);
        string GetTags();
        void AddAliases(string aliasesJson);
        void RemoveAlias(string label);
        void SetLanguage(string languageCode);
        void AddEmail(string email);
        void RemoveEmail(string email);
        void AddSms(string phoneNumber);
        void RemoveSms(string phoneNumber);
        string GetEmails();
        string GetSmsNumbers();
        string GetPushSubscription();
        void OptIn();
        void OptOut();
        bool GetPermission();
        void RequestPermission(bool fallbackToSettings, Action<bool> completion);
        void ClearAllNotifications();
        /// <summary>Tek bildirimi kaldırır. Android: tepsi kimliği (tamsayı metni); iOS: <c>UNNotificationRequest.identifier</c>.</summary>
        void RemoveNotification(string id);
        /// <summary>Android <c>android_group</c> / iOS <c>thread-id</c> grubundaki bildirimleri kaldırır.</summary>
        void RemoveGroupedNotifications(string group);

        /// <summary>
        /// true: ön planda gelen bildirim yerelde ertelenir ve <c>foreground</c> olayı (<c>eventId</c> ile) üretilir;
        /// karar <see cref="ForegroundDisplay"/> ile verilir. false: SDK varsayılanı (bildirim gösterilir).
        /// </summary>
        void SetForegroundHandlerEnabled(bool enabled);

        void ForegroundDisplay(string eventId);
        void AddTriggers(string triggersJson);
        void RemoveTriggers(string keysJson);
        void ClearTriggers();
        string GetTriggers();
        void SetScreen(string? name);
        bool GetInAppPaused();
        void SetInAppPaused(bool paused);
        void InboxList(int limit, string? cursor, NativeResult result);
        void InboxMarkRead(string id, NativeResult result);
        void InboxMarkAllRead(NativeResult result);
        void InboxArchive(string id, NativeResult result);
        void InboxUnreadCount(NativeResult result);

        /// <summary>
        /// Başarı JSON'u: <c>{"preferences":[{"category","channel","optedIn","explicit","source","updatedAt"}],
        /// "categories":[{"key","name":{},"description":{},"channels":[…]|null,"defaultOptedIn"}]}</c>.
        /// </summary>
        void GetPreferences(NativeResult result);

        /// <summary>
        /// <paramref name="preferencesJson"/>: <c>[{"category","channel":null|"…","optedIn"}]</c>; başarı JSON'u
        /// <see cref="GetPreferences"/> ile aynı (güncel liste).
        /// </summary>
        void SetPreferences(string preferencesJson, NativeResult result);
        void SendEvent(string name, string propertiesJson, double value, bool hasValue);

        /// <summary>
        /// Yerel SDK'da Live Activities var mı (yalnızca iOS). false ise <see cref="LiveActivitiesManager"/> çağrıları köprüye
        /// iletmeden <c>PLATFORM_NOT_SUPPORTED</c> ile reddeder.
        /// </summary>
        bool SupportsLiveActivities { get; }

        /// <summary>
        /// Genel <c>PushizoDefaultLiveActivityAttributes</c> türünü izlemeye alır. Başarı JSON'u: null; hata kodları
        /// <c>PLATFORM_NOT_SUPPORTED</c> | <c>NOT_READY</c> | <c>INVALID_ARGUMENT</c> | <c>LIVE_ACTIVITY_FAILED</c>.
        /// </summary>
        void SetupDefaultLiveActivity(NativeResult result);

        /// <summary>
        /// Genel türle yerel etkinlik başlatır. <paramref name="attributesJson"/> / <paramref name="contentStateJson"/>: düz
        /// JSON nesnesi (değerler metin, sayı, bool). Başarı JSON'u: null; hata kodları <see cref="SetupDefaultLiveActivity"/> ile aynı.
        /// </summary>
        void StartDefaultLiveActivity(string activityId, string attributesJson, string contentStateJson, NativeResult result);

        /// <summary>Konum paylaşımı açık mı (yerel SDK'nın kalıcı tercihi; izin durumundan bağımsız).</summary>
        bool GetLocationShared();

        /// <summary>Konum paylaşımını açar/kapatır; kapatınca yerel SDK sunucuya <c>location: null</c> gönderir.</summary>
        void SetLocationShared(bool shared);

        /// <summary>
        /// Ön plan konum iznini ister (önce açıklama diyaloğu; <paramref name="rationale"/> null ise SDK'nın varsayılan
        /// metni). Sonuç: izin var/verildi mi; herhangi bir iş parçacığından gelebilir.
        /// </summary>
        void RequestLocationPermission(string? rationale, Action<bool> completion);
    }
}
